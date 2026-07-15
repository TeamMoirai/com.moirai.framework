using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Composites;
using UnityEngine.InputSystem.Controls;
#endif

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class InputSummaryInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Input Summary Information</b>");
                GUILayout.BeginVertical("box");
                {
#if ENABLE_LEGACY_INPUT_MANAGER
                    DrawItem("Back Button Leaves App", UnityEngine.Input.backButtonLeavesApp.ToString());
                    DrawItem("Device Orientation", UnityEngine.Input.deviceOrientation.ToString());
                    DrawItem("Mouse Present", UnityEngine.Input.mousePresent.ToString());
                    DrawItem("Mouse Position", UnityEngine.Input.mousePosition.ToString());
                    DrawItem("Mouse Scroll Delta", UnityEngine.Input.mouseScrollDelta.ToString());
                    DrawItem("Any Key", UnityEngine.Input.anyKey.ToString());
                    DrawItem("Any Key Down", UnityEngine.Input.anyKeyDown.ToString());
                    DrawItem("Input String", UnityEngine.Input.inputString);
                    DrawItem("IME Is Selected", UnityEngine.Input.imeIsSelected.ToString());
                    DrawItem("IME Composition Mode", UnityEngine.Input.imeCompositionMode.ToString());
                    DrawItem("Compensate Sensors", UnityEngine.Input.compensateSensors.ToString());
                    DrawItem("Composition Cursor Position", UnityEngine.Input.compositionCursorPos.ToString());
                    DrawItem("Composition String", UnityEngine.Input.compositionString);
#elif ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                    DrawItem("Back Button Leaves App", "需监听设备返回键事件实现"); // 无直接对应
                    DrawItem("Device Orientation", Accelerometer.current?.acceleration.ReadValue().ToString() ?? "未检测到加速度计");
                    DrawItem("Mouse Present", (Mouse.current != null).ToString());
                    DrawItem("Mouse Position", Mouse.current?.position.ReadValue().ToString() ?? "无鼠标设备");
                    DrawItem("Mouse Scroll Delta", Mouse.current?.scroll.ReadValue().ToString() ?? "无滚轮输入");
                    DrawItem("Any Key", Keyboard.current?.anyKey.isPressed.ToString() ?? "false");
                    DrawItem("Any Key Down", Keyboard.current?.anyKey.wasPressedThisFrame.ToString() ?? "false");
                    // DrawItem("Input String", Keyboard.current?.GetKeyPressesString() ?? "");
                    // DrawItem("IME Is Selected", Keyboard.current?.imeSelected.ToString() ?? "false");
                    // DrawItem("IME Composition Mode", Keyboard.current?.imeCompositionMode.ToString() ?? "Disabled");
                    // DrawItem("Compensate Sensors", "需通过InputSystem.settings配置"); // 系统级设置
                    // DrawItem("Composition Cursor Position", Keyboard.current?.compositionCursorPosition.ToString() ?? "(0,0)");
                    // DrawItem("Composition String", Keyboard.current?.compositionString ?? "");
#else
                    DrawItem("UnityEngine.Input 已禁用，基于输入模块的信息统计未实现", "");
#endif
                }
                GUILayout.EndVertical();
            }
        }
    }
}
