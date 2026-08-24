using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务作用域容器。管理单个作用域内服务的注册表、轮询列表和迭代安全机制。
    /// <para><b>所有权</b>：注册/注销仅由 <see cref="ServiceWorld.BuildAsync"/> 在构建期驱动，
    /// 外部代码不直接操作本类；作用域的创建与销毁由 <see cref="ServiceWorld"/> 统一调度。</para>
    /// <para>OnInit 由 <see cref="ServiceWorld.BuildAsync"/> 按拓扑序统一驱动。</para>
    /// <para>Dispose 时逆注册序关闭全部服务（= 逆依赖拓扑序：依赖方先关闭，被依赖方后关闭）。</para>
    /// <para><b>线程契约</b>：所有方法仅限 Unity 主线程调用。</para>
    /// </summary>
    internal sealed class ServiceScope : IDisposable
    {
        #region 字段 [FIELDS]

        // --- 服务存储 ---

        private readonly ServiceWorld _world;
        private readonly Dictionary<RuntimeTypeHandle, IService> _servicesByContract = new Dictionary<RuntimeTypeHandle, IService>();
        private readonly Dictionary<IService, ServiceEntry> _entriesByService = new Dictionary<IService, ServiceEntry>(ReferenceComparer<IService>.Instance);
        private readonly List<IService> _registrationOrder = new List<IService>();

        // --- 轮询列表（按 Priority 降序排列，dirty-flag + lazy-sort 维护） ---

        private readonly List<IServiceTickable> _tickables = new List<IServiceTickable>();
        private readonly List<IServiceFixedTickable> _fixedTickables = new List<IServiceFixedTickable>();
        private readonly List<IServiceLateTickable> _lateTickables = new List<IServiceLateTickable>();
        private readonly List<IServiceGizmoDrawable> _gizmoDrawables = new List<IServiceGizmoDrawable>();

        // --- 迭代安全状态 ---

        private bool _isIterating;
        private bool _disposePending;

        // --- 延迟变更队列：迭代中注册/注销请求延迟到本轮迭代结束后执行 ---

        private readonly List<PendingChange> _pendingChanges = new List<PendingChange>();

        // --- CreationIndex：同优先级服务的稳定排序（按注册顺序） ---

        private int _nextCreationIndex;

        // --- 轮询列表（按 Priority 降序排列，dirty-flag + lazy-sort 维护） ---

        private bool _tickablesDirty;
        private bool _fixedTickablesDirty;
        private bool _lateTickablesDirty;
        private bool _gizmoDrawablesDirty;

        // --- 实例级 Comparison 委托：CreationIndex tiebreaker 需访问 _entriesByService，不可为 static ---

        private readonly Comparison<IServiceTickable> _tickComparison;
        private readonly Comparison<IServiceFixedTickable> _fixedTickComparison;
        private readonly Comparison<IServiceLateTickable> _lateTickComparison;
        private readonly Comparison<IServiceGizmoDrawable> _gizmoComparison;

        private const int MISSING_INDEX = -1;

        #endregion

        #region 属性 [PROPERTIES]

        internal EServiceScopeKind Kind { get; }
        public string Name { get; }
        internal bool IsDisposed { get; private set; }
        internal int ServiceCount => _registrationOrder.Count;

        /// <summary>
        /// 作用域排序优先级（数值越小越先初始化、越后关闭）。
        /// 由 <see cref="ServiceScopeOrder.FromKind"/> 映射，替代隐式枚举值比较。
        /// </summary>
        internal int Order => ServiceScopeOrder.FromKind(Kind);

        #endregion

        #region 构造 [CONSTRUCTION]

        internal ServiceScope(EServiceScopeKind kind, string name, ServiceWorld world)
        {
            Kind = kind;
            Name = name;
            _world = world;
            _tickComparison = CompareByPriority<IServiceTickable>;
            _fixedTickComparison = CompareByPriority<IServiceFixedTickable>;
            _lateTickComparison = CompareByPriority<IServiceLateTickable>;
            _gizmoComparison = CompareByPriority<IServiceGizmoDrawable>;
        }

        #endregion

        #region 注册 [REGISTER]

        /// <summary>
        /// 将服务注册到作用域。仅存储引用和更新轮询列表，不调用 OnInit。
        /// <para>fail-fast：契约重复、作用域已销毁/销毁中、迭代中注册均抛出 <see cref="GameException"/>——
        /// 静默拒绝会产生"已创建未入册"的影子服务（随后被 OnInit 却永不 Shutdown）。</para>
        /// <para>此方法仅由 <see cref="ServiceWorld.BuildAsync"/> 调用，不参与延迟队列——构建不在 Tick 中发生。</para>
        /// </summary>
        internal void Register(Type[] contractTypes, IService service)
        {
            // BuildAsync 可能跨越 await（OnInitAsync）挂起，期间作用域被关闭（如场景卸载）后恢复，
            // 后续注册必须中止而不是写入已销毁的注册表
            if (IsDisposed)
                throw new GameException(StringUtility.Format(
                    "Scope {0} has been disposed; registration of '{1}' is rejected.",
                    Kind, contractTypes[0].FullName));

            // 检查所有契约是否已被注册：重复契约属于组合根编程错误
            for (int i = 0; i < contractTypes.Length; i++)
            {
                if (_servicesByContract.ContainsKey(contractTypes[i].TypeHandle))
                    throw new GameException(StringUtility.Format(
                        "Contract '{0}' has already been registered in {1} scope; reject duplicate registration of '{2}'.",
                        contractTypes[i].FullName, Kind, service.GetType().FullName));
            }

            if (_disposePending)
                throw new GameException(StringUtility.Format(
                    "Scope {0} is being disposed; registration of '{1}' is rejected.",
                    Kind, contractTypes[0].FullName));

            if (_isIterating)
                throw new GameException(StringUtility.Format(
                    "Cannot register '{0}' while {1} scope is iterating. " +
                    "Registration is driven by BuildAsync only; do not trigger a build of the same scope from within Tick.",
                    contractTypes[0].FullName, Kind));

            RegisterInternal(service, contractTypes);
        }

        /// <summary>
        /// 运行时注册单个服务到已构建的作用域。
        /// <para>与 <see cref="Register"/> 不同，此方法在注册完成后立即驱动服务生命周期
        /// （<see cref="IServiceLifecycle.Initialize"/> → <c>OnInit</c>），并触发
        /// <see cref="GameServices.onServiceRegistered"/> 事件。</para>
        /// <para>迭代中（Tick）调用时，默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）；
        /// 传入 <see cref="EDeferMode.Throw"/> 则立即抛出异常。</para>
        /// </summary>
        /// <typeparam name="T">服务契约类型。</typeparam>
        /// <param name="service">要注册的服务实例。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>注册的服务实例（延迟模式下尚未完成初始化）。</returns>
        internal T RegisterRuntime<T>(T service, EDeferMode deferMode = EDeferMode.Defer) where T : class, IService
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            var contractType = typeof(T);

            if (IsDisposed)
                throw new GameException(StringUtility.Format(
                    "Scope {0} has been disposed; runtime registration is rejected.", Kind));

            if (_disposePending)
                throw new GameException(StringUtility.Format(
                    "Scope {0} is being disposed; runtime registration is rejected.", Kind));

            // 契约查重：无论是否迭代，重复契约立即失败
            if (_servicesByContract.ContainsKey(contractType.TypeHandle))
                throw new GameException(StringUtility.Format(
                    "Contract '{0}' has already been registered in {1} scope.",
                    contractType.FullName, Kind));

            if (_isIterating)
            {
                if (deferMode == EDeferMode.Throw)
                    throw new GameException(StringUtility.Format(
                        "Cannot register '{0}' while {1} scope is iterating (EDeferMode.Throw).",
                        contractType.FullName, Kind));

                // 检查 pending 中是否已有同一契约的注册
                for (int i = 0; i < _pendingChanges.Count; i++)
                {
                    if (_pendingChanges[i].IsRegister && _pendingChanges[i].ContractType == contractType)
                        throw new GameException(StringUtility.Format(
                            "Contract '{0}' has a pending registration in {1} scope.",
                            contractType.FullName, Kind));
                }

                _pendingChanges.Add(PendingChange.ForRegister(service, contractType));
                return service;
            }

            var contractTypes = new[] { contractType };
            RegisterInternal(service, contractTypes);

            if (service is IServiceLifecycle lifecycle)
                lifecycle.Initialize(_world, this);

            return service;
        }

        /// <summary>
        /// 运行时注销并关闭单个服务（按运行时类型）。
        /// <para>触发 <see cref="IServiceLifecycle.Destroy"/>（→ <c>Shutdown</c>）并从注册表移除。</para>
        /// <para>迭代中（Tick）调用时，默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）；
        /// 传入 <see cref="EDeferMode.Throw"/> 则立即抛出异常。</para>
        /// </summary>
        /// <param name="serviceType">服务契约类型。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>成功注销返回 true；未找到返回 false。</returns>
        internal bool UnregisterRuntime(Type serviceType, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (IsDisposed) return false;

            if (!_servicesByContract.ContainsKey(serviceType.TypeHandle))
                return false;

            if (_isIterating)
            {
                if (deferMode == EDeferMode.Throw)
                    throw new GameException(StringUtility.Format(
                        "Cannot unregister '{0}' while {1} scope is iterating (EDeferMode.Throw).",
                        serviceType.FullName, Kind));

                _pendingChanges.Add(PendingChange.ForUnregister(serviceType));
                return true;
            }

            var service = _servicesByContract[serviceType.TypeHandle];

            if (service is IServiceLifecycle lifecycle)
                lifecycle.Destroy();

            if (_entriesByService.TryGetValue(service, out var entry))
                RemoveServiceInternal(service, entry);

            return true;
        }

        /// <summary>
        /// 运行时注销并关闭单个服务。
        /// <para>触发 <see cref="IServiceLifecycle.Destroy"/>（→ <c>Shutdown</c>）并从注册表移除。</para>
        /// <para>迭代中（Tick）调用时，默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）；
        /// 传入 <see cref="EDeferMode.Throw"/> 则立即抛出异常。</para>
        /// </summary>
        /// <typeparam name="T">服务契约类型。</typeparam>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>成功注销返回 true；未找到返回 false。</returns>
        internal bool UnregisterRuntime<T>(EDeferMode deferMode = EDeferMode.Defer) where T : class, IService
        {
            return UnregisterRuntime(typeof(T), deferMode);
        }

        private void RegisterInternal(IService service, Type[] contractTypes)
        {
            // MonoBehaviour 服务的 Tick 应由 Unity 生命周期驱动，不可混入 ServiceScope 轮询列表
            if (service is MonoBehaviour)
            {
                if (service is IServiceTickable)
                    throw new GameException(StringUtility.Format(
                        "MonoBehaviour service '{0}' cannot implement IServiceTickable. " +
                        "Use Unity's Update() instead.", service.GetType().FullName));
                if (service is IServiceFixedTickable)
                    throw new GameException(StringUtility.Format(
                        "MonoBehaviour service '{0}' cannot implement IServiceFixedTickable. " +
                        "Use Unity's FixedUpdate() instead.", service.GetType().FullName));
                if (service is IServiceLateTickable)
                    throw new GameException(StringUtility.Format(
                        "MonoBehaviour service '{0}' cannot implement IServiceLateTickable. " +
                        "Use Unity's LateUpdate() instead.", service.GetType().FullName));
            }

            var handles = new RuntimeTypeHandle[contractTypes.Length];
            for (int i = 0; i < contractTypes.Length; i++)
            {
                handles[i] = contractTypes[i].TypeHandle;
                _servicesByContract[handles[i]] = service;
                _world.AddContract(this, handles[i], service);
            }

            var entry = new ServiceEntry
            {
                ContractHandles = handles,
                CreationIndex = _nextCreationIndex++,
                TickIndex = MISSING_INDEX,
                FixedTickIndex = MISSING_INDEX,
                LateTickIndex = MISSING_INDEX,
                GizmoIndex = MISSING_INDEX,
            };

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

        /// <summary>
        /// 非泛型查找（用于构造注入期按 Type 解析）。
        /// </summary>
        internal bool TryGet(Type serviceType, out IService service)
        {
            if (_servicesByContract.TryGetValue(serviceType.TypeHandle, out var raw))
            {
                service = raw;
                return service != null;
            }
            service = null;
            return false;
        }

        /// <summary>
        /// 实例是否已注册（构建回滚时用于识别"已创建未注册"的孤儿实例）。
        /// </summary>
        internal bool Contains(IService service) => _entriesByService.ContainsKey(service);

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
                if (GameServices.HasInterceptors)
                {
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
                else
                {
                    // 无拦截器（发布构建常态）：跳过逐服务通知与 as 转换——对齐零开销轮询路径。
                    // 注：若服务在 Tick 中途添加拦截器，本轮余下服务不通知，下一帧生效。
                    for (int i = 0; i < count; i++)
                    {
                        var tickable = _tickables[i];
                        try { tickable.Tick(elapseSeconds, realElapseSeconds); }
                        catch (Exception ex) { LogTickFailure(tickable, nameof(Tick), ex); }
                    }
                }
            }
            finally
            {
                _isIterating = false;
                FlushDisposeIfPending();
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
            finally { _isIterating = false; FlushDisposeIfPending(); FlushPendingChanges(); }
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
            finally { _isIterating = false; FlushDisposeIfPending(); FlushPendingChanges(); }
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
            finally { _isIterating = false; FlushDisposeIfPending(); FlushPendingChanges(); }
        }

        private static void LogTickFailure(object service, string methodName, Exception ex)
        {
            LogUtility.Error("Service '{0}' threw in {1}:\n{2}",
                service.GetType().FullName, methodName, ex);
        }

        #endregion

        #region 迭代安全 [ITERATION SAFETY]

        private void FlushDisposeIfPending()
        {
            // 迭代中请求的作用域销毁：待迭代结束后执行
            if (_disposePending)
                DisposeInternal();
        }

        /// <summary>
        /// 处理迭代中积累的延迟注册/注销请求。在每个轮询方法结束后调用。
        /// <para>作用域已销毁时清空队列（不处理）。</para>
        /// </summary>
        private void FlushPendingChanges()
        {
            if (_pendingChanges.Count == 0) return;

            if (IsDisposed)
            {
                _pendingChanges.Clear();
                return;
            }

            for (int i = 0; i < _pendingChanges.Count; i++)
            {
                var change = _pendingChanges[i];
                try
                {
                    if (change.IsRegister)
                    {
                        if (change.Service == null) continue;
                        if (_entriesByService.ContainsKey(change.Service)) continue;
                        if (_servicesByContract.ContainsKey(change.ContractType.TypeHandle)) continue;

                        var contractTypes = new[] { change.ContractType };
                        RegisterInternal(change.Service, contractTypes);

                        if (change.Service is IServiceLifecycle lifecycle)
                            lifecycle.Initialize(_world, this);
                    }
                    else
                    {
                        if (!_servicesByContract.TryGetValue(change.ContractType.TypeHandle, out var service))
                            continue;

                        if (service is IServiceLifecycle lifecycle)
                            lifecycle.Destroy();

                        if (_entriesByService.TryGetValue(service, out var entry))
                            RemoveServiceInternal(service, entry);
                    }
                }
                catch (Exception ex)
                {
                    LogUtility.Error("Failed to flush pending {0} for '{1}':\n{2}",
                        change.IsRegister ? "register" : "unregister",
                        change.ContractType.FullName, ex);
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
                {
                    _servicesByContract.Remove(entry.ContractHandles[i]);
                    _world.RemoveContract(this, entry.ContractHandles[i], service);
                }
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
            {
                _servicesByContract.Remove(entry.ContractHandles[i]);
                _world.RemoveContract(this, entry.ContractHandles[i], service);
            }

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

        /// <summary>
        /// 异步销毁作用域。对实现 <see cref="IAsyncShutdownService"/> 的服务先调用 <c>OnShutdownAsync</c>，
        /// 再调用同步 <c>Shutdown</c>。逆注册序（= 逆依赖拓扑序）执行。
        /// </summary>
        internal async UniTask DisposeAsync()
        {
            if (IsDisposed) return;

            if (_isIterating)
            {
                _disposePending = true;
                return;
            }

            _isIterating = false;
            _disposePending = false;
            _pendingChanges.Clear();
            _isDisposing = true;

            for (int i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                if (service == null || !_entriesByService.ContainsKey(service)) continue;

                if (service is IAsyncShutdownService asyncSvc)
                {
                    try { await asyncSvc.OnShutdownAsync(); }
                    catch (Exception ex)
                    {
                        LogUtility.Error("Service '{0}' OnShutdownAsync failed:\n{1}",
                            service.GetType().FullName, ex);
                    }
                }

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
                    ContractType = type != null ? type.FullName : "<unknown>",
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
            _tickables.Sort(_tickComparison);
            for (int i = 0; i < _tickables.Count; i++)
            {
                if (_tickables[i] is IService svc && _entriesByService.TryGetValue(svc, out var e))
                {
                    e.TickIndex = i;
                    _entriesByService[svc] = e;
                }
            }
            _tickablesDirty = false;
        }

        private void SortFixedTickablesIfDirty()
        {
            if (!_fixedTickablesDirty) return;
            _fixedTickables.Sort(_fixedTickComparison);
            for (int i = 0; i < _fixedTickables.Count; i++)
            {
                if (_fixedTickables[i] is IService svc && _entriesByService.TryGetValue(svc, out var e))
                {
                    e.FixedTickIndex = i;
                    _entriesByService[svc] = e;
                }
            }
            _fixedTickablesDirty = false;
        }

        private void SortLateTickablesIfDirty()
        {
            if (!_lateTickablesDirty) return;
            _lateTickables.Sort(_lateTickComparison);
            for (int i = 0; i < _lateTickables.Count; i++)
            {
                if (_lateTickables[i] is IService svc && _entriesByService.TryGetValue(svc, out var e))
                {
                    e.LateTickIndex = i;
                    _entriesByService[svc] = e;
                }
            }
            _lateTickablesDirty = false;
        }

        private void SortGizmoDrawablesIfDirty()
        {
            if (!_gizmoDrawablesDirty) return;
            _gizmoDrawables.Sort(_gizmoComparison);
            for (int i = 0; i < _gizmoDrawables.Count; i++)
            {
                if (_gizmoDrawables[i] is IService svc && _entriesByService.TryGetValue(svc, out var e))
                {
                    e.GizmoIndex = i;
                    _entriesByService[svc] = e;
                }
            }
            _gizmoDrawablesDirty = false;
        }

        /// <summary>
        /// Priority 降序比较器（高优先在前）；同优先级按 CreationIndex 升序（先注册先执行）。
        /// <para>实例方法：CreationIndex 从 <c>_entriesByService</c> 查找，需访问实例状态。</para>
        /// </summary>
        private int CompareByPriority<T>(T a, T b)
        {
            int leftPriority = (a is IService sa) ? sa.Priority : 0;
            int rightPriority = (b is IService sb) ? sb.Priority : 0;
            int result = rightPriority.CompareTo(leftPriority);
            if (result != 0) return result;

            int leftCreation = GetCreationIndex(a as IService);
            int rightCreation = GetCreationIndex(b as IService);
            return leftCreation.CompareTo(rightCreation);
        }

        private int GetCreationIndex(IService service)
        {
            if (service != null && _entriesByService.TryGetValue(service, out var entry))
                return entry.CreationIndex;
            return int.MaxValue;
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
                {
                    e.TickIndex = index;
                    _entriesByService[svc] = e;
                }
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
                {
                    e.FixedTickIndex = index;
                    _entriesByService[svc] = e;
                }
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
                {
                    e.LateTickIndex = index;
                    _entriesByService[svc] = e;
                }
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
                {
                    e.GizmoIndex = index;
                    _entriesByService[svc] = e;
                }
            }
            _gizmoDrawablesDirty = true;
        }

        #endregion

        #region 内部数据结构 [INTERNAL STRUCTURES]

        /// <summary>
        /// 服务的注册元数据。struct 以消除堆分配；字典回写模式（<c>_entriesByService[svc] = e</c>）更新字段。
        /// </summary>
        internal struct ServiceEntry
        {
            public RuntimeTypeHandle[] ContractHandles;
            public int CreationIndex;
            public int TickIndex;
            public int FixedTickIndex;
            public int LateTickIndex;
            public int GizmoIndex;
        }

        /// <summary>
        /// 迭代中延迟执行的注册/注销请求。
        /// </summary>
        private readonly struct PendingChange
        {
            public readonly bool IsRegister;
            public readonly IService Service;
            public readonly Type ContractType;

            private PendingChange(bool isRegister, IService service, Type contractType)
            {
                IsRegister = isRegister;
                Service = service;
                ContractType = contractType;
            }

            public static PendingChange ForRegister(IService service, Type contractType)
                => new PendingChange(true, service, contractType);

            public static PendingChange ForUnregister(Type contractType)
                => new PendingChange(false, null, contractType);
        }

        #endregion
    }
}
