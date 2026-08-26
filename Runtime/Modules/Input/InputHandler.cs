using System;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入处理器抽象基类（策略模式抽象策略）。
    /// <para>同时承载输入状态管理（<see cref="Enabled"/>、<see cref="LockPlayerController"/>、<see cref="PreventInteractionUI"/>）。</para>
    /// </summary>
    [Serializable]
    public abstract class InputHandler : FrameworkHandler
    {
        #region 状态管理 [STATE MANAGEMENT]

        [Flags]
        private enum EInputStateFlags
        {
            None = 0,
            LockPlayerController = 1,
            PreventInteractionUI = 2,
        }

        [NonSerialized] private EInputStateFlags _stateFlags;
        [NonSerialized] private bool _hasUIModal;
        [NonSerialized] private bool _enabled = true;

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
                if (!_enabled) ResetAllInputStates();
            }
        }

        /// <summary>
        /// 获取或设置是否锁定玩家控制器。
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
                    ResetAllInputStates();
                }
                else
                {
                    _stateFlags &= ~EInputStateFlags.LockPlayerController;
                }
            }
        }

        /// <summary>
        /// 获取或设置是否禁止 UI 交互。
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
                    ResetAllInputStates();
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
        internal void SetUIModal(bool hasModal)
        {
            _hasUIModal = hasModal;
        }

        #endregion

        #region 按钮 [BUTTON]

        /// <summary>
        /// 按钮是否被按下
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按下</returns>
        public abstract bool GetButtonDown(string actionName, string actionGroup = "");

        /// <summary>
        /// 按钮是否被松开
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否抬起</returns>
        public abstract bool GetButtonUp(string actionName, string actionGroup = "");

        #endregion

        #region 动作值 [ACTION VALUE]

        /// <summary>
        /// 获取指定输入动作的 bool
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns></returns>
        public abstract bool GetBool(string actionName, string actionGroup = "");

        /// <summary>
        /// 获取指定输入动作的 float
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns></returns>
        public abstract float GetFloat(string actionName, string actionGroup = "");

        /// <summary>
        /// 获取指定输入动作的 Vector2
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns></returns>
        public abstract Vector2 GetVector2(string actionName, string actionGroup = "");

        #endregion

        #region 鼠标 [MOUSE]

        /// <summary>
        /// 按下鼠标按键
        /// </summary>
        /// <returns></returns>
        public abstract bool GetMouseButtonDown(EMouseButton button);

        /// <summary>
        /// 抬起鼠标按键
        /// </summary>
        /// <returns></returns>
        public abstract bool GetMouseButtonUp(EMouseButton button);

        /// <summary>
        /// 按住鼠标按键
        /// </summary>
        /// <returns></returns>
        public abstract bool GetMouseButtonPressed(EMouseButton button);

        /// <summary>
        /// 返回鼠标的当前位置
        /// </summary>
        /// <returns></returns>
        public abstract Vector2 GetMousePosition();

        /// <summary>
        /// 获取鼠标滚轮滚动值
        /// </summary>
        /// <returns></returns>
        public abstract Vector2 GetScrollDelta();

        #endregion

        #region 通用 [COMMON]

        /// <summary>
        /// 清除所有输入状态
        /// </summary>
        public abstract void ResetAllInputStates();

        #endregion
    }
}
