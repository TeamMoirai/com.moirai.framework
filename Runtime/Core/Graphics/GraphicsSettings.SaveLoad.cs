using UnityEngine;

namespace Moirai.Atropos
{
    public partial class GraphicsSettings
    {
#if UNITY_EDITOR
        /// <summary>
        /// 重置静态状态：兼容关闭 Domain Reload 的编辑器工作流（进入 Play 时清空上一会话残留）。
        /// </summary>
        /// <remarks>
        /// 五个静态变更事件（声明分布在 FullScreen/WindowMode/VSync/Resolution 各分部）统一在此置空： <br />
        /// 关闭域重载时上一会话的订阅者多引用旧 UI 实例，不摘会跨会话残留、重复收到回调。 <br />
        /// 值配置不在此复位：分辨率/全屏/窗口模式/垂直同步由 <see cref="GraphicsSettingsInitiation"/>（AfterAssembliesLoaded）
        /// 每会话从存档重播；AllowResolutionChangeOnMobile 保持声明默认（false），由游戏侧按会话自行写入，不与其启动期赋值竞争。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            onFullScreenChanged = null;
            onWindowModeChanged = null;
            onVSyncChanged = null;
            onMaxResolutionChanged = null;
            onResolutionChanged = null;
        }
#endif

        /// <summary>
        /// 写入分辨率设置：只改配置项，落盘由设置界面在确认时统一调 <see cref="SettingUtility.Save"/>。
        /// </summary>
        public static void SetResolutionSettings(int resolutionIndex)
        {
            SetResolutionIndex(resolutionIndex);
            SettingUtility.SetInt(GameConstant.Setting.GRAPHICS_RESOLUTION, GetResolutionIndex());
        }

        /// <summary>
        /// 写入全屏设置：只改配置项，落盘同上。
        /// </summary>
        public static void SetFullScreenSettings(bool fullScreen)
        {
            SetFullScreen(fullScreen);
            SettingUtility.SetBool(GameConstant.Setting.GRAPHICS_FULLSCREEN, GetFullScreen());
        }

        /// <summary>
        /// 写入窗口模式设置：只改配置项，落盘同上。
        /// </summary>
        public static void SetWindowModeSettings(int fullScreenMode)
        {
            SetWindowModeIndex(fullScreenMode);
            SettingUtility.SetInt(GameConstant.Setting.GRAPHICS_FULLSCREEN_MODE, GetWindowModeIndex());
        }

        /// <summary>
        /// 设置垂直同步：只改配置项，落盘同上。
        /// </summary>
        public static void SetVSyncSettings(bool enabled)
        {
            SetVSync(enabled);
            SettingUtility.SetBool(GameConstant.Setting.GRAPHICS_VSYNC, VSyncEnabled);
        }

        /// <summary>
        /// 加载设置。
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

            LogUtility.Info("[GraphicsSettings] <color=orange>Resolution:{0} FullScreen:{1} FullScreenMode:{2} vSync:{3}</color>",
                Instance._lastKnownResolution, Instance._lastKnownFullScreen, Instance._lastKnownWindowMode, VSyncEnabled);
        }

        /// <summary>
        /// 移除设置：四条键都摘掉，落盘由调用方统一做。
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