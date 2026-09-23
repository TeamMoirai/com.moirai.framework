using System.Collections.Generic;
using System;
using Moirai.Atropos.ConfigTable;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 配置表数据源本地化处理器（默认实现）。
    /// <para>从 <see cref="ConfigTableService"/> 加载的多语言配置表获取语言代码与字符串字典。</para>
    /// <para><b>语言必须随表自报</b>：<c>GetLocalizationLanguageCodes</c> 返回空时本处理器产出空批，
    /// 由基类以「数据未就绪」拒载并保持重试——拒绝回落任何全局注册表，语言列序只有一个真相源。</para>
    /// </summary>
    [Serializable]
    internal class ConfigTableLocalizationHandler : LocalizationServiceHandler
    {
        /// <summary>
        /// 从配置表加载一批词条：语言由表自报，列序即自报顺序（经外观共享的 <c>ResolveLanguages</c> 解析，
        /// 与编辑器预览同一条语言解析路径）。
        /// </summary>
        internal override LocalizationTextBatch LoadLocalizedTextBatch()
        {
            var strings = ConfigTableService.GetAllLocalizedStrings();
            var languages = LocalizationService.ResolveLanguages(ConfigTableService.GetLocalizationLanguageCodes());
            return new LocalizationTextBatch(languages, strings, "config-table");
        }
    }
}
