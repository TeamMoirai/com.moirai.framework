using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class InputLocationInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Input Location Information</b>");
                GUILayout.BeginVertical("box");
                {
#if ENABLE_LEGACY_INPUT_MANAGER
                    GUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("Enable", GUILayout.Height(30f)))
                        {
                            UnityEngine.Input.location.Start();
                        }
                        if (GUILayout.Button("Disable", GUILayout.Height(30f)))
                        {
                            UnityEngine.Input.location.Stop();
                        }
                    }
                    GUILayout.EndHorizontal();

                    DrawItem("Is Enabled By User", UnityEngine.Input.location.isEnabledByUser.ToString());
                    DrawItem("Status", UnityEngine.Input.location.status.ToString());
                    if (UnityEngine.Input.location.status == LocationServiceStatus.Running)
                    {
                        DrawItem("Horizontal Accuracy", UnityEngine.Input.location.lastData.horizontalAccuracy.ToString());
                        DrawItem("Vertical Accuracy", UnityEngine.Input.location.lastData.verticalAccuracy.ToString());
                        DrawItem("Longitude", UnityEngine.Input.location.lastData.longitude.ToString());
                        DrawItem("Latitude", UnityEngine.Input.location.lastData.latitude.ToString());
                        DrawItem("Altitude", UnityEngine.Input.location.lastData.altitude.ToString());
                        DrawItem("Timestamp", UnityEngine.Input.location.lastData.timestamp.ToString());
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
