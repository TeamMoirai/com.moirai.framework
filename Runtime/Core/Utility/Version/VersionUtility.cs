namespace Moirai.Atropos
{
    /// <summary>
    /// 版本号类。
    /// </summary>
    public static partial class VersionUtility
    {
        private static IVersionHelper s_VersionHelper = null;

        /// <summary>
        /// 设置版本号辅助器。
        /// </summary>
        /// <param name="versionHelper">要设置的版本号辅助器。</param>
        public static void SetVersionHelper(IVersionHelper versionHelper)
        {
            s_VersionHelper = versionHelper;
        }
        
        /// <summary>
        /// 获取游戏版本号。
        /// </summary>
        public static string GameVersion
        {
            get
            {
                if (s_VersionHelper == null)
                {
                    return string.Empty;
                }

                return s_VersionHelper.GameVersion;
            }
        }

        /// <summary>
        /// 获取内部游戏版本号。
        /// </summary>
        public static string InternalGameVersion
        {
            get
            {
                if (s_VersionHelper == null)
                {
                    return string.Empty;
                }

                return s_VersionHelper.InternalGameVersion;
            }
        }
        
        /// <summary>
        /// 获取资源版本号。
        /// </summary>
        public static string ResourceVersion
        {
            get
            {
                if (s_VersionHelper == null)
                {
                    return string.Empty;
                }

                return s_VersionHelper.ResourceVersion;
            }
        }
        
        /// <summary>
        /// 获取内部资源版本号。
        /// </summary>
        public static string InternalResourceVersion
        {
            get
            {
                if (s_VersionHelper == null)
                {
                    return string.Empty;
                }

                return s_VersionHelper.InternalResourceVersion;
            }
        }
    }
}
