using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    [FrameworkSetting("[服务]UI设置", "UI窗口管理后端配置", -470)]
    public sealed partial class UIServiceSettings : FrameworkSettings<UIServiceSettings>
    {
        [InfoBox("默认使用内置 UI 后端。可替换为自定义 UI 后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private UIServiceHandler m_UIServiceHandler = new UGUIHandler();

        /// <summary>UI处理器（后端）。</summary>
        public static UIServiceHandler UIServiceHandler => Instance.m_UIServiceHandler;
    }
}
