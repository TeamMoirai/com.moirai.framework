#if ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine;
using System.Collections.Generic;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 按照 Unity 的输入管理器规范来实现输入检测功能。
    /// </summary>
    /// <remarks>此方案用于桌面游戏。</remarks>
    public sealed class UnityInputManagerHandler : IInputHandler
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

        private readonly Dictionary<string, Vector2Action> _vector2Actions = new Dictionary<string, Vector2Action>();
        private readonly HashSet<string> _validAxes = new HashSet<string>();
        private bool _axesCached;

        private void EnsureAxesCached()
        {
            if (_axesCached) return;
            _axesCached = true;

            var axes = UnityEngine.Input.GetJoystickNames();
            for (int i = 0; i < axes.Length; i++)
            {
                if (!string.IsNullOrEmpty(axes[i]))
                {
                    _validAxes.Add(axes[i]);
                }
            }
        }

        private bool IsValidAxis(string axisName)
        {
            return _validAxes.Contains(axisName);
        }

        public bool GetButtonDown(string actionName, string actionGroup)
        {
            if (!IsValidAxis(actionName))
            {
                PrintInputWarning(actionName);
                return false;
            }

            return UnityEngine.Input.GetButtonDown(actionName);
        }

        public bool GetButtonUp(string actionName, string actionGroup)
        {
            if (!IsValidAxis(actionName))
            {
                PrintInputWarning(actionName);
                return false;
            }

            return UnityEngine.Input.GetButtonUp(actionName);
        }
        
        public bool GetBool(string actionName, string actionGroup = "")
        {
            if (!IsValidAxis(actionName))
            {
                PrintInputWarning(actionName);
                return false;
            }

            return UnityEngine.Input.GetButton(actionName);
        }

        public float GetFloat(string actionName, string actionGroup = "")
        {
            if (!IsValidAxis(actionName))
            {
                PrintInputWarning(actionName);
                return 0f;
            }

            return UnityEngine.Input.GetAxisRaw(actionName);
        }

        public Vector2 GetVector2(string actionName, string actionGroup = "")
        {
            bool found = _vector2Actions.TryGetValue(actionName, out Vector2Action vector2Action);

            if (!found)
            {
                vector2Action = new Vector2Action(
                    string.Concat(actionName, " X"),
                    string.Concat(actionName, " Y")
                );

                _vector2Actions.Add(actionName, vector2Action);
            }

            float x = IsValidAxis(vector2Action.X) ? UnityEngine.Input.GetAxisRaw(vector2Action.X) : 0f;
            float y = IsValidAxis(vector2Action.Y) ? UnityEngine.Input.GetAxisRaw(vector2Action.Y) : 0f;

            if (!IsValidAxis(vector2Action.X) || !IsValidAxis(vector2Action.Y))
            {
                PrintInputWarning(vector2Action.X, vector2Action.Y);
            }

            return new Vector2(x, y);
        }

        public bool GetMouseButtonPressed(EMouseButton button)
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

        public bool GetMouseButtonDown(EMouseButton button)
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
        
        public bool GetMouseButtonUp(EMouseButton button)
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

        public Vector2 GetMousePosition()
        {
            return UnityEngine.Input.mousePosition;
        }

        public Vector2 GetScrollDelta()
        {
            return UnityEngine.Input.mouseScrollDelta;
        }

        public void OnInit()
        {
            EnsureAxesCached();
        }

        public void ResetAllInputStates()
        {
            UnityEngine.Input.ResetInputAxes();
        }

        private void PrintInputWarning(string actionName)
        {
            Debug.LogWarning(StringUtility.Format("[{0}] action not found! Please make sure this action is included in your input settings (axis).", actionName));
        }

        private void PrintInputWarning(string actionXName, string actionYName)
        {
            Debug.LogWarning(StringUtility.Format("[{0}] and/or [{1}] actions not found! Please make sure both of these actions are included in your input settings (axis).", actionXName, actionYName));
        }
    }
}
#endif
