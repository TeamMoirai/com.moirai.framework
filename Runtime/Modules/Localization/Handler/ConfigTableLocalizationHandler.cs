using System.Collections.Generic;
using System;
using Moirai.Atropos.ConfigTable;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 配置表数据源本地化配置（默认实现，从 <see cref="ConfigTableService"/> 加载多语言配置表）。
    /// </summary>
    [Serializable]
    public sealed class ConfigTableLocalizationHandlerConfig : LocalizationServiceHandlerConfig
    {
        /// <inheritdoc />
        public override LocalizationServiceHandler CreateHandler()
        {
            return new ConfigTableLocalizationHandler();
        }
    }

    /// <summary>
    /// 配置表数据源本地化处理器（默认实现）。
    /// <para>从 <see cref="ConfigTableService"/> 加载的多语言配置表获取语言列表与字符串字典。</para>
    /// <para>由 <see cref="ConfigTableLocalizationHandlerConfig"/> 工厂创建（普通运行时类，不参与序列化）。</para>
    /// </summary>
    public class ConfigTableLocalizationHandler : LocalizationServiceHandler
    {
        /// <summary>
        /// 从配置表加载本地化数据源。
        /// </summary>
        protected override (List<Language> languages, Dictionary<string, List<string>> strings) LoadLocalizedData()
        {
            // 必须先取多语言字符串：数据源（如 LubanHandler）在首次解析字符串时才注册可用语言，
            // 元组字面量从左到右求值，若先取语言列表会捕获到空集合。
            var strings = ConfigTableService.GetAllLocalizedStrings();
            return (LocalizationService.GetAllAvailableLanguages(), strings);
        }
    }
}
