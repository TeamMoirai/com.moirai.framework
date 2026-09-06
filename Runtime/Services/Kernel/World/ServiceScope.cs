using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务作用域容器。管理单个作用域内服务的注册表、轮询列表和迭代安全机制。
    /// <para><b>所有权</b>：注册/注销由 <see cref="ServiceWorld"/> 驱动，外部代码不直接操作本类。</para>
    /// <para>两阶段构建：<see cref="RegisterDeferred"/> 仅入注册表（不驱动生命周期、不加入轮询列表）；
    /// 世界 <see cref="ServiceWorld.Initialize"/> 拓扑排序后逐服务 <see cref="ActivateService"/> 补齐轮询列表并驱动 OnInit，
    /// 同时记录激活完成序。</para>
    /// <para>Dispose 时按逆激活序（= 逆初始化序，依赖方先关闭）关闭全部已初始化服务；
    /// 未初始化服务归入兜底桶按逆注册序关闭。</para>
    /// <para><b>线程契约</b>：所有方法仅限 Unity 主线程调用。</para>
    /// </summary>
    internal sealed class ServiceScope : IDisposable
    {
        #region 常量与字段 [CONSTANTS & FIELDS]

        // --- 服务存储 ---

        private readonly ServiceWorld _world;
        private readonly Dictionary<RuntimeTypeHandle, IService> _servicesByContract = new Dictionary<RuntimeTypeHandle, IService>();
        private readonly Dictionary<IService, ServiceEntry> _entriesByService = new Dictionary<IService, ServiceEntry>(ReferenceComparer<IService>.Instance);
        private readonly List<IService> _registrationOrder = new List<IService>();

        // --- 激活完成序（= 初始化完成序）---

        // ActivateService 成功驱动 OnInit 后追加。两阶段构建下初始化顺序由世界拓扑排序决定，
        // 注册顺序不保证等于激活顺序——关闭必须依据本列表按"逆激活序（依赖方先）"执行，
        // 逆注册序无法替代（违反 IService.OnShutdown 的"严格逆初始化序"契约）。

        private readonly List<IService> _activationOrder = new List<IService>();

        // --- 轮询列表（按 Priority 降序排列，dirty-flag + lazy-sort 维护） ---

        private readonly List<IServiceTickable> _tickables = new List<IServiceTickable>();
        private readonly List<IServiceFixedTickable> _fixedTickables = new List<IServiceFixedTickable>();
        private readonly List<IServiceLateTickable> _lateTickables = new List<IServiceLateTickable>();
        private readonly List<IServiceGizmoDrawable> _gizmoDrawables = new List<IServiceGizmoDrawable>();

        // --- 迭代安全状态 ---

        private bool _isIterating;
        private bool _disposePending;

        // --- 轮询失败粘性标记：发生过任一轮询异常后置位，启用成功路径的失败计数清零检查。
        // 常态 false——健康服务热路径零字典访问；一旦发生过异常则保持置位（会话级）。 ---

        private bool _hasPollFailures;

        // --- 延迟变更队列：迭代中注册/注销请求延迟到本轮迭代结束后执行 ---

        private readonly List<PendingChange> _pendingChanges = new List<PendingChange>();

        // --- CreationIndex：同优先级服务的稳定排序（按注册顺序） ---

        private int _nextCreationIndex;

        // --- 轮询列表脏标记 ---

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

        // ── Tick 异常分级策略：开发期 fail-fast（记录后上抛，第一时间暴露缺陷），发布期隔离续跑（单服务故障不拖垮整帧）──
        // const 门控：JIT 裁剪死分支，Release 零运行时成本。
        internal const bool RETHROW_TICK_EXCEPTIONS =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                true;
#else
                false;
#endif

        // ── Tick 异常熔断：同一服务在同一轮询类别连续失败达到阈值即摘出对应轮询列表并汇总告警一次 ──

        /// <summary>
        /// 连续失败熔断默认阈值。
        /// </summary>
        internal const int DEFAULT_TICK_TRIP_THRESHOLD = 300;

        /// <summary>
        /// 连续失败熔断阈值：同一服务在同一轮询类别连续异常达到该次数即被摘除出对应轮询列表。
        /// 运行时可调（测试与运维调优）；重新注册服务即完全重置。
        /// </summary>
        internal static int s_TickFailureTripThreshold = DEFAULT_TICK_TRIP_THRESHOLD;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Stopwatch 时间戳 → 毫秒换算系数（轮询耗时统计专用）
        private static readonly double TIMESTAMP_TO_MS = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
#endif

        #endregion

        #region 属性 [PROPERTIES]

        internal EServiceScopeKind Kind { get; }
        public string Name { get; }
        internal bool IsDisposed { get; private set; }
        internal int ServiceCount => _registrationOrder.Count;

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
        /// 两阶段第一阶段：仅入注册表（契约映射 + 条目 + 注册序），不驱动生命周期、不加入轮询列表。
        /// 世界初始化时经 <see cref="ActivateService"/> 补齐轮询列表并驱动 OnInit。
        /// </summary>
        internal void RegisterDeferred(Type contractType, IService service, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            if (IsDisposed || _disposePending)
                throw new GameException(StringUtility.Format(
                    "Scope {0} is disposed or disposing; registration is rejected.", Kind));

            if (_servicesByContract.ContainsKey(contractType.TypeHandle))
                throw new GameException(StringUtility.Format(
                    "Contract '{0}' has already been registered in {1} scope.",
                    contractType.FullName, Kind));

            RegisterInternal(service, new[] { contractType }, activateTickables: false);
        }

        /// <summary>
        /// 两阶段第二阶段（逐服务）：补齐轮询列表并驱动 OnInit。由世界按拓扑序调用。
        /// <para>OnInit 成功完成后记录进激活序（<see cref="_activationOrder"/>）——
        /// 作用域关闭按逆激活序执行；初始化抛异常的服务不进入激活序，销毁时归入未激活桶兜底关闭。</para>
        /// </summary>
        internal void ActivateService(IService service)
        {
            ActivateTickables(service);

            if (service is IServiceLifecycle lifecycle)
                lifecycle.Initialize(_world, this);

            if (_entriesByService.TryGetValue(service, out var entry) && !entry.ActivationRecorded)
            {
                entry.ActivationRecorded = true;
                _entriesByService[service] = entry;
                _activationOrder.Add(service);
            }
        }

        /// <summary>
        /// 待初始化阶段注销：从注册表移除（无生命周期、无事件——服务从未初始化）。
        /// </summary>
        internal bool UnregisterDeferred(Type contractType, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (IsDisposed) return false;
            if (!_servicesByContract.TryGetValue(contractType.TypeHandle, out var service)) return false;

            if (_entriesByService.TryGetValue(service, out var entry))
            {
                for (int i = 0; i < entry.ContractHandles.Length; i++)
                {
                    _servicesByContract.Remove(entry.ContractHandles[i]);
                    _world.RemoveBinding(this, entry.ContractHandles[i], service);
                }
                _registrationOrder.Remove(service);
                _entriesByService.Remove(service);
            }
            return true;
        }

        /// <summary>
        /// 运行时注册（世界已初始化）：立即驱动服务生命周期（OnInit）。
        /// <para>迭代中（Tick）调用时，默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）。</para>
        /// </summary>
        internal IService RegisterRuntime(Type contractType, IService service, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            if (IsDisposed)
                throw new GameException(StringUtility.Format(
                    "Scope {0} has been disposed; runtime registration is rejected.", Kind));

            if (_disposePending)
                throw new GameException(StringUtility.Format(
                    "Scope {0} is being disposed; runtime registration is rejected.", Kind));

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

                for (int i = 0; i < _pendingChanges.Count; i++)
                {
                    if (_pendingChanges[i].Kind != PendingChangeKind.Unregister &&
                        _pendingChanges[i].ContractType == contractType)
                        throw new GameException(StringUtility.Format(
                            "Contract '{0}' has a pending registration in {1} scope.",
                            contractType.FullName, Kind));
                }

                _pendingChanges.Add(PendingChange.ForRegister(service, contractType));
                return service;
            }

            RegisterInternal(service, new[] { contractType }, activateTickables: false);

            // 与世界初始化路径共用 ActivateService：补齐轮询列表 + 驱动 OnInit + 记录激活序
            ActivateService(service);

            return service;
        }

        /// <summary>
        /// 运行时注销并关闭单个服务（触发 OnShutdown 并从注册表移除）。
        /// </summary>
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
                lifecycle.Destroy(_world);

            if (_entriesByService.TryGetValue(service, out var entry))
                RemoveServiceInternal(service, entry);

            return true;
        }

        /// <summary>
        /// 为已注册的服务实例附加一个新契约绑定（多契约支持）。
        /// </summary>
        internal void BindAdditionalContractRuntime(Type contractType, IService service, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (service == null) throw new ArgumentNullException(nameof(service));

            if (IsDisposed || _disposePending)
                throw new GameException(StringUtility.Format(
                    "Scope {0} is disposed or disposing; contract binding is rejected.", Kind));

            if (_servicesByContract.ContainsKey(contractType.TypeHandle))
                throw new GameException(StringUtility.Format(
                    "Contract '{0}' has already been registered in {1} scope.",
                    contractType.FullName, Kind));

            if (!_entriesByService.ContainsKey(service))
                throw new GameException(StringUtility.Format(
                    "Service '{0}' is not registered in {1} scope; register it before binding additional contracts.",
                    service.GetType().FullName, Kind));

            if (_isIterating)
            {
                if (deferMode == EDeferMode.Throw)
                    throw new GameException(StringUtility.Format(
                        "Cannot bind '{0}' while {1} scope is iterating (EDeferMode.Throw).",
                        contractType.FullName, Kind));

                for (int i = 0; i < _pendingChanges.Count; i++)
                {
                    if (_pendingChanges[i].Kind != PendingChangeKind.Unregister &&
                        _pendingChanges[i].ContractType == contractType)
                        throw new GameException(StringUtility.Format(
                            "Contract '{0}' has a pending registration in {1} scope.",
                            contractType.FullName, Kind));
                }

                _pendingChanges.Add(PendingChange.ForBind(service, contractType));
                return;
            }

            _world.InvokeRegistering(service, contractType, Kind);
            AttachContractCore(service, contractType);

            // 待初始化阶段的附加契约：同步挂起图（拓扑边解析需要全部契约）；
            // 世界已初始化的运行时绑定不进入挂起图
            if (!_world.IsInitialized)
                _world.TrackPendingContract(contractType, service);
        }

        /// <summary>
        /// 附加契约句柄到既有条目（立即路径与延迟 flush 共用）。
        /// </summary>
        private void AttachContractCore(IService service, Type contractType)
        {
            var entry = _entriesByService[service];

            var oldHandles = entry.ContractHandles;
            var newHandles = new RuntimeTypeHandle[oldHandles.Length + 1];
            Array.Copy(oldHandles, newHandles, oldHandles.Length);
            newHandles[oldHandles.Length] = contractType.TypeHandle;
            entry.ContractHandles = newHandles;

            _servicesByContract[newHandles[oldHandles.Length]] = service;
            _world.AddBinding(this, newHandles[oldHandles.Length], service);

            _entriesByService[service] = entry;
            _world.InvokeRegistered(service, contractType, Kind);
        }

        private void RegisterInternal(IService service, Type[] contractTypes, bool activateTickables)
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
                _world.AddBinding(this, handles[i], service);
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

            _registrationOrder.Add(service);
            _entriesByService[service] = entry;

            if (activateTickables)
                ActivateTickables(service);

            _world.InvokeRegistering(service, contractTypes[0], Kind);
        }

        /// <summary>
        /// 将服务加入其能力接口对应的轮询列表（幂等——已有索引即跳过）。
        /// </summary>
        private void ActivateTickables(IService service)
        {
            if (!_entriesByService.TryGetValue(service, out var entry)) return;

            if (service is IServiceTickable tickable && entry.TickIndex == MISSING_INDEX)
            {
                entry.TickIndex = _tickables.Count;
                _tickables.Add(tickable);
                _tickablesDirty = true;
            }
            if (service is IServiceFixedTickable fixedTickable && entry.FixedTickIndex == MISSING_INDEX)
            {
                entry.FixedTickIndex = _fixedTickables.Count;
                _fixedTickables.Add(fixedTickable);
                _fixedTickablesDirty = true;
            }
            if (service is IServiceLateTickable lateTickable && entry.LateTickIndex == MISSING_INDEX)
            {
                entry.LateTickIndex = _lateTickables.Count;
                _lateTickables.Add(lateTickable);
                _lateTickablesDirty = true;
            }
            if (service is IServiceGizmoDrawable gizmo && entry.GizmoIndex == MISSING_INDEX)
            {
                entry.GizmoIndex = _gizmoDrawables.Count;
                _gizmoDrawables.Add(gizmo);
                _gizmoDrawablesDirty = true;
            }

            _entriesByService[service] = entry;
        }

        #endregion

        #region 查找 [LOOKUP]

        /// <summary>
        /// 非泛型查找（用于注册期按 Type 解析与 Mono 服务副本检测）。
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
        /// 实例是否已注册（用于识别同实例多契约绑定）。
        /// </summary>
        internal bool Contains(IService service) => _entriesByService.ContainsKey(service);

        #endregion

        #region 轮询 [TICK]

        // 单一循环体——拦截器已上移至世界层作用域帧边界，本层无快/慢路径分裂。
        // 逐服务耗时统计仅编辑器/开发构建启用（编译期门控，Release 零成本）。

        internal void Tick(float elapseSeconds, float realElapseSeconds)
        {
            SortTickablesIfDirty();
            _isIterating = true;
            // 局部变量阻断编译期可达性折叠——避免 throw 后的熔断补偿代码触发 CS0162（JIT 常量传播，零运行时差异）
            bool rethrow = RETHROW_TICK_EXCEPTIONS;
            try
            {
                int count = _tickables.Count;
                for (int i = 0; i < count; i++)
                {
                    var tickable = _tickables[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    try
                    {
                        tickable.Tick(elapseSeconds, realElapseSeconds);
                        ResetPollFailuresIfAny(tickable, PollCategory.Tick);
                    }
                    catch (Exception ex)
                    {
                        LogTickFailure(tickable, nameof(Tick), ex);
                        bool tripped = RecordPollFailure(tickable, PollCategory.Tick, nameof(Tick));
                        if (rethrow) throw;
                        if (tripped) { i--; count--; }
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    finally { RecordPollDuration(tickable, System.Diagnostics.Stopwatch.GetTimestamp() - start); }
#endif
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
            bool rethrow = RETHROW_TICK_EXCEPTIONS;
            try
            {
                int count = _fixedTickables.Count;
                for (int i = 0; i < count; i++)
                {
                    var fixedTickable = _fixedTickables[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    try
                    {
                        fixedTickable.FixedTick(elapseSeconds, realElapseSeconds);
                        ResetPollFailuresIfAny(fixedTickable, PollCategory.FixedTick);
                    }
                    catch (Exception ex)
                    {
                        LogTickFailure(fixedTickable, nameof(FixedTick), ex);
                        bool tripped = RecordPollFailure(fixedTickable, PollCategory.FixedTick, nameof(FixedTick));
                        if (rethrow) throw;
                        if (tripped) { i--; count--; }
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    finally { RecordPollDuration(fixedTickable, System.Diagnostics.Stopwatch.GetTimestamp() - start); }
#endif
                }
            }
            finally { _isIterating = false; FlushDisposeIfPending(); FlushPendingChanges(); }
        }

        internal void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            SortLateTickablesIfDirty();
            _isIterating = true;
            bool rethrow = RETHROW_TICK_EXCEPTIONS;
            try
            {
                int count = _lateTickables.Count;
                for (int i = 0; i < count; i++)
                {
                    var lateTickable = _lateTickables[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    try
                    {
                        lateTickable.LateTick(elapseSeconds, realElapseSeconds);
                        ResetPollFailuresIfAny(lateTickable, PollCategory.LateTick);
                    }
                    catch (Exception ex)
                    {
                        LogTickFailure(lateTickable, nameof(LateTick), ex);
                        bool tripped = RecordPollFailure(lateTickable, PollCategory.LateTick, nameof(LateTick));
                        if (rethrow) throw;
                        if (tripped) { i--; count--; }
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    finally { RecordPollDuration(lateTickable, System.Diagnostics.Stopwatch.GetTimestamp() - start); }
#endif
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
                    catch (Exception ex)
                    {
                        LogTickFailure(_gizmoDrawables[i], "OnDrawGizmos", ex);
                        if (RETHROW_TICK_EXCEPTIONS) throw;
                    }
                }
            }
            finally { _isIterating = false; FlushDisposeIfPending(); FlushPendingChanges(); }
        }

        private static void LogTickFailure(IService service, string methodName, Exception ex)
        {
            LogUtility.Error("Service '{0}' threw in {1}:\n{2}",
                service.GetType().FullName, methodName, ex);
        }

        /// <summary>
        /// 轮询类别。异常熔断按类别独立计数与摘除——某类轮询失败不影响其它类别的连续性判定。
        /// </summary>
        private enum PollCategory : byte
        {
            Tick = 0,
            FixedTick = 1,
            LateTick = 2,
        }

        /// <summary>
        /// 记录一次轮询异常并按需熔断。
        /// </summary>
        /// <param name="service">抛出异常的服务实例。</param>
        /// <param name="category">轮询类别（独立计数）。</param>
        /// <param name="methodName">轮询方法名（告警文案用）。</param>
        /// <returns>是否已将服务从对应轮询列表移除；迭代方需回退索引以补偿 swap-remove 移位。</returns>
        private bool RecordPollFailure(IService service, PollCategory category, string methodName)
        {
            _hasPollFailures = true;

            if (!_entriesByService.TryGetValue(service, out var entry))
                return false;

            int failures;
            switch (category)
            {
                case PollCategory.Tick:
                    failures = ++entry.TickConsecutiveFailures;
                    break;
                case PollCategory.FixedTick:
                    failures = ++entry.FixedTickConsecutiveFailures;
                    break;
                default:
                    failures = ++entry.LateTickConsecutiveFailures;
                    break;
            }

            _entriesByService[service] = entry;

            if (failures < s_TickFailureTripThreshold) return false;

            TripFromPollList(service, entry, category, methodName, failures);
            return true;
        }

        /// <summary>
        /// 熔断：将服务从对应轮询类别移除（swap-remove O(1)）并汇总告警一次。
        /// 服务条目保留——仍可解析、仍参与其它类别轮询；重新注册即完全重置。
        /// </summary>
        private void TripFromPollList(IService service, ServiceEntry entry, PollCategory category, string methodName, int failures)
        {
            switch (category)
            {
                case PollCategory.Tick:
                    if (entry.TickIndex != MISSING_INDEX) RemoveTickableAt(entry.TickIndex);
                    entry.TickIndex = MISSING_INDEX;
                    break;
                case PollCategory.FixedTick:
                    if (entry.FixedTickIndex != MISSING_INDEX) RemoveFixedTickableAt(entry.FixedTickIndex);
                    entry.FixedTickIndex = MISSING_INDEX;
                    break;
                default:
                    if (entry.LateTickIndex != MISSING_INDEX) RemoveLateTickableAt(entry.LateTickIndex);
                    entry.LateTickIndex = MISSING_INDEX;
                    break;
            }

            _entriesByService[service] = entry;

            LogUtility.Warning(
                "Service '{0}' was removed from {1} polling after {2} consecutive failures (trip threshold {3}).",
                service.GetType().FullName, methodName, failures, s_TickFailureTripThreshold);
        }

        /// <summary>
        /// 对应类别成功一次即清零该类别的连续失败计数。仅在发生过失败后才有实际开销
        /// （<see cref="_hasPollFailures"/> 常态为 false，健康服务热路径零字典访问）。
        /// </summary>
        private void ResetPollFailuresIfAny(IService service, PollCategory category)
        {
            if (!_hasPollFailures) return;

            if (!_entriesByService.TryGetValue(service, out var entry))
                return;

            switch (category)
            {
                case PollCategory.Tick:
                    if (entry.TickConsecutiveFailures == 0) return;
                    entry.TickConsecutiveFailures = 0;
                    break;
                case PollCategory.FixedTick:
                    if (entry.FixedTickConsecutiveFailures == 0) return;
                    entry.FixedTickConsecutiveFailures = 0;
                    break;
                default:
                    if (entry.LateTickConsecutiveFailures == 0) return;
                    entry.LateTickConsecutiveFailures = 0;
                    break;
            }

            _entriesByService[service] = entry;
        }

        /// <summary>
        /// 记录单次轮询耗时。仅编辑器/开发构建写入；Release 下方法体为空，JIT 裁剪为零开销。
        /// </summary>
        private void RecordPollDuration(IService service, long elapsedTimestamps)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_entriesByService.TryGetValue(service, out var entry))
                return;

            float ms = (float)(elapsedTimestamps * TIMESTAMP_TO_MS);
            entry.PollSamples++;
            entry.PollTotalMs += ms;
            if (ms > entry.PollPeakMs) entry.PollPeakMs = ms;

            _entriesByService[service] = entry;
#endif
        }

        #endregion

        #region 迭代安全 [ITERATION SAFETY]

        private void FlushDisposeIfPending()
        {
            if (_disposePending)
                DisposeInternal();
        }

        /// <summary>
        /// 处理迭代中积累的延迟注册/注销请求。在每个轮询方法结束后调用。
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
                    switch (change.Kind)
                    {
                        case PendingChangeKind.Register:
                        {
                            if (change.Service == null) continue;
                            if (_servicesByContract.ContainsKey(change.ContractType.TypeHandle)) continue;

                            // 前序延迟注册已为该实例创建条目——本请求退化为附加契约绑定
                            if (_entriesByService.ContainsKey(change.Service))
                            {
                                AttachContractCore(change.Service, change.ContractType);
                                continue;
                            }

                            RegisterInternal(change.Service, new[] { change.ContractType }, activateTickables: false);

                            // 与世界初始化路径共用 ActivateService：补齐轮询列表 + 驱动 OnInit + 记录激活序
                            ActivateService(change.Service);
                            break;
                        }

                        case PendingChangeKind.BindAdditionalContract:
                        {
                            if (change.Service == null) continue;
                            if (!_entriesByService.ContainsKey(change.Service)) continue;

                            if (_servicesByContract.ContainsKey(change.ContractType.TypeHandle)) continue;

                            AttachContractCore(change.Service, change.ContractType);
                            break;
                        }

                        case PendingChangeKind.Unregister:
                        {
                            if (!_servicesByContract.TryGetValue(change.ContractType.TypeHandle, out var service))
                                continue;

                            if (service is IServiceLifecycle lifecycle)
                                lifecycle.Destroy(_world);

                            if (_entriesByService.TryGetValue(service, out var entry))
                                RemoveServiceInternal(service, entry);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogUtility.Error("Failed to flush pending {0} for '{1}':\n{2}",
                        change.Kind, change.ContractType.FullName, ex);
                }
            }
            _pendingChanges.Clear();
        }

        #endregion

        #region 关闭 [SHUTDOWN]

        private bool _isDisposing;

        /// <summary>
        /// 关闭单个服务：生命周期驱动统一走 <see cref="IServiceLifecycle.Destroy"/>（状态机唯一路径），
        /// 本方法仅负责注册表清理。
        /// </summary>
        private void ShutdownService(IService service)
        {
            if (!_entriesByService.TryGetValue(service, out var entry)) return;

            if (service is IServiceLifecycle lifecycle)
            {
                lifecycle.Destroy(_world);
            }
            else
            {
                // 非生命周期服务（裸 IService 实现）：无状态机，直接回调
                _world.InvokeShutdown(service);
                try { service.OnShutdown(); }
                catch (Exception ex) { LogUtility.Error(ex.ToString()); }
            }

            // 整体销毁时跳过逐项列表移除（由 DisposeInternal 统一 Clear），
            // 但注册表和 entries 必须逐项清理——否则作用域关闭后仍能解析到已关闭的服务
            if (_isDisposing)
            {
                for (int i = 0; i < entry.ContractHandles.Length; i++)
                {
                    _servicesByContract.Remove(entry.ContractHandles[i]);
                    _world.RemoveBinding(this, entry.ContractHandles[i], service);
                }
                _entriesByService.Remove(service);
                _world.InvokeUnregistered(service);
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
                _world.RemoveBinding(this, entry.ContractHandles[i], service);
            }

            // _registrationOrder 必须保持注册序——使用 List.Remove（O(n) 移位保序），
            // 不用 swap-with-last（会破坏依赖方的关闭顺序保证）。
            // _activationOrder 同理移除——注销的服务不得残留在关闭序列中。
            _registrationOrder.Remove(service);
            _activationOrder.Remove(service);

            // 轮询列表使用 swap-with-last O(1) 移除 + 置脏标记，下次迭代前 lazy-sort。
            if (entry.TickIndex != MISSING_INDEX) RemoveTickableAt(entry.TickIndex);
            if (entry.FixedTickIndex != MISSING_INDEX) RemoveFixedTickableAt(entry.FixedTickIndex);
            if (entry.LateTickIndex != MISSING_INDEX) RemoveLateTickableAt(entry.LateTickIndex);
            if (entry.GizmoIndex != MISSING_INDEX) RemoveGizmoDrawableAt(entry.GizmoIndex);

            _entriesByService.Remove(service);
            _world.InvokeUnregistered(service);
        }

        #endregion

        #region 销毁 [DISPOSE]

        public void Dispose()
        {
            if (IsDisposed) return;

            if (_isIterating)
            {
                _disposePending = true;
                return;
            }

            DisposeInternal();
        }

        private void DisposeInternal()
        {
            if (IsDisposed) return;
            PrepareDisposal();

            // 逆激活序（= 逆初始化序）关闭：依赖方（后初始化）先关闭，被依赖方后关闭。
            // 初始化顺序由世界拓扑排序决定、与注册顺序无关——逆注册序不保证满足
            // IService.OnShutdown 的"严格逆初始化序"契约（IService.cs），故依据激活记录执行。
            for (int i = _activationOrder.Count - 1; i >= 0; i--)
            {
                var service = _activationOrder[i];
                if (service != null && _entriesByService.ContainsKey(service))
                    ShutdownService(service);
            }

            // 未激活服务（世界未完成初始化即销毁；或 OnInit 抛异常未完成激活）：
            // 从未 OnInit，无初始化序可逆——保持既有兜底语义，仍驱动 OnShutdown，
            // 相对顺序沿用旧实现的逆注册序。
            for (int i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                if (service != null && _entriesByService.ContainsKey(service))
                    ShutdownService(service);
            }

            CompleteDisposal();
        }

        /// <summary>
        /// 销毁前置状态复位（同步/异步销毁共用）。
        /// </summary>
        private void PrepareDisposal()
        {
            _isIterating = false;
            _disposePending = false;
            _pendingChanges.Clear();

            _isDisposing = true;
        }

        /// <summary>
        /// 销毁收尾（同步/异步销毁共用）。
        /// </summary>
        private void CompleteDisposal()
        {
            _isDisposing = false;

            _registrationOrder.Clear();
            _activationOrder.Clear();
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
        /// 再调用同步 <c>OnShutdown</c>。按逆激活序（= 逆初始化序）执行；
        /// 未激活服务归入兜底桶按逆注册序关闭（与 <see cref="DisposeInternal"/> 同语义）。
        /// </summary>
        internal async UniTask DisposeAsync()
        {
            if (IsDisposed) return;

            if (_isIterating)
            {
                _disposePending = true;
                return;
            }

            PrepareDisposal();

            // 已激活服务：逆激活序，先异步关闭再同步关闭
            for (int i = _activationOrder.Count - 1; i >= 0; i--)
            {
                var service = _activationOrder[i];
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

            // 未激活服务：无初始化序可逆，保持既有兜底语义按逆注册序关闭
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

            CompleteDisposal();
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    PollAvgMs = entry.PollSamples > 0 ? entry.PollTotalMs / entry.PollSamples : 0f,
                    PollPeakMs = entry.PollPeakMs,
                    PollSamples = entry.PollSamples,
#endif
                });
            }
        }

        /// <summary>
        /// 清零本作用域全部服务的轮询耗时统计（不影响失败计数与熔断状态）。
        /// </summary>
        internal void ResetPollStatistics()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 遍历 _registrationOrder 而非 _entriesByService——索引器回写会使字典版本号递增，
            // 边遍历边写回同一字典会抛 InvalidOperationException
            for (int i = 0; i < _registrationOrder.Count; i++)
            {
                var service = _registrationOrder[i];
                if (service == null || !_entriesByService.TryGetValue(service, out var entry)) continue;

                if (entry.PollSamples == 0 && entry.PollTotalMs == 0f && entry.PollPeakMs == 0f) continue;

                entry.PollTotalMs = 0f;
                entry.PollPeakMs = 0f;
                entry.PollSamples = 0;
                _entriesByService[service] = entry;
            }
#endif
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
                var svc = _tickables[i];
                if (_entriesByService.TryGetValue(svc, out var e))
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
                var svc = _fixedTickables[i];
                if (_entriesByService.TryGetValue(svc, out var e))
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
                var svc = _lateTickables[i];
                if (_entriesByService.TryGetValue(svc, out var e))
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
                var svc = _gizmoDrawables[i];
                if (_entriesByService.TryGetValue(svc, out var e))
                {
                    e.GizmoIndex = i;
                    _entriesByService[svc] = e;
                }
            }
            _gizmoDrawablesDirty = false;
        }

        /// <summary>
        /// Priority 降序比较器（高优先在前）；同优先级按 CreationIndex 升序（先注册先执行）。
        /// </summary>
        private int CompareByPriority<T>(T a, T b) where T : class, IService
        {
            int result = b.Priority.CompareTo(a.Priority);
            if (result != 0) return result;

            int leftCreation = GetCreationIndex(a);
            int rightCreation = GetCreationIndex(b);
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
                if (_entriesByService.TryGetValue(moved, out var e))
                {
                    e.TickIndex = index;
                    _entriesByService[moved] = e;
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
                if (_entriesByService.TryGetValue(moved, out var e))
                {
                    e.FixedTickIndex = index;
                    _entriesByService[moved] = e;
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
                if (_entriesByService.TryGetValue(moved, out var e))
                {
                    e.LateTickIndex = index;
                    _entriesByService[moved] = e;
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
                if (_entriesByService.TryGetValue(moved, out var e))
                {
                    e.GizmoIndex = index;
                    _entriesByService[moved] = e;
                }
            }
            _gizmoDrawablesDirty = true;
        }

        #endregion

        #region 内部数据结构 [INTERNAL STRUCTURES]

        /// <summary>
        /// 服务的注册元数据。struct 以消除堆分配；字典回写模式（<c>_entriesByService[svc] = e</c>）更新字段。
        /// <para>轮询耗时统计字段仅编辑器/开发构建存在（编译期裁剪，Release 零内存成本）。</para>
        /// </summary>
        internal struct ServiceEntry
        {
            public RuntimeTypeHandle[] ContractHandles;
            public int CreationIndex;
            public int TickIndex;
            public int FixedTickIndex;
            public int LateTickIndex;
            public int GizmoIndex;

            /// <summary>是否已记录进激活序（幂等守卫——ActivateService 重复调用不重复入列）。</summary>
            public bool ActivationRecorded;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // ── 轮询耗时统计（仅编辑器/开发构建；Release 结构体不含此 20 字节）──

            public float PollTotalMs;
            public float PollPeakMs;
            public int PollSamples;
#endif

            // ── 各轮询类别的连续失败计数（异常熔断依据；对应类别成功一次即清零；Release 保留）──

            public int TickConsecutiveFailures;
            public int FixedTickConsecutiveFailures;
            public int LateTickConsecutiveFailures;
        }

        /// <summary>
        /// 迭代中延迟执行的注册/注销/附加契约请求。
        /// </summary>
        private readonly struct PendingChange
        {
            public readonly PendingChangeKind Kind;
            public readonly IService Service;
            public readonly Type ContractType;

            private PendingChange(PendingChangeKind kind, IService service, Type contractType)
            {
                Kind = kind;
                Service = service;
                ContractType = contractType;
            }

            public static PendingChange ForRegister(IService service, Type contractType)
                => new PendingChange(PendingChangeKind.Register, service, contractType);

            public static PendingChange ForUnregister(Type contractType)
                => new PendingChange(PendingChangeKind.Unregister, null, contractType);

            public static PendingChange ForBind(IService service, Type contractType)
                => new PendingChange(PendingChangeKind.BindAdditionalContract, service, contractType);
        }

        /// <summary>
        /// 延迟变更种类。
        /// </summary>
        private enum PendingChangeKind : byte
        {
            Register = 0,
            Unregister = 1,
            BindAdditionalContract = 2,
        }

        #endregion
    }
}
