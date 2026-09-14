using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 基于场景 UI 元素（InputButton/InputAxes 组件）的虚拟输入处理器，适用于移动端。
    /// <para>虚拟组件经 <see cref="UIMobileInputRegistry"/> 自注册（OnEnable/OnDisable），
    /// 延迟实例化与销毁均自动同步，本处理器不做场景扫描。</para>
    /// <para>GetButtonDown/GetButtonUp 读取组件上的帧闩锁边沿（指针事件写入，与查询次数无关，
    /// 同帧点按不丢边，语义与 Input System 的 WasPressedThisFrame 对齐）。</para>
    /// <remarks>
    /// 虚拟输入按组件的 ActionName 寻址：查询时优先解析 "Group/Name"（兼容始终传分组的生成配置类），
    /// 未命中回退平铺名称。鼠标类查询在此后端无意义，按降级契约返回默认值。
    /// </remarks>
    /// </summary>
    [Serializable]
    public sealed class UIMobileInputHandler : InputServiceHandler
    {
        // 状态组合语义（Enabled/Lock/PreventUI/UIModal）——组合持有，压制态自动清零虚拟按键
        private readonly InputStateMachine _state = new InputStateMachine();

        public override bool Enabled
        {
            get => _state.Enabled;
            set => _state.Enabled = value;
        }

        public override bool LockPlayerController
        {
            get => _state.LockPlayerController;
            set => _state.LockPlayerController = value;
        }

        public override bool PreventInteractionUI
        {
            get => _state.PreventInteractionUI;
            set => _state.PreventInteractionUI = value;
        }

        internal override void SetUIModal(bool hasModal)
        {
            _state.SetUIModal(hasModal);
        }

        protected override void OnInit()
        {
            _state.ResetRequested += ResetAllInputStates;
        }

        protected override void OnShutdown()
        {
            _state.ResetRequested -= ResetAllInputStates;
        }

        public override bool GetButtonDown(string actionName, string actionGroup= "")
        {
            return TryResolveButton(actionName, actionGroup, out InputButton button) && button.WasPressedThisFrame;
        }

        public override bool GetButtonUp(string actionName, string actionGroup= "")
        {
            return TryResolveButton(actionName, actionGroup, out InputButton button) && button.WasReleasedThisFrame;
        }

        public override bool GetBool(string actionName, string actionGroup = "")
        {
            return TryResolveButton(actionName, actionGroup, out InputButton inputButton) && inputButton.BoolValue;
        }

        // IUIFloatAction 尚无对应虚拟组件（节流杆/力度按钮未实现），按降级契约返回默认值
        public override float GetFloat(string actionName, string actionGroup = "")
        {
            return 0f;
        }

        public override Vector2 GetVector2(string actionName, string actionGroup = "")
        {
            if (!string.IsNullOrEmpty(actionGroup) &&
                UIMobileInputRegistry.TryGetAxes(string.Concat(actionGroup, "/", actionName), out InputAxes combined))
            {
                return combined.Vector2Value;
            }

            return UIMobileInputRegistry.TryGetAxes(actionName, out InputAxes flat)
                ? flat.Vector2Value
                : Vector2.zero;
        }

        public override bool GetMouseButtonDown(EMouseButton button)
        {
            return false;
        }

        public override bool GetMouseButtonUp(EMouseButton button)
        {
            return false;
        }

        public override bool GetMouseButtonPressed(EMouseButton button)
        {
            return false;
        }

        public override Vector2 GetMousePosition()
        {
            return Vector2.zero;
        }

        public override Vector2 GetScrollDelta()
        {
            return Vector2.zero;
        }

        public override void ResetAllInputStates()
        {
            foreach (var inputButton in UIMobileInputRegistry.Buttons)
            {
                // 防御：异常路径下注册表可能残留已销毁引用
                if (inputButton == null) continue;
                inputButton.ResetState();
            }

            foreach (var inputAxes in UIMobileInputRegistry.Axes)
            {
                if (inputAxes == null) continue;
                inputAxes.Vector2Value = Vector2.zero;
            }
        }

        /// <summary>
        /// 解析虚拟按钮：优先按 "Group/Name"（与新输入后端一致，兼容始终传分组的生成配置类），
        /// 未命中时回退组件上平铺的 ActionName。
        /// </summary>
        private static bool TryResolveButton(string actionName, string actionGroup, out InputButton button)
        {
            if (!string.IsNullOrEmpty(actionGroup) &&
                UIMobileInputRegistry.TryGetButton(string.Concat(actionGroup, "/", actionName), out button))
            {
                return true;
            }

            return UIMobileInputRegistry.TryGetButton(actionName, out button);
        }
    }
}
