using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档序列化后端注册表：后端标识 → 序列化器实例的静态查询点。
    /// <para>内置后端在静态构造期注册；自定义后端经 <see cref="Register"/> 开放注册（重复后端 fail-fast）。
    /// 查询未注册后端（依赖未接入/标识非法）由 <see cref="GetRequired"/> fail-fast 抛出 <see cref="GameException"/>，
    /// 避免静默降级导致块数据损坏。</para>
    /// </summary>
    public static class SaveSerializerRegistry
    {
        /// <summary>后端标识 → 序列化器实例表。</summary>
        private static readonly Dictionary<ESaveBackend, ISaveSerializer> s_Serializers = BuildBuiltInSerializers();

        /// <summary>注册表读写门（读路径可能在异步管线工作线程，读写并发须互斥）。</summary>
        private static readonly object s_Gate = new object();

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

        #region 开放注册 [OPEN REGISTRATION]

        /// <summary>
        /// 注册自定义序列化后端（重复后端 fail-fast——同标识两实现并存会使旧档还原结果不可预期）。
        /// </summary>
        /// <param name="serializer">序列化器实例（<see cref="ISaveSerializer.Backend"/> 提供注册标识）。</param>
        /// <exception cref="ArgumentNullException">序列化器为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">后端标识已注册，或试图占用保留的 <see cref="ESaveBackend.KeyValue"/>（组件捕获格式专用）。</exception>
        public static void Register(ISaveSerializer serializer)
        {
            if (serializer == null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            ESaveBackend backend = serializer.Backend;
            if (backend == ESaveBackend.KeyValue)
            {
                throw new ArgumentException("KeyValue backend is reserved for the component capture format and cannot be registered.", nameof(serializer));
            }

            lock (s_Gate)
            {
                if (s_Serializers.ContainsKey(backend))
                {
                    throw new ArgumentException(StringUtility.Format("Save serializer for backend '{0}' is already registered.", backend), nameof(serializer));
                }

                s_Serializers.Add(backend, serializer);
            }
        }

        /// <summary>
        /// 注销序列化后端。
        /// <para>注销内置后端会使依赖该后端的存量块在 <see cref="GetRequired"/> 处 fail-fast——仅测试与后端热替换场景使用。</para>
        /// </summary>
        /// <param name="backend">后端标识（保留的 <see cref="ESaveBackend.KeyValue"/> 恒返回 <c>false</c>）。</param>
        /// <returns>实际注销返回 <c>true</c>；后端未注册返回 <c>false</c>。</returns>
        public static bool Unregister(ESaveBackend backend)
        {
            if (backend == ESaveBackend.KeyValue)
            {
                return false;
            }

            lock (s_Gate)
            {
                return s_Serializers.Remove(backend);
            }
        }

        #endregion

        #region 查询 [QUERIES]

        /// <summary>
        /// 尝试获取指定后端的序列化器。
        /// </summary>
        /// <param name="backend">后端标识。</param>
        /// <param name="serializer">命中时的序列化器实例。</param>
        /// <returns>已注册返回 <c>true</c>。</returns>
        public static bool TryGet(ESaveBackend backend, out ISaveSerializer serializer)
        {
            lock (s_Gate)
            {
                return s_Serializers.TryGetValue(backend, out serializer);
            }
        }

        /// <summary>
        /// 获取指定后端的序列化器（未注册时 fail-fast——静默降级会导致块数据无法还原）。
        /// </summary>
        /// <param name="backend">后端标识。</param>
        /// <returns>序列化器实例。</returns>
        public static ISaveSerializer GetRequired(ESaveBackend backend)
        {
            lock (s_Gate)
            {
                if (!s_Serializers.TryGetValue(backend, out ISaveSerializer serializer))
                {
                    throw new GameException(StringUtility.Format("Save serializer for backend '{0}' is not registered.", backend));
                }

                return serializer;
            }
        }

        #endregion
    }
}
