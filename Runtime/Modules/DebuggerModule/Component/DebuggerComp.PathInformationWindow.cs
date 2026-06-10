using System;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class PathInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Path Information</b>");
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Current Directory", PathUtility.GetRegularPath(Environment.CurrentDirectory));
                    DrawItem("Data Path", PathUtility.GetRegularPath(Application.dataPath));
                    DrawItem("Persistent Data Path", PathUtility.GetRegularPath(Application.persistentDataPath));
                    DrawItem("Streaming Assets Path", PathUtility.GetRegularPath(Application.streamingAssetsPath));
                    DrawItem("Temporary Cache Path", PathUtility.GetRegularPath(Application.temporaryCachePath));
#if UNITY_2018_3_OR_NEWER
                    DrawItem("Console Log Path", PathUtility.GetRegularPath(Application.consoleLogPath));
#endif
                }
                GUILayout.EndVertical();
            }
        }
    }
}
