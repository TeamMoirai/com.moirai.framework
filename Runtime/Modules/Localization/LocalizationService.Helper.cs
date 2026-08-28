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

        // 所有内置语言
        private static readonly Dictionary<string, Language> s_AllBuildInLanguageMap = Language.BuiltinLanguages.ToDictionary(_ => _.Name.ToLower(), _ => _);
        // 所有内置语言代码
        private static readonly Dictionary<string, Language> s_AllBuildInLanguageCodeMap = Language.BuiltinLanguages.ToDictionary(_ => _.Code.ToLower(), _ => _);

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
                str = defaultLanguage.Name.ToLower();
            }

            str = str.ToLower();
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
        /// <returns></returns>
        public static Language ToLanguage(string str, bool onlySupported)
        {
            // 处理边界条件：str 为空或 null
            if (string.IsNullOrEmpty(str))
            {
                return defaultLanguage;
            }
            
            str = str.ToLower();
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