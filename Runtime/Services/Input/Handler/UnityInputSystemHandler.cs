#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 基于 Unity Input System（Package），需定义 ENABLE_INPUT_SYSTEM。
    /// </summary>
    [Serializable]
    public sealed class UnityInputSystemHandler : InputServiceHandler
    {
        [Tooltip("留空使用 Edit > Project Settings > Input System Package 中的设置。")]
        [SerializeField] private InputActionAsset m_InputActions;

        public InputActionAsset InputActions => m_InputActions ?? InputSystem.actions;

        // 状态组合语义（Enabled/Lock/PreventUI/UIModal）——组合持有，压制态自动重置设备输入
        private readonly InputStateMachine _state = new InputStateMachine();

        // 缓存 InputAction 引用提升性能；值类型不匹配负缓存（动作+读取类型只探测/告警一次，之后直接降级）
        private readonly Dictionary<string, InputAction> _inputActionsDictionary = new Dictionary<string, InputAction>();
        private readonly HashSet<string> _typeMismatchKeys = new HashSet<string>();

        // 当前构建缓存所依据的动作资产引用——运行期资产被替换（Addressables 热更/设置变更）时重建缓存，使负缓存可恢复
        private InputActionAsset _boundActions;

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
            _inputActionsDictionary.Clear();
            _typeMismatchKeys.Clear();

            if (InputActions == null)
            {
                LogUtility.Error("Please set Input Actions in {0} or 'Project Settings -> Input System Package'", nameof(InputServiceSettings));
                return;
            }

            for (int i = 0; i < InputActions.actionMaps.Count; i++)
            {
                InputActionMap actionMap = InputActions.actionMaps[i];
                for (int j = 0; j < actionMap.actions.Count; j++)
                {
                    InputAction action = actionMap.actions[j];
                    _inputActionsDictionary.Add($"{actionMap.name}/{action.name}", action);
                }
            }

            _boundActions = InputActions;
        }

        protected override void OnShutdown()
        {
            _state.ResetRequested -= ResetAllInputStates;
            _inputActionsDictionary.Clear();
            _typeMismatchKeys.Clear();
            _boundActions = null;
        }

        public override bool GetButtonDown(string actionName, string actionGroup = "")
        {
            bool output = false;
            
            var action = GetInputAction(actionGroup, actionName);
            if (action != null) output = action.WasPressedThisFrame();
            
            return output;
        }

        public override bool GetButtonUp(string actionName, string actionGroup = "")
        {
            bool output = false;
            
            var action = GetInputAction(actionGroup, actionName);
            if (action != null) output = action.WasReleasedThisFrame();
            
            return output;
        }
        
        public override bool GetBool(string actionName, string actionGroup = "")
        {
            bool output = false;
            
            var action = GetInputAction(actionGroup, actionName);
            if (action != null) output = action.IsPressed();
            
            return output;
        }

        public override float GetFloat(string actionName, string actionGroup = "")
        {
            var action = GetInputAction(actionGroup, actionName);
            if (action == null) return 0f;

            string mismatchKey = BuildMismatchKey(actionGroup, actionName, "float");
            if (_typeMismatchKeys.Contains(mismatchKey)) return 0f;

            try
            {
                return action.ReadValue<float>();
            }
            catch (ArgumentException)
            {
                // 动作值类型与 float 不兼容（如配置为 Vector2）——负缓存后不再逐帧 throw
                MarkTypeMismatch(mismatchKey, action, "float");
                return 0f;
            }
        }

        public override Vector2 GetVector2(string actionName, string actionGroup = "")
        {
            var action = GetInputAction(actionGroup, actionName);
            if (action == null) return Vector2.zero;

            string mismatchKey = BuildMismatchKey(actionGroup, actionName, "Vector2");
            if (_typeMismatchKeys.Contains(mismatchKey)) return Vector2.zero;

            try
            {
                return action.ReadValue<Vector2>();
            }
            catch (ArgumentException)
            {
                MarkTypeMismatch(mismatchKey, action, "Vector2");
                return Vector2.zero;
            }
        }

        public override bool GetMouseButtonDown(EMouseButton button)
        {
            return GetMouseButtonControl(button)?.wasPressedThisFrame ?? false;
        }

        public override bool GetMouseButtonUp(EMouseButton button)
        {
            return GetMouseButtonControl(button)?.wasReleasedThisFrame ?? false;
        }

        public override bool GetMouseButtonPressed(EMouseButton button)
        {
            return GetMouseButtonControl(button)?.isPressed ?? false;
        }

        public override Vector2 GetMousePosition()
        {
            // Mouse.current 在无鼠标设备（掌机/部分主机/移动端）上为 null，按降级契约返回默认值
            var mouse = Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        }

        private static readonly Vector2 s_ScalingFactor = new Vector2(0.00833f, 0.00833f); // 1/120
        public override Vector2 GetScrollDelta()
        {
            var mouse = Mouse.current;
            if (mouse == null) return Vector2.zero;

            // 新输入系统的 scroll 返回的是 tick（刻度），每滚一格通常是 120
            // 除以 120 是为了与旧系统值范围相似
            return Vector2.Scale(mouse.scroll.ReadValue(), s_ScalingFactor);
        }

        /// <summary>
        /// 获取指定鼠标按键的控件；无鼠标设备时返回 null（由调用方按降级契约兜底）。
        /// </summary>
        private static ButtonControl GetMouseButtonControl(EMouseButton button)
        {
            var mouse = Mouse.current;
            if (mouse == null) return null;

            switch (button)
            {
                case EMouseButton.Middle:
                    return mouse.middleButton;
                case EMouseButton.Right:
                    return mouse.rightButton;
                default:
                    return mouse.leftButton;
            }
        }

        public override void ResetAllInputStates()
        {
            InputSystem.FlushDisconnectedDevices();
            foreach (var device in InputSystem.devices)
            {
                if (device.added) InputSystem.ResetDevice(device);
            }
        }

        private InputAction GetInputAction(string actionGroup, string actionName)
        {
            string fullActionName = string.IsNullOrEmpty(actionGroup) ? actionName : $"{actionGroup}/{actionName}";

            var asset = InputActions;
            if (asset == null)
            {
                // OnInit 已报错，此处按降级契约静默返回默认值
                return null;
            }

            // 资产引用变化（运行期替换/延迟赋值）时重建缓存，使此前的负缓存（未找到/类型不匹配）可恢复
            if (!ReferenceEquals(asset, _boundActions))
            {
                _inputActionsDictionary.Clear();
                _typeMismatchKeys.Clear();
                _boundActions = asset;
            }

            if (!_inputActionsDictionary.TryGetValue(fullActionName, out InputAction action))
            {
                action = asset.FindAction(fullActionName);
                if (action != null)
                {
                    action.Enable();
                    _inputActionsDictionary.Add(fullActionName, action);
                }
                else
                {
                    _inputActionsDictionary.Add(fullActionName, null);
                    LogUtility.Warning($"Action '{fullActionName}' not found! " +
                                     "Please check Input Action Asset configuration.");
                }
                // Debug.Log($"GetInputAction: {fullActionName} - {action}");
            }

            return action;
        }

        /// <summary>
        /// 类型不匹配负缓存键：使用全限定动作名，避免跨 Map 同名动作互相吞掉告警/降级。
        /// </summary>
        private static string BuildMismatchKey(string actionGroup, string actionName, string readType)
        {
            string fullActionName = string.IsNullOrEmpty(actionGroup) ? actionName : $"{actionGroup}/{actionName}";
            return $"{fullActionName}/{readType}";
        }

        /// <summary>
        /// 记录类型不匹配并告警一次（之后 GetFloat/GetVector2 直接返回默认值，不再触发异常）。
        /// </summary>
        private void MarkTypeMismatch(string mismatchKey, InputAction action, string readType)
        {
            if (!_typeMismatchKeys.Add(mismatchKey)) return;

            LogUtility.Warning($"Action '{mismatchKey}' value type is not compatible with {readType}. " +
                             $"Expected control type: '{action.expectedControlType}'. Returning default value.");
        }
    }
}
#endif