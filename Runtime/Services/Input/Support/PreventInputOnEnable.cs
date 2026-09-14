using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 启用时，阻止用户输入。
    /// <remarks>
    /// 所有权语义：OnDisable 仅恢复本组件实际修改过的标志，未勾选的选项不会触碰全局状态。
    /// 多个本组件同时启用时仍会互相覆盖（全局布尔无引用计数），嵌套场景请避免叠加使用。
    /// </remarks>
    /// </summary>
    public sealed class PreventInputOnEnable : MonoBehaviour
    {
        [Tooltip("禁止角色控制器移动")]
        [SerializeField] private bool m_LockPlayerController = false;
        [Tooltip("禁止交互UI")]
        [SerializeField] private bool m_PreventInteractionUI = false;

        // 所有权标记：仅恢复自己实际修改过的标志，避免未启用的选项在 OnDisable 时把其他系统设置的值误清
        private bool _ownedLockPlayerController;
        private bool _ownedPreventInteractionUI;
        private bool _savedLockPlayerController;
        private bool _savedPreventInteractionUI;

        private void OnEnable()
        {
            if (m_LockPlayerController)
            {
                _savedLockPlayerController = InputService.LockPlayerController;
                InputService.LockPlayerController = true;
                _ownedLockPlayerController = true;
            }

            if (m_PreventInteractionUI)
            {
                _savedPreventInteractionUI = InputService.PreventInteractionUI;
                InputService.PreventInteractionUI = true;
                _ownedPreventInteractionUI = true;
            }
        }

        private void OnDisable()
        {
            if (_ownedLockPlayerController)
            {
                InputService.LockPlayerController = _savedLockPlayerController;
                _ownedLockPlayerController = false;
            }

            if (_ownedPreventInteractionUI)
            {
                InputService.PreventInteractionUI = _savedPreventInteractionUI;
                _ownedPreventInteractionUI = false;
            }
        }
    }
}
