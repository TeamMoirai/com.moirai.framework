namespace Moirai.Atropos
{
    /// <summary>
    /// 常用设置相关常量。
    /// </summary>
    public static partial class Constant
    {
        /// <summary>
        /// 游戏版本
        /// </summary>
        public const string GAME_VERSION = "GameVersion";
        
        /// <summary>
        /// 设置相关常量。
        /// </summary>
        public static class Setting
        {
            /// <summary>
            /// 当前语言
            /// </summary>
            public const string LANGUAGE = "Setting.Language";
            
            /// <summary>
            /// 主音量当前音量
            /// </summary>
            public const string AUDIO_MASTER_VOLUME = "Setting.Audio.MasterVolume";
            /// <summary>
            /// 主音量是否静音
            /// </summary>
            public const string AUDIO_MASTER_MUTED = "Setting.Audio.MasterMuted";
            /// <summary>
            /// 音频组当前音量
            /// </summary>
            public const string AUDIO_GROUP_VOLUME = "Setting.Audio.{0}Volume";
            /// <summary>
            /// 音频组是否静音
            /// </summary>
            public const string AUDIO_GROUP_MUTED = "Setting.Audio.{0}Muted";

            /// <summary>
            /// 屏幕分辨率（<see cref="UnityEngine.Screen.currentResolution"/>）
            /// </summary>
            public const string GRAPHICS_RESOLUTION = "Setting.Graphics.Screen.Resolution";
            /// <summary>
            /// 屏幕是否全屏（<see cref="UnityEngine.Screen.fullScreen"/>）
            /// </summary>
            public const string GRAPHICS_FULLSCREEN = "Setting.Graphics.Screen.Fullscreen";
            /// <summary>
            /// 屏幕全屏模式（<see cref="UnityEngine.FullScreenMode"/>）
            /// </summary>
            public const string GRAPHICS_FULLSCREEN_MODE = "Setting.Graphics.Screen.FullscreenMode";
            /// <summary>垂直同步</summary>
            public const string GRAPHICS_VSYNC = "Setting.Graphics.VSync";
        }
    }
}