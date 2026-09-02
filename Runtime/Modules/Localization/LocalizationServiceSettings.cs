using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    [FrameworkSetting("[服务]本地化设置", "多语言数据源配置", -450)]
    public sealed class LocalizationServiceSettings : FrameworkSettings<LocalizationServiceSettings>
    {
        [InfoBox("默认使用配置表数据源。可替换为自定义数据源（如 JSON 文件、远程词库等）。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private LocalizationServiceHandler m_LocalizationServiceHandler = new ConfigTableLocalizationHandler();

        /// <summary>
        /// 本地化处理器实例（由 Inspector 序列化配置，可替换数据源策略）。
        /// </summary>
        public static LocalizationServiceHandler LocalizationServiceHandler => Instance.m_LocalizationServiceHandler;

        private void Reset()
        {
            m_LocalizationServiceHandler = new ConfigTableLocalizationHandler();
        }
    }
}
