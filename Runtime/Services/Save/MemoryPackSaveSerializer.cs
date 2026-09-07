using MemoryPack;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// MemoryPack 序列化后端（<c>MemoryPack</c> NuGet 包，零编码开销二进制格式）。
    /// <para>数据类型需 <c>[MemoryPackable]</c> partial 标注并依赖 MemoryPack SourceGenerator 产出 AOT 安全序列化代码。</para>
    /// </summary>
    public sealed class MemoryPackSaveSerializer : ISaveSerializer
    {
        /// <summary>
        /// 后端标识（恒为 <see cref="ESaveBackend.MemoryPack"/>）。
        /// </summary>
        public ESaveBackend Backend => ESaveBackend.MemoryPack;

        /// <summary>
        /// 将数据对象序列化为 MemoryPack 字节。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="data">数据对象。</param>
        /// <returns>序列化字节。</returns>
        public byte[] Serialize<T>(T data)
        {
            return MemoryPackSerializer.Serialize(data);
        }

        /// <summary>
        /// 从 MemoryPack 字节反序列化数据对象。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="bytes">序列化字节。</param>
        /// <returns>反序列化后的数据对象。</returns>
        public T Deserialize<T>(byte[] bytes)
        {
            return MemoryPackSerializer.Deserialize<T>(bytes);
        }
    }
}
