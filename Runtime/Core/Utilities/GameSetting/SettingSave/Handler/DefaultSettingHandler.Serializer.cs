namespace Moirai.Atropos
{
    public sealed partial class DefaultSettingHandler
    {
        /// <summary>
        /// 默认游戏配置序列化器。
        /// </summary>
        internal class Serializer : GameSerializer<Settings>
        {
            private static readonly byte[] s_Header = new byte[] { (byte)'M', (byte)'A', (byte)'S' };

            /// <summary>
            /// 初始化默认游戏配置序列化器的新实例。
            /// </summary>
            public Serializer() { }

            /// <summary>
            /// 获取默认游戏配置头标识。
            /// </summary>
            /// <returns>默认游戏配置头标识。</returns>
            protected override byte[] GetHeader()
            {
                return s_Header;
            }
        }
    }
}