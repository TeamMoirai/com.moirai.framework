using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    [FrameworkSetting("[服务]本地化设置", "多语言数据源配置", -450)]
    public sealed class LocalizationServiceSettings : FrameworkSettings<LocalizationServiceSettings>
    {
#if UNITY_EDITOR

        [DisableInPlayMode, PropertyOrder(-999)]
        [ValueDropdown(nameof(GetLanguageOptions))]
        [SerializeField] private string m_EditorLanguage = Language.Unspecified.Name;
        private static IEnumerable<string> GetLanguageOptions() => Language.BuiltinLanguages.Select(lang => lang.Name);

        /// <summary>
        /// 获取或设置编辑器语言（仅编辑器内有效）。
        /// </summary>
        public static string EditorLanguage
        {
            get => Instance.m_EditorLanguage;
            set
            {
                if (Instance.m_EditorLanguage == value) return;

                Instance.m_EditorLanguage = value;
                LocalizationService.ChangeLanguage(value);
            }
        }

#endif

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
