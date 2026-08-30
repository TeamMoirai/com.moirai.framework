using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    // ReSharper disable once InconsistentNaming
    [FrameworkSetting("[服务]输入设置", "输入管理器类型选择", -460)]
    public sealed class InputServiceSettings : FrameworkSettings<InputServiceSettings>
    {
        [InfoBox("使用时确保使用「动作名称」与配置的动作相匹配。\n" +
                 "可以使用 InputActionsConfiguration 来桥接配置，避免陷入修改黑洞。", InfoMessageType.None)]

        [ProviderDropdown]
        [SerializeReference] private InputServiceHandlerConfig m_HandlerConfig = new UnityInputSystemHandlerConfig();
        public static InputServiceHandlerConfig InputServiceHandlerConfig => Instance.m_HandlerConfig;

        private void Reset()
        {
            m_HandlerConfig =
#if ENABLE_INPUT_SYSTEM
                new UnityInputSystemHandlerConfig()
#elif ENABLE_LEGACY_INPUT_MANAGER
                new UnityInputManagerHandlerConfig()
#endif
                ;
        }
    }
}
