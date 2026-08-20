using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务作用域容器。管理单个作用域内服务的注册表、轮询列表和迭代安全机制。
    /// <para>OnInit 由 <see cref="ServiceContainer.BuildAsync"/> 按拓扑序统一驱动。</para>
    /// <para>Dispose 时逆注册序关闭全部服务（= 逆依赖拓扑序：依赖方先关闭，被依赖方后关闭）。</para>
    /// <para><b>线程契约</b>：所有方法仅限 Unity 主线程调用。</para>
    /// </summary>
    internal sealed class ServiceScope : IDisposable
    {
        #region 字段 [FIELDS]

        // --- 服务存储 ---

        private readonly Dictionary<RuntimeTypeHandle, IService> _servicesByContract = new();
        private readonly Dictionary<IService, ServiceEntry> _entriesByService
            = new(ReferenceComparer<IService>.Instance);
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
        internal int ServiceCount => _registrationOrder.Count;

        #endregion

        #region 构造 [CONSTRUCTION]

        internal ServiceScope(EServiceScopeKind kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        #endregion

        #region 注册 [REGISTER]

        /// <summary>
        /// 将服务注册到作用域。仅存储引用和更新轮询列表，不调用 OnInit。
        /// </summary>
        internal void Register(Type interfaceType, IService service)
        {
            var handle = interfaceType.TypeHandle;

            if (_servicesByContract.ContainsKey(handle))
            {
                LogUtility.Warning("{0} has already been registered in {1} scope.",
                    interfaceType.FullName, Kind);
                return;
            }

            if (_disposePending)
            {
                LogUtility.Warning("Scope {0} is being disposed; registration of {1} is rejected.",
                    Kind, interfaceType.FullName);
                return;
            }

            if (_isIterating)
            {
                // 注册仅发生在 BuildAsync（非迭代期），此分支为防御性兜底
                _pendingChanges.Add(PendingChange.Register(service, interfaceType));
                return;
            }

            RegisterInternal(service, interfaceType, handle);
        }

        private void RegisterInternal(IService service, Type interfaceType, RuntimeTypeHandle handle)
        {
            _servicesByContract[handle] = service;

            var entry = new ServiceEntry { InterfaceHandle = handle };

            // _registrationOrder 记录插入序（= 依赖拓扑序），用于逆序关闭与诊断收集；
            // 轮询列表按 Priority 降序插入——Priority 只读，运行时不变。
            _registrationOrder.Add(service);
            if (service is IServiceTickable tickable) InsertSorted(_tickables, tickable);
            if (service is IServiceFixedTickable fixedTickable) InsertSorted(_fixedTickables, fixedTickable);
            if (service is IServiceLateTickable lateTickable) InsertSorted(_lateTickables, lateTickable);
            if (service is IServiceGizmoDrawable gizmo) InsertSorted(_gizmoDrawables, gizmo);

            _entriesByService[service] = entry;

            GameServices.InvokeRegistering(service, interfaceType, Kind);
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
                    try { _fixedTickables[i].FixedTick(elapseSeconds, realElapseSeconds); }
                    catch (Exception ex) { LogTickFailure(_fixedTickables[i], nameof(FixedTick), ex); }
                }
            }
            finally { _isIterating = false; FlushPendingChanges(); }
        }

        internal void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            _isIterating = true;
            try
            {
                int count = _lateTickables.Count;
                for (int i = 0; i < count; i++)
                {
                    try { _lateTickables[i].LateTick(elapseSeconds, realElapseSeconds); }
                    catch (Exception ex) { LogTickFailure(_lateTickables[i], nameof(LateTick), ex); }
                }
            }
            finally { _isIterating = false; FlushPendingChanges(); }
        }

        internal void DrawGizmos()
        {
            _isIterating = true;
            try
            {
                int count = _gizmoDrawables.Count;
                for (int i = 0; i < count; i++)
                {
                    try { _gizmoDrawables[i].OnDrawGizmos(); }
                    catch (Exception ex) { LogTickFailure(_gizmoDrawables[i], "OnDrawGizmos", ex); }
                }
            }
            finally { _isIterating = false; FlushPendingChanges(); }
        }

        private static void LogTickFailure(object service, string methodName, Exception ex)
        {
            LogUtility.Error("Service '{0}' threw in {1}:\n{2}",
                service.GetType().FullName, methodName, ex);
        }

        #endregion

        #region 迭代安全 [ITERATION SAFETY]

        private void FlushPendingChanges()
        {
            if (_disposePending)
            {
                // 迭代中请求的作用域销毁：待迭代结束后执行，pending 的一并随作用域销毁
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

        #region 关闭 [SHUTDOWN]

        private bool _isDisposing;

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

            // 整体销毁时跳过逐项列表移除（由 DisposeInternal 统一 Clear），
            // 但注册表和 entries 必须逐项清理——否则作用域关闭后 Provider 仍能解析到已关闭的服务
            if (_isDisposing)
            {
                _servicesByContract.Remove(entry.InterfaceHandle);
                _entriesByService.Remove(service);
                GameServices.InvokeUnregistered(service);
            }
            else
            {
                RemoveServiceInternal(service, entry);
            }
        }

        private void RemoveServiceInternal(IService service, ServiceEntry entry)
        {
            _servicesByContract.Remove(entry.InterfaceHandle);

            _registrationOrder.Remove(service);
            if (service is IServiceTickable tickable) _tickables.Remove(tickable);
            if (service is IServiceFixedTickable fixedTickable) _fixedTickables.Remove(fixedTickable);
            if (service is IServiceLateTickable lateTickable) _lateTickables.Remove(lateTickable);
            if (service is IServiceGizmoDrawable gizmo) _gizmoDrawables.Remove(gizmo);

            _entriesByService.Remove(service);
            GameServices.InvokeUnregistered(service);
        }

        #endregion

        #region 销毁 [DISPOSE]

        public void Dispose()
        {
            if (IsDisposed) return;

            if (_isIterating)
            {
                // 迭代中销毁会缩短正在遍历的列表导致越界，延迟到本轮迭代结束执行
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
            _isDisposing = true;

            // 逆插入序关闭 = 逆依赖拓扑序：依赖方（后注册）先关闭，被依赖方后关闭。
            // 循环依赖在 BuildAsync 拓扑排序时即被阻止，此处无需再做环检测。
            for (int i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                if (service != null && _entriesByService.ContainsKey(service))
                    ShutdownService(service);
            }

            _isDisposing = false;

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

        #region 诊断 [DIAGNOSTICS]

        /// <summary>
        /// 按注册顺序收集此作用域内已注册服务的诊断信息。
        /// </summary>
        internal void CollectDiagnosticInfo(List<GameServices.DiagnosticInfo> buffer)
        {
            if (IsDisposed) return;

            for (int i = 0; i < _registrationOrder.Count; i++)
            {
                var service = _registrationOrder[i];
                if (service == null || !_entriesByService.TryGetValue(service, out var entry)) continue;

                var type = Type.GetTypeFromHandle(entry.InterfaceHandle);
                buffer.Add(new GameServices.DiagnosticInfo
                {
                    InterfaceType = type != null ? type.FullName : "<unknown>",
                    ImplementationType = service.GetType().FullName,
                    Scope = Kind,
                    Priority = service.Priority,
                    HasUpdate = service is IServiceTickable,
                    HasFixedUpdate = service is IServiceFixedTickable,
                    HasLateUpdate = service is IServiceLateTickable,
                    HasGizmo = service is IServiceGizmoDrawable,
                });
            }
        }

        #endregion

        #region 排序工具 [SORT UTILITIES]

        /// <summary>
        /// 按 Priority 降序插入。Priority 只读，注册后不会变更，无需后续重排序。
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

        /// <summary>服务的注册元数据。class 而非 struct——字典中直接修改字段无需回写。</summary>
        internal class ServiceEntry
        {
            public RuntimeTypeHandle InterfaceHandle;
        }

        /// <summary>迭代期间暂缓的注册/注销操作。</summary>
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
                => new(true, service, interfaceType);

            public static PendingChange Unregister(IService service)
                => new(false, service, null);
        }

        #endregion
    }
}
