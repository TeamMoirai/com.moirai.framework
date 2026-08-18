using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 这个输入处理器实现了针对移动端 UI 的输入检测功能。
    /// </summary>
    [Serializable]
    public sealed class UIMobileInputHandler : InputHandler
    {
        private readonly Dictionary<string, InputButton> _inputButtons = new Dictionary<string, InputButton>();
        private readonly Dictionary<string, InputAxes> _inputAxes = new Dictionary<string, InputAxes>();

        protected override void OnInit()
        {
            InputButton[] inputButtonsArray = UnityUtility.FindObjectsByType<InputButton>();
            for (int i = 0; i < inputButtonsArray.Length; i++)
                _inputButtons.Add(inputButtonsArray[i].ActionName, inputButtonsArray[i]);

            InputAxes[] inputAxesArray = UnityUtility.FindObjectsByType<InputAxes>();
            for (int i = 0; i < inputAxesArray.Length; i++)
                _inputAxes.Add(inputAxesArray[i].ActionName, inputAxesArray[i]);
        }

        public override bool GetButtonDown(string actionName, string actionGroup)
        {
            throw new System.NotImplementedException();
        }

        public override bool GetButtonUp(string actionName, string actionGroup)
        {
            throw new System.NotImplementedException();
        }

        public override bool GetBool(string actionName, string actionGroup = "")
        {
            bool found = _inputButtons.TryGetValue(actionName, out InputButton inputButton);

            return found ? inputButton.BoolValue : false;
        }

        public override float GetFloat(string actionName, string actionGroup = "")
        {
            return 0f;
        }

        public override Vector2 GetVector2(string actionName, string actionGroup = "")
        {
            bool found = _inputAxes.TryGetValue(actionName, out InputAxes element);

            return found ? element.Vector2Value : Vector2.zero;
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
            foreach (var inputButton in _inputButtons)
            {
                inputButton.Value.BoolValue = false;
            }

            foreach (var inputAxes in _inputAxes)
            {
                inputAxes.Value.Vector2Value = Vector2.zero;
            }
        }
    }
}
