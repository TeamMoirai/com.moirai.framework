#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 基于 Unity Input System（Package），需定义 ENABLE_INPUT_SYSTEM。
    /// </summary>
    [Serializable]
    public sealed class UnityInputSystemHandler : InputHandler
    {
        [Tooltip("留空使用 Edit > Project Settings > Input System Package 中的设置。")]
        [SerializeField] private InputActionAsset m_InputActions;

        public InputActionAsset InputActions => m_InputActions ?? InputSystem.actions;

        // 缓存 InputAction 引用提升性能
        private readonly Dictionary<string, InputAction> _inputActionsDictionary = new Dictionary<string, InputAction>();

        public override bool GetButtonDown(string actionName, string actionGroup)
        {
            bool output = false;
            
            var action = GetInputAction(actionGroup, actionName);
            if (action != null) output = action.WasPressedThisFrame();
            
            return output;
        }

        public override bool GetButtonUp(string actionName, string actionGroup)
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
            float output = 0f;
            
            var action = GetInputAction(actionGroup, actionName);
            if (action != null) output = action.ReadValue<float>();
            
            return output;
        }

        public override Vector2 GetVector2(string actionName, string actionGroup = "")
        {
            Vector2 output = Vector2.zero;
            
            var action = GetInputAction(actionGroup, actionName);
            if (action != null) output = action.ReadValue<Vector2>();
            
            return output;
        }

        public override bool GetMouseButtonDown(EMouseButton button)
        {
            switch (button)
            {
                case EMouseButton.Middle:
                    return Mouse.current.middleButton.wasPressedThisFrame;
                case EMouseButton.Right:
                    return Mouse.current.rightButton.wasPressedThisFrame;
                default:
                    return Mouse.current.leftButton.wasPressedThisFrame;
            }
        }

        public override bool GetMouseButtonUp(EMouseButton button)
        {
            switch (button)
            {
                case EMouseButton.Middle:
                    return Mouse.current.middleButton.wasReleasedThisFrame;
                case EMouseButton.Right:
                    return Mouse.current.rightButton.wasReleasedThisFrame;
                default:
                    return Mouse.current.leftButton.wasReleasedThisFrame;
            }
        }

        public override bool GetMouseButtonPressed(EMouseButton button)
        {
            switch (button)
            {
                case EMouseButton.Middle:
                    return Mouse.current.middleButton.isPressed;
                case EMouseButton.Right:
                    return Mouse.current.rightButton.isPressed;
                default:
                    return Mouse.current.leftButton.isPressed;
            }
        }

        public override Vector2 GetMousePosition()
        {
            return Mouse.current.position.ReadValue();
        }

        private static readonly Vector2 s_ScalingFactor = new Vector2(0.00833f, 0.00833f); // 1/120
        public override Vector2 GetScrollDelta()
        {
            // 新输入系统的 scroll 返回的是 tick（刻度），每滚一格通常是 120
            // 除以 120 是为了与旧系统值范围相似
            return Vector2.Scale(Mouse.current.scroll.ReadValue(), s_ScalingFactor);
        }

        protected override void OnInit()
        {
            _inputActionsDictionary.Clear();

            if (InputActions == null)
            {
                LogUtility.Error("Please set Input Actions in {0} or 'Project Settings -> Input System Package'", nameof(InputSettings));
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
            if (!_inputActionsDictionary.TryGetValue(fullActionName, out InputAction action))
            {
                action = InputActions.FindAction(fullActionName);
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
    }
}
#endif