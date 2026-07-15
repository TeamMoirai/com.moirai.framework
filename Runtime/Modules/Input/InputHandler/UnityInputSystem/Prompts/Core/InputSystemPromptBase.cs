#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

namespace Moirai.Atropos.Input.Prompts
{
    /// <summary>
    /// InputSystem 提示抽象类
    /// </summary>
    public abstract class InputSystemPromptBase : MonoBehaviour
    {
        /// <summary>
        /// 是否有效，用于检验必须的组件。
        /// </summary>
        protected abstract bool IsValid { get; }
        
        private void OnEnable()
        {
            if (!IsValid) return;
            
            RefreshPrompt();
            // 监听设备更改
            InputDevicePromptSystem.OnActiveDeviceChanged += DeviceChanged;
        }
        
        private void OnDisable()
        {
            if (!IsValid) return;
            
            // 取消事件监听
            InputDevicePromptSystem.OnActiveDeviceChanged -= DeviceChanged;
        }
        
        /// <summary>
        /// 刷新提示。
        /// </summary>
        protected abstract void RefreshPrompt();
        
        /// <summary>
        /// 当活动输入设备更改时调用
        /// </summary>
        /// <param name="device"></param>
        private void DeviceChanged(InputDevice device)
        {
            RefreshPrompt();
        }
    }
}
#endif