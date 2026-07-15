using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class InputGyroscopeInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Input Gyroscope Information</b>");
                GUILayout.BeginVertical("box");
                {
#if ENABLE_LEGACY_INPUT_MANAGER
                    GUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("Enable", GUILayout.Height(30f)))
                        {
                            UnityEngine.Input.gyro.enabled = true;
                        }
                        if (GUILayout.Button("Disable", GUILayout.Height(30f)))
                        {
                            UnityEngine.Input.gyro.enabled = false;
                        }
                    }
                    GUILayout.EndHorizontal();

                    DrawItem("Enabled", UnityEngine.Input.gyro.enabled.ToString());
                    if (UnityEngine.Input.gyro.enabled)
                    {
                        DrawItem("Update Interval", UnityEngine.Input.gyro.updateInterval.ToString());
                        DrawItem("Attitude", UnityEngine.Input.gyro.attitude.eulerAngles.ToString());
                        DrawItem("Gravity", UnityEngine.Input.gyro.gravity.ToString());
                        DrawItem("Rotation Rate", UnityEngine.Input.gyro.rotationRate.ToString());
                        DrawItem("Rotation Rate Unbiased", UnityEngine.Input.gyro.rotationRateUnbiased.ToString());
                        DrawItem("User Acceleration", UnityEngine.Input.gyro.userAcceleration.ToString());
                    }
#else
                    DrawItem("UnityEngine.Input 已禁用，基于输入模块的信息统计未实现", "");
#endif
                }
                GUILayout.EndVertical();
            }
        }
    }
}
