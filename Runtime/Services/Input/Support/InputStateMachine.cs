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

        // 上一次对外广播时的有效压制态——仅在实际变化时触发 SuppressionChanged
        private bool _lastPlayerSuppressed;
        private bool _lastUISuppressed;

        /// <summary>
        /// 进入压制态（禁用/锁定/禁 UI）时触发——由持有方接线到后端的输入重置。
        /// </summary>
        public event Action ResetRequested;

        /// <summary>
        /// 有效压制态（<see cref="IsPlayerInputSuppressed"/> / <see cref="IsUIInteractionSuppressed"/>）实际变化时触发——
        /// 由持有方接线到后端的上下文切换（如 Input System 的 Action Map 启用/禁用）。
        /// </summary>
        public event Action SuppressionChanged;

        /// <summary>
        /// 有效玩家输入压制（未启用 / 锁定玩家控制器 / UI 模态的并集）。
        /// <para>与 <see cref="LockPlayerController"/> 读取同值——压制语义的唯一权威出口。</para>
        /// </summary>
        public bool IsPlayerInputSuppressed => LockPlayerController;

        /// <summary>
        /// 有效 UI 交互压制（未启用 / 禁止 UI 交互的并集）。
        /// <para>与 <see cref="PreventInteractionUI"/> 读取同值——压制语义的唯一权威出口。</para>
        /// </summary>
        public bool IsUIInteractionSuppressed => PreventInteractionUI;

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
                NotifySuppressionChanged();
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

                NotifySuppressionChanged();
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

                NotifySuppressionChanged();
            }
        }

        /// <summary>
        /// 设置 UI 模态状态。由 <see cref="InputService"/> 的事件回调驱动。
        /// <para>进入模态与进入其他压制态语义对齐：触发一次输入重置，清掉残留的按住状态，
        /// 避免模态弹出瞬间 gameplay 输入泄漏；退出模态不重置（玩家可能正合法按住输入）。</para>
        /// </summary>
        public void SetUIModal(bool hasModal)
        {
            if (_hasUIModal == hasModal) return;

            _hasUIModal = hasModal;
            if (hasModal) ResetRequested?.Invoke();
            NotifySuppressionChanged();
        }

        /// <summary>
        /// 有效压制态变化检测与广播——任何底层状态变更后调用，仅在实际变化时触发事件。
        /// </summary>
        private void NotifySuppressionChanged()
        {
            bool playerSuppressed = IsPlayerInputSuppressed;
            bool uiSuppressed = IsUIInteractionSuppressed;

            if (playerSuppressed == _lastPlayerSuppressed && uiSuppressed == _lastUISuppressed) return;

            _lastPlayerSuppressed = playerSuppressed;
            _lastUISuppressed = uiSuppressed;
            SuppressionChanged?.Invoke();
        }
    }
}
