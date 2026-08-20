using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace Moirai.Atropos
{
    /// <summary>
    /// 静态服务管理中心。统一管理服务的注册、获取、注销、轮询驱动与作用域关闭。
    /// <para><b>线程契约</b>：所有公共方法仅限 Unity 主线程调用。
    /// 后台线程请通过 <see cref="MainThreadDispatcher.Post(Action)"/> / <see cref="MainThreadDispatcher.Send(Action)"/> 切回。</para>
    /// </summary>
    public static partial class GameServices
    {
        #region 常量与状态 [CONSTANTS / STATE]

        private const int ScopeSlotCount = 3;

        private static int s_MainThreadId;
        private static readonly ServiceScope[] s_ScopeContainers = new ServiceScope[ScopeSlotCount];
        private static readonly Dictionary<RuntimeTypeHandle, ScopeBindings> s_ServiceMaps = new();
        private static readonly List<IAsyncInitService> s_AsyncInitBuffer = new();
        private static readonly List<IServiceInterceptor> s_Interceptors = new();

        #endregion

        #region 事件 [EVENTS]

        /// <summary>服务注册完成（OnInit 已调用）后触发。</summary>
        public static event Action<IService, Type, EServiceScopeKind> ServiceRegistered;

        /// <summary>服务注销完成（Shutdown 已调用）后触发。</summary>
        public static event Action<IService> ServiceUnregistered;

        #endregion

        #region 拦截器 [INTERCEPTORS]

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

        /// <summary>移除服务拦截器。</summary>
        public static void RemoveInterceptor(IServiceInterceptor interceptor)
        {
            EnsureMainThread();
            s_Interceptors.Remove(interceptor);
        }

        /// <summary>当前已注册的拦截器（只读视图）。</summary>
        public static IReadOnlyList<IServiceInterceptor> Interceptors => s_Interceptors;

        // --- 由 ServiceScope 调用的内部拦截器分发 ---

        internal static void InvokeRegistering(IService service, Type interfaceType, EServiceScopeKind scope)
        {
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceRegistering(service, interfaceType, scope);
        }

        internal static void InvokeRegistered(IService service, Type interfaceType, EServiceScopeKind scope)
        {
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceRegistered(service, interfaceType, scope);
        }

        internal static void InvokeUnregistering(IService service)
        {
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceUnregistering(service);
        }

        internal static void InvokeUnregistered(IService service)
        {
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceUnregistered(service);
        }

        internal static void InvokeTick(IService service, float elapseSeconds, float realElapseSeconds)
        {
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceTick(service, elapseSeconds, realElapseSeconds);
        }

        internal static void InvokeShutdown(IService service)
        {
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
            EnsureScope(EServiceScopeKind.App);
        }

        private static void EnsureMainThread()
        {
            Assert.IsTrue(
                s_MainThreadId == 0 || System.Threading.Thread.CurrentThread.ManagedThreadId == s_MainThreadId,
                "GameServices must only be used from the main thread. " +
                "From a background thread/callback, wrap the call with MainThreadDispatcher.Post/Send.");
        }

        #endregion

        #region 作用域管理 [SCOPE MANAGEMENT]

        internal static ServiceScope EnsureScope(EServiceScopeKind kind)
        {
            int idx = (int)kind;
            if (s_ScopeContainers[idx] == null || s_ScopeContainers[idx].IsDisposed)
                s_ScopeContainers[idx] = new ServiceScope(kind, kind.ToString());
            return s_ScopeContainers[idx];
        }

        public static void ShutdownScope(EServiceScopeKind scope)
        {
            EnsureMainThread();
            int idx = (int)scope;
            if (s_ScopeContainers[idx] == null) return;
            s_ScopeContainers[idx].Dispose();
            s_ScopeContainers[idx] = null;
        }

        public static void Shutdown()
        {
            EnsureMainThread();
            // 逆序关闭：Gameplay → Scene → App，保证依赖方先于被依赖方释放
            ShutdownScope(EServiceScopeKind.Gameplay);
            ShutdownScope(EServiceScopeKind.Scene);
            ShutdownScope(EServiceScopeKind.App);
            ClearAll();
        }

        #endregion

        #region 全局映射 [GLOBAL MAP]

        // --- 由 ServiceScope 调用的内部方法 ---

        internal static void AddToGlobalMap(RuntimeTypeHandle handle, IService service, EServiceScopeKind scope)
        {
            if (!s_ServiceMaps.TryGetValue(handle, out var bindings))
            {
                bindings = new ScopeBindings();
                s_ServiceMaps[handle] = bindings;
            }
            switch (scope)
            {
                case EServiceScopeKind.App: bindings.App = service; break;
                case EServiceScopeKind.Scene: bindings.Scene = service; break;
                case EServiceScopeKind.Gameplay: bindings.Gameplay = service; break;
            }
        }

        internal static void RemoveFromGlobalMap(RuntimeTypeHandle handle, IService service, EServiceScopeKind scope)
        {
            if (!s_ServiceMaps.TryGetValue(handle, out var bindings)) return;
            switch (scope)
            {
                case EServiceScopeKind.App: if (ReferenceEquals(bindings.App, service)) bindings.App = null; break;
                case EServiceScopeKind.Scene: if (ReferenceEquals(bindings.Scene, service)) bindings.Scene = null; break;
                case EServiceScopeKind.Gameplay: if (ReferenceEquals(bindings.Gameplay, service)) bindings.Gameplay = null; break;
            }
            if (bindings.IsEmpty) s_ServiceMaps.Remove(handle);
        }

        internal static void SetContext(IService service, ServiceScope scope)
        {
            var ctx = new ServiceContext(s_ServiceMaps, scope);
            if (service is ServiceBase sb) sb.SetContext(ctx);
            else if (service is ServiceMonoBase mono) mono.SetContext(ctx);
        }

        internal static bool IsRegistered(Type interfaceType)
            => s_ServiceMaps.TryGetValue(interfaceType.TypeHandle, out var bindings) && !bindings.IsEmpty;

        internal static void SetState(IService service, EServiceState state)
        {
            if (service is ServiceBase sb) sb.State = state;
            else if (service is ServiceMonoBase mono) mono.State = state;
        }

        internal static EServiceState GetState(IService service)
        {
            if (service is ServiceBase sb) return sb.State;
            if (service is ServiceMonoBase mono) return mono.State;
            return EServiceState.Created;
        }

        internal static void RaiseServiceRegistered(IService service, Type interfaceType, EServiceScopeKind scope)
            => ServiceRegistered?.Invoke(service, interfaceType, scope);

        internal static void RaiseServiceUnregistered(IService service)
            => ServiceUnregistered?.Invoke(service);

        #endregion

        #region 诊断 [DIAGNOSTICS]

        internal static bool IsIterating
        {
            get
            {
                for (int i = 0; i < ScopeSlotCount; i++)
                {
                    if (s_ScopeContainers[i] != null && s_ScopeContainers[i].IsIterating)
                        return true;
                }
                return false;
            }
        }

        internal static int PendingChangesCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < ScopeSlotCount; i++)
                {
                    if (s_ScopeContainers[i] != null)
                        count += s_ScopeContainers[i].PendingChangesCount;
                }
                return count;
            }
        }

        #endregion

        #region 轮询驱动 [TICK DRIVERS]

        public static void Tick(float elapseSeconds, float realElapseSeconds)
        {
            for (int i = 0; i < ScopeSlotCount; i++)
                s_ScopeContainers[i]?.Tick(elapseSeconds, realElapseSeconds);
        }

        public static void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            for (int i = 0; i < ScopeSlotCount; i++)
                s_ScopeContainers[i]?.FixedTick(elapseSeconds, realElapseSeconds);
        }

        public static void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            for (int i = 0; i < ScopeSlotCount; i++)
                s_ScopeContainers[i]?.LateTick(elapseSeconds, realElapseSeconds);
        }

        public static void DrawGizmos()
        {
            for (int i = 0; i < ScopeSlotCount; i++)
                s_ScopeContainers[i]?.DrawGizmos();
        }

        #endregion

        #region 查找 [LOOKUP]

        private static void ValidateInterface(Type interfaceType)
        {
            if (!interfaceType.IsInterface)
                throw new GameException(StringUtility.Format("You must use service by interface, but '{0}' is not.", interfaceType.FullName));
        }

        /// <summary>
        /// 统一服务查找：先查 preferredScope（若指定），未命中走 GetBest 回退（Gameplay > Scene > App）。
        /// </summary>
        internal static bool TryResolve<T>(EServiceScopeKind? preferredScope, out T service) where T : class
        {
            if (preferredScope.HasValue)
            {
                int idx = (int)preferredScope.Value;
                if (s_ScopeContainers[idx] != null && s_ScopeContainers[idx].TryGet<T>(out service))
                    return true;
            }

            if (s_ServiceMaps.TryGetValue(typeof(T).TypeHandle, out var bindings))
            {
                var best = bindings.GetBest();
                if (best != null)
                {
                    service = best as T;
                    return service != null;
                }
            }

            service = null;
            return false;
        }

        /// <summary>
        /// 按接口获取服务。查找顺序：Gameplay > Scene > App（跨作用域遮蔽）。
        /// </summary>
        /// <exception cref="GameException">接口未注册或传入的不是接口类型。</exception>
        public static T GetService<T>() where T : class
        {
            EnsureMainThread();
            ValidateInterface(typeof(T));

            if (TryResolve<T>(null, out var service)) return service;
            throw new GameException(StringUtility.Format("Service '{0}' is not registered. Ensure it is registered before access.", typeof(T).FullName));
        }

        /// <summary>
        /// 偏好 Scope 查找：先查指定 scope，未命中走 GetBest。
        /// </summary>
        /// <exception cref="GameException">接口未注册或传入的不是接口类型。</exception>
        public static T GetService<T>(EServiceScopeKind preferredScope) where T : class
        {
            EnsureMainThread();
            ValidateInterface(typeof(T));

            if (TryResolve<T>(preferredScope, out var service)) return service;
            throw new GameException(StringUtility.Format("Service '{0}' not found in scope {1}.", typeof(T).FullName, preferredScope));
        }

        #endregion

        #region 注册 [REGISTER]

        /// <summary>
        /// 按接口合约注册服务。若同作用域已存在同合约注册，仅告警并返回已有实例。
        /// </summary>
        public static T RegisterService<T>(IService service) where T : class
        {
            EnsureMainThread();
            Type interfaceType = typeof(T);
            ValidateInterface(interfaceType);

            if (!interfaceType.IsInstanceOfType(service))
                throw new GameException(StringUtility.Format("Service '{0}' does not implement interface '{1}'.", service.GetType().FullName, interfaceType.FullName));

            var scope = EnsureScope(service.Scope);
            return scope.Register<T>(service);
        }

        /// <summary>
        /// 按运行时接口类型注册服务（用于编译期无法确定合约类型的场景，如 <see cref="ServiceMono{TScope}.RegisterAs"/>）。
        /// </summary>
        public static IService RegisterService(IService service, Type interfaceType)
        {
            EnsureMainThread();
            if (interfaceType == null)
                throw new GameException("Interface type must not be null.");

            ValidateInterface(interfaceType);

            if (!interfaceType.IsInstanceOfType(service))
                throw new GameException(StringUtility.Format("Service '{0}' does not implement interface '{1}'.", service.GetType().FullName, interfaceType.FullName));

            var scope = EnsureScope(service.Scope);
            return scope.Register(service, interfaceType);
        }

        /// <summary>
        /// 将同一服务实例注册到两个合约接口。
        /// </summary>
        public static void RegisterService<TPrimary, TSecondary>(IService service)
            where TPrimary : class
            where TSecondary : class
        {
            RegisterService<TPrimary>(service);
            RegisterService<TSecondary>(service);
        }

        #endregion

        #region 注销 [UNREGISTER]

        /// <summary>
        /// 按接口注销当前最高优先作用域中的服务。
        /// </summary>
        public static bool UnregisterService<T>() where T : class
        {
            EnsureMainThread();
            ValidateInterface(typeof(T));

            if (!s_ServiceMaps.TryGetValue(typeof(T).TypeHandle, out var bindings)) return false;
            var service = bindings.GetBest();
            if (service == null) return false;

            return s_ScopeContainers[(int)service.Scope]?.Unregister(service) ?? false;
        }

        /// <summary>
        /// 按实例注销服务。
        /// </summary>
        public static bool UnregisterService(IService service)
        {
            if (service == null) return false;
            EnsureMainThread();
            return s_ScopeContainers[(int)service.Scope]?.Unregister(service) ?? false;
        }

        #endregion

        #region 异步初始化 [ASYNC INIT]

        /// <summary>
        /// 按作用域顺序（App → Scene → Gameplay）及各作用域内的注册顺序异步初始化服务。
        /// 注册顺序即依赖拓扑序（依赖验证强制依赖先注册），因此被依赖服务的 OnInitAsync 先于依赖方执行。
        /// 仅处理实现 <see cref="IAsyncInitService"/> 的服务。
        /// </summary>
        public static UniTask InitializeAsync()
        {
            EnsureMainThread();

            s_AsyncInitBuffer.Clear();
            for (int i = 0; i < ScopeSlotCount; i++)
                s_ScopeContainers[i]?.CollectAsyncInitServices(s_AsyncInitBuffer);

            // 快速路径：无异步初始化服务时零分配返回
            if (s_AsyncInitBuffer.Count == 0) return UniTask.CompletedTask;

            return InitializeAsyncCore();
        }

        private static async UniTask InitializeAsyncCore()
        {
            // 快照后执行：OnInitAsync 内可能注册/注销服务，修改收集缓冲
            var services = s_AsyncInitBuffer.ToArray();
            s_AsyncInitBuffer.Clear();

            for (int i = 0; i < services.Length; i++)
                await services[i].OnInitAsync();
        }

        #endregion

        #region 清理 [CLEANUP]

        private static void ClearAll()
        {
            for (int i = 0; i < ScopeSlotCount; i++)
            {
                s_ScopeContainers[i]?.Dispose();
                s_ScopeContainers[i] = null;
            }
            s_ServiceMaps.Clear();
            s_AsyncInitBuffer.Clear();
            s_Interceptors.Clear();
            ServiceRegistered = null;
            ServiceUnregistered = null;
            MemoryPool.ClearAll();
            MarshalUtility.FreeCachedHGlobal();
        }

        #endregion
    }
}
