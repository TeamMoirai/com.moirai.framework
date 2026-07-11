using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 版本号处理器基类。
    /// </summary>
    [Serializable]
    public abstract class VersionHandler
    {
        /// <summary>
        /// 获取游戏版本号。
        /// </summary>
        public abstract string GameVersion { get; }

        /// <summary>
        /// 获取内部游戏版本号。
        /// </summary>
        public abstract string InternalGameVersion { get; }

        /// <summary>
        /// 获取游戏资源版本号。
        /// </summary>
        public abstract string ResourceVersion { get; }

        /// <summary>
        /// 获取内部游戏资源版本号。
        /// </summary>
        public abstract string InternalResourceVersion { get; }
    }
}