using System;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入处理器抽象基类（策略模式抽象策略）。
    /// <para>框架内置三种实现：</para>
    /// <list type="bullet">
    /// <item><description><see cref="UnityInputSystemHandler"/>：基于 Unity Input System（Package），需定义 ENABLE_INPUT_SYSTEM。</description></item>
    /// <item><description><see cref="UnityInputManagerHandler"/>：基于 Unity 旧版输入管理器，需定义 ENABLE_LEGACY_INPUT_MANAGER。</description></item>
    /// <item><description><see cref="UIMobileInputHandler"/>：基于场景 UI 元素（InputButton/InputAxes 组件），适用于移动端虚拟摇杆。</description></item>
    /// </list>
    /// </summary>
    [Serializable]
    public abstract class InputHandler : FrameworkHandler
    {
        #region 按钮 [BUTTON]

        /// <summary>
        /// 按钮是否被按下
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按下</returns>
        public abstract bool GetButtonDown(string actionName, string actionGroup);

        /// <summary>
        /// 按钮是否被松开
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否抬起</returns>
        public abstract bool GetButtonUp(string actionName, string actionGroup);

        /// <summary>
        /// 按钮是否被按住
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按住</returns>
        public virtual bool GetButtonPressed(string actionName, string actionGroup) => GetBool(actionName, actionGroup);

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
