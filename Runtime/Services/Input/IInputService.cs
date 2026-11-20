using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入服务契约（策略模式契约接口，纯契约，不含状态字段）：输入后端的唯一实现契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 状态组合语义（Enabled/LockPlayerController/PreventInteractionUI/UIModal）由实现类经 <see cref="InputStateMachine"/> 组合持有。 <br />
    /// 门控契约：<c>Enabled=false</c> 为全局硬门控，所有后端必须在动作类查询入口检查并降级。 <br />
    /// 玩家/UI 上下文压制的强制方式由后端能力决定（Input System 经 Action Map 切换中心强制，无 Map 概念的后端仅供消费者协作自查）。 <br />
    /// 鼠标查询不参与门控。实现约束：实现类须继承 <see cref="Moirai.Atropos.FrameworkHandler"/>（生命周期与序列化基座）。
    /// </remarks>
    public interface IInputService
    {
        #region 状态契约 [STATE CONTRACT]

        /// <summary>获取或设置是否启用输入。</summary>
        bool Enabled { get; set; }

        /// <summary>获取或设置是否锁定玩家控制器。</summary>
        bool LockPlayerController { get; set; }

        /// <summary>获取或设置是否禁止 UI 交互。</summary>
        bool PreventInteractionUI { get; set; }

        /// <summary>设置 UI 模态状态。由 <see cref="InputService"/> 的事件回调驱动。</summary>
        void SetUIModal(bool hasModal);

        #endregion

        #region 按钮 [BUTTON]

        /// <summary>按钮是否被按下。</summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空。</param>
        bool GetButtonDown(string actionName, string actionGroup = "");

        /// <summary>按钮是否被松开。</summary>
        bool GetButtonUp(string actionName, string actionGroup = "");

        #endregion

        #region 动作值 [ACTION VALUE]

        /// <summary>获取指定输入动作的 bool。</summary>
        bool GetBool(string actionName, string actionGroup = "");

        /// <summary>获取指定输入动作的 float。</summary>
        float GetFloat(string actionName, string actionGroup = "");

        /// <summary>获取指定输入动作的 Vector2。</summary>
        Vector2 GetVector2(string actionName, string actionGroup = "");

        #endregion

        #region 鼠标 [MOUSE]

        /// <summary>按下鼠标按键。</summary>
        bool GetMouseButtonDown(EMouseButton button);

        /// <summary>抬起鼠标按键。</summary>
        bool GetMouseButtonUp(EMouseButton button);

        /// <summary>按住鼠标按键。</summary>
        bool GetMouseButtonPressed(EMouseButton button);

        /// <summary>返回鼠标的当前位置。</summary>
        Vector2 GetMousePosition();

        /// <summary>获取鼠标滚轮滚动值。</summary>
        Vector2 GetScrollDelta();

        #endregion

        #region 通用 [COMMON]

        /// <summary>清除所有输入状态。</summary>
        void ResetAllInputStates();

        #endregion
    }
}
