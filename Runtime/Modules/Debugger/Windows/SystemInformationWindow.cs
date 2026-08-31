using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 系统信息窗口。
    /// </summary>
    public sealed class SystemInformationWindow : PollingDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement card = AddSection(root, "System Information");
            AddRow(card, "Device Unique ID", SystemInfo.deviceUniqueIdentifier);
            AddRow(card, "Device Name", SystemInfo.deviceName);
            AddRow(card, "Device Type", SystemInfo.deviceType.ToString());
            AddRow(card, "Device Model", SystemInfo.deviceModel);
            AddRow(card, "Processor Type", SystemInfo.processorType);
            AddRow(card, "Processor Count", SystemInfo.processorCount.ToString());
            AddRow(card, "Processor Frequency", StringUtility.Format("{0} MHz", SystemInfo.processorFrequency));
            AddRow(card, "System Memory Size", StringUtility.Format("{0} MB", SystemInfo.systemMemorySize));
            AddRow(card, "Operating System Family", SystemInfo.operatingSystemFamily.ToString());
            AddRow(card, "Operating System", SystemInfo.operatingSystem);
            AddRow(card, "Battery Status", SystemInfo.batteryStatus.ToString());
            AddRow(card, "Battery Level", GetBatteryLevelString(SystemInfo.batteryLevel));
            AddRow(card, "Supports Audio", SystemInfo.supportsAudio.ToString());
            AddRow(card, "Supports Location Service", SystemInfo.supportsLocationService.ToString());
            AddRow(card, "Supports Accelerometer", SystemInfo.supportsAccelerometer.ToString());
            AddRow(card, "Supports Gyroscope", SystemInfo.supportsGyroscope.ToString());
            AddRow(card, "Supports Vibration", SystemInfo.supportsVibration.ToString());
            AddRow(card, "Genuine", Application.genuine.ToString());
            AddRow(card, "Genuine Check Available", Application.genuineCheckAvailable.ToString());
        }

        #endregion

        #region 私有 [PRIVATE]

        private static string GetBatteryLevelString(float batteryLevel)
        {
            return batteryLevel < 0f ? "Unavailable" : batteryLevel.ToString("P0");
        }

        #endregion
    }
}
