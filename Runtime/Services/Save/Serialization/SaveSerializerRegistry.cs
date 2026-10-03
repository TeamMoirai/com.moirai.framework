using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档序列化后端注册表：后端标识 → 序列化器实例的静态查询点。
    /// </summary>
    /// <remarks>
    /// 内置后端在静态构造期注册；自定义后端经 <see cref="Register"/> 开放注册（重复后端 fail-fast）。 <br />
    /// 查询未注册后端（依赖未接入/标识非法）由 <see cref="GetRequired"/> 抛 <see cref="GameException"/> fail-fast，避免静默降级导致块数据损坏。 <br />
    /// 键是 <see cref="SaveBackendIds"/> 那一套 2 字节线标识，取值不受框架枚举封版——项目新增后端直接注册自己的 ID 即可。
    /// </remarks>
    public static class SaveSerializerRegistry
    {
        /// <summary>后端标识 → 序列化器实例表。</summary>
        private static readonly Dictionary<ushort, ISaveSerializer> s_Serializers = BuildBuiltInSerializers();

        /// <summary>注册表读写门（读路径可能在异步管线工作线程，读写并发须互斥）。</summary>
        private static readonly object s_Gate = new object();

        /// <summary>
        /// 构建内置序列化器表（新增内置后端在此追加注册）。
        /// </summary>
        /// <returns>内置序列化器表。</returns>
        private static Dictionary<ushort, ISaveSerializer> BuildBuiltInSerializers()
        {
            var serializers = new Dictionary<ushort, ISaveSerializer>
            {
                { SaveBackendIds.JSON, new JsonSaveSerializer() },
#if MESSAGEPACK_INSTALLED
                { SaveBackendIds.MESSAGE_PACK, new MessagePackSaveSerializer() },
#endif
#if MEMORYPACK_INSTALLED
                { SaveBackendIds.MEMORY_PACK, new MemoryPackSaveSerializer() },
#endif
#if PROTOBUF_INSTALLED
                { SaveBackendIds.PROTOBUF, new ProtobufSaveSerializer() },
#endif
            };
            return serializers;
        }

        /// <summary>是否为框架内建后端标识（与依赖是否接入无关——占号即意味着将来会有实现落到它上面）。</summary>
        /// <param name="backendId">待判定的后端标识。</param>
        /// <returns>属内建集合返回 <c>true</c>。</returns>
        private static bool IsBuiltInBackendId(ushort backendId) =>
            backendId == SaveBackendIds.JSON || backendId == SaveBackendIds.MESSAGE_PACK
            || backendId == SaveBackendIds.MEMORY_PACK || backendId == SaveBackendIds.PROTOBUF;

        #region 开放注册 [OPEN REGISTRATION]

        /// <summary>
        /// 注册自定义序列化后端（重复后端 fail-fast——同标识两实现并存会使旧档还原结果不可预期）。
        /// </summary>
        /// <param name="serializer">序列化器实例（<see cref="ISaveSerializer.BackendId"/> 提供注册标识）。</param>
        /// <exception cref="ArgumentNullException">序列化器为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">后端标识已注册；或落在保留区 <c>0</c>~<see cref="SaveBackendIds.RESERVED_MAX"/>
        /// 内却不是内建标识（<see cref="SaveBackendIds.KEY_VALUE"/> 为组件捕获格式专用）。</exception>
        public static void Register(ISaveSerializer serializer)
        {
            if (serializer == null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            ushort backendId = serializer.BackendId;
            if (backendId == SaveBackendIds.KEY_VALUE)
            {
                throw new ArgumentException("KeyValue backend is reserved for the component capture format and cannot be registered.", nameof(serializer));
            }

            // 保留区内只承认内建标识：否则将来内置扩号会与项目自定义后端静默相撞
            if (backendId <= SaveBackendIds.RESERVED_MAX && !IsBuiltInBackendId(backendId))
            {
                throw new ArgumentException(StringUtility.Format(
                    "Backend id '{0}' is inside the framework reserved range 0-{1};" +
                    " allocate custom backends from 1000.",
                    backendId, SaveBackendIds.RESERVED_MAX), nameof(serializer));
            }

            lock (s_Gate)
            {
                if (s_Serializers.ContainsKey(backendId))
                {
                    throw new ArgumentException(StringUtility.Format(
                        "Save serializer for backend '{0}' is already registered.",
                        SaveBackendIds.DisplayName(backendId)), nameof(serializer));
                }

                s_Serializers.Add(backendId, serializer);
            }
        }

        /// <summary>
        /// 注销序列化后端。
        /// </summary>
        /// <remarks>注销内置后端会使依赖该后端的存量块在 <see cref="GetRequired"/> 处 fail-fast——仅测试与后端热替换场景使用。</remarks>
        /// <param name="backendId">后端标识（保留的 <see cref="SaveBackendIds.KEY_VALUE"/> 恒返回 <c>false</c>）。</param>
        /// <returns>实际注销返回 <c>true</c>；后端未注册返回 <c>false</c>。</returns>
        public static bool Unregister(ushort backendId)
        {
            if (backendId == SaveBackendIds.KEY_VALUE)
            {
                return false;
            }

            lock (s_Gate)
            {
                return s_Serializers.Remove(backendId);
            }
        }

        #endregion

        #region 查询 [QUERIES]

        /// <summary>
        /// 尝试获取指定后端的序列化器。
        /// </summary>
        /// <param name="backendId">后端标识。</param>
        /// <param name="serializer">命中时的序列化器实例。</param>
        /// <returns>已注册返回 <c>true</c>。</returns>
        public static bool TryGet(ushort backendId, out ISaveSerializer serializer)
        {
            lock (s_Gate)
            {
                return s_Serializers.TryGetValue(backendId, out serializer);
            }
        }

        /// <summary>
        /// 获取指定后端的序列化器（未注册时 fail-fast——静默降级会导致块数据无法还原）。
        /// </summary>
        /// <param name="backendId">后端标识。</param>
        /// <returns>序列化器实例。</returns>
        public static ISaveSerializer GetRequired(ushort backendId)
        {
            lock (s_Gate)
            {
                if (!s_Serializers.TryGetValue(backendId, out ISaveSerializer serializer))
                {
                    throw new GameException(StringUtility.Format("Save serializer for backend '{0}' is not registered.",
                        SaveBackendIds.DisplayName(backendId)));
                }

                return serializer;
            }
        }

        #endregion
    }
}
