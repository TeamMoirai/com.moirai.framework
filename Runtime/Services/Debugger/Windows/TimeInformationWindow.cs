using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 时间信息窗口。
    /// </summary>
    public sealed class TimeInformationWindow : PollingDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement card = AddSection(root, "Time Information");
            AddRow(card, "Time Scale", StringUtility.Format("{0} [{1}]", Time.timeScale, GetTimeScaleDescription(Time.timeScale)));
            AddRow(card, "Realtime Since Startup", Time.realtimeSinceStartup.ToString());
            AddRow(card, "Time Since Level Load", Time.timeSinceLevelLoad.ToString());
            AddRow(card, "Time", Time.time.ToString());
            AddRow(card, "Fixed Time", Time.fixedTime.ToString());
            AddRow(card, "Unscaled Time", Time.unscaledTime.ToString());
            AddRow(card, "Fixed Unscaled Time", Time.fixedUnscaledTime.ToString());
            AddRow(card, "Delta Time", Time.deltaTime.ToString());
            AddRow(card, "Fixed Delta Time", Time.fixedDeltaTime.ToString());
            AddRow(card, "Unscaled Delta Time", Time.unscaledDeltaTime.ToString());
            AddRow(card, "Fixed Unscaled Delta Time", Time.fixedUnscaledDeltaTime.ToString());
            AddRow(card, "Smooth Delta Time", Time.smoothDeltaTime.ToString());
            AddRow(card, "Maximum Delta Time", Time.maximumDeltaTime.ToString());
            AddRow(card, "Maximum Particle Delta Time", Time.maximumParticleDeltaTime.ToString());
            AddRow(card, "Frame Count", Time.frameCount.ToString());
            AddRow(card, "Rendered Frame Count", Time.renderedFrameCount.ToString());
            AddRow(card, "Capture Framerate", Time.captureFramerate.ToString());
            AddRow(card, "Capture Delta Time", Time.captureDeltaTime.ToString());
            AddRow(card, "In Fixed Time Step", Time.inFixedTimeStep.ToString());
        }

        #endregion

        #region 私有 [PRIVATE]

        private static string GetTimeScaleDescription(float timeScale)
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

        #endregion
    }
}
