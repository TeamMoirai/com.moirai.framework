using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 鼠标按键枚举。
    /// </summary>
    public enum EMouseButton { Left = 0, Right = 1, Middle = 2 }

    /// <summary>
    /// 输入模块接口。提供按钮、轴值、鼠标等统一输入查询能力，屏蔽底层 Input System / Input Manager 差异。
    /// </summary>
    public interface IInputModule
    {
        /// <summary>是否启用输入</summary>
        bool Enabled { get; set; }

        /// <summary>是否锁定玩家控制器</summary>
        bool LockPlayerController { get; set; }

        /// <summary>是否禁止UI交互</summary>
        bool PreventInteractionUI { get; set; }

        #region 动作值 [ACTION VALUE]
        
        /// <summary>
        /// 按钮是否被按下
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按下</returns>
        bool GetButtonDown(string actionName, string actionGroup = "");
        
        /// <summary>
        /// 按钮是否被松开
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否抬起</returns>
        bool GetButtonUp(string actionName, string actionGroup = "");
        
        /// <summary>
        /// 按钮是否被按住
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按住</returns>
        bool GetButtonPressed(string actionName, string actionGroup = "");

        /// <summary>
        /// 按钮是否被按住
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按住</returns>
        public bool GetButton(string actionName, string actionGroup = "") => GetButtonPressed(actionName, actionGroup);
        
        /// <summary>
        /// 获取指定输入动作的 bool
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>按钮状态布尔值。</returns>
        bool GetBool(string actionName, string actionGroup = "");

        /// <summary>
        /// 获取指定输入动作的 float
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>轴值。</returns>
        float GetFloat(string actionName, string actionGroup = "");

        /// <summary>
        /// 获取指定输入动作的 Vector2
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>二维轴值。</returns>
        Vector2 GetVector2(string actionName, string actionGroup = "");

        #endregion

        #region 鼠标 [MOUSE]

        /// <summary>
        /// 按下鼠标按键
        /// </summary>
        /// <param name="button">鼠标按键。</param>
        /// <returns>是否在本帧按下。</returns>
        bool GetMouseButtonDown(EMouseButton button);

        /// <summary>
        /// 抬起鼠标按键
        /// </summary>
        /// <param name="button">鼠标按键。</param>
        /// <returns>是否在本帧抬起。</returns>
        bool GetMouseButtonUp(EMouseButton button);

        /// <summary>
        /// 按住鼠标按键
        /// </summary>
        /// <param name="button">鼠标按键。</param>
        /// <returns>是否正在按住。</returns>
        bool GetMouseButtonPressed(EMouseButton button);

        /// <summary>
        /// 返回鼠标的当前位置
        /// </summary>
        /// <returns>鼠标屏幕坐标。</returns>
        Vector2 GetMousePosition();

        /// <summary>
        /// 获取鼠标滚轮滚动值
        /// </summary>
        /// <returns>滚轮滚动增量。</returns>
        Vector2 GetScrollDelta();

        #endregion

    }
}