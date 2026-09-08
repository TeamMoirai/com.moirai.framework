using System;
using System.Collections.Generic;
using Moirai.Atropos;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档序列化后端注册表：后端标识 → 序列化器实例的静态查询点。
    /// <para>内置后端在静态构造期注册；查询未注册后端（依赖未接入/标识非法）由 <see cref="GetRequired"/> fail-fast 抛出 <see cref="GameException"/>，
    /// 避免静默降级导致块数据损坏。</para>
    /// </summary>
    public static class SaveSerializerRegistry
    {
        /// <summary>后端标识 → 序列化器实例表。</summary>
        private static readonly Dictionary<ESaveBackend, ISaveSerializer> s_Serializers = BuildBuiltInSerializers();

        /// <summary>
        /// 构建内置序列化器表（新增内置后端在此追加注册）。
        /// </summary>
        /// <returns>内置序列化器表。</returns>
        private static Dictionary<ESaveBackend, ISaveSerializer> BuildBuiltInSerializers()
        {
            var serializers = new Dictionary<ESaveBackend, ISaveSerializer>
            {
                { ESaveBackend.Json, new JsonSaveSerializer() },
#if MESSAGEPACK_INSTALLED
                { ESaveBackend.MessagePack, new MessagePackSaveSerializer() },
#endif
#if MEMORYPACK_INSTALLED
                { ESaveBackend.MemoryPack, new MemoryPackSaveSerializer() },
#endif
#if PROTOBUF_INSTALLED
                { ESaveBackend.Protobuf, new ProtobufSaveSerializer() },
#endif
            };
            return serializers;
        }

        /// <summary>
        /// 尝试获取指定后端的序列化器。
        /// </summary>
        /// <param name="backend">后端标识。</param>
        /// <param name="serializer">命中时的序列化器实例。</param>
        /// <returns>已注册返回 <c>true</c>。</returns>
        public static bool TryGet(ESaveBackend backend, out ISaveSerializer serializer)
        {
            return s_Serializers.TryGetValue(backend, out serializer);
        }

        /// <summary>
        /// 获取指定后端的序列化器（未注册时 fail-fast——静默降级会导致块数据无法还原）。
        /// </summary>
        /// <param name="backend">后端标识。</param>
        /// <returns>序列化器实例。</returns>
        public static ISaveSerializer GetRequired(ESaveBackend backend)
        {
            if (!s_Serializers.TryGetValue(backend, out ISaveSerializer serializer))
            {
                throw new GameException(StringUtility.Format("Save serializer for backend '{0}' is not registered.", backend));
            }

            return serializer;
        }
    }
}
