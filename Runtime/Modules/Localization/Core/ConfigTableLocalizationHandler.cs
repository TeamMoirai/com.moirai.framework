using System.Collections.Generic;
using Moirai.Atropos.ConfigTable;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 配置表数据源本地化处理器（默认实现）。
    /// <para>从 <see cref="ConfigMgr"/> 加载的多语言配置表获取语言列表与字符串字典。</para>
    /// </summary>
    [System.Serializable]
    public class ConfigTableLocalizationHandler : LocalizationHandler
    {
        /// <summary>
        /// 从配置表加载本地化数据源。
        /// </summary>
        protected internal override (List<Language> languages, Dictionary<string, List<string>> strings) LoadLocalizedData()
        {
            return (LocalizationService.GetAllAvailableLanguages(), ConfigMgr.GetAllLocalizedStrings());
        }
    }
}
