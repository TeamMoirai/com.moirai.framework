using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 默认本地化辅助器。
    /// </summary>
    partial class LocalizationService
    {
        /// <summary>不存在时的默认语言。</summary>
        public static readonly Language DefaultLanguage = Language.English;

        // 所有内置语言（Name / Code，忽略大小写直接命中，省掉每次查询的 ToLower 分配）
        private static readonly Dictionary<string, Language> s_AllBuildInLanguageMap =
            Language.BuiltinLanguages.ToDictionary(_ => _.Name, _ => _, StringComparer.OrdinalIgnoreCase);
        // 所有内置语言代码
        private static readonly Dictionary<string, Language> s_AllBuildInLanguageCodeMap =
            Language.BuiltinLanguages.ToDictionary(_ => _.Code, _ => _, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 复位一次性日志闸门。
        /// </summary>
        /// <remarks>只复位日志闸门：编辑器关闭域重载时 <c>static</c> 跨会话存活，
        /// 否则"未初始化"这类一次性警告在第二次会话里彻底哑火。</remarks>
        internal static void ResetOneShotLogs()
        {
            s_HasLoggedWarning = false;
        }

        /// <summary>
        /// 按 Name 或 Code 精确解析内置语言，不做默认语言兜底。
        /// </summary>
        /// <remarks>供配置项校验使用：<see cref="ToLanguage"/> 对无法识别的输入静默回落默认语言，
        /// 会把写错的语言代码当成合法配置放过。</remarks>
        internal static bool TryGetBuiltInLanguage(string str, out Language language)
        {
            language = null;
            if (string.IsNullOrEmpty(str)) return false;

            // 内置语言的 Name 与 Code 互不重叠，两种写法都能唯一命中
            return s_AllBuildInLanguageCodeMap.TryGetValue(str, out language)
                   || s_AllBuildInLanguageMap.TryGetValue(str, out language);
        }

        private static bool s_HasLoggedWarning;

        #region 多语言解析 [LOCALIZE RESOLVER]

        // 运行期预览数据路径的解析器方法组（静态缓存，Localize 每调用零委托分配）
        private static readonly MarkerResolver s_RuntimeResolver = ResolveForRuntime;

        /// <summary>标记 ID 解析器：入参为格式串中的 ID 切片，返回译文；<c>null</c> 表示未解析。</summary>
        internal delegate string MarkerResolver(ReadOnlySpan<char> id);

        /// <summary>
        /// 返回本地化后的字符串：把 <b>{l10n:ID}</b>/<b>{i18n:ID}</b>/<b>{g11n:ID}</b> 标记替换为本地化条目。
        /// </summary>
        /// <remarks>
        /// 单遍扫描、无正则：无标记时零分配直返原串，有标记时经池化构建器一次拼装。 <br />
        /// 未解析的标记（ID 不存在或解析失败）原样保留。
        /// </remarks>
        /// <param name="format">使用本地化的字符串。</param>
        /// <list type="tabel">
        /// <item><term>l10n</term><description>本地化，Localization 缩写</description></item>
        /// <item><term>i18n</term><description>国际化，Internationalization 缩写</description></item>
        /// <item><term>g11n</term><description>全球化，Globalization 缩写</description></item>
        /// </list>
        public static string Localize(string format)
        {
            if (string.IsNullOrEmpty(format)) return format;

            if (!Application.isPlaying || IsValid) return Localize(format, s_RuntimeResolver);
            
            if (!s_HasLoggedWarning) LogUtility.Warning("{0} not initialized!", nameof(LocalizationService));
            s_HasLoggedWarning = true;
            
            return format;
        }

        /// <summary>
        /// 标记替换：单遍扫描 <c>{l10n:…}</c>/<c>{i18n:…}</c>/<c>{g11n:…}</c>（前缀大小写不敏感）。
        /// </summary>
        /// <remarks>命中后由 <paramref name="resolver"/> 解析 ID（两端空白裁剪、大小写保留），返回 <c>null</c> 即未解析、标记原样保留。 <br />
        /// 扫描走 span、不切中间串：无标记时零分配直返原串；ID 以切片交给解析器（运行期路径查字典，故每个标记物化一次字符串）；发生替换时另加结果串一次分配。</remarks>
        /// <param name="format">原始字符串。</param>
        /// <param name="resolver">ID 切片 → 译文；返回 <c>null</c> 表示未解析，该标记原样保留（告警由解析方负责）。</param>
        /// <returns>无标记或全部标记未解析时返回原串（同一实例）；否则返回拼装结果。</returns>
        internal static string Localize(string format, MarkerResolver resolver)
        {
            if (string.IsNullOrEmpty(format)) return format;

            IStringBuilder builder = null;
            var cursor = 0; // 未刷入构建器的原文起点：未解析标记留在该区间里，最终一并带回
            var search = 0;
            while (search < format.Length)
            {
                var open = format.IndexOf('{', search);
                if (open < 0) break;

                if (!TryReadMarker(format, open, out var idStart, out var idEnd, out var markerEnd))
                {
                    search = open + 1;
                    continue;
                }

                // ID 两端空白裁剪（对齐旧实现的 Trim 语义），不分配
                while (idStart < idEnd && char.IsWhiteSpace(format[idStart])) idStart++;
                while (idEnd > idStart && char.IsWhiteSpace(format[idEnd - 1])) idEnd--;

                var replacement = resolver(format.AsSpan(idStart, idEnd - idStart));
                if (replacement != null)
                {
                    // 首个成功替换才建构建器：无标记/全未解析不租池、不产出新串
                    builder ??= StringUtility.CreateStringBuilder(format.Length);
                    builder.Append(format, cursor, open - cursor);
                    builder.Append(replacement);
                    cursor = markerEnd;
                }

                search = markerEnd;
            }

            if (builder == null) return format;

            builder.Append(format, cursor, format.Length - cursor);
            return builder.ToStringAndDispose();
        }

        /// <summary>内联占位符前缀白名单（小写）；冒号按命中前缀的实际长度定位，各前缀长度不必一致。</summary>
        /// <remarks>只认表内前缀，任意 <c>{foo:bar}</c> 不会被当成译文标记吃掉。</remarks>
        private static readonly string[] s_MarkerPrefixes = { "l10n", "i18n", "g11n" };
        
        /// <summary>
        /// 识别占位符标记（前缀大小写不敏感）；命中时给出 ID 区间（未裁剪）与整段标记的结束下标。
        /// </summary>
        private static bool TryReadMarker(string format, int openIndex, out int idStart, out int idEnd, out int markerEnd)
        {
            idStart = 0;
            idEnd = 0;
            markerEnd = 0;

            var prefixStart = openIndex + 1;
            var prefixLength = 0;
            for (var p = 0; p < s_MarkerPrefixes.Length; p++)
            {
                prefixLength = MatchMarkerPrefix(format, prefixStart, s_MarkerPrefixes[p]);
                if (prefixLength != 0) break;
            }
            if (prefixLength == 0) return false;

            // 冒号跟在命中的前缀之后——步长随前缀长度走，新前缀不必再改这里的 +N
            var i = prefixStart + prefixLength;
            if (i >= format.Length || format[i] != ':') return false;

            i++;
            var close = format.IndexOf('}', i);
            if (close < 0) return false;

            idStart = i;
            idEnd = close;
            markerEnd = close + 1;
            return true;
            
            // 从下标处比较小写前缀（位或小写化只对 A-Z 生效，数字/符号位或后对照不产生误命中）；命中返回前缀长度，未命中返回 0。
            static int MatchMarkerPrefix(string value, int offset, string lowerPrefix)
            {
                if (offset + lowerPrefix.Length > value.Length) return 0;

                for (var i = 0; i < lowerPrefix.Length; i++)
                {
                    if ((value[offset + i] | 0x20) != lowerPrefix[i]) return 0;
                }

                return lowerPrefix.Length;
            }
        }
        
        private static string ResolveForRuntime(ReadOnlySpan<char> textId)
        {
            // 直查：ID 物化一次进字典，不做索引/缓存
            var id = textId.ToString();
            try
            {
                // 单趟解析：命中即取译文；此前 Has + GetTextFromId 两趟查询
                return TryGetTextFromId(id, out var text) ? text : null;
            }
            catch (Exception ex)
            {
                LogUtility.Fatal("Failed to resolve localization for ID: {0}. Error: {1}", id, ex);
                return null;
            }
        }

        #endregion

        /// <summary>
        /// 把自报语言代码序列解析为语言序列，列序即输入序。
        /// </summary>
        /// <remarks>
        /// 内置语言按 Name/Code 命中；认不出的代码按自定义语言直通（<see cref="Language"/> 相等性按 Code）。 <br />
        /// 本方法是语言列序的唯一解析入口，运行期处理与编辑器预览共用，不存在第二份语言真相源。
        /// </remarks>
        internal static List<Language> ResolveLanguages(IReadOnlyList<string> codes)
        {
            var languages = new List<Language>(codes?.Count ?? 0);
            if (codes == null) return languages;

            for (var i = 0; i < codes.Count; i++)
            {
                var code = codes[i];
                if (string.IsNullOrEmpty(code)) continue;

                if (TryGetBuiltInLanguage(code, out var language))
                {
                    if (!languages.Contains(language)) languages.Add(language);
                }
                else
                {
                    var custom = new Language(code, code);
                    if (!languages.Contains(custom)) languages.Add(custom);
                }
            }

            return languages;
        }

        /// <summary>
        /// 当前数据源自报的导出语言代码，顺序即列序；后端没自报语言或未注册时为空。
        /// </summary>
        /// <remarks>读数来自当前数据源的 <see cref="LocalizationServiceHandler.ExportedLanguageCodes"/>（覆写点是它的 <c>SelfReportedLanguageCodes</c>）；本项目即配置表那份转表期生成的语言清单。列语言选项目标请优先用 <see cref="ExportedLanguages"/>。</remarks>
        public static IReadOnlyList<string> ExportedLanguageCodes
        {
            get
            {
                var source = ExportedLanguageSource;
                return source != null ? source.ExportedLanguageCodes : Array.Empty<string>();
            }
        }

        /// <summary>
        /// 导出语言解析成的 <see cref="Language"/>，已去空码与去重码；未自报时为空。
        /// </summary>
        /// <remarks>
        /// 去空去重后它可能比 <see cref="ExportedLanguageCodes"/> 短，两者下标不保证互指。与 <see cref="LoadedLanguages"/> 的分工：
        /// 本属性答「数据源自报有哪几门语言」，装载前即可用；那份答「当前批内收录了哪几门」，装载后才有。
        /// 解析与缓存都落在处理器实例上，返回共享数组，<b>调用方禁止原地改写元素或长度</b>。
        /// </remarks>
        public static IReadOnlyList<Language> ExportedLanguages
        {
            get
            {
                var source = ExportedLanguageSource;
                return source != null ? source.ExportedLanguages : Array.Empty<Language>();
            }
        }

        /// <summary>
        /// 导出语言的读数来源：已注册的处理器优先，编辑器内没有注册那份时直读 Settings 里配置的实例。
        /// </summary>
        /// <remarks>不装进 <c>s_Handler</c>、不触发初始化也不注册服务：语言候选要在服务世界之外也能成立。</remarks>
        private static LocalizationServiceHandler ExportedLanguageSource
        {
            get
            {
                if (s_Handler != null) return s_Handler;
#if UNITY_EDITOR
                return LocalizationServiceSettings.LocalizationServiceHandler;
#else
                return null;
#endif
            }
        }

        /// <summary>编辑器语言设置是否为「未选」哨兵：<c>Unspecified</c> 的 Name 与 Code 两种写法都算。</summary>
        /// <remarks>空值与认不出的写法判非哨兵，原样交给检测链解析；Name 与 Code 都判，是因为资产里落过两种写法。</remarks>
        internal static bool IsEditorLanguageUnspecified(string setting) =>
            TryGetBuiltInLanguage(setting, out var language) && language == Language.Unspecified;

        /// <summary>
        /// 根据 名称/Code 获取语言。
        /// </summary>
        /// <param name="str">语言 Name 或 Code（不区分大小写）。</param>
        /// <param name="onlySupported">是否只获取当前批内收录的语言。</param>
        /// <returns>无法识别的输入、或 <paramref name="onlySupported"/> 为真且语言不在批内时为 <see cref="DefaultLanguage"/>。</returns>
        /// <remarks>「是否支持」的唯一真相源是已加载的语言批（全局注册表已删）：批未就绪时退化为身份解析
        /// 直接放行，可用性由切换方在加载完成后校验（<c>ChangeLanguage</c> 有一次性告警）——。 <br />
        /// 不再出现"数据没加载就把 zh-Hans 静默落成默认英语"的双源歧义。</remarks>
        public static Language ToLanguage(string str, bool onlySupported)
        {
            // 处理边界条件：str 为空或 null
            if (string.IsNullOrEmpty(str))
            {
                return DefaultLanguage;
            }

            var handler = s_Handler;
            // 「是否支持」的判定实现只有一份：交给已注册的那个处理器按自己的语言批判
            if (onlySupported && handler != null) return handler.ResolvePreference(str);

            return TryGetBuiltInLanguage(str, out var language) ? language : DefaultLanguage;
        }
    }
}