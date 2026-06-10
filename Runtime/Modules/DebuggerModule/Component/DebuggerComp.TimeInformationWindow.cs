using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class TimeInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Time Information</b>");
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Time Scale", TextUtility.Format("{0} [{1}]", UnityEngine.Time.timeScale, GetTimeScaleDescription(UnityEngine.Time.timeScale)));
                    DrawItem("Realtime Since Startup", UnityEngine.Time.realtimeSinceStartup.ToString());
                    DrawItem("Time Since Level Load", UnityEngine.Time.timeSinceLevelLoad.ToString());
                    DrawItem("Time", UnityEngine.Time.time.ToString());
                    DrawItem("Fixed Time", UnityEngine.Time.fixedTime.ToString());
                    DrawItem("Unscaled Time", UnityEngine.Time.unscaledTime.ToString());
#if UNITY_5_6_OR_NEWER
                    DrawItem("Fixed Unscaled Time", UnityEngine.Time.fixedUnscaledTime.ToString());
#endif
                    DrawItem("Delta Time", UnityEngine.Time.deltaTime.ToString());
                    DrawItem("Fixed Delta Time", UnityEngine.Time.fixedDeltaTime.ToString());
                    DrawItem("Unscaled Delta Time", UnityEngine.Time.unscaledDeltaTime.ToString());
#if UNITY_5_6_OR_NEWER
                    DrawItem("Fixed Unscaled Delta Time", UnityEngine.Time.fixedUnscaledDeltaTime.ToString());
#endif
                    DrawItem("Smooth Delta Time", UnityEngine.Time.smoothDeltaTime.ToString());
                    DrawItem("Maximum Delta Time", UnityEngine.Time.maximumDeltaTime.ToString());
#if UNITY_5_5_OR_NEWER
                    DrawItem("Maximum Particle Delta Time", UnityEngine.Time.maximumParticleDeltaTime.ToString());
#endif
                    DrawItem("Frame Count", UnityEngine.Time.frameCount.ToString());
                    DrawItem("Rendered Frame Count", UnityEngine.Time.renderedFrameCount.ToString());
                    DrawItem("Capture Framerate", UnityEngine.Time.captureFramerate.ToString());
#if UNITY_2019_2_OR_NEWER
                    DrawItem("Capture Delta Time", UnityEngine.Time.captureDeltaTime.ToString());
#endif
#if UNITY_5_6_OR_NEWER
                    DrawItem("In Fixed Time Step", UnityEngine.Time.inFixedTimeStep.ToString());
#endif
                }
                GUILayout.EndVertical();
            }

            private string GetTimeScaleDescription(float timeScale)
            {
                if (timeScale <= 0f)
                {
                    return "Pause";
                }

                if (timeScale < 1f)
                {
                    return "Slower";
                }

                if (timeScale > 1f)
                {
                    return "Faster";
                }

                return "Normal";
            }
        }
    }
}
