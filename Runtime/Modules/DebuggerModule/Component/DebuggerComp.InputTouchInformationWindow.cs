using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class InputTouchInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Input Touch Information</b>");
                GUILayout.BeginVertical("box");
                {
#if ENABLE_LEGACY_INPUT_MANAGER
                    DrawItem("Touch Supported", UnityEngine.Input.touchSupported.ToString());
                    DrawItem("Touch Pressure Supported", UnityEngine.Input.touchPressureSupported.ToString());
                    DrawItem("Stylus Touch Supported", UnityEngine.Input.stylusTouchSupported.ToString());
                    DrawItem("Simulate Mouse With Touches", UnityEngine.Input.simulateMouseWithTouches.ToString());
                    DrawItem("Multi Touch Enabled", UnityEngine.Input.multiTouchEnabled.ToString());
                    DrawItem("Touch Count", UnityEngine.Input.touchCount.ToString());
                    DrawItem("Touches", GetTouchesString(UnityEngine.Input.touches));
#else
                    DrawItem("UnityEngine.Input 已禁用，基于输入模块的信息统计未实现", "");
#endif
                }
                GUILayout.EndVertical();
            }

            private string GetTouchString(Touch touch)
            {
                return StringUtility.Format("{0}, {1}, {2}, {3}, {4}", touch.position, touch.deltaPosition, touch.rawPosition, touch.pressure, touch.phase);
            }

            private string GetTouchesString(Touch[] touches)
            {
                string[] touchStrings = new string[touches.Length];
                for (int i = 0; i < touches.Length; i++)
                {
                    touchStrings[i] = GetTouchString(touches[i]);
                }

                return string.Join("; ", touchStrings);
            }
        }
    }
}
