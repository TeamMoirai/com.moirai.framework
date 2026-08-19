namespace Moirai.Atropos
{
    /// <summary>
    /// 版本号类。
    /// </summary>
    public static partial class VersionUtility
    {
        private static VersionHandler s_Handler = null;
        /// <summary>
        /// 获取/设置版本号处理器。
        /// </summary>
        public static VersionHandler Handler
        {
            get
            {
                if (s_Handler == null) Handler = new DefaultVersionHandler();
                return s_Handler;
            }
            set
            {
                if (s_Handler == value || value == null) return;

                value.Internal_Init();
                var previous = s_Handler;
                s_Handler = value;
                previous?.Internal_Shutdown();
            }
        }

        /// <summary>
        /// 获取游戏版本号。
        /// </summary>
        public static string GameVersion => Handler.GameVersion;

        /// <summary>
        /// 获取内部游戏版本号。
        /// </summary>
        public static string InternalGameVersion => Handler.InternalGameVersion;

        /// <summary>
        /// 获取资源版本号。
        /// </summary>
        public static string ResourceVersion => Handler.ResourceVersion;

        /// <summary>
        /// 获取内部资源版本号。
        /// </summary>
        public static string InternalResourceVersion => Handler.InternalResourceVersion;
    }
}
