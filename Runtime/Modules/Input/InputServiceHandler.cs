using System;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入处理器抽象基类（策略模式抽象策略，纯契约）。
    /// <para>状态组合语义（Enabled/LockPlayerController/PreventInteractionUI/UIModal）由实现类经
    /// <see cref="InputStateMachine"/> 组合持有——基类不含任何状态字段。</para>
    /// </summary>
    [Serializable]
    public abstract class InputServiceHandler : FrameworkHandler
    {
        #region 状态契约 [STATE CONTRACT]

        /// <summary>
        /// 获取或设置是否启用输入。
        /// </summary>
        public abstract bool Enabled { get; set; }

        /// <summary>
        /// 获取或设置是否锁定玩家控制器。
        /// </summary>
        public abstract bool LockPlayerController { get; set; }

        /// <summary>
        /// 获取或设置是否禁止 UI 交互。
        /// </summary>
        public abstract bool PreventInteractionUI { get; set; }

        /// <summary>
        /// 设置 UI 模态状态。由 <see cref="InputService"/> 的事件回调驱动。
        /// </summary>
        internal abstract void SetUIModal(bool hasModal);

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
