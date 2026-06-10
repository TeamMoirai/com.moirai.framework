using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class InputAccelerationInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Input Acceleration Information</b>");
                GUILayout.BeginVertical("box");
                {
#if ENABLE_LEGACY_INPUT_MANAGER
                    DrawItem("Acceleration", UnityEngine.Input.acceleration.ToString());
                    DrawItem("Acceleration Event Count", UnityEngine.Input.accelerationEventCount.ToString());
                    DrawItem("Acceleration Events", GetAccelerationEventsString(UnityEngine.Input.accelerationEvents));
#else
                    DrawItem("UnityEngine.Input 已禁用，基于输入模块的信息统计未实现", "");
#endif
                }
                GUILayout.EndVertical();
            }

            private string GetAccelerationEventString(AccelerationEvent accelerationEvent)
            {
                return TextUtility.Format("{0}, {1}", accelerationEvent.acceleration, accelerationEvent.deltaTime);
            }

            private string GetAccelerationEventsString(AccelerationEvent[] accelerationEvents)
            {
                string[] accelerationEventStrings = new string[accelerationEvents.Length];
                for (int i = 0; i < accelerationEvents.Length; i++)
                {
                    accelerationEventStrings[i] = GetAccelerationEventString(accelerationEvents[i]);
                }

                return string.Join("; ", accelerationEventStrings);
            }
        }
    }
}
