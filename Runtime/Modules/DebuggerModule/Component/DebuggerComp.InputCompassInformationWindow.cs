using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class InputCompassInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Input Compass Information</b>");
                GUILayout.BeginVertical("box");
                {
#if ENABLE_LEGACY_INPUT_MANAGER
                    GUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("Enable", GUILayout.Height(30f)))
                        {
                            UnityEngine.Input.compass.enabled = true;
                        }
                        if (GUILayout.Button("Disable", GUILayout.Height(30f)))
                        {
                            UnityEngine.Input.compass.enabled = false;
                        }
                    }
                    GUILayout.EndHorizontal();

                    DrawItem("Enabled", UnityEngine.Input.compass.enabled.ToString());
                    if (UnityEngine.Input.compass.enabled)
                    {
                        DrawItem("Heading Accuracy", UnityEngine.Input.compass.headingAccuracy.ToString());
                        DrawItem("Magnetic Heading", UnityEngine.Input.compass.magneticHeading.ToString());
                        DrawItem("Raw Vector", UnityEngine.Input.compass.rawVector.ToString());
                        DrawItem("Timestamp", UnityEngine.Input.compass.timestamp.ToString());
                        DrawItem("True Heading", UnityEngine.Input.compass.trueHeading.ToString());
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
