namespace Moirai.Atropos
{
    public static partial class VersionUtility
    {
        /// <summary>
        /// 版本号辅助器接口。
        /// </summary>
        public interface IVersionHelper
        {
            /// <summary>
            /// 获取游戏版本号。
            /// </summary>
            string GameVersion { get; }
            
            /// <summary>
            /// 获取内部游戏版本号。
            /// </summary>
            string InternalGameVersion { get; }
            
            /// <summary>
            /// 获取游戏资源版本号。
            /// </summary>
            string ResourceVersion { get; }
            
            /// <summary>
            /// 获取内部游戏资源版本号。
            /// </summary>
            string InternalResourceVersion { get; }
        }
    }
}
