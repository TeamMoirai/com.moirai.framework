using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期作用域种类。
    /// </summary>
    public enum EServiceScopeKind : byte
    {
        /// <summary>应用级，生命周期最长，适合资源、音频、UI、计时器等全局服务。</summary>
        App = 0,
        /// <summary>场景级，主场景切换时会重置，适合当前场景状态。</summary>
        Scene = 1,
        /// <summary>玩法级，适合一局战斗或一个玩法实例的服务。</summary>
        Gameplay = 2,
    }

    /// <summary>
    /// 服务作用域容器。每个作用域独立持有自己的服务字典、tick 列表和迭代安全机制。
    /// <para>Dispose 时逆序关闭全部服务并清理——O(1) 对外、不影响其他作用域。</para>
    /// <para><b>线程契约</b>：所有方法仅限 Unity 主线程调用。</para>
    /// </summary>
    internal sealed class ServiceScope : IDisposable
    {
        #region 字段 [FIELDS]

        // --- 服务存储 ---

        private readonly Dictionary<RuntimeTypeHandle, IService> _servicesByContract = new();
        private readonly Dictionary<IService, ServiceEntry> _entriesByService = new(ReferenceComparer<IService>.Instance);
        private readonly List<IService> _registrationOrder = new();

        // --- 轮询列表（按 Priority 降序排列，由 InsertSorted 在注册时维护） ---

        private readonly List<IServiceTickable> _tickables = new();
        private readonly List<IServiceFixedTickable> _fixedTickables = new();
        private readonly List<IServiceLateTickable> _lateTickables = new();
        private readonly List<IServiceGizmoDrawable> _gizmoDrawables = new();

        // --- 迭代安全缓冲 ---

        private readonly List<PendingChange> _pendingChanges = new();
        private bool _isIterating;
        private bool _disposePending;

        #endregion

        #region 属性 [PROPERTIES]

        internal EServiceScopeKind Kind { get; }
        public string Name { get; }
        internal bool IsDisposed { get; private set; }
        internal bool IsIterating => _isIterating;
        internal int PendingChangesCount => _pendingChanges.Count;
        internal int ServiceCount => _registrationOrder.Count;

        #endregion

        #region 构造 [CONSTRUCTION]

        internal ServiceScope(EServiceScopeKind kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        #endregion

        #region 异步初始化收集 [ASYNC INIT COLLECTION]

        /// <summary>
        /// 按注册顺序收集需要异步初始化的服务。
        /// 注册顺序即依赖拓扑序（依赖验证强制依赖先注册），因此异步初始化按拓扑序执行：
        /// 被依赖服务的 OnInitAsync 先于依赖方执行。
        /// </summary>
        internal void CollectAsyncInitServices(List<IAsyncInitService> buffer)
        {
            for (int i = 0; i < _registrationOrder.Count; i++)
            {
                if (_registrationOrder[i] is IAsyncInitService asyncInit)
                    buffer.Add(asyncInit);
            }
        }

        #endregion

        #region 查找 [LOOKUP]

        internal bool TryGet<T>(out T service) where T : class
        {
            if (_servicesByContract.TryGetValue(typeof(T).TypeHandle, out var raw))
            {
                service = raw as T;
                return service != null;
            }
            service = null;
            return false;
        }

        #endregion

        #region 注册与注销 [REGISTER / UNREGISTER]

        internal T Register<T>(IService service) where T : class
            => (T)Register(service, typeof(T));

        internal IService Register(IService service, Type interfaceType)
        {
            var handle = interfaceType.TypeHandle;

            if (_servicesByContract.ContainsKey(handle))
            {
                var existing = _servicesByContract[handle];
                LogUtility.Warning("{0} has already been registered in {1} scope.", interfaceType.FullName, Kind);
                return existing;
            }

            if (_disposePending)
            {
                LogUtility.Warning("Scope {0} is being disposed; registration of {1} is rejected.", Kind, interfaceType.FullName);
                return service;
            }

            if (_isIterating)
            {
                _pendingChanges.Add(PendingChange.Register(service, interfaceType));
                return service;
            }

            RegisterInternal(service, interfaceType, handle);
            return service;
        }

        internal void RegisterInternal(IService service, Type interfaceType, RuntimeTypeHandle handle)
        {
            // 依赖验证：确保依赖的服务已注册（适用于 ServiceBase 与 ServiceMonoBase）。
            // 该约束使注册顺序成为依赖拓扑序——插入序正序即拓扑序，逆序即逆拓扑序。
            var deps = service.Dependencies;
            if (deps != null && deps.Length > 0)
            {
                for (int i = 0; i < deps.Length; i++)
                {
                    if (!GameServices.IsRegistered(deps[i]))
                        throw new GameException(StringUtility.Format(
                            "Service '{0}' depends on '{1}' which is not registered.\nDeclared dependencies: {2}\nEnsure dependencies are registered before this service.",
                            service.GetType().FullName,
                            deps[i].FullName,
                            string.Join(", ", System.Linq.Enumerable.Select(deps, d => d.FullName))));
                }
            }

            _servicesByContract[handle] = service;
            GameServices.SetContext(service, this);
            GameServices.AddToGlobalMap(handle, service, Kind);

            var entry = new ServiceEntry { InterfaceHandle = handle };

            // _registrationOrder 记录纯插入序（= 依赖拓扑序），用于异步初始化收集与逆序关闭；
            // 轮询列表按 Priority 降序插入，顺序在注册时确定——Priority 只读，运行时不变。
            _registrationOrder.Add(service);
            if (service is IServiceTickable tickable) InsertSorted(_tickables, tickable);
            if (service is IServiceFixedTickable fixedTickable) InsertSorted(_fixedTickables, fixedTickable);
            if (service is IServiceLateTickable lateTickable) InsertSorted(_lateTickables, lateTickable);
            if (service is IServiceGizmoDrawable gizmo) InsertSorted(_gizmoDrawables, gizmo);

            _entriesByService[service] = entry;

            GameServices.InvokeRegistering(service, interfaceType, Kind);

            service.OnInit();
            GameServices.SetState(service, EServiceState.Initialized);
            GameServices.RaiseServiceRegistered(service, interfaceType, Kind);
            GameServices.InvokeRegistered(service, interfaceType, Kind);
        }

        internal bool Unregister(IService service)
        {
            if (service == null || !_entriesByService.TryGetValue(service, out var entry)) return false;

            // 作用域已标记销毁：服务将随作用域统一关闭，无需单独注销
            if (_disposePending) return true;

            if (_isIterating)
            {
            if (entry.PendingRemove) return true;
            entry.PendingRemove = true;
            _pendingChanges.Add(PendingChange.Unregister(service));
            return true;
            }

            ShutdownService(service);
            return true;
        }

        #endregion

        #region 关闭与移除 [SHUTDOWN / REMOVE]

        private void ShutdownService(IService service)
        {
            if (!_entriesByService.TryGetValue(service, out var entry)) return;

            // 幂等守卫：已处于关闭中或已销毁的服务不再重复关闭
            var state = GameServices.GetState(service);
            if (state >= EServiceState.ShuttingDown) return;

            GameServices.SetState(service, EServiceState.ShuttingDown);
            GameServices.InvokeShutdown(service);

            try { service.Shutdown(); }
            catch (Exception ex) { LogUtility.Error(ex.ToString()); }

            GameServices.SetState(service, EServiceState.Disposed);
            entry.PendingRemove = false;

            // 作用域整体销毁时跳过逐项列表移除（由 DisposeInternal 统一 Clear），
            // 但全局映射和 entries 必须逐项清理——否则 ShutdownScope 后 GetBest 仍返回已关闭的服务
            if (IsDisposing)
            {
                _servicesByContract.Remove(entry.InterfaceHandle);
                GameServices.RemoveFromGlobalMap(entry.InterfaceHandle, service, Kind);
                _entriesByService.Remove(service);
                GameServices.RaiseServiceUnregistered(service);
                GameServices.InvokeUnregistered(service);
            }
            else
            {
                RemoveServiceInternal(service, entry);
            }
        }

        private bool IsDisposing { get; set; }

        private void RemoveServiceInternal(IService service, ServiceEntry entry)
        {
            _servicesByContract.Remove(entry.InterfaceHandle);
            GameServices.RemoveFromGlobalMap(entry.InterfaceHandle, service, Kind);

            _registrationOrder.Remove(service);
            if (service is IServiceTickable tickable) _tickables.Remove(tickable);
            if (service is IServiceFixedTickable fixedTickable) _fixedTickables.Remove(fixedTickable);
            if (service is IServiceLateTickable lateTickable) _lateTickables.Remove(lateTickable);
            if (service is IServiceGizmoDrawable gizmo) _gizmoDrawables.Remove(gizmo);

            _entriesByService.Remove(service);
            GameServices.RaiseServiceUnregistered(service);
            GameServices.InvokeUnregistered(service);
        }

        #endregion

        #region 轮询 [TICK]

        // 四个轮询方法结构相同但刻意不提取为泛型委托——避免热路径上的委托分配开销。

        internal void Tick(float elapseSeconds, float realElapseSeconds)
        {
            _isIterating = true;
            try
            {
                int count = _tickables.Count;
                for (int i = 0; i < count; i++)
                {
                    var tickable = _tickables[i];
                    try
                    {
                        GameServices.InvokeTick(tickable as IService, elapseSeconds, realElapseSeconds);
                        tickable.Tick(elapseSeconds, realElapseSeconds);
                    }
                    catch (Exception ex) { LogTickFailure(tickable, nameof(Tick), ex); }
                }
            }
            finally
            {
                _isIterating = false;
                FlushPendingChanges();
            }
        }

        internal void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            _isIterating = true;
            try
            {
                int count = _fixedTickables.Count;
                for (int i = 0; i < count; i++)
                {
                    var tickable = _fixedTickables[i];
                    try { tickable.FixedTick(elapseSeconds, realElapseSeconds); }
                    catch (Exception ex) { LogTickFailure(tickable, nameof(FixedTick), ex); }
                }
            }
            finally
            {
                _isIterating = false;
                FlushPendingChanges();
            }
        }

        internal void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            _isIterating = true;
            try
            {
                int count = _lateTickables.Count;
                for (int i = 0; i < count; i++)
                {
                    var tickable = _lateTickables[i];
                    try { tickable.LateTick(elapseSeconds, realElapseSeconds); }
                    catch (Exception ex) { LogTickFailure(tickable, nameof(LateTick), ex); }
                }
            }
            finally
            {
                _isIterating = false;
                FlushPendingChanges();
            }
        }

        internal void DrawGizmos()
        {
            _isIterating = true;
            try
            {
                int count = _gizmoDrawables.Count;
                for (int i = 0; i < count; i++)
                {
                    var drawable = _gizmoDrawables[i];
                    try { drawable.OnDrawGizmos(); }
                    catch (Exception ex) { LogTickFailure(drawable, "OnDrawGizmos", ex); }
                }
            }
            finally
            {
                _isIterating = false;
                FlushPendingChanges();
            }
        }

        private static void LogTickFailure(object service, string methodName, Exception ex)
        {
            LogUtility.Error("Service '{0}' threw in {1}:\n{2}", service.GetType().FullName, methodName, ex);
        }

        #endregion

        #region 迭代安全 [ITERATION SAFETY]

        private void FlushPendingChanges()
        {
            if (_disposePending)
            {
                // 迭代中请求的作用域销毁：待迭代结束后执行，pending 的注册/注销一并随作用域销毁
                DisposeInternal();
                return;
            }

            if (_pendingChanges.Count == 0) return;

            for (int i = 0; i < _pendingChanges.Count; i++)
            {
                var change = _pendingChanges[i];
                if (change.IsRegister)
                {
                    if (!_servicesByContract.ContainsKey(change.InterfaceType.TypeHandle))
                        RegisterInternal(change.Service, change.InterfaceType, change.InterfaceType.TypeHandle);
                }
                else
                {
                    ShutdownService(change.Service);
                }
            }
            _pendingChanges.Clear();
        }

        #endregion

        #region 销毁 [DISPOSE]

        public void Dispose()
        {
            if (IsDisposed) return;

            if (_isIterating)
            {
                // 迭代中销毁：若立即移除服务会缩短正在遍历的列表导致越界，延迟到本轮迭代结束执行
                _disposePending = true;
                return;
            }

            DisposeInternal();
        }

        private void DisposeInternal()
        {
            if (IsDisposed) return;
            _isIterating = false;
            _disposePending = false;
            _pendingChanges.Clear();

            // 标记正在整体销毁：ShutdownService 跳过逐项列表移除，
            // 由循环结束后统一 Clear() 清空全部列表——避免逆序遍历时 List.Remove 修改被遍历列表
            IsDisposing = true;

            // 逆插入序关闭 = 逆依赖拓扑序：依赖方（后注册）先关闭，被依赖方后关闭。
            // 循环依赖在注册期即被依赖验证阻止，此处无需再做环检测。
            for (int i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                if (service != null && _entriesByService.ContainsKey(service))
                    ShutdownService(service);
            }

            IsDisposing = false;

            _registrationOrder.Clear();
            _tickables.Clear();
            _fixedTickables.Clear();
            _lateTickables.Clear();
            _gizmoDrawables.Clear();
            _entriesByService.Clear();
            _servicesByContract.Clear();
            IsDisposed = true;
        }

        #endregion

        #region 排序工具 [SORT UTILITIES]

        /// <summary>
        /// 按 Priority 降序插入。Priority 是只读属性，注册后不会变更，因此无需后续重排序。
        /// </summary>
        private static void InsertSorted<T>(List<T> list, T item) where T : class
        {
            int priority = (item is IService si) ? si.Priority : 0;
            int insertAt = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                int existingPriority = (list[i] is IService ei) ? ei.Priority : 0;
                if (priority > existingPriority) { insertAt = i; break; }
            }
            list.Insert(insertAt, item);
        }

        #endregion

        #region 内部数据结构 [INTERNAL STRUCTURES]

        /// <summary>
        /// 服务的注册元数据。class 而非 struct——字典中直接修改字段无需回写。
        /// </summary>
        internal class ServiceEntry
        {
            public RuntimeTypeHandle InterfaceHandle;
            public bool PendingRemove;
        }

        /// <summary>
        /// 迭代期间暂缓的注册/注销操作。
        /// </summary>
        internal struct PendingChange
        {
            public readonly bool IsRegister;
            public readonly IService Service;
            public readonly Type InterfaceType;

            private PendingChange(bool isRegister, IService service, Type interfaceType)
            {
                IsRegister = isRegister;
                Service = service;
                InterfaceType = interfaceType;
            }

            public static PendingChange Register(IService service, Type interfaceType)
                => new PendingChange(true, service, interfaceType);

            public static PendingChange Unregister(IService service)
                => new PendingChange(false, service, null);
        }

        #endregion
    }
}
