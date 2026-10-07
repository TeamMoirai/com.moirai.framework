using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 基于场景 UI 元素（InputButton/InputAxes 组件）的虚拟输入处理器，适用于移动端。
    /// </summary>
    /// <remarks>
    /// 虚拟组件经 <see cref="UIMobileInputRegistry"/> 自注册（OnEnable/OnDisable），延迟实例化与销毁均自动同步，本处理器不做场景扫描。 <br />
    /// GetButtonDown/GetButtonUp 读取组件上的帧闩锁边沿，与查询次数无关，同帧点按不丢边。 <br />
    /// 虚拟输入按组件的 ActionName 寻址：优先解析 <c>Group/Name</c>，未命中回退平铺名称；鼠标类查询在此后端无意义，按降级契约返回默认值。
    /// <c>Enabled=false</c> 为全局硬门控（动作类查询一律降级）；玩家/UI 上下文压制为 Input System 后端专属能力。
    /// </remarks>
    [ProviderDisplay(title: "UI 虚拟输入", description: "场景 InputButton/InputAxes 组件自注册，适合移动端虚拟按键")]
    [Serializable]
    internal sealed class UIMobileInputHandler : InputServiceHandler
    {
        // 状态组合语义（Enabled/Lock/PreventUI/UIModal）——组合持有，压制态自动清零虚拟按键
        private readonly InputStateMachine _state = new InputStateMachine();

        // 解析缓存（值类型组合键，查询热路径零分配；命中已销毁引用时惰性失效并重解析。
        // 负结果按注册表版本缓存：版本未变时未命中的动作名直接短路（省去全名拼接与两次注册表查找），
        // 任何新注册都会令版本自增、负缓存失效——延迟实例化的虚拟组件注册后仍能命中）
        private readonly Dictionary<InputActionKey, InputButton> _resolvedButtons = new Dictionary<InputActionKey, InputButton>();
        private readonly Dictionary<InputActionKey, InputAxes> _resolvedAxes = new Dictionary<InputActionKey, InputAxes>();
        private readonly Dictionary<InputActionKey, int> _unresolvedButtons = new Dictionary<InputActionKey, int>();
        private readonly Dictionary<InputActionKey, int> _unresolvedAxes = new Dictionary<InputActionKey, int>();

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
            _state.onResetRequested += ResetAllInputStates;
        }

        protected override void OnShutdown()
        {
            _state.onResetRequested -= ResetAllInputStates;
            _resolvedButtons.Clear();
            _resolvedAxes.Clear();
            _unresolvedButtons.Clear();
            _unresolvedAxes.Clear();
        }

        public override bool GetButtonDown(string actionName, string actionGroup= "")
        {
            return _state.Enabled && TryResolveButton(actionName, actionGroup, out InputButton button) && button.WasPressedThisFrame;
        }

        public override bool GetButtonUp(string actionName, string actionGroup= "")
        {
            return _state.Enabled && TryResolveButton(actionName, actionGroup, out InputButton button) && button.WasReleasedThisFrame;
        }

        public override bool GetBool(string actionName, string actionGroup = "")
        {
            return _state.Enabled && TryResolveButton(actionName, actionGroup, out InputButton inputButton) && inputButton.BoolValue;
        }

        // IUIFloatAction 尚无对应虚拟组件（节流杆/力度按钮未实现），按降级契约返回默认值
        public override float GetFloat(string actionName, string actionGroup = "")
        {
            return 0f;
        }

        public override Vector2 GetVector2(string actionName, string actionGroup = "")
        {
            return _state.Enabled && TryResolveAxes(actionName, actionGroup, out InputAxes axes)
                ? axes.Vector2Value
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
        /// 解析虚拟按钮：优先按 <c>Group/Name</c>，未命中回退组件上平铺的 <c>ActionName</c>。
        /// </summary>
        /// <remarks>解析结果缓存；负结果按 <see cref="UIMobileInputRegistry.Version"/> 缓存，任何新注册自动失效。</remarks>
        private bool TryResolveButton(string actionName, string actionGroup, out InputButton button)
        {
            var key = new InputActionKey(actionGroup, actionName);
            if (_resolvedButtons.TryGetValue(key, out button))
            {
                // 防御：组件被销毁后缓存引用失效——惰性移除并重解析
                if (button != null) return true;
                _resolvedButtons.Remove(key);
            }

            // 负缓存命中：注册表自上次未命中以来没有新注册，直接短路
            int version = UIMobileInputRegistry.Version;
            if (_unresolvedButtons.TryGetValue(key, out int missVersion) && missVersion == version)
            {
                button = null;
                return false;
            }

            if ((key.HasGroup && UIMobileInputRegistry.TryGetButton(key.ToFullName(), out button)) ||
                UIMobileInputRegistry.TryGetButton(actionName, out button))
            {
                _unresolvedButtons.Remove(key);
                _resolvedButtons.Add(key, button);
                return true;
            }

            _unresolvedButtons[key] = version;
            button = null;
            return false;
        }

        /// <summary>
        /// 解析虚拟摇杆：规则与 <see cref="TryResolveButton"/> 一致。
        /// </summary>
        private bool TryResolveAxes(string actionName, string actionGroup, out InputAxes axes)
        {
            var key = new InputActionKey(actionGroup, actionName);
            if (_resolvedAxes.TryGetValue(key, out axes))
            {
                if (axes != null) return true;
                _resolvedAxes.Remove(key);
            }

            // 负缓存命中：注册表自上次未命中以来没有新注册，直接短路
            int version = UIMobileInputRegistry.Version;
            if (_unresolvedAxes.TryGetValue(key, out int missVersion) && missVersion == version)
            {
                axes = null;
                return false;
            }

            if ((key.HasGroup && UIMobileInputRegistry.TryGetAxes(key.ToFullName(), out axes)) ||
                UIMobileInputRegistry.TryGetAxes(actionName, out axes))
            {
                _unresolvedAxes.Remove(key);
                _resolvedAxes.Add(key, axes);
                return true;
            }

            _unresolvedAxes[key] = version;
            axes = null;
            return false;
        }
    }
}
