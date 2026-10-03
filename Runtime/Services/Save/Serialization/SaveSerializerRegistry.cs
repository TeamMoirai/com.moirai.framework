using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档序列化后端注册表：后端标识 → 序列化器的静态查询点。
    /// </summary>
    /// <remarks>
    /// 注册有两种形态，落进同一套表：<see cref="Register(ushort, Type)"/> 只挂 ID→类型，首次被查询才实例化（实现均无状态，一个会话内每后端至多一个对象）—— <br />
    /// <c>[RegisterSerializer]</c> 由 SaveServiceCodegen 生成的模块初始化器走的就是这条，框架内置后端与项目自定义后端在此完全同形，注册表不再硬编码任何实现。 <br />
    /// <see cref="Register(ISaveSerializer)"/> / <see cref="Register{T}"/> 登记现成实例（运行期注入与测试用）。 <br />
    /// 查询未注册后端（依赖未接入/标识非法）由 <see cref="GetRequired"/> 抛 <see cref="GameException"/> fail-fast，避免静默降级导致块数据损坏。 <br />
    /// 键是 <see cref="SaveBackendIds"/> 那一套 2 字节线标识，取值不受框架枚举封版——项目新增后端直接登记自己的 ID 即可。 <br />
    /// <see cref="Unregister"/> 连 ID→类型记录一并摘除：只删实例会让下一次查询按类型表把它悄悄重建出来，注销等于没生效。
    /// </remarks>
    public static class SaveSerializerRegistry
    {
        /// <summary>后端标识 → 序列化器实例（登记时即给出实例的，与按需实例化出来的落同一张表）。</summary>
        private static readonly Dictionary<ushort, ISaveSerializer> s_Serializers = new Dictionary<ushort, ISaveSerializer>();

        /// <summary>后端标识 → 尚未实例化的实现类型（声明式注册落点；首次查询时取出、实例化并从本表移入 <see cref="s_Serializers"/>）。</summary>
        private static readonly Dictionary<ushort, Type> s_PendingTypes = new Dictionary<ushort, Type>();

        /// <summary>注册表读写门（读路径可能在异步管线工作线程，读写并发须互斥）。</summary>
        private static readonly object s_Gate = new object();

        /// <summary>
        /// 取后端实例：实例表命中直接返回，未命中时按待实例化类型表建一次并回填。
        /// </summary>
        /// <remarks>调用方须持 <see cref="s_Gate"/>。</remarks>
        /// <param name="backendId">后端标识。</param>
        /// <param name="serializer">命中时的序列化器实例。</param>
        /// <returns>该后端可服务返回 <c>true</c>。</returns>
        private static bool TryResolve(ushort backendId, out ISaveSerializer serializer)
        {
            if (s_Serializers.TryGetValue(backendId, out serializer))
            {
                return true;
            }

            if (!s_PendingTypes.TryGetValue(backendId, out Type pendingType))
            {
                return false;
            }

            s_PendingTypes.Remove(backendId);
            serializer = (ISaveSerializer)Activator.CreateInstance(pendingType, true);
            s_Serializers[backendId] = serializer;
            return true;
        }

        /// <summary>是否为框架占号的后端标识。</summary>
        /// <remarks>占号与依赖是否接入无关（占号即意味着将来会有实现落到它上面），故与实现类型表无关——那张表现在由注册动作自己填。</remarks>
        /// <param name="backendId">待判定的后端标识。</param>
        /// <returns>属框架占号集合返回 <c>true</c>。</returns>
        private static bool IsFrameworkOwnedBackendId(ushort backendId) =>
            backendId == SaveBackendIds.JSON || backendId == SaveBackendIds.MESSAGE_PACK
            || backendId == SaveBackendIds.MEMORY_PACK || backendId == SaveBackendIds.PROTOBUF;

        #region 开放注册 [OPEN REGISTRATION]

        /// <summary>
        /// 注册自定义序列化后端（重复后端 fail-fast——同标识两实现并存会使旧档还原结果不可预期）。
        /// </summary>
        /// <param name="serializer">序列化器实例（<see cref="ISaveSerializer.BackendId"/> 提供注册标识）。</param>
        /// <exception cref="ArgumentNullException">序列化器为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">后端标识已被占用（实例表或内置类型表任一处命中）；或落在保留区 <c>0</c>~<see cref="SaveBackendIds.RESERVED_MAX"/>
        /// 内却不是框架占号标识（<see cref="SaveBackendIds.KEY_VALUE"/> 为组件捕获格式专用）。</exception>
        public static void Register(ISaveSerializer serializer)
        {
            if (serializer == null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            ushort backendId = serializer.BackendId;
            EnsureIdAcceptable(backendId, nameof(serializer));
            lock (s_Gate)
            {
                EnsureNotDuplicated(backendId, nameof(serializer));
                s_Serializers[backendId] = serializer;
            }
        }

        /// <summary>
        /// 声明式注册序列化后端：只登记 ID→类型，首次查询到该后端才实例化（重复后端 fail-fast）。
        /// </summary>
        /// <remarks>
        /// <c>[RegisterSerializer]</c> 生成的模块初始化器走的就是这条，框架内置实现与项目自定义实现在此同形。 <br />
        /// 标识由调用方给出（生成器静态取自实现的 <see cref="ISaveSerializer.BackendId"/>），实例化用可访问的（含非公共）无参构造。 <br />
        /// **占号冲突与重号不抛**：本方法跑在模块初始化期（<c>&lt;Module&gt;.cctor</c>），在那里抛出会连累整个编辑器——实测 Unity 的源生成扫描会因 <c>TypeInitializationException</c> 原生崩溃。 <br />
        /// 因此撞号只记一次 Fatal 并保留先到那份；先到者取决于各程序集初始化顺序，跨程序集撞号要靠编译期判据与评审消除。 <br />
        /// 形状不合法（null / 非具体 ISaveSerializer 实现）仍抛：那是手写调用的编程错误，生成器侧由 MIRAI309 在编译期挡。
        /// </remarks>
        /// <param name="backendId">后端标识（容器逐块落盘的 2 字节 ID）。</param>
        /// <param name="serializerType">序列化器实现类型。</param>
        public static void Register(ushort backendId, Type serializerType)
        {
            if (serializerType == null)
            {
                throw new ArgumentNullException(nameof(serializerType));
            }

            if (serializerType.IsAbstract || !typeof(ISaveSerializer).IsAssignableFrom(serializerType))
            {
                throw new ArgumentException(StringUtility.Format(
                    "Type '{0}' is not a concrete ISaveSerializer implementation.", serializerType.FullName), nameof(serializerType));
            }

            string rejection = DescribeIdRejection(backendId);
            if (rejection != null)
            {
                LogUtility.Fatal("[SaveService] {0}; declaration from '{1}' is dropped.", rejection, serializerType.FullName);
                return;
            }

            lock (s_Gate)
            {
                string incumbentName = s_Serializers.TryGetValue(backendId, out ISaveSerializer serving)
                    ? serving.GetType().FullName
                    : (s_PendingTypes.TryGetValue(backendId, out Type pending) ? pending.FullName : null);
                if (incumbentName != null)
                {
                    LogUtility.Fatal("[SaveService] Backend id '{0}' is already served by '{1}'; declaration from '{2}' is dropped." +
                                     " Two implementations on one wire id make existing saves unreadable.",
                        SaveBackendIds.DisplayName(backendId), incumbentName, serializerType.FullName);
                    return;
                }

                s_PendingTypes[backendId] = serializerType;
            }
        }

        /// <summary>
        /// 注册自定义序列化后端（由框架实例化，重复后端 fail-fast）。
        /// </summary>
        /// <remarks>实现须有公共无参构造；<typeparamref name="T"/> 自述的 <see cref="ISaveSerializer.BackendId"/> 即注册标识。</remarks>
        /// <typeparam name="T">序列化器实现类型。</typeparam>
        public static void Register<T>() where T : ISaveSerializer, new()
        {
            Register(new T());
        }

        /// <summary>
        /// 注册前的公共门禁：组件捕获专用标识与保留区占号判据（实例登记路径拒绝即抛）。
        /// </summary>
        /// <remarks>重号判据要持门后再查，不在这里。</remarks>
        /// <param name="backendId">后端标识。</param>
        /// <param name="paramName">拒绝时随异常带出的参数名。</param>
        private static void EnsureIdAcceptable(ushort backendId, string paramName)
        {
            string rejection = DescribeIdRejection(backendId);
            if (rejection != null)
            {
                throw new ArgumentException(rejection + " and cannot be registered.", paramName);
            }
        }

        /// <summary>
        /// 占号判据的拒绝理由：组件捕获格式专用标识，或落在框架保留区却不是占号标识。
        /// </summary>
        /// <remarks>返回 <c>null</c> 即该标识可登记。同一份判据既给抛出路径也用给声明式路径（后者记 Fatal）。</remarks>
        /// <param name="backendId">后端标识。</param>
        /// <returns>拒绝理由；可登记时为 <c>null</c>。</returns>
        private static string DescribeIdRejection(ushort backendId)
        {
            if (backendId == SaveBackendIds.KEY_VALUE)
            {
                return "KeyValue backend is reserved for the component capture format";
            }

            // 保留区内只承认框架占号：否则将来内置扩号会与项目自定义后端静默相撞
            if (backendId <= SaveBackendIds.RESERVED_MAX && !IsFrameworkOwnedBackendId(backendId))
            {
                return StringUtility.Format(
                    "Backend id '{0}' is inside the framework reserved range 0-{1};" +
                    " custom backends start at 1000, leaving headroom above the enforced range",
                    backendId, SaveBackendIds.RESERVED_MAX);
            }

            return null;
        }

        /// <summary>
        /// 持门后的重号判据：只查实例表会让同标识的实现静默盖掉那份待实例化的类型登记。
        /// </summary>
        /// <param name="backendId">后端标识。</param>
        /// <param name="paramName">拒绝时随异常带出的参数名。</param>
        private static void EnsureNotDuplicated(ushort backendId, string paramName)
        {
            if (s_Serializers.ContainsKey(backendId) || s_PendingTypes.ContainsKey(backendId))
            {
                throw new ArgumentException(StringUtility.Format(
                    "Save serializer for backend '{0}' is already registered.",
                    SaveBackendIds.DisplayName(backendId)), paramName);
            }
        }

        /// <summary>
        /// 注销序列化后端。
        /// </summary>
        /// <remarks>注销会连 ID→类型记录一起摘除，依赖该后端的存量块此后在 <see cref="GetRequired"/> 处 fail-fast——仅测试与后端热替换场景使用。</remarks>
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
                // 两张表一起摘：只删实例的话，下一次查询会按类型记录把它重新实例化出来
                bool removedSerializer = s_Serializers.Remove(backendId);
                bool removedPendingType = s_PendingTypes.Remove(backendId);
                return removedSerializer || removedPendingType;
            }
        }

        #endregion

        #region 查询 [QUERIES]

        /// <summary>
        /// 尝试获取指定后端的序列化器（按类型登记的实现在此处完成首次实例化）。
        /// </summary>
        /// <param name="backendId">后端标识。</param>
        /// <param name="serializer">命中时的序列化器实例。</param>
        /// <returns>已注册返回 <c>true</c>。</returns>
        public static bool TryGet(ushort backendId, out ISaveSerializer serializer)
        {
            lock (s_Gate)
            {
                return TryResolve(backendId, out serializer);
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
                if (!TryResolve(backendId, out ISaveSerializer serializer))
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
