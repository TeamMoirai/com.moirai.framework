using MessagePack;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// MessagePack 序列化后端（<c>MessagePack</c> NuGet 包 v3）。
    /// <para>数据类型需 <c>[MessagePackObject]</c> 标注并依赖 MessagePack SourceGenerator 产出 AOT 安全 formatter；
    /// 未标注类型在 IL2CPP 下不受支持（编辑器 Mono 动态解析仅可用于调试）。</para>
    /// </summary>
    public sealed class MessagePackSaveSerializer : ISaveSerializer
    {
        /// <summary>
        /// 序列化选项（默认 Standard 组合）。
        /// </summary>
        private static readonly MessagePackSerializerOptions s_Options = MessagePackSerializerOptions.Standard;

        /// <summary>
        /// 后端标识（恒为 <see cref="ESaveBackend.MessagePack"/>）。
        /// </summary>
        public ESaveBackend Backend => ESaveBackend.MessagePack;

        /// <summary>
        /// 将数据对象序列化为 MessagePack 字节。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="data">数据对象。</param>
        /// <returns>序列化字节。</returns>
        public byte[] Serialize<T>(T data)
        {
            return MessagePackSerializer.Serialize(data, s_Options);
        }

        /// <summary>
        /// 从 MessagePack 字节反序列化数据对象。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="bytes">序列化字节。</param>
        /// <returns>反序列化后的数据对象。</returns>
        public T Deserialize<T>(byte[] bytes)
        {
            return MessagePackSerializer.Deserialize<T>(bytes, s_Options);
        }
    }
}
