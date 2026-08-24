namespace Moirai.Atropos
{
    /// <summary>
    /// 版本号类。
    /// </summary>
    [HandlerHost(typeof(VersionHandler))]
    public static partial class VersionUtility
    {
        private static VersionHandler CreateDefaultHandler()
        {
            return new DefaultVersionHandler();
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
