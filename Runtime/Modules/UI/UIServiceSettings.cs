using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    [FrameworkSetting("[服务]UI设置", "UI窗口管理后端配置", -470)]
    public sealed partial class UIServiceSettings : FrameworkSettings<UIServiceSettings>
    {
        [InfoBox("默认使用内置 UI 后端。可替换为自定义 UI 后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private UIServiceHandlerConfig m_HandlerConfig = new UGUIHandlerConfig();

        /// <summary>UI 后端配置（纯数据，经 <see cref="UIServiceHandlerConfig.CreateHandler"/> 创建处理器实例）。</summary>
        public static UIServiceHandlerConfig UIServiceHandlerConfig => Instance.m_HandlerConfig;
    }
}
