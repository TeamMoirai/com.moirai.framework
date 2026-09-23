using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Moirai.Atropos.ConfigTable;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 默认本地化辅助器。
    /// </summary>
    public partial class LocalizationService
    {
        /// <summary>不存在时的默认语言</summary>
        public static readonly Language defaultLanguage = Language.English;

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

        #region 版本 3 [VERSION 3]

        // 预编译正则表达式
        // 使用正则表达式匹配 {l10n:...} 或 {i18n:...} 或 {g11n:...}
        // (l10n|i18n|g11n) 是第一个捕获组，匹配标签类型。
        // (.*?) 是第二个捕获组，匹配文本 ID。
        private static readonly Regex s_LocalizedRegex = new Regex(@"\{(l10n|i18n|g11n):(.*?)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        /// <summary>
        /// 返回一个本地化字符串，将 <b>{l10n:ID}</b>/<b>{i18n:ID}</b>/<b>{g11n:ID}</b> 替换为本地化条目
        /// </summary>
        /// <param name="format">使用格式更新的字符串</param>
        /// <returns></returns>
        /// <list type="tabel">
        /// <item><term>l10n</term><description>本地化，Localization 缩写</description></item>
        /// <item><term>i18n</term><description>国际化，Internationalization 缩写</description></item>
        /// <item><term>g11n</term><description>全球化，Globalization 缩写</description></item>
        /// </list>
        public static string Localize(string format)
        {
            if (string.IsNullOrEmpty(format)) return format;

            var playing = Application.isPlaying;
            if (playing)
            {
                if (!IsValid)
                {
                    if (!s_HasLoggedWarning) LogUtility.Warning("{0} not initialized!", nameof(LocalizationService));
                    s_HasLoggedWarning = true;
                    return format;
                }
            }
            else if (GetEditorPreviewStore() == null)
            {
                // 编辑器预览取不到数据（表未生成等）时原样返回，失败日志已在预览侧限流
                return format;
            }

            var matches = s_LocalizedRegex.Matches(format);
            if (matches.Count == 0) return format;

            foreach (Match match in matches)
            {
                string textId = match.Groups[2].Value.Trim(); // LocalizedRegex 的第二个捕获组专门用于匹配文本 ID。

                try
                {
                    var has = playing ? Has(textId) : EditorPreviewHasText(textId);
                    if (!has)
                    {
                        if (playing) LogUtility.Warning("Text ID: {0}({1}) not available.", textId, match.Groups[1].Value);
                        continue;
                    }

                    string replacement = playing ? GetTextFromId(textId) : ResolveForEditorPreview(textId);
                    // LogUtility.Info("Resolving localization for ID: {0}({1})", textId, replacement);
                    format = format.Replace(match.Value, replacement);
                }
                catch (Exception ex)
                {
                    LogUtility.Fatal("Failed to resolve localization for ID: {0}. Error: {1}", textId, ex);
                }
            }

            return format;
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
        /// <returns>无法识别的输入、或 <paramref name="onlySupported"/> 为真且语言不在批内时为 <see cref="defaultLanguage"/></returns>
        /// <remarks>「是否支持」的唯一真相源是已加载的语言批（全局注册表已删）：批未就绪时退化为身份解析
        /// 直接放行，可用性由切换方在加载完成后校验（<c>ChangeLanguage</c> 有一次性告警）——
        /// 不再出现"数据没加载就把 zh-Hans 静默落成默认英语"的双源歧义。</remarks>
        public static Language ToLanguage(string str, bool onlySupported)
        {
            // 处理边界条件：str 为空或 null
            if (string.IsNullOrEmpty(str))
            {
                return defaultLanguage;
            }

            Language target = defaultLanguage;
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

            return handler.IsLanguageAvailable(target) ? target : defaultLanguage;
        }

        #region 编辑器预览 [EDITOR PREVIEW]

        private static LocalizationStore s_PreviewStore;
        // 缓存键 = 当时的编辑器语言；语言变了就重取
        private static string s_PreviewLanguageSetting;
        // 预览数据取不到时只认一次，Inspector 每帧重绘不能每帧刷一条 Error
        private static bool s_PreviewFailed;

        /// <summary>
        /// 丢弃编辑器预览缓存。改了编辑器语言、或重新转表之后调用。
        /// </summary>
        public static void InvalidateEditorPreview()
        {
            s_PreviewStore = null;
            s_PreviewLanguageSetting = null;
            s_PreviewFailed = false;
        }

        /// <summary>编辑器预览是否已就绪（非播放态、且表数据取到了）。</summary>
        public static bool IsEditorPreviewAvailable => GetEditorPreviewStore() != null;

        /// <summary>
        /// 编辑器预览用的语言：Inspector 里设的编辑器语言优先，未设或该语言不在表内时取表内的英语列，再退到首列。
        /// </summary>
        public static Language EditorPreviewLanguage
        {
            get
            {
                var store = GetEditorPreviewStore();
                var index = GetPreviewLanguageIndex(store);
                return index < 0 ? defaultLanguage : store.Batch.Languages[index];
            }
        }

        /// <summary>编辑器预览：ID 是否存在于表内。</summary>
        public static bool EditorPreviewHasText(string id)
        {
            var store = GetEditorPreviewStore();
            return store != null && store.HasKey(id);
        }

        /// <summary>
        /// 编辑器预览解析：非播放态直读配置表出译文，取不到时原样返回 ID。
        /// </summary>
        /// <remarks>预览刻意<strong>不</strong>套用回退链：某格缺译时编辑器里直接露出 ID，
        /// 正是策划要看见的信息（运行期仍按回退链兜底，两者语义不同是有意为之）。</remarks>
        public static string ResolveForEditorPreview(string id)
        {
            var store = GetEditorPreviewStore();
            if (store == null || string.IsNullOrEmpty(id)) return id;

            var index = GetPreviewLanguageIndex(store);
            var text = store.Resolve(id, index < 0 ? null : store.Batch.Languages[index], index, null, null);
            return text ?? id;
        }

        /// <summary>编辑器预览用的语言列下标（预览不可用时为 -1）。</summary>
        public static int EditorPreviewLanguageIndex => GetPreviewLanguageIndex(GetEditorPreviewStore());

        private static int GetPreviewLanguageIndex(LocalizationStore store)
        {
            if (store == null) return -1;

            var languages = store.Batch.Languages;
#if UNITY_EDITOR
            if (TryGetBuiltInLanguage(LocalizationServiceSettings.EditorLanguage, out var preferred))
            {
                var preferredIndex = store.IndexOf(preferred);
                if (preferredIndex >= 0) return preferredIndex;
            }
#endif
            var fallbackIndex = store.IndexOf(defaultLanguage);
            return fallbackIndex >= 0 ? fallbackIndex : (languages.Length > 0 ? 0 : -1);
        }

        /// <summary>
        /// 取（并按需重建）编辑器预览用的存储。
        /// </summary>
        /// <remarks>播放态恒返回 <c>null</c>：预览只服务非播放态的 Inspector/Scene，运行期只允许一条数据路径。</remarks>
        private static LocalizationStore GetEditorPreviewStore()
        {
#if UNITY_EDITOR
            if (Application.isPlaying) return null;

            var languageSetting = LocalizationServiceSettings.EditorLanguage;
            if (s_PreviewLanguageSetting != languageSetting)
            {
                s_PreviewStore = null;
                s_PreviewFailed = false;
                s_PreviewLanguageSetting = languageSetting;
            }

            if (s_PreviewStore != null || s_PreviewFailed) return s_PreviewStore;

            try
            {
                var strings = ConfigTableService.GetLocalizedStringsForEditorPreview();
                var codes = ConfigTableService.GetLocalizationLanguageCodesForEditorPreview();
                if (strings == null || strings.Count == 0)
                {
                    s_PreviewFailed = true;
                    LogUtility.Warning("Localization preview unavailable: generate config table first.");
                    return null;
                }

                // 语言必须随表自报：未自报即预览不可用，不再回落任何全局注册表
                if (codes == null || codes.Count == 0)
                {
                    s_PreviewFailed = true;
                    LogUtility.Warning("Localization preview unavailable: the table does not self-report its languages.");
                    return null;
                }

                var languages = ResolveLanguages(codes);
                if (languages.Count == 0)
                {
                    s_PreviewFailed = true;
                    LogUtility.Error("Localization preview unavailable: table language codes are all empty.");
                    return null;
                }

                var store = new LocalizationStore();
                if (!store.TryApply(new LocalizationTextBatch(languages.ToArray(), strings, "editor-preview"), out var rejectedKey))
                {
                    s_PreviewFailed = true;
                    LogUtility.Error("Localization preview unavailable: entry '{0}' column count mismatches {1} languages.",
                        rejectedKey, languages.Count);
                    return null;
                }

                s_PreviewStore = store;
                return store;
            }
            catch (Exception ex)
            {
                // 预览面在 Inspector 的重绘路径上，任何一次抛出都会打断编辑器；失败即静默降级为显示 ID
                s_PreviewFailed = true;
                LogUtility.Error(ex);
                return null;
            }
#else
            return null;
#endif
        }

        #endregion
    }
}