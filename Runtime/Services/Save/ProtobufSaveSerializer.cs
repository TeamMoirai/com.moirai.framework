using System;
using System.IO;
using ProtoBuf;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// protobuf-net 序列化后端（<c>protobuf-net</c> NuGet 包，Proto3 契约）。
    /// <para>数据类型需 <c>[ProtoContract]</c>/<c>[ProtoMember(n)]</c> 标注并依赖 protobuf-net BuildTools SourceGenerator
    /// 预生成 AOT 安全序列化器（<c>RuntimeTypeModel</c> 反射发射路径在 IL2CPP 下不可用，禁止依赖）。</para>
    /// </summary>
    public sealed class ProtobufSaveSerializer : ISaveSerializer
    {
        /// <summary>
        /// 后端标识（恒为 <see cref="ESaveBackend.Protobuf"/>）。
        /// </summary>
        public ESaveBackend Backend => ESaveBackend.Protobuf;

        /// <summary>
        /// 将数据对象序列化为 Proto 字节。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="data">数据对象。</param>
        /// <returns>序列化字节。</returns>
        public byte[] Serialize<T>(T data)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Serializer.Serialize(stream, data);
                return stream.ToArray();
            }
        }

        /// <summary>
        /// 从 Proto 字节反序列化数据对象。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="bytes">序列化字节。</param>
        /// <returns>反序列化后的数据对象。</returns>
        public T Deserialize<T>(byte[] bytes)
        {
            // 显式 ReadOnlySpan 重载：byte[] 对 span/memory 双重隐式转换会引发 CS0121 歧义
            return Serializer.Deserialize<T>(new ReadOnlySpan<byte>(bytes));
        }
    }
}
