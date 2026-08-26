using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    [FrameworkSetting("UI设置", "UI窗口管理后端配置", -486)]
    public sealed partial class UISettings : FrameworkSettings<UISettings>
    {
        [InfoBox("默认使用内置 UI 后端。可替换为自定义 UI 后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private UIHandler m_UIHandler = new UIHandler();

        /// <summary>UI处理器（后端）。</summary>
        public static UIHandler UIHandler => Instance.m_UIHandler;
    }
}
