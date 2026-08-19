using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架服务实现类管理系统。
    /// </summary>
    public static partial class ServiceSystem
    {
        private const int DESIGN_SERVICE_COUNT = 16;
        private const int MISSING_INDEX = -1;

        // 主线程守卫：编辑器加载 / 运行时子系统注册阶段捕获（均在主线程触发）
        private static int s_MainThreadId;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureMainThreadId()
        {
            s_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// 断言当前处于主线程（仅编辑器与开发构建生效，发布版零开销）。
        /// </summary>
        /// <remarks>
        /// ServiceSystem 主线程亲和。后台线程/异步回调需调用时，
        /// 请显式通过 <see cref="MainThreadDispatcher"/> 的 Dispatch/DispatchAsync 切回主线程，
        /// 而非由框架内部静默调度（会破坏返回值语义与读己之写顺序）。
        /// </remarks>
        private static void EnsureMainThread()
        {
            Assert.IsTrue(
                s_MainThreadId == 0 || System.Threading.Thread.CurrentThread.ManagedThreadId == s_MainThreadId,
                "ServiceSystem must only be used from the main thread. " +
                "From a background thread/callback, wrap the call with MainThreadDispatcher.Dispatch/DispatchAsync.");
        }

        // 每个接口类型可注册在不同 Scope 中，查找时按 Gameplay > Scene > App 优先返回
        private static readonly Dictionary<RuntimeTypeHandle, ScopeBindings> s_ServiceMaps
            = new Dictionary<RuntimeTypeHandle, ScopeBindings>(DESIGN_SERVICE_COUNT);

        // 按优先级排序的全量服务列表
        private static readonly List<IService> s_Services = new List<IService>(DESIGN_SERVICE_COUNT);

        // 生命周期列表 — 元素限定为对应接口：编译期防止误注册，轮询热路径零类型转换
        private static readonly List<IServiceTickable> s_UpdateServices = new List<IServiceTickable>(DESIGN_SERVICE_COUNT);
        private static readonly List<IServiceFixedTickable> s_FixedUpdateServices = new List<IServiceFixedTickable>(DESIGN_SERVICE_COUNT);
        private static readonly List<IServiceLateTickable> s_LateUpdateServices = new List<IServiceLateTickable>(DESIGN_SERVICE_COUNT);
        private static readonly List<IServiceGizmoDrawable> s_GizmoServices = new List<IServiceGizmoDrawable>(DESIGN_SERVICE_COUNT);

        // 服务 → 在各列表中的索引（用于 O(1) swap-remove）
        private static readonly Dictionary<IService, ServiceEntry> s_Entries
            = new Dictionary<IService, ServiceEntry>(DESIGN_SERVICE_COUNT, ReferenceComparer<IService>.Instance);

        // 迭代安全 — PendingChanges（注册与注销在迭代期间均延迟应用）
        internal static readonly List<PendingChange> s_PendingChanges = new List<PendingChange>();
        internal static bool s_IsIterating;

        /// <summary>
        /// 服务注册后触发（主线程）。在 <see cref="IService.OnInit"/> 之后调用。
        /// </summary>
        public static event Action<IService, Type, ServiceScope> ServiceRegistered;

        /// <summary>
        /// 服务注销后触发（主线程，在 <see cref="IService.Shutdown"/> 之后）。
        /// </summary>
        public static event Action<IService> ServiceUnregistered;

        /// <summary>
        /// 所有游戏框架服务轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        /// <remarks>由 <see cref="GameApp"/>（MonoBehaviour 生命周期）驱动，Unity 契约保证主线程调用，无需守护。</remarks>
        public static void Tick(float elapseSeconds, float realElapseSeconds)
        {
            s_IsIterating = true;
            try
            {
                int count = s_UpdateServices.Count;
                for (int i = 0; i < count; i++)
                {
                    s_UpdateServices[i].Tick(elapseSeconds, realElapseSeconds);
                }
            }
            finally
            {
                s_IsIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 所有游戏框架服务轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        /// <remarks>由 <see cref="GameApp"/>（MonoBehaviour 生命周期）驱动，Unity 契约保证主线程调用，无需守护。</remarks>
        public static void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            s_IsIterating = true;
            try
            {
                int count = s_FixedUpdateServices.Count;
                for (int i = 0; i < count; i++)
                {
                    s_FixedUpdateServices[i].FixedTick(elapseSeconds, realElapseSeconds);
                }
            }
            finally
            {
                s_IsIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 所有游戏框架服务轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        /// <remarks>由 <see cref="GameApp"/>（MonoBehaviour 生命周期）驱动，Unity 契约保证主线程调用，无需守护。</remarks>
        public static void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            s_IsIterating = true;
            try
            {
                int count = s_LateUpdateServices.Count;
                for (int i = 0; i < count; i++)
                {
                    s_LateUpdateServices[i].LateTick(elapseSeconds, realElapseSeconds);
                }
            }
            finally
            {
                s_IsIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 所有游戏框架服务绘制 Gizmos。
        /// </summary>
        public static void DrawGizmos()
        {
            s_IsIterating = true;
            try
            {
                int count = s_GizmoServices.Count;
                for (int i = 0; i < count; i++)
                {
                    s_GizmoServices[i].OnDrawGizmos();
                }
            }
            finally
            {
                s_IsIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 关闭并清理所有游戏框架服务。按 Gameplay → Scene → App 逆序关闭。
        /// </summary>
        public static void Shutdown()
        {
            EnsureMainThread();
            ShutdownScope(ServiceScope.Gameplay);
            ShutdownScope(ServiceScope.Scene);
            ShutdownScope(ServiceScope.App);
            ClearAll();
        }

        /// <summary>
        /// 关闭指定作用域的所有服务。
        /// </summary>
        /// <param name="scope">要关闭的作用域。</param>
        /// <remarks>迭代期间调用时移除操作延迟到本轮迭代结束后应用。</remarks>
        public static void ShutdownScope(ServiceScope scope)
        {
            EnsureMainThread();

            for (int i = s_Services.Count - 1; i >= 0; i--)
            {
                var service = s_Services[i];
                if (!s_Entries.TryGetValue(service, out var entry)) continue;
                if (entry.Scope != scope) continue;

                if (s_IsIterating)
                {
                    // 迭代期间不直接移除，延迟应用（PendingRemove 防止重复入队）
                    if (entry.PendingRemove) continue;
                    entry.PendingRemove = true;
                    s_Entries[service] = entry;
                    s_PendingChanges.Add(PendingChange.Unregister(service));
                    continue;
                }

                ShutdownService(service);
            }
        }

        /// <summary>
        /// 注销服务。按接口类型查找当前最高优先作用域（Gameplay &gt; Scene &gt; App）中的绑定。
        /// </summary>
        /// <typeparam name="T">服务接口类型。</typeparam>
        /// <returns>是否找到并成功注销。</returns>
        public static bool UnregisterService<T>() where T : class
        {
            EnsureMainThread();

            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new GameException(StringUtility.Format("You must unregister service by interface, but '{0}' is not.", interfaceType.FullName));
            }

            if (!s_ServiceMaps.TryGetValue(interfaceType.TypeHandle, out var bindings)) return false;
            var service = bindings.GetBest();
            if (service == null) return false;

            return UnregisterServiceInternal(service);
        }

        /// <summary>
        /// 注销指定服务实例。
        /// </summary>
        /// <param name="service">要注销的服务。</param>
        /// <returns>是否找到并成功注销。</returns>
        public static bool UnregisterService(IService service)
        {
            if (service == null) return false;
            EnsureMainThread();
            return UnregisterServiceInternal(service);
        }

        private static bool UnregisterServiceInternal(IService service)
        {
            if (!s_Entries.TryGetValue(service, out var entry)) return false;

            if (s_IsIterating)
            {
                if (entry.PendingRemove) return true;
                entry.PendingRemove = true;
                s_Entries[service] = entry;
                s_PendingChanges.Add(PendingChange.Unregister(service));
                return true;
            }

            ShutdownService(service);
            return true;
        }

        /// <summary>
        /// 关闭单个服务并从系统中移除。单个服务关闭异常不中断其余服务的清理。
        /// </summary>
        private static void ShutdownService(IService service)
        {
            if (!s_Entries.TryGetValue(service, out var entry)) return;

            try
            {
                service.Shutdown();
            }
            catch (Exception exception)
            {
                LogUtility.Error(exception.ToString());
            }

            entry.PendingRemove = false;
            RemoveServiceInternal(service, entry);
        }

        /// <summary>
        /// 获取游戏框架服务。
        /// </summary>
        /// <typeparam name="T">要获取的游戏框架服务类型。</typeparam>
        /// <returns>要获取的游戏框架服务。</returns>
        /// <remarks>
        /// 如果要获取的游戏框架服务不存在，则自动创建该游戏框架服务。
        /// <para>查找顺序：Gameplay &gt; Scene &gt; App（跨作用域遮蔽）。</para>
        /// <para>
        /// 反射回退约定：未注册时按 <c>IXxxService → 命名空间.XxxService（同程序集）</c> 自动创建。
        /// 内置服务在 <c>AppSettings.Initiation()</c>（AfterAssembliesLoaded 阶段）由配置注册，
        /// 早于任何游戏代码调用本方法，因此配置实现优先；仅在接口从未被注册时才会触发反射回退。
        /// 自定义服务若不遵循此命名约定，必须先显式 <see cref="RegisterService{T}"/>。
        /// </para>
        /// </remarks>
        public static T GetService<T>() where T : class
        {
            EnsureMainThread();

            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new GameException(StringUtility.Format("You must get service by interface, but '{0}' is not.", interfaceType.FullName));
            }

            if (s_ServiceMaps.TryGetValue(interfaceType.TypeHandle, out var bindings))
            {
                var best = bindings.GetBest();
                if (best != null) return best as T;
            }

            // 如果要获取的游戏框架服务不存在，则自动创建该游戏框架服务。
            string serviceName = StringUtility.Format("{0}.{1}, {2}", interfaceType.Namespace, interfaceType.Name.Substring(1), interfaceType.Assembly.GetName().Name);
            Type serviceType = Type.GetType(serviceName);
            if (serviceType == null)
            {
                throw new GameException(StringUtility.Format("Can not find Game Framework service type '{0}'.", serviceName));
            }

            Service service = (Service)Activator.CreateInstance(serviceType);
            if (service == null)
            {
                throw new GameException(StringUtility.Format("Can not create service '{0}'.", serviceType.FullName));
            }

            RegisterServiceInternal(interfaceType, service, service.Scope);

            return service as T;
        }

        /// <summary>
        /// 注册自定义Service。
        /// </summary>
        /// <param name="service">Service。</param>
        /// <returns>Service实例。</returns>
        /// <exception cref="GameException">框架异常。</exception>
        public static T RegisterService<T>(IService service) where T : class
        {
            EnsureMainThread();

            Type interfaceType = typeof(T);

            if (!interfaceType.IsInterface)
            {
                throw new GameException(StringUtility.Format("You must get service by interface, but '{0}' is not.", interfaceType.FullName));
            }

            // 快速失败：服务必须实现所注册的接口，否则 GetService<T> 返回 as T = null 会在远处炸出
            if (!interfaceType.IsInstanceOfType(service))
            {
                throw new GameException(StringUtility.Format("Service '{0}' does not implement interface '{1}'.", service.GetType().FullName, interfaceType.FullName));
            }

            var handle = interfaceType.TypeHandle;
            if (s_ServiceMaps.TryGetValue(handle, out var existing))
            {
                // 重复检查限定在同一作用域内：不同 Scope 可注册同一接口（跨作用域遮蔽）
                var occupied = existing.Get(service.Scope);
                if (occupied != null)
                {
                    LogUtility.Warning("{0} has already been registered in {1} scope.", interfaceType.FullName, service.Scope);
                    return occupied as T;
                }
            }

            if (s_IsIterating)
            {
                s_PendingChanges.Add(PendingChange.Register(service, interfaceType, service.Scope));
                return service as T;
            }

            RegisterServiceInternal(interfaceType, service, service.Scope);

            return service as T;
        }

        /// <summary>
        /// 注册服务到多个接口契约。
        /// </summary>
        /// <typeparam name="TPrimary">主接口类型。</typeparam>
        /// <typeparam name="TSecondary">次接口类型。</typeparam>
        /// <param name="service">服务实例。</param>
        public static void RegisterService<TPrimary, TSecondary>(IService service)
            where TPrimary : class
            where TSecondary : class
        {
            RegisterService<TPrimary>(service);
            RegisterService<TSecondary>(service);
        }

        private static void RegisterServiceInternal(Type interfaceType, IService service, ServiceScope scope)
        {
            var handle = interfaceType.TypeHandle;
            if (!s_ServiceMaps.TryGetValue(handle, out var bindings))
                bindings = default;

            switch (scope)
            {
                case ServiceScope.App: bindings.App = service; break;
                case ServiceScope.Scene: bindings.Scene = service; break;
                case ServiceScope.Gameplay: bindings.Gameplay = service; break;
            }
            s_ServiceMaps[handle] = bindings;

            // 注入上下文（Service 和 MonoServiceBehaviour 均支持）
            if (service is Service m) m.SetContext(new ServiceContext(s_ServiceMaps, scope));
            else if (service is MonoServiceBehaviourBase monoBase) monoBase.SetContext(new ServiceContext(s_ServiceMaps, scope));

            // 先占位 entry（索引在全部插入完成后统一重建）
            s_Entries[service] = new ServiceEntry
            {
                InterfaceHandle = handle,
                AllIndex = MISSING_INDEX,
                UpdateIndex = MISSING_INDEX,
                FixedUpdateIndex = MISSING_INDEX,
                LateUpdateIndex = MISSING_INDEX,
                GizmoIndex = MISSING_INDEX,
                Scope = scope,
            };

            // 注册时一次性转换到各生命周期接口（每服务仅一次，热路径零转换）
            var updateService = service as IServiceTickable;
            var fixedUpdateService = service as IServiceFixedTickable;
            var lateUpdateService = service as IServiceLateTickable;
            var gizmoService = service as IServiceGizmoDrawable;

            InsertSorted(s_Services, service);
            if (updateService != null) InsertSorted(s_UpdateServices, updateService);
            if (fixedUpdateService != null) InsertSorted(s_FixedUpdateServices, fixedUpdateService);
            if (lateUpdateService != null) InsertSorted(s_LateUpdateServices, lateUpdateService);
            if (gizmoService != null) InsertSorted(s_GizmoServices, gizmoService);

            // InsertSorted 会移动已有元素，统一重建全部列表索引（含新服务自身），保证 swap-remove 使用的索引始终有效
            RebuildAllIndices();

            service.OnInit();

            ServiceRegistered?.Invoke(service, interfaceType, scope);
        }

        /// <summary>
        /// 按优先级将服务插入排序列表，返回插入索引。
        /// </summary>
        private static int InsertSorted<T>(List<T> list, T item) where T : class
        {
            int priority = GetPriority(item);
            int insertAt = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                if (priority > GetPriority(list[i]))
                {
                    insertAt = i;
                    break;
                }
            }
            list.Insert(insertAt, item);
            return insertAt;
        }

        private static int GetPriority<T>(T item) where T : class => (item as IService)?.Priority ?? 0;

        /// <summary>
        /// 重建全部列表的索引（InsertSorted 会移动已有元素，导致其 entry 缓存的索引失效）。
        /// 每次注册后调用，保证 swap-remove 使用的索引始终正确。
        /// </summary>
        private static void RebuildAllIndices()
        {
            for (int i = 0; i < s_Services.Count; i++)
                if (s_Entries.TryGetValue(s_Services[i], out var e)) { e.AllIndex = i; s_Entries[s_Services[i]] = e; }

            for (int i = 0; i < s_UpdateServices.Count; i++)
                if (s_UpdateServices[i] is IService m && s_Entries.TryGetValue(m, out var e)) { e.UpdateIndex = i; s_Entries[m] = e; }

            for (int i = 0; i < s_FixedUpdateServices.Count; i++)
                if (s_FixedUpdateServices[i] is IService m && s_Entries.TryGetValue(m, out var e)) { e.FixedUpdateIndex = i; s_Entries[m] = e; }

            for (int i = 0; i < s_LateUpdateServices.Count; i++)
                if (s_LateUpdateServices[i] is IService m && s_Entries.TryGetValue(m, out var e)) { e.LateUpdateIndex = i; s_Entries[m] = e; }

            for (int i = 0; i < s_GizmoServices.Count; i++)
                if (s_GizmoServices[i] is IService m && s_Entries.TryGetValue(m, out var e)) { e.GizmoIndex = i; s_Entries[m] = e; }
        }

        /// <summary>
        /// Swap-Remove — O(1) 删除。被移动的元素需要更新索引。
        /// </summary>
        private static void SwapRemoveAt<T>(List<T> list, int index) where T : class
        {
            int lastIndex = list.Count - 1;
            if (index == lastIndex)
            {
                list.RemoveAt(lastIndex);
                return;
            }

            T moved = list[lastIndex];
            list[index] = moved;
            list.RemoveAt(lastIndex);

            if (moved is IService movedService && s_Entries.TryGetValue(movedService, out var movedEntry))
            {
                // 更新被移动服务的索引
                if (ReferenceEquals(list, s_Services)) movedEntry.AllIndex = index;
                else if (ReferenceEquals(list, s_UpdateServices)) movedEntry.UpdateIndex = index;
                else if (ReferenceEquals(list, s_FixedUpdateServices)) movedEntry.FixedUpdateIndex = index;
                else if (ReferenceEquals(list, s_LateUpdateServices)) movedEntry.LateUpdateIndex = index;
                else if (ReferenceEquals(list, s_GizmoServices)) movedEntry.GizmoIndex = index;
                s_Entries[movedService] = movedEntry;
            }
        }

        private static void RemoveServiceInternal(IService service, ServiceEntry entry)
        {
            // 清除对应 Scope 的绑定（接口句柄由 entry 直接持有，O(1)）
            if (s_ServiceMaps.TryGetValue(entry.InterfaceHandle, out var bindings))
            {
                switch (entry.Scope)
                {
                    case ServiceScope.App:     bindings.App = null; break;
                    case ServiceScope.Scene:    bindings.Scene = null; break;
                    case ServiceScope.Gameplay: bindings.Gameplay = null; break;
                }
                if (bindings.IsEmpty)
                    s_ServiceMaps.Remove(entry.InterfaceHandle);
                else
                    s_ServiceMaps[entry.InterfaceHandle] = bindings;
            }

            if (entry.AllIndex >= 0) SwapRemoveAt(s_Services, entry.AllIndex);
            if (entry.UpdateIndex >= 0) SwapRemoveAt(s_UpdateServices, entry.UpdateIndex);
            if (entry.FixedUpdateIndex >= 0) SwapRemoveAt(s_FixedUpdateServices, entry.FixedUpdateIndex);
            if (entry.LateUpdateIndex >= 0) SwapRemoveAt(s_LateUpdateServices, entry.LateUpdateIndex);
            if (entry.GizmoIndex >= 0) SwapRemoveAt(s_GizmoServices, entry.GizmoIndex);

            s_Entries.Remove(service);

            ServiceUnregistered?.Invoke(service);
        }

        private static void FlushPendingChanges()
        {
            if (s_PendingChanges.Count == 0) return;

            for (int i = 0; i < s_PendingChanges.Count; i++)
            {
                var change = s_PendingChanges[i];
                if (change.IsRegister)
                {
                    if (!s_ServiceMaps.TryGetValue(change.InterfaceType.TypeHandle, out var b) || b.Get(change.Scope) == null)
                    {
                        RegisterServiceInternal(change.InterfaceType, change.Service, change.Scope);
                    }
                }
                else
                {
                    // 注销：ShutdownService 内部自带 PendingRemove/entry 存在性检查
                    ShutdownService(change.Service);
                }
            }

            s_PendingChanges.Clear();
        }

        /// <summary>
        /// 异步初始化所有已注册的 <see cref="IAsyncInitService"/> 服务。
        /// <para>应在所有服务注册完成后调用（如 <c>AppSettings.Initiation</c> 之后、进入流程链之前）。</para>
        /// <para>未实现 <see cref="IAsyncInitService"/> 的服务被跳过，零开销。</para>
        /// </summary>
        public static async UniTask InitializeAsync()
        {
            EnsureMainThread();

            int count = s_Services.Count;
            for (int i = 0; i < count; i++)
            {
                if (s_Services[i] is IAsyncInitService asyncInit)
                    await asyncInit.OnInitAsync();
            }
        }

        private static void ClearAll()
        {
            s_Services.Clear();
            s_ServiceMaps.Clear();
            s_UpdateServices.Clear();
            s_FixedUpdateServices.Clear();
            s_LateUpdateServices.Clear();
            s_GizmoServices.Clear();
            s_Entries.Clear();
            s_PendingChanges.Clear();

            ServiceRegistered = null;
            ServiceUnregistered = null;

            MemoryPool.ClearAll();
            MarshalUtility.FreeCachedHGlobal();
        }
    }
}
