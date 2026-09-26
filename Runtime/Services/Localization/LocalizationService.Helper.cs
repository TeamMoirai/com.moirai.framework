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
        /// <summary>不存在时的默认语言</summary>
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
        private static readonly Func<string, string> s_RuntimeResolver = ResolveForRuntime;

        /// <summary>
        /// 返回一个本地化字符串，将 <b>{l10n:ID}</b>/<b>{i18n:ID}</b>/<b>{g11n:ID}</b> 替换为本地化条目。
        /// <para>单遍扫描、无正则：无标记时零分配直返原串；有标记时经池化构建器一次拼装，
        /// 替代旧实现「每次 MatchCollection + 逐标记整串 Replace」的分配链（UILabel 高频消费）。</para>
        /// <para>未解析的标记（ID 不存在/解析失败）原样保留，与旧实现一致。</para>
        /// </summary>
        /// <param name="format">使用本地化的字符串</param>
        /// <returns></returns>
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
        /// 标记替换：单遍扫描 <c>{l10n:…}</c>/<c>{i18n:…}</c>/<c>{g11n:…}</c>（前缀大小写不敏感），
        /// 命中后由 <paramref name="resolver"/> 解析 ID（两端空白裁剪、大小写保留）。
        /// </summary>
        /// <param name="format">原始字符串。</param>
        /// <param name="resolver">ID → 译文；返回 <c>null</c> 表示未解析，该标记原样保留（告警由解析方负责）。</param>
        /// <returns>无标记或全部标记未解析时返回原串（同一实例）；否则返回拼装结果。</returns>
        internal static string Localize(string format, Func<string, string> resolver)
        {
            if (string.IsNullOrEmpty(format)) return format;

            IStringBuilder builder = null;
            var cursor = 0;
            var replaced = false;
            while (cursor < format.Length)
            {
                var open = format.IndexOf('{', cursor);
                if (open < 0) break;

                if (!TryReadMarker(format, open, out var idStart, out var idEnd, out var markerEnd))
                {
                    cursor = open + 1;
                    continue;
                }

                // ID 两端空白裁剪（对齐旧实现的 Trim 语义），不分配
                while (idStart < idEnd && char.IsWhiteSpace(format[idStart])) idStart++;
                while (idEnd > idStart && char.IsWhiteSpace(format[idEnd - 1])) idEnd--;
                var textId = format.Substring(idStart, idEnd - idStart);

                var replacement = resolver(textId);

                // 首个有效标记才建构建器：无标记/全未解析的输入零分配直返原串
                builder ??= StringUtility.CreateStringBuilder(format.Length);
                builder.Append(format, cursor, open - cursor);
                if (replacement != null)
                {
                    builder.Append(replacement);
                    replaced = true;
                }
                else
                {
                    builder.Append(format, open, markerEnd - open);
                }

                cursor = markerEnd;
            }

            if (builder == null) return format;

            builder.Append(format, cursor, format.Length - cursor);
            if (!replaced)
            {
                builder.Dispose();
                return format;
            }

            return builder.ToStringAndDispose();
        }

        /// <summary>
        /// 内联占位符前缀白名单（小写）。长度不必一致——冒号按命中前缀的实际长度定位。
        /// <para>只认表内前缀：任意 <c>{foo:bar}</c> 不会被当成译文标记吃掉。</para>
        /// </summary>
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
        
        private static string ResolveForRuntime(string textId)
        {
            try
            {
                // 单趟解析：命中即取译文；此前 Has + GetTextFromId 两趟查询
                if (TryGetTextFromId(textId, out var text)) return text;

                LogUtility.Warning("Text ID: {0} not available.", textId);
                return null;
            }
            catch (Exception ex)
            {
                LogUtility.Fatal("Failed to resolve localization for ID: {0}. Error: {1}", textId, ex);
                return null;
            }
        }

        #endregion

        /// <summary>
        /// 把自报语言代码序列解析为语言序列，列序即输入序。
        /// <para>内置语言按 Name/Code 命中；认不出的代码按自定义语言直通（<see cref="Language"/> 相等性按 Code，
        /// 自定义实例与同 Code 的内置实例等价，项目自定义语言无需改框架即可随表发行）。</para>
        /// <para>本方法是语言列序的<strong>唯一</strong>解析入口——运行期处理与编辑器预览共用，不存在第二份语言真相源。</para>
        /// </summary>
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
        /// 根据 名称/Code 获取语言。
        /// </summary>
        /// <param name="str">语言 Name 或 Code（不区分大小写）</param>
        /// <param name="onlySupported">是否只获取当前批内收录的语言</param>
        /// <returns>无法识别的输入、或 <paramref name="onlySupported"/> 为真且语言不在批内时为 <see cref="DefaultLanguage"/></returns>
        /// <remarks>「是否支持」的唯一真相源是已加载的语言批（全局注册表已删）：批未就绪时退化为身份解析
        /// 直接放行，可用性由切换方在加载完成后校验（<c>ChangeLanguage</c> 有一次性告警）——
        /// 不再出现"数据没加载就把 zh-Hans 静默落成默认英语"的双源歧义。</remarks>
        public static Language ToLanguage(string str, bool onlySupported)
        {
            // 处理边界条件：str 为空或 null
            if (string.IsNullOrEmpty(str))
            {
                return DefaultLanguage;
            }

            Language target = DefaultLanguage;
            // 尝试从语言代码映射中获取语言
            if (s_AllBuildInLanguageCodeMap.TryGetValue(str, out var langFromCode))
            {
                target = langFromCode;
            }

            // 尝试从语言名称映射中获取语言
            if (s_AllBuildInLanguageMap.TryGetValue(str, out var langFromName))
            {
                target = langFromName;
            }

            if (!onlySupported) return target;

            var handler = s_Handler;
            if (handler == null || !handler.IsDataLoaded) return target;

            return handler.IsLanguageAvailable(target) ? target : DefaultLanguage;
        }
    }
}