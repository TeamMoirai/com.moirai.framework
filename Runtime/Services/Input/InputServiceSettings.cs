using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    // ReSharper disable once InconsistentNaming
    [FrameworkSetting("[服务]输入设置", "输入管理器类型选择", -460)]
    public sealed partial class InputServiceSettings : FrameworkSettings<InputServiceSettings>
    {
        [InfoBox("使用时确保使用「动作名称」与配置的动作相匹配。\n" +
                 "可以使用 InputActionsConfiguration 来桥接配置，避免陷入修改黑洞。", InfoMessageType.None)]

        // 内联默认实现兜底——资产绕过 Reset 创建或编译符号变更后不为 null（外观降级契约的前置保证）
        [ProviderDropdown]
        [SerializeReference] private InputServiceHandler m_InputServiceHandler =
#if ENABLE_INPUT_SYSTEM
            new UnityInputSystemHandler();
#elif ENABLE_LEGACY_INPUT_MANAGER
            new UnityInputManagerHandler();
#else
            null;
#endif

        /// <summary>
        /// 当前输入处理器实例。
        /// </summary>
        public static InputServiceHandler InputServiceHandler => Instance.m_InputServiceHandler;

        private void Reset()
        {
            m_InputServiceHandler =
#if ENABLE_INPUT_SYSTEM
                new UnityInputSystemHandler()
#elif ENABLE_LEGACY_INPUT_MANAGER
                new UnityInputManagerHandler()
#endif
                ;
        }
    }
}
