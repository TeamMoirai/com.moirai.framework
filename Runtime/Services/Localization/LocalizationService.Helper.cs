using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        
        // 已加载的语言
        private static readonly HashSet<Language> s_LoadedLanguage = new HashSet<Language>();

        // 所有内置语言（Name / Code，忽略大小写直接命中，省掉每次查询的 ToLower 分配）
        private static readonly Dictionary<string, Language> s_AllBuildInLanguageMap =
            Language.BuiltinLanguages.ToDictionary(_ => _.Name, _ => _, StringComparer.OrdinalIgnoreCase);
        // 所有内置语言代码
        private static readonly Dictionary<string, Language> s_AllBuildInLanguageCodeMap =
            Language.BuiltinLanguages.ToDictionary(_ => _.Code, _ => _, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 复位一次性日志闸门。
        /// </summary>
        /// <remarks>
        /// <para>刻意不清 <see cref="s_LoadedLanguage"/>：可用语言是数据源解析词条时的副作用
        /// （见 <c>LubanHandler.ResolveLocalization</c>），而词条字典在配置表侧是缓存且从不失效的。
        /// 关服时清掉注册表，重开局就不会再有任何地方重新注册语言，结果是整套本地化静默失效。</para>
        /// <para>只复位日志闸门：编辑器关闭域重载时 <c>static</c> 跨会话存活，
        /// 否则"未初始化"这类一次性警告在第二次会话里彻底哑火。</para>
        /// </remarks>
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
            // todo 编辑器预览
            if (!Application.isPlaying) return format;

            if (string.IsNullOrEmpty(format)) return format;

            if (!IsValid)
            {
                if (!s_HasLoggedWarning) LogUtility.Warning("{0} not initialized!", nameof(LocalizationService));
                s_HasLoggedWarning = true;
                return format;
            }

            var matches = s_LocalizedRegex.Matches(format);
            if (matches.Count == 0) return format;

            foreach (Match match in matches)
            {
                string textId = match.Groups[2].Value.Trim(); // LocalizedRegex 的第二个捕获组专门用于匹配文本 ID。

                try
                {
                    if (!Has(textId))
                    {
                        if (Application.isPlaying) LogUtility.Warning("Text ID: {0}({1}) not available.", textId, match.Groups[1].Value);
                        continue;
                    }

                    string replacement = GetTextFromId(textId);
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
        /// 注册可用的多语言
        /// </summary>
        /// <param name="str"></param>
        public static void RegisterLanguageMap(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                str = defaultLanguage.Name;
            }

            var language = Language.Unspecified;
            if (s_AllBuildInLanguageMap.TryGetValue(str, out var foundByName))
            {
                language = foundByName;
            }
            else if (s_AllBuildInLanguageCodeMap.TryGetValue(str, out var foundByCode))
            {
                language = foundByCode;
            }
            
            if (language != Language.Unspecified && s_LoadedLanguage.Add(language))
            {
                LogUtility.Info("Registered language[{0}]: {1}",s_LoadedLanguage.Count , language);
            }
        }

        /// <summary>
        /// 获取所用可用的多语言
        /// </summary>
        /// <returns></returns>
        public static List<Language> GetAllAvailableLanguages() => s_LoadedLanguage.ToList();
        
        /// <summary>
        /// 根据 名称/Code 获取语言。
        /// </summary>
        /// <param name="str"></param>
        /// <param name="onlySupported">是否只获取支持的语言，<c>false</c>表示仅根据设置获取语言，不关心本地化是否支持</param>
        /// <returns>无法识别或未收录时为 <see cref="defaultLanguage"/></returns>
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
            
            return s_LoadedLanguage.Contains(target) ? target : defaultLanguage;
        }
    }
}