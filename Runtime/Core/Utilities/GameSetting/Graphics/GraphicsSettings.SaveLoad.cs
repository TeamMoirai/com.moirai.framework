using UnityEngine;

namespace Moirai.Atropos
{
    public partial class GraphicsSettings
    {
        /// <summary>
        /// 写入分辨率设置
        /// </summary>
        public static void SetResolutionSettings(int resolutionIndex)
        {
            SetResolutionIndex(resolutionIndex);
            SettingUtility.SetInt(GameConstant.Setting.GRAPHICS_RESOLUTION, GetResolutionIndex());
        }

        /// <summary>
        /// 写入全屏设置
        /// </summary>
        public static void SetFullScreenSettings(bool fullScreen)
        {
            SetFullScreen(fullScreen);
            SettingUtility.SetBool(GameConstant.Setting.GRAPHICS_FULLSCREEN, GetFullScreen());
        }

        /// <summary>
        /// 写入窗口模式设置
        /// </summary>
        public static void SetWindowModeSettings(int fullScreenMode)
        {
            SetWindowModeIndex(fullScreenMode);
            SettingUtility.SetInt(GameConstant.Setting.GRAPHICS_FULLSCREEN_MODE, GetWindowModeIndex());
        }

        /// <summary>
        /// 设置垂直同步
        /// </summary>
        /// <param name="enabled"></param>
        public static void SetVSyncSettings(bool enabled)
        {
            SetVSync(enabled);
            SettingUtility.SetBool(GameConstant.Setting.GRAPHICS_VSYNC, VSyncEnabled);
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void GraphicsSettingsInitiation()
        {
            int resolution = SettingUtility.GetInt(GameConstant.Setting.GRAPHICS_RESOLUTION, GetResolutionIndex());
            SetResolutionIndex(resolution);

            bool fullScreen = SettingUtility.GetBool(GameConstant.Setting.GRAPHICS_FULLSCREEN, Screen.fullScreen);
            SetFullScreen(fullScreen);

            int fullScreenMode = SettingUtility.GetInt(GameConstant.Setting.GRAPHICS_FULLSCREEN_MODE, (int)Screen.fullScreenMode);
            SetWindowModeIndex(fullScreenMode);

            bool vSync = SettingUtility.GetBool(GameConstant.Setting.GRAPHICS_VSYNC, VSyncEnabled);
            SetVSync(vSync);

            LogUtility.Info("[GraphicsSettings] <color=orange>" +
                 $"Resolution:{Instance._lastKnownResolution} " +
                 $"FullScreen:{Instance._lastKnownFullScreen} " +
                 $"FullScreenMode:{Instance._lastKnownWindowMode} " +
                 $"vSync:{VSyncEnabled}" +
                 "</color>");
        }

        /// <summary>
        /// 移除设置
        /// </summary>
        public static void RemoveSetting()
        {
            SettingUtility.RemoveSetting(GameConstant.Setting.GRAPHICS_RESOLUTION);
            SettingUtility.RemoveSetting(GameConstant.Setting.GRAPHICS_FULLSCREEN);
            SettingUtility.RemoveSetting(GameConstant.Setting.GRAPHICS_FULLSCREEN_MODE);
            SettingUtility.RemoveSetting(GameConstant.Setting.GRAPHICS_VSYNC);
        }
    }
}