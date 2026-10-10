using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 屏幕信息窗口。
    /// </summary>
    public sealed class ScreenInformationWindow : PollingDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement card = AddSection(root, "Screen Information");
            AddRow(card, "Current Resolution", GetResolutionString(Screen.currentResolution));
            AddRow(card, "Screen Width", StringUtility.Format("{0} px / {1:F2} in / {2:F2} cm", Screen.width, ConverterUtility.GetInchesFromPixels(Screen.width), ConverterUtility.GetCentimetersFromPixels(Screen.width)));
            AddRow(card, "Screen Height", StringUtility.Format("{0} px / {1:F2} in / {2:F2} cm", Screen.height, ConverterUtility.GetInchesFromPixels(Screen.height), ConverterUtility.GetCentimetersFromPixels(Screen.height)));
            AddRow(card, "Screen DPI", Screen.dpi.ToString("F2"));
            AddRow(card, "Screen Orientation", Screen.orientation.ToString());
            AddRow(card, "Is Full Screen", Screen.fullScreen.ToString());
            AddRow(card, "Full Screen Mode", Screen.fullScreenMode.ToString());
            AddRow(card, "Sleep Timeout", GetSleepTimeoutDescription(Screen.sleepTimeout));
            AddRow(card, "Brightness", Screen.brightness.ToString("F2"));
            AddRow(card, "Cursor Visible", UnityEngine.Cursor.visible.ToString());
            AddRow(card, "Cursor Lock State", UnityEngine.Cursor.lockState.ToString());
            AddRow(card, "Auto Landscape Left", Screen.autorotateToLandscapeLeft.ToString());
            AddRow(card, "Auto Landscape Right", Screen.autorotateToLandscapeRight.ToString());
            AddRow(card, "Auto Portrait", Screen.autorotateToPortrait.ToString());
            AddRow(card, "Auto Portrait Upside Down", Screen.autorotateToPortraitUpsideDown.ToString());
            AddRow(card, "Safe Area", Screen.safeArea.ToString());
            AddRow(card, "Cutouts", GetCutoutsString(Screen.cutouts));
            AddRow(card, "Support Resolutions", GetResolutionsString(Screen.resolutions));
        }

        #endregion

        #region 私有 [PRIVATE]

        private static string GetSleepTimeoutDescription(int sleepTimeout)
        {
            if (sleepTimeout == SleepTimeout.NeverSleep)
            {
                return "Never Sleep";
            }

            if (sleepTimeout == SleepTimeout.SystemSetting)
            {
                return "System Setting";
            }

            return sleepTimeout.ToString();
        }

        private static string GetResolutionString(Resolution resolution)
        {
            return StringUtility.Format("{0} x {1} @ {2}Hz", resolution.width, resolution.height, resolution.refreshRateRatio);
        }

        private static string GetCutoutsString(Rect[] cutouts)
        {
            if (cutouts == null || cutouts.Length == 0)
            {
                return "None";
            }

            string result = cutouts[0].ToString();
            for (int i = 1; i < cutouts.Length; i++)
            {
                result = StringUtility.Concat(result, "; ", cutouts[i].ToString());
            }

            return result;
        }

        private static string GetResolutionsString(Resolution[] resolutions)
        {
            if (resolutions == null || resolutions.Length == 0)
            {
                return "None";
            }

            string result = GetResolutionString(resolutions[0]);
            for (int i = 1; i < resolutions.Length; i++)
            {
                result = StringUtility.Concat(result, "; ", GetResolutionString(resolutions[i]));
            }

            return result;
        }

        #endregion
    }
}
