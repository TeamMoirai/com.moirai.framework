#if ENABLE_LEGACY_INPUT_MANAGER
using System;
using UnityEngine;
using System.Collections.Generic;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 基于 Unity 旧版输入管理器，需定义 ENABLE_LEGACY_INPUT_MANAGER。
    /// </summary>
    /// <remarks>此方案用于桌面游戏。</remarks>
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

        public override bool GetButtonDown(string actionName, string actionGroup)
        {
            if (!IsValidAxis(actionName))
            {
                PrintInputWarning(actionName);
                return false;
            }

            return UnityEngine.Input.GetButtonDown(actionName);
        }

        public override bool GetButtonUp(string actionName, string actionGroup)
        {
            if (!IsValidAxis(actionName))
            {
                PrintInputWarning(actionName);
                return false;
            }

            return UnityEngine.Input.GetButtonUp(actionName);
        }
        
        public override bool GetBool(string actionName, string actionGroup = "")
        {
            if (!IsValidAxis(actionName))
            {
                PrintInputWarning(actionName);
                return false;
            }

            return UnityEngine.Input.GetButton(actionName);
        }

        public override float GetFloat(string actionName, string actionGroup = "")
        {
            if (!IsValidAxis(actionName))
            {
                PrintInputWarning(actionName);
                return 0f;
            }

            return UnityEngine.Input.GetAxisRaw(actionName);
        }

        public override Vector2 GetVector2(string actionName, string actionGroup = "")
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

        protected override void OnInit()
        {
            EnsureAxesCached();
        }

        public override void ResetAllInputStates()
        {
            UnityEngine.Input.ResetInputAxes();
        }

        private void PrintInputWarning(string actionName)
        {
            LogUtility.Warning(StringUtility.Format("[{0}] action not found! Please make sure this action is included in your input settings (axis).", actionName));
        }

        private void PrintInputWarning(string actionXName, string actionYName)
        {
            LogUtility.Warning(StringUtility.Format("[{0}] and/or [{1}] actions not found! Please make sure both of these actions are included in your input settings (axis).", actionXName, actionYName));
        }
    }
}
#endif
