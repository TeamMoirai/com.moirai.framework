using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    // ReSharper disable once InconsistentNaming
    [FrameworkSetting("输入设置", "输入管理器类型选择", -461)]
    public sealed class InputSettings : FrameworkSettings<InputSettings>
    {
        [InfoBox("输入管理器：Unity 的旧版输入管理器\n" +
                 "UI 移动端：它使用场景中的特定 UI 元素（InputButton 和 InputAxes 组件）作为输入。\n\n" +
                 "请确保这些元素的「动作名称」与想要触发的角色动作相匹配。", InfoMessageType.None)]

        [ProviderDropdown]
        [SerializeReference] private InputHandler m_InputHandler = CreateDefaultHandler();

        private static InputHandler s_InputHandler = null;
        /// <summary>
        /// 获取/设置当前的输入处理器组件。
        /// </summary>
        public static InputHandler InputHandler
        {
            get
            {
                if (s_InputHandler != null) return s_InputHandler;

                s_InputHandler = Instance.m_InputHandler;
                s_InputHandler.Internal_Init();

                return s_InputHandler;
            }
            set
            {
                s_InputHandler?.Internal_Shutdown();
                s_InputHandler = value;
                s_InputHandler?.Internal_Init();
            }
        }

        protected internal override void Reset()
        {
            m_InputHandler = CreateDefaultHandler();
        }

        private static InputHandler CreateDefaultHandler()
        {
#if ENABLE_INPUT_SYSTEM
            return new UnityInputSystemHandler();
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new UnityInputManagerHandler();
#else
            return null;
#endif
        }
    }
}
