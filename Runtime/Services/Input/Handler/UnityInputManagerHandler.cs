#if ENABLE_LEGACY_INPUT_MANAGER
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 基于 Unity 旧版输入管理器，需定义 ENABLE_LEGACY_INPUT_MANAGER。
    /// </summary>
    /// <remarks>
    /// 此方案用于桌面游戏。动作名直接映射 Input Manager（Project Settings &gt; Input Manager）中注册的
    /// Axis/Button 名称，<paramref name="actionGroup"/> 参数仅为外观契约兼容，此实现忽略分组。
    /// <para>Vector2 动作遵循约定：读取同名 "X X"/"Y Y" 后缀轴（例如 "Move" 读取 "Move X" 与 "Move Y"）。</para>
    /// <para>门控能力：旧版 API 无动作分组概念，此后端仅支持 <c>Enabled=false</c> 全局硬门控（动作类查询
    /// 一律降级返回默认值）；玩家/UI 上下文压制（Action Map 切换）为 Input System 后端专属能力，
    /// 需要上下文隔离的项目请使用 Input System 后端。</para>
    /// </remarks>
    [Serializable]
    public sealed class UnityInputManagerHandler : InputServiceHandler
    {
        private struct Vector2Action
        {
            public string X;
            public string Y;

            public Vector2Action(string x, string y)
            {
                X = x;
                Y = y;
            }
        }

        // 状态组合语义（Enabled/Lock/PreventUI/UIModal）——组合持有，压制态自动重置输入轴
        private readonly InputStateMachine _state = new InputStateMachine();

        private readonly Dictionary<string, Vector2Action> _vector2Actions = new Dictionary<string, Vector2Action>();

        // 注册校验缓存：Input Manager 轴表是构建期静态数据，按调用种类（Button/Axis）惰性探测一次后终身缓存。
        // 探测借助旧版 Input API 对未注册名称抛 ArgumentException 的行为实现，不依赖编辑器 API；
        // 探测为只读操作（GetButtonDown/GetAxisRaw 无消费语义），不影响输入状态本身。
        private readonly Dictionary<string, bool> _registeredButtons = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _registeredAxes = new Dictionary<string, bool>();

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
            _vector2Actions.Clear();
            _registeredButtons.Clear();
            _registeredAxes.Clear();
        }

        public override bool GetButtonDown(string actionName, string actionGroup = "")
        {
            return _state.Enabled && IsRegisteredButton(actionName) && UnityEngine.Input.GetButtonDown(actionName);
        }

        public override bool GetButtonUp(string actionName, string actionGroup = "")
        {
            return _state.Enabled && IsRegisteredButton(actionName) && UnityEngine.Input.GetButtonUp(actionName);
        }

        public override bool GetBool(string actionName, string actionGroup = "")
        {
            return _state.Enabled && IsRegisteredButton(actionName) && UnityEngine.Input.GetButton(actionName);
        }

        public override float GetFloat(string actionName, string actionGroup = "")
        {
            return _state.Enabled && IsRegisteredAxis(actionName) ? UnityEngine.Input.GetAxisRaw(actionName) : 0f;
        }

        public override Vector2 GetVector2(string actionName, string actionGroup = "")
        {
            if (!_state.Enabled) return Vector2.zero;

            if (!_vector2Actions.TryGetValue(actionName, out Vector2Action vector2Action))
            {
                vector2Action = new Vector2Action(
                    string.Concat(actionName, " X"),
                    string.Concat(actionName, " Y")
                );

                _vector2Actions.Add(actionName, vector2Action);
            }

            float x = IsRegisteredAxis(vector2Action.X) ? UnityEngine.Input.GetAxisRaw(vector2Action.X) : 0f;
            float y = IsRegisteredAxis(vector2Action.Y) ? UnityEngine.Input.GetAxisRaw(vector2Action.Y) : 0f;

            return new Vector2(x, y);
        }

        public override bool GetMouseButtonPressed(EMouseButton button)
        {
            switch (button)
            {
                case EMouseButton.Right:
                    return UnityEngine.Input.GetMouseButton(1);
                case EMouseButton.Middle:
                    return UnityEngine.Input.GetMouseButton(2);
                default:
                    return UnityEngine.Input.GetMouseButton(0);
            }
        }

        public override bool GetMouseButtonDown(EMouseButton button)
        {
            switch (button)
            {
                case EMouseButton.Right:
                    return UnityEngine.Input.GetMouseButtonDown(1);
                case EMouseButton.Middle:
                    return UnityEngine.Input.GetMouseButtonDown(2);
                default:
                    return UnityEngine.Input.GetMouseButtonDown(0);
            }
        }

        public override bool GetMouseButtonUp(EMouseButton button)
        {
            switch (button)
            {
                case EMouseButton.Right:
                    return UnityEngine.Input.GetMouseButtonUp(1);
                case EMouseButton.Middle:
                    return UnityEngine.Input.GetMouseButtonUp(2);
                default:
                    return UnityEngine.Input.GetMouseButtonUp(0);
            }
        }

        public override Vector2 GetMousePosition()
        {
            return UnityEngine.Input.mousePosition;
        }

        public override Vector2 GetScrollDelta()
        {
            return UnityEngine.Input.mouseScrollDelta;
        }

        public override void ResetAllInputStates()
        {
            UnityEngine.Input.ResetInputAxes();
        }

        /// <summary>
        /// 校验动作名是否已在 Input Manager 中注册为 Button（未注册时惰性探测一次并缓存，告警只在探测失败时发一次）。
        /// </summary>
        private bool IsRegisteredButton(string actionName)
        {
            if (!_registeredButtons.TryGetValue(actionName, out bool registered))
            {
                try
                {
                    _ = UnityEngine.Input.GetButtonDown(actionName);
                    registered = true;
                }
                catch (ArgumentException)
                {
                    registered = false;
                    LogUtility.Warning(StringUtility.Format(
                        "[{0}] action not found! Please make sure this action is included in your input settings (button).", actionName));
                }

                _registeredButtons.Add(actionName, registered);
            }

            return registered;
        }

        /// <summary>
        /// 校验动作名是否已在 Input Manager 中注册为 Axis（未注册时惰性探测一次并缓存，告警只在探测失败时发一次）。
        /// </summary>
        private bool IsRegisteredAxis(string actionName)
        {
            if (!_registeredAxes.TryGetValue(actionName, out bool registered))
            {
                try
                {
                    _ = UnityEngine.Input.GetAxisRaw(actionName);
                    registered = true;
                }
                catch (ArgumentException)
                {
                    registered = false;
                    LogUtility.Warning(StringUtility.Format(
                        "[{0}] action not found! Please make sure this action is included in your input settings (axis).", actionName));
                }

                _registeredAxes.Add(actionName, registered);
            }

            return registered;
        }
    }
}
#endif
