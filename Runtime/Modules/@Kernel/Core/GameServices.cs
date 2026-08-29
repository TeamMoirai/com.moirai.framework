using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace Moirai.Atropos
{
    /// <summary>
    /// 静态服务管理外观。统一管理容器生命周期、服务查找、轮询驱动和拦截器。
    /// <para><b>线程契约</b>：所有公共方法仅限 Unity 主线程调用。
    /// 后台线程请通过 <c>MainThreadDispatcher.Post(Action)</c> / <c>MainThreadDispatcher.Send(Action)</c> 切回。</para>
    /// </summary>
    public static partial class GameServices
    {
        #region 状态 [STATE]

        private static int s_MainThreadId;

        private static ServiceWorld s_World;

        /// <summary>
        /// App 作用域是否活跃。
        /// </summary>
        public static bool HasApp => s_World?.HasScope(EServiceScopeKind.App) ?? false;

        /// <summary>
        /// Scene 作用域是否活跃。
        /// </summary>
        public static bool HasScene => s_World?.HasScope(EServiceScopeKind.Scene) ?? false;

        /// <summary>
        /// Gameplay 作用域是否活跃。
        /// </summary>
        public static bool HasGameplay => s_World?.HasScope(EServiceScopeKind.Gameplay) ?? false;

        #endregion

        #region 事件 [EVENTS]

        /// <summary>
        /// 服务注册完成（OnInit 已调用）后触发。
        /// </summary>
        public static event Action<IService, Type, EServiceScopeKind> onServiceRegistered;

        /// <summary>
        /// 服务注销完成（Shutdown 已调用）后触发。
        /// </summary>
        public static event Action<IService> onServiceUnregistered;

        #endregion

        #region 拦截器 [INTERCEPTORS]

        private static readonly List<IServiceInterceptor> s_Interceptors = new List<IServiceInterceptor>();

        /// <summary>
        /// 当前已注册的拦截器（只读视图）。
        /// </summary>
        public static IReadOnlyList<IServiceInterceptor> Interceptors => s_Interceptors;

        /// <summary>
        /// 是否存在已注册的拦截器。轮询热路径据此选择快/慢路径（无拦截器时跳过逐服务通知）。
        /// </summary>
        internal static bool HasInterceptors => s_Interceptors.Count > 0;

        /// <summary>
        /// 添加服务拦截器。按 <see cref="IServiceInterceptor.Priority"/> 降序插入。
        /// </summary>
        public static void AddInterceptor(IServiceInterceptor interceptor)
        {
            EnsureMainThread();
            if (interceptor == null) return;

            int priority = interceptor.Priority;
            int insertAt = s_Interceptors.Count;
            for (int i = 0; i < s_Interceptors.Count; i++)
            {
                if (priority > s_Interceptors[i].Priority) { insertAt = i; break; }
            }
            s_Interceptors.Insert(insertAt, interceptor);
        }

        /// <summary>
        /// 移除服务拦截器。
        /// </summary>
        public static void RemoveInterceptor(IServiceInterceptor interceptor)
        {
            EnsureMainThread();
            s_Interceptors.Remove(interceptor);
        }

        // ──── 拦截器与事件分发（由 ServiceScope 调用） ────

        internal static void InvokeRegistering(IService service, Type interfaceType, EServiceScopeKind scope)
        {
            if (s_Interceptors.Count == 0) return;
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceRegistering(service, interfaceType, scope);
        }

        internal static void InvokeRegistered(IService service, Type interfaceType, EServiceScopeKind scope)
        {
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceRegistered(service, interfaceType, scope);
            onServiceRegistered?.Invoke(service, interfaceType, scope);
        }

        internal static void InvokeUnregistered(IService service)
        {
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceUnregistered(service);
            onServiceUnregistered?.Invoke(service);
        }

        internal static void InvokeTick(IService service, float elapseSeconds, float realElapseSeconds)
        {
            if (s_Interceptors.Count == 0) return;
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceTick(service, elapseSeconds, realElapseSeconds);
        }

        internal static void InvokeShutdown(IService service)
        {
            if (s_Interceptors.Count == 0) return;
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceShutdown(service);
        }

        #endregion

        #region 初始化 [INITIALIZATION]

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureMainThreadId()
        {
            s_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        internal static void EnsureMainThread()
        {
            Assert.IsTrue(
                s_MainThreadId == 0 ||
                System.Threading.Thread.CurrentThread.ManagedThreadId == s_MainThreadId,
                "GameServices must only be used from the main thread. " +
                "From a background thread/callback, wrap the call with MainThreadDispatcher.Post/Send.");
        }

        #endregion

        #region 容器管理 [CONTAINER MANAGEMENT]


        /// <summary>
        /// 关闭指定作用域。服务按逆注册序（依赖方先）关闭。
        /// </summary>
        public static void ShutdownContainer(EServiceScopeKind scope)
        {
            EnsureMainThread();
            s_World?.ShutdownScope(scope);
        }

        /// <summary>
        /// 异步关闭指定作用域。对实现 <see cref="IAsyncShutdownService"/> 的服务先异步关闭，
        /// 再执行同步 <c>Shutdown</c>。
        /// </summary>
        /// <param name="scope">要关闭的作用域。</param>
        public static async UniTask ShutdownContainerAsync(EServiceScopeKind scope)
        {
            EnsureMainThread();
            if (s_World == null) return;
            await s_World.ShutdownScopeAsync(scope);
        }

        /// <summary>
        /// 关闭全部作用域。逆序：Gameplay → Scene → App（依赖方先于被依赖方释放）。
        /// </summary>
        public static void Shutdown()
        {
            EnsureMainThread();
            ShutdownContainer(EServiceScopeKind.Gameplay);
            ShutdownContainer(EServiceScopeKind.Scene);
            ShutdownContainer(EServiceScopeKind.App);
            s_World?.Dispose();
            s_World = null;
            ClearAll();
        }

        /// <summary>
        /// 异步关闭全部作用域。逆序：Gameplay → Scene → App。
        /// 对实现 <see cref="IAsyncShutdownService"/> 的服务先异步关闭。
        /// </summary>
        public static async UniTask ShutdownAsync()
        {
            EnsureMainThread();
            await ShutdownContainerAsync(EServiceScopeKind.Gameplay);
            await ShutdownContainerAsync(EServiceScopeKind.Scene);
            await ShutdownContainerAsync(EServiceScopeKind.App);
            s_World?.Dispose();
            s_World = null;
            ClearAll();
        }

        #endregion

        #region 重复契约策略 [DUPLICATE CONTRACT POLICY]

        // 编辑器/开发构建默认 Warn（意外抢占契约不再静默），发布构建默认 Skip（零运行时成本）。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static EDuplicateContractPolicy s_DuplicateContractPolicy = EDuplicateContractPolicy.Warn;
#else
        private static EDuplicateContractPolicy s_DuplicateContractPolicy = EDuplicateContractPolicy.Skip;
#endif

        /// <summary>
        /// 重复契约注册处置策略。仅作用于"同作用域内已占用契约再次显式注册不同实例"的场景；
        /// 同实例幂等与依赖链自动去重不受影响。
        /// </summary>
        public static EDuplicateContractPolicy DuplicateContractPolicy
        {
            get => s_DuplicateContractPolicy;
            set
            {
                EnsureMainThread();
                s_DuplicateContractPolicy = value;
            }
        }

        #endregion

        #region 运行时服务注册 [RUNTIME SERVICE REGISTRATION]

        /// <summary>
        /// 已注册服务表（作用域 → (契约类型 → 实例)）。重复注册幂等跳过的判断依据。
        /// </summary>
        private static readonly Dictionary<EServiceScopeKind, Dictionary<Type, IService>> s_Registered = new()
        {
            { EServiceScopeKind.App, new Dictionary<Type, IService>() },
            { EServiceScopeKind.Scene, new Dictionary<Type, IService>() },
            { EServiceScopeKind.Gameplay, new Dictionary<Type, IService>() },
        };

        /// <summary>
        /// 注册中栈——循环依赖检测依据。
        /// </summary>
        private static readonly Stack<Type> s_InFlight = new();

        /// <summary>
        /// 类型 → 依赖类型数组缓存（特性元数据仅读取一次）。
        /// <para>主线程专用（所有注册入口均经 <see cref="EnsureMainThread"/> 守卫），
        /// 随 <see cref="ClearAll"/> 清空——关闭 Domain Reload 的工程重启世界时不残留过期元数据。</para>
        /// </summary>
        private static readonly Dictionary<Type, Type[]> s_DependencyCache = new();

        /// <summary>
        /// 注册服务到指定作用域（统一入口）。
        /// <para>注册前读取实现类型的 <see cref="ServiceDependencyAttribute"/> 声明：依赖未注册时优先递归注册依赖，
        /// 全部依赖就绪后再注册当前服务（立即驱动 <c>OnInit</c>）。</para>
        /// <para>同实例重复注册幂等——直接跳过并返回既有实例；以<b>不同实例</b>抢占已占用契约时按
        /// <see cref="DuplicateContractPolicy"/> 处置（默认：开发期告警并保留既有实例，发布期静默）；
        /// 循环依赖注册期即抛 <see cref="GameException"/>。</para>
        /// <para>迭代中（Tick）调用时默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）；
        /// 传入 <see cref="EDeferMode.Throw"/> 则立即抛出异常。</para>
        /// </summary>
        /// <typeparam name="T">服务具体类型（契约即类型本身）。</typeparam>
        /// <param name="scope">目标作用域。</param>
        /// <param name="service">要注册的服务实例。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>注册的服务实例（重复注册时返回既有实例）。</returns>
        public static T RegisterService<T>(
            EServiceScopeKind scope,
            T service,
            EDeferMode deferMode = EDeferMode.Defer) where T : class, IService
        {
            EnsureMainThread();
            RegisterWithDependencies(scope, typeof(T), typeof(T), () => service, deferMode, service);
            return (T)s_Registered[scope][typeof(T)];
        }

        /// <summary>
        /// 以显式契约类型注册服务实例（运行时 Type 版本）。
        /// <para>用于跨作用域遮蔽同接口、以接口为契约注册等泛型推断不便的场景；
        /// 同一实例可依次以多个契约注册（多契约绑定）——首个调用创建条目，后续调用仅附加契约句柄。</para>
        /// <para>依赖声明始终从 <c>service.GetType()</c> 实现类型读取（契约是接口时依赖仍能自动预注册）。</para>
        /// </summary>
        /// <param name="scope">目标作用域。</param>
        /// <param name="contractType">契约类型（注册键与解析键）。</param>
        /// <param name="service">要注册的服务实例。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>注册的服务实例（重复注册时返回既有实例）。</returns>
        public static IService RegisterService(
            EServiceScopeKind scope,
            Type contractType,
            IService service,
            EDeferMode deferMode = EDeferMode.Defer)
        {
            EnsureMainThread();
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (service == null) throw new ArgumentNullException(nameof(service));
            RegisterWithDependencies(scope, contractType, service.GetType(), () => service, deferMode, service);
            return s_Registered[scope][contractType];
        }

        /// <summary>
        /// 递归注册：依赖预注册 → 注册当前服务。
        /// <para>①契约已注册跳过（嵌套依赖重复注册免疫；显式传入的不同实例按
        /// <see cref="DuplicateContractPolicy"/> 处置）②栈内检测循环③按声明序递归注册依赖④注册并初始化自身；
        /// 同一实例已在作用域中以其他契约注册时，仅附加新契约绑定（不重复初始化/关闭）。</para>
        /// </summary>
        /// <param name="scope">目标作用域。</param>
        /// <param name="contractType">契约类型（注册表与容器的键）。</param>
        /// <param name="implType">实现类型（<see cref="ServiceDependencyAttribute"/> 读取来源与循环检测键）。</param>
        /// <param name="factory">实例工厂（依赖就绪后调用）。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <param name="explicitInstance">
        /// 调用方显式传入的实例；用于区分"同实例幂等"与"不同实例抢占契约"。依赖链递归路径传 null（去重始终静默）。
        /// </param>
        private static void RegisterWithDependencies(
            EServiceScopeKind scope,
            Type contractType,
            Type implType,
            Func<IService> factory,
            EDeferMode deferMode,
            IService explicitInstance = null)
        {
            var registry = s_Registered[scope];

            // ① 去重：嵌套依赖链中已注册的直接返回。
            // 显式传入不同实例 → 按策略处置（Warn/Throw 可见，Skip 静默）；同实例与依赖链路径保持静默幂等
            if (registry.TryGetValue(contractType, out IService existingService))
            {
                if (explicitInstance != null && !ReferenceEquals(existingService, explicitInstance))
                    ApplyDuplicateContractPolicy(scope, contractType, existingService);
                return;
            }

            // ② 循环依赖检测：当前实现类型已在注册栈中即构成环
            if (s_InFlight.Contains(implType))
            {
                throw new GameException(StringUtility.Format(
                    "Circular service dependency detected: {0} -> {1}",
                    string.Join(" -> ", s_InFlight), implType.FullName));
            }

            s_InFlight.Push(implType);
            try
            {
                // ③ 按声明序递归预注册依赖（依赖实例由框架工厂表创建）
                Type[] dependencies = GetDeclaredDependencies(implType);
                for (int i = 0; i < dependencies.Length; i++)
                {
                    Type depType = dependencies[i];
                    if (!registry.ContainsKey(depType))
                    {
                        // 递归注册依赖——工厂延迟调用，循环检测在递归入口的 s_InFlight 检查中触发
                        RegisterWithDependencies(scope, depType, depType, () => CreateDefaultService(depType), deferMode);
                    }
                }

                // ④ 依赖就绪，创建实例并注册（立即 OnInit + 加入轮询列表）
                IService instance = factory();
                s_World ??= new ServiceWorld();
                ServiceScope targetScope = s_World.EnsureScope(scope);

                if (targetScope.TryGet(contractType, out IService existing))
                {
                    // 容器已有同契约实例——采纳既有实例，幂等
                    registry[contractType] = existing;
                    return;
                }

                if (targetScope.Contains(instance))
                {
                    // 同实例已在本作用域以其他契约注册——仅附加新契约绑定，不新建条目
                    targetScope.BindAdditionalContractRuntime(contractType, instance, deferMode);
                    registry[contractType] = instance;
                    return;
                }

                // 用显式契约类型注册，避免泛型推断为 IService 基类
                targetScope.RegisterRuntime(contractType, instance, deferMode);
                registry[contractType] = instance;
            }
            finally
            {
                s_InFlight.Pop();
            }
        }

        /// <summary>
        /// 按 <see cref="DuplicateContractPolicy"/> 处置"不同实例抢占已占用契约"的冲突。
        /// </summary>
        private static void ApplyDuplicateContractPolicy(
            EServiceScopeKind scope,
            Type contractType,
            IService existing)
        {
            switch (s_DuplicateContractPolicy)
            {
                case EDuplicateContractPolicy.Throw:
                    throw new GameException(StringUtility.Format(
                        "Duplicate contract registration rejected: contract '{0}' is already bound to '{1}' in {2} scope.",
                        contractType.FullName, existing.GetType().FullName, scope));

                case EDuplicateContractPolicy.Warn:
                    LogUtility.Warning(
                        "Duplicate contract registration discarded: contract '{0}' is already bound to '{1}' in {2} scope; the new instance will be ignored.",
                        contractType.FullName, existing.GetType().FullName, scope);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// 运行时注销并关闭指定作用域中的单个服务。
        /// <para>触发 <c>Shutdown</c> 并从注册表移除；同步清理内部已注册表，
        /// 注销后可重新以同契约注册全新实例。</para>
        /// <para>迭代中（Tick）调用时默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）；
        /// 传入 <see cref="EDeferMode.Throw"/> 则立即抛出异常。</para>
        /// </summary>
        /// <typeparam name="T">服务契约类型。</typeparam>
        /// <param name="scope">目标作用域。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>成功注销返回 true；未找到返回 false。</returns>
        public static bool UnregisterService<T>(
            EServiceScopeKind scope,
            EDeferMode deferMode = EDeferMode.Defer) where T : class, IService
        {
            return UnregisterService(scope, typeof(T), deferMode);
        }

        /// <summary>
        /// 以显式契约类型运行时注销单个服务（运行时 Type 版本）。
        /// <para>触发 <c>Shutdown</c> 并从注册表移除；同步清理内部已注册表。</para>
        /// </summary>
        /// <param name="scope">目标作用域。</param>
        /// <param name="contractType">契约类型（注册键）。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>成功注销返回 true；未找到返回 false。</returns>
        public static bool UnregisterService(
            EServiceScopeKind scope,
            Type contractType,
            EDeferMode deferMode = EDeferMode.Defer)
        {
            EnsureMainThread();
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (s_World == null) return false;
            if (!s_World.TryGetScope(scope, out var targetScope)) return false;

            bool removed = targetScope.UnregisterRuntime(contractType, deferMode);
            if (removed) s_Registered[scope].Remove(contractType);
            return removed;
        }

        /// <summary>
        /// 获取内部 <see cref="ServiceWorld"/> 实例。
        /// </summary>
        internal static ServiceWorld GetWorldInternal() => s_World;

        #endregion

        #region 查找 [LOOKUP]

        /// <summary>
        /// 获取服务（未找到抛 <see cref="GameException"/>）。
        /// <para>按 Gameplay &gt; Scene &gt; App 优先级返回最优服务；容器未构建时同样抛出。</para>
        /// </summary>
        /// <typeparam name="T">服务契约类型。</typeparam>
        public static T GetRequiredService<T>() where T : class
        {
            if (s_World != null && s_World.TryGet(out T service)) return service;
            throw new GameException(StringUtility.Format(
                "Service '{0}' was not found in any active scope.", typeof(T).FullName));
        }

        /// <summary>
        /// 获取服务（未找到返回 null）。
        /// <para>按 Gameplay &gt; Scene &gt; App 优先级返回最优服务；容器未构建时返回 null。</para>
        /// </summary>
        /// <typeparam name="T">服务契约类型。</typeparam>
        public static T GetService<T>() where T : class
        {
            return s_World != null && s_World.TryGet(out T service) ? service : null;
        }

        /// <summary>
        /// 尝试获取服务。
        /// <para>按 Gameplay &gt; Scene &gt; App 优先级返回最优服务；容器未构建时返回 false。</para>
        /// </summary>
        /// <typeparam name="T">服务契约类型。</typeparam>
        /// <param name="service">获取到的服务；未找到时为 null。</param>
        public static bool TryGetService<T>(out T service) where T : class
        {
            if (s_World != null && s_World.TryGet(out service)) return true;
            service = null;
            return false;
        }

        #endregion

        #region 依赖声明 [DEPENDENCY DECLARATION]

        /// <summary>
        /// 读取类型的 <see cref="ServiceDependencyAttribute"/> 声明（带缓存）。
        /// </summary>
        private static Type[] GetDeclaredDependencies(Type serviceType)
        {
            if (s_DependencyCache.TryGetValue(serviceType, out Type[] cached))
                return cached;

            object[] attrs = serviceType.GetCustomAttributes(typeof(ServiceDependencyAttribute), false);
            if (attrs.Length == 0)
            {
                s_DependencyCache[serviceType] = Array.Empty<Type>();
                return Array.Empty<Type>();
            }

            int total = 0;
            for (int i = 0; i < attrs.Length; i++)
            {
                total += ((ServiceDependencyAttribute)attrs[i]).DependencyTypes.Length;
            }

            var deps = new Type[total];
            int offset = 0;
            for (int i = 0; i < attrs.Length; i++)
            {
                Type[] types = ((ServiceDependencyAttribute)attrs[i]).DependencyTypes;
                Array.Copy(types, 0, deps, offset, types.Length);
                offset += types.Length;
            }

            s_DependencyCache[serviceType] = deps;
            return deps;
        }

        #endregion

        #region 轮询驱动 [TICK DRIVERS]

        public static void Tick(float elapseSeconds, float realElapseSeconds)
            => s_World?.Tick(elapseSeconds, realElapseSeconds);

        public static void FixedTick(float elapseSeconds, float realElapseSeconds)
            => s_World?.FixedTick(elapseSeconds, realElapseSeconds);

        public static void LateTick(float elapseSeconds, float realElapseSeconds)
            => s_World?.LateTick(elapseSeconds, realElapseSeconds);

        public static void DrawGizmos()
            => s_World?.DrawGizmos();

        #endregion

        #region 状态辅助 [STATE HELPERS]

        internal static void SetState(IService service, EServiceState state)
        {
            if (service is ServiceBase sb) sb.State = state;
        }

        internal static EServiceState GetState(IService service)
        {
            if (service is ServiceBase sb) return sb.State;
            return EServiceState.Created;
        }

        #endregion

        #region 清理 [CLEANUP]

        private static void ClearAll()
        {
            // 拦截器和事件在全部作用域关闭后清理——此时无活跃服务可触发事件
            s_Interceptors.Clear();
            onServiceRegistered = null;
            onServiceUnregistered = null;

            // 各作用域注册表、循环检测栈与依赖元数据缓存同步清空——
            // 关闭后可重新注册重建（域重载安全；关闭 Domain Reload 的工程亦不残留过期缓存）
            foreach (var registry in s_Registered.Values)
                registry.Clear();
            s_InFlight.Clear();
            s_DependencyCache.Clear();

            // MemoryPool 和 MarshalUtility 缓存清理在全部服务关闭后执行——
            // 此时无活跃的池化对象引用（所有 Service 已 Shutdown），安全清空。
            // 这确保域重载或重新初始化时不会残留过期对象。
            MemoryPool.ClearAll();
        }

        #endregion
    }
}
