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

        private readonly Dictionary<RuntimeTypeHandle, IService> _servicesByContract = new Dictionary<RuntimeTypeHandle, IService>();
        private readonly Dictionary<IService, ServiceEntry> _entriesByService = new Dictionary<IService, ServiceEntry>(ReferenceComparer<IService>.Instance);
        private readonly List<IService> _registrationOrder = new List<IService>();

        // --- 轮询列表（按 Priority 降序排列，由 InsertSorted 在注册时维护） ---

        private readonly List<IServiceTickable> _tickables = new List<IServiceTickable>();
        private readonly List<IServiceFixedTickable> _fixedTickables = new List<IServiceFixedTickable>();
        private readonly List<IServiceLateTickable> _lateTickables = new List<IServiceLateTickable>();
        private readonly List<IServiceGizmoDrawable> _gizmoDrawables = new List<IServiceGizmoDrawable>();

        // --- 迭代安全缓冲 ---

        private readonly List<PendingChange> _pendingChanges = new List<PendingChange>();
        private bool _isIterating;
        private bool _disposePending;

        // --- 轮询列表（按 Priority 降序排列，dirty-flag + lazy-sort 维护） ---

        private bool _tickablesDirty;
        private bool _fixedTickablesDirty;
        private bool _lateTickablesDirty;
        private bool _gizmoDrawablesDirty;

        private const int MISSING_INDEX = -1;

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
        internal void Register(Type[] contractTypes, IService service)
        {
            // 检查所有契约是否已被注册
            for (int i = 0; i < contractTypes.Length; i++)
            {
                if (_servicesByContract.ContainsKey(contractTypes[i].TypeHandle))
                {
                    LogUtility.Warning("{0} has already been registered in {1} scope.",
                        contractTypes[i].FullName, Kind);
                    return;
                }
            }

            if (_disposePending)
            {
                LogUtility.Warning("Scope {0} is being disposed; registration of {1} is rejected.",
                    Kind, contractTypes[0].FullName);
                return;
            }

            if (_isIterating)
            {
                // 注册仅发生在 BuildAsync（非迭代期），此分支为防御性兜底
                _pendingChanges.Add(PendingChange.Register(service, contractTypes));
                return;
            }

            RegisterInternal(service, contractTypes);
        }

        private void RegisterInternal(IService service, Type[] contractTypes)
        {
            var handles = new RuntimeTypeHandle[contractTypes.Length];
            for (int i = 0; i < contractTypes.Length; i++)
            {
                handles[i] = contractTypes[i].TypeHandle;
                _servicesByContract[handles[i]] = service;
            }

            var entry = new ServiceEntry { ContractHandles = handles };

            // _registrationOrder 记录插入序（= 依赖拓扑序），用于逆序关闭与诊断收集。
            // 轮询列表追加到末尾 + 置脏标记，下次 Tick 前 lazy-sort 并重建索引——
            // 比逐项 InsertSorted 更高效（k 次注册: O(k) Add + O(n log n) 排序 vs O(k×n) 插入移位）。
            _registrationOrder.Add(service);
            if (service is IServiceTickable tickable)
            {
                entry.TickIndex = _tickables.Count;
                _tickables.Add(tickable);
                _tickablesDirty = true;
            }
            if (service is IServiceFixedTickable fixedTickable)
            {
                entry.FixedTickIndex = _fixedTickables.Count;
                _fixedTickables.Add(fixedTickable);
                _fixedTickablesDirty = true;
            }
            if (service is IServiceLateTickable lateTickable)
            {
                entry.LateTickIndex = _lateTickables.Count;
                _lateTickables.Add(lateTickable);
                _lateTickablesDirty = true;
            }
            if (service is IServiceGizmoDrawable gizmo)
            {
                entry.GizmoIndex = _gizmoDrawables.Count;
                _gizmoDrawables.Add(gizmo);
                _gizmoDrawablesDirty = true;
            }

            _entriesByService[service] = entry;

            GameServices.InvokeRegistering(service, contractTypes[0], Kind);
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
            SortTickablesIfDirty();
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
            SortFixedTickablesIfDirty();
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
            SortLateTickablesIfDirty();
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
            SortGizmoDrawablesIfDirty();
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
                    if (!_servicesByContract.ContainsKey(change.ContractTypes[0].TypeHandle))
                        RegisterInternal(change.Service, change.ContractTypes);
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
                for (int i = 0; i < entry.ContractHandles.Length; i++)
                    _servicesByContract.Remove(entry.ContractHandles[i]);
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
            for (int i = 0; i < entry.ContractHandles.Length; i++)
                _servicesByContract.Remove(entry.ContractHandles[i]);

            // _registrationOrder 必须保持拓扑序——使用 List.Remove（O(n) 移位保序），
            // 不用 swap-with-last（会破坏依赖方的关闭顺序保证）。
            _registrationOrder.Remove(service);

            // 轮询列表使用 swap-with-last O(1) 移除 + 置脏标记，下次迭代前 lazy-sort。
            if (entry.TickIndex != MISSING_INDEX) RemoveTickableAt(entry.TickIndex);
            if (entry.FixedTickIndex != MISSING_INDEX) RemoveFixedTickableAt(entry.FixedTickIndex);
            if (entry.LateTickIndex != MISSING_INDEX) RemoveLateTickableAt(entry.LateTickIndex);
            if (entry.GizmoIndex != MISSING_INDEX) RemoveGizmoDrawableAt(entry.GizmoIndex);

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
            _tickablesDirty = false;
            _fixedTickablesDirty = false;
            _lateTickablesDirty = false;
            _gizmoDrawablesDirty = false;
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

                var type = Type.GetTypeFromHandle(entry.ContractHandles[0]);
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

        #region 排序与移除工具 [SORT & REMOVE UTILITIES]

        // ── lazy-sort：脏标记置位后，下次迭代前排序 + 重建索引 ──

        private void SortTickablesIfDirty()
        {
            if (!_tickablesDirty) return;
            _tickables.Sort(CompareByPriority);
            for (int i = 0; i < _tickables.Count; i++)
            {
                if (_tickables[i] is IService svc && _entriesByService.TryGetValue(svc, out var e))
                    e.TickIndex = i;
            }
            _tickablesDirty = false;
        }

        private void SortFixedTickablesIfDirty()
        {
            if (!_fixedTickablesDirty) return;
            _fixedTickables.Sort(CompareByPriority);
            for (int i = 0; i < _fixedTickables.Count; i++)
            {
                if (_fixedTickables[i] is IService svc && _entriesByService.TryGetValue(svc, out var e))
                    e.FixedTickIndex = i;
            }
            _fixedTickablesDirty = false;
        }

        private void SortLateTickablesIfDirty()
        {
            if (!_lateTickablesDirty) return;
            _lateTickables.Sort(CompareByPriority);
            for (int i = 0; i < _lateTickables.Count; i++)
            {
                if (_lateTickables[i] is IService svc && _entriesByService.TryGetValue(svc, out var e))
                    e.LateTickIndex = i;
            }
            _lateTickablesDirty = false;
        }

        private void SortGizmoDrawablesIfDirty()
        {
            if (!_gizmoDrawablesDirty) return;
            _gizmoDrawables.Sort(CompareByPriority);
            for (int i = 0; i < _gizmoDrawables.Count; i++)
            {
                if (_gizmoDrawables[i] is IService svc && _entriesByService.TryGetValue(svc, out var e))
                    e.GizmoIndex = i;
            }
            _gizmoDrawablesDirty = false;
        }

        /// <summary>
        /// Priority 降序比较器（高优先在前）。
        /// </summary>
        private static int CompareByPriority<T>(T a, T b)
        {
            int left = (a is IService sa) ? sa.Priority : 0;
            int right = (b is IService sb) ? sb.Priority : 0;
            return right.CompareTo(left);
        }

        // ── swap-with-last O(1) 移除：末尾元素填补被删位置，更新其索引 ──

        private void RemoveTickableAt(int index)
        {
            int last = _tickables.Count - 1;
            if (index == last) { _tickables.RemoveAt(last); }
            else
            {
                var moved = _tickables[last];
                _tickables[index] = moved;
                _tickables.RemoveAt(last);
                if (moved is IService svc && _entriesByService.TryGetValue(svc, out var e))
                    e.TickIndex = index;
            }
            _tickablesDirty = true;
        }

        private void RemoveFixedTickableAt(int index)
        {
            int last = _fixedTickables.Count - 1;
            if (index == last) { _fixedTickables.RemoveAt(last); }
            else
            {
                var moved = _fixedTickables[last];
                _fixedTickables[index] = moved;
                _fixedTickables.RemoveAt(last);
                if (moved is IService svc && _entriesByService.TryGetValue(svc, out var e))
                    e.FixedTickIndex = index;
            }
            _fixedTickablesDirty = true;
        }

        private void RemoveLateTickableAt(int index)
        {
            int last = _lateTickables.Count - 1;
            if (index == last) { _lateTickables.RemoveAt(last); }
            else
            {
                var moved = _lateTickables[last];
                _lateTickables[index] = moved;
                _lateTickables.RemoveAt(last);
                if (moved is IService svc && _entriesByService.TryGetValue(svc, out var e))
                    e.LateTickIndex = index;
            }
            _lateTickablesDirty = true;
        }

        private void RemoveGizmoDrawableAt(int index)
        {
            int last = _gizmoDrawables.Count - 1;
            if (index == last) { _gizmoDrawables.RemoveAt(last); }
            else
            {
                var moved = _gizmoDrawables[last];
                _gizmoDrawables[index] = moved;
                _gizmoDrawables.RemoveAt(last);
                if (moved is IService svc && _entriesByService.TryGetValue(svc, out var e))
                    e.GizmoIndex = index;
            }
            _gizmoDrawablesDirty = true;
        }

        #endregion

        #region 内部数据结构 [INTERNAL STRUCTURES]

        /// <summary>服务的注册元数据。class 而非 struct——字典中直接修改字段无需回写。</summary>
        internal class ServiceEntry
        {
            public RuntimeTypeHandle[] ContractHandles;
            public int TickIndex = MISSING_INDEX;
            public int FixedTickIndex = MISSING_INDEX;
            public int LateTickIndex = MISSING_INDEX;
            public int GizmoIndex = MISSING_INDEX;
        }

        /// <summary>迭代期间暂缓的注册/注销操作。</summary>
        internal struct PendingChange
        {
            public readonly bool IsRegister;
            public readonly IService Service;
            public readonly Type[] ContractTypes;

            private PendingChange(bool isRegister, IService service, Type[] contractTypes)
            {
                IsRegister = isRegister;
                Service = service;
                ContractTypes = contractTypes;
            }

            public static PendingChange Register(IService service, Type[] contractTypes) =>
                new PendingChange(true, service, contractTypes);

            public static PendingChange Unregister(IService service) => new PendingChange(false, service, null);
        }

        #endregion
    }
}
