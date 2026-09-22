using System.Collections.Generic;
using System;
using Moirai.Atropos.ConfigTable;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 配置表数据源本地化处理器（默认实现）。
    /// <para>从 <see cref="ConfigTableService"/> 加载的多语言配置表获取语言列表与字符串字典。</para>
    /// </summary>
    [Serializable]
    internal class ConfigTableLocalizationHandler : LocalizationServiceHandler
    {
        /// <summary>
        /// 从配置表加载一批词条：语言由表自报，列序即自报顺序。
        /// </summary>
        internal override LocalizationTextBatch LoadLocalizedTextBatch()
        {
            var strings = ConfigTableService.GetAllLocalizedStrings();
            var languages = ResolveLanguages(ConfigTableService.GetLocalizationLanguageCodes());
            return new LocalizationTextBatch(languages, strings, "config-table");
        }

        /// <summary>
        /// 优先采用配置表自报的语言；自报缺失或全部认不出（例如项目自定义语言不在内置表里）时
        /// 回落到全局语言注册表，保持存量处理器的行为。
        /// </summary>
        private static List<Language> ResolveLanguages(IReadOnlyList<string> codes)
        {
            if (codes == null || codes.Count == 0) return LocalizationService.GetAllAvailableLanguages();

            var languages = new List<Language>(codes.Count);
            for (var i = 0; i < codes.Count; i++)
            {
                if (LocalizationService.TryGetBuiltInLanguage(codes[i], out var language) && !languages.Contains(language))
                {
                    languages.Add(language);
                }
            }

            // 认不出的 Code 不静默丢弃后凑出个短列——交给处理器的列数校验，让它在加载期就响
            return languages.Count > 0 ? languages : LocalizationService.GetAllAvailableLanguages();
        }
    }
}