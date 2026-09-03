using System;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入状态机（Enabled / LockPlayerController / PreventInteractionUI / UIModal 的组合语义）。
    /// <para>独立于后端的可测单元——状态组合与"进入压制态时请求重置输入"的副作用在此收敛；
    /// 后端 Handler 经组合持有（每实现类一份），抽象基类保持纯契约。</para>
    /// <para>线程契约：仅主线程。</para>
    /// </summary>
    internal sealed class InputStateMachine
    {
        [Flags]
        private enum EInputStateFlags
        {
            None = 0,
            LockPlayerController = 1,
            PreventInteractionUI = 2,
        }

        private EInputStateFlags _stateFlags;
        private bool _hasUIModal;
        private bool _enabled = true;

        /// <summary>
        /// 进入压制态（禁用/锁定/禁 UI）时触发——由持有方接线到后端的输入重置。
        /// </summary>
        public event Action ResetRequested;

        /// <summary>
        /// 获取或设置是否启用输入。
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (!_enabled) ResetRequested?.Invoke();
            }
        }

        /// <summary>
        /// 获取或设置是否锁定玩家控制器（未启用或有 UI 模态时强制为 true）。
        /// </summary>
        public bool LockPlayerController
        {
            get => !_enabled || _stateFlags.HasFlag(EInputStateFlags.LockPlayerController) || _hasUIModal;
            set
            {
                if (_stateFlags.HasFlag(EInputStateFlags.LockPlayerController) == value) return;
                if (value)
                {
                    _stateFlags |= EInputStateFlags.LockPlayerController;
                    ResetRequested?.Invoke();
                }
                else
                {
                    _stateFlags &= ~EInputStateFlags.LockPlayerController;
                }
            }
        }

        /// <summary>
        /// 获取或设置是否禁止 UI 交互（未启用时强制为 true）。
        /// </summary>
        public bool PreventInteractionUI
        {
            get => !_enabled || _stateFlags.HasFlag(EInputStateFlags.PreventInteractionUI);
            set
            {
                if (_stateFlags.HasFlag(EInputStateFlags.PreventInteractionUI) == value) return;
                if (value)
                {
                    _stateFlags |= EInputStateFlags.PreventInteractionUI;
                    ResetRequested?.Invoke();
                }
                else
                {
                    _stateFlags &= ~EInputStateFlags.PreventInteractionUI;
                }
            }
        }

        /// <summary>
        /// 设置 UI 模态状态。由 <see cref="InputService"/> 的事件回调驱动。
        /// </summary>
        public void SetUIModal(bool hasModal)
        {
            _hasUIModal = hasModal;
        }
    }
}
