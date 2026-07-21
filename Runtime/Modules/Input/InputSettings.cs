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
                 "请确保这些元素的“动作名称”与想要触发的角色动作相匹配。", InfoMessageType.None)]

        [HelperDropdown(typeof(IInputHandler), "Input Handler")]
        [SerializeField] private string m_InputHandlerTypeName;

        private static IInputHandler s_InputHandler = null;
        /// <summary>
        /// 获取/设置当前的输入处理器组件。
        /// </summary>
        public static IInputHandler InputHandler
        {
            get
            {
                if (s_InputHandler != null) return s_InputHandler;

                // 初始化
                s_InputHandler = ResolveTypeOption<IInputHandler>(Instance.m_InputHandlerTypeName);
                s_InputHandler.OnInit();

                return s_InputHandler;
            }
            set
            {
                s_InputHandler = value;
                s_InputHandler?.OnInit();
            }
        }

        protected internal override void Reset()
        {
#if ENABLE_INPUT_SYSTEM
            m_InputHandlerTypeName = typeof(UnityInputSystemHandler).FullName;
#elif ENABLE_LEGACY_INPUT_MANAGER
            m_InputHandlerTypeName = typeof(UnityInputManagerHandler).FullName;
#endif
        }
    }
}