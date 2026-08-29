using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务作用域容器。管理单个作用域内服务的注册表、轮询列表和迭代安全机制。
    /// <para><b>所有权</b>：注册/注销由 <see cref="GameServices.RegisterService"/> 驱动，
    /// 外部代码不直接操作本类；作用域的创建与销毁由 <see cref="ServiceWorld"/> 统一调度。</para>
    /// <para>OnInit 由 <see cref="GameServices.RegisterService"/> 在注册后立即驱动。</para>
    /// <para>Dispose 时逆注册序关闭全部服务（依赖方先关闭，被依赖方后关闭）。</para>
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

        // --- 轮询失败粘性标记：发生过任一轮询异常后置位，启用成功路径的失败计数清零检查。
        // 常态 false——健康服务热路径零字典访问；一旦发生过异常则保持置位（会话级）。 ---

        private bool _hasPollFailures;

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

        // ── Tick 异常分级策略：开发期 fail-fast（记录后上抛，第一时间暴露缺陷），发布期隔离续跑（单服务故障不拖垮整帧）──
        // const 门控：JIT 裁剪死分支，Release 零运行时成本。
        internal const bool RETHROW_TICK_EXCEPTIONS =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                true;
#else
                false;
#endif

        // ── Tick 异常熔断：同一服务在同一轮询类别连续失败达到阈值即摘出对应轮询列表并汇总告警一次，
        // 防止发布期坏服务每帧刷错误日志拖垮性能。开发环境在上抛前同样计数（编辑器可测试、诊断数据完整）。

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
                    if (_pendingChanges[i].Kind != PendingChangeKind.Unregister &&
                        _pendingChanges[i].ContractType == contractType)
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
                lifecycle.Initialize(this);

            return service;
        }

        /// <summary>
        /// 运行时注册服务到当前作用域（显式契约类型）。
        /// <para>与泛型 <see cref="RegisterRuntime{T}"/> 功能一致，但允许调用方指定契约类型，
        /// 避免传入 <c>IService</c> 基类引用时泛型推断为 <c>IService</c> 而非具体类型。</para>
        /// </summary>
        /// <param name="contractType">契约类型（注册键）。</param>
        /// <param name="service">服务实例。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>注册的服务实例。</returns>
        internal IService RegisterRuntime(Type contractType, IService service, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

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

            RegisterInternal(service, new[] { contractType });

            if (service is IServiceLifecycle lifecycle)
                lifecycle.Initialize(this);

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

        /// <summary>
        /// 为已注册的服务实例附加一个新契约绑定（多契约支持）。
        /// <para>不创建新条目、不驱动生命周期、不加入轮询列表——仅追加契约句柄到既有条目；
        /// 注销/作用域关闭时随条目一并移除。契约冲突立即失败。</para>
        /// <para>迭代中（Tick）调用时默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）。</para>
        /// </summary>
        /// <param name="contractType">附加的契约类型。</param>
        /// <param name="service">已在当前作用域注册的服务实例。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        internal void BindAdditionalContractRuntime(Type contractType, IService service, EDeferMode deferMode = EDeferMode.Defer)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (service == null) throw new ArgumentNullException(nameof(service));

            if (IsDisposed || _disposePending)
                throw new GameException(StringUtility.Format(
                    "Scope {0} is disposed or disposing; contract binding is rejected.", Kind));

            // 契约查重：无论是否迭代，重复契约立即失败
            if (_servicesByContract.ContainsKey(contractType.TypeHandle))
                throw new GameException(StringUtility.Format(
                    "Contract '{0}' has already been registered in {1} scope.",
                    contractType.FullName, Kind));

            // 实例必须已有条目（先经 RegisterRuntime 注册）
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

            GameServices.InvokeRegistering(service, contractType, Kind);
            AttachContractCore(service, contractType);
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
            _world.AddContract(this, newHandles[oldHandles.Length], service);

            _entriesByService[service] = entry;
            GameServices.InvokeRegistered(service, contractType, Kind);
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

            // _registrationOrder 记录插入序，用于逆序关闭与诊断收集。
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
            // 局部变量阻断编译期可达性折叠——避免 throw 后的熔断补偿代码触发 CS0162（JIT 常量传播，零运行时差异）
            bool rethrow = RETHROW_TICK_EXCEPTIONS;
            try
            {
                int count = _tickables.Count;
                if (GameServices.HasInterceptors)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var tickable = _tickables[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        long start = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                        try
                        {
                            GameServices.InvokeTick(tickable, elapseSeconds, realElapseSeconds);
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
                else
                {
                    // 无拦截器（发布构建常态）：跳过逐服务通知——对齐零开销轮询路径。
                    // 注：若服务在 Tick 中途添加拦截器，本轮余下服务不通知，下一帧生效。
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
        /// <para>开发环境在上抛前调用（计数跨帧累积，编辑器可测试）；发布期隔离路径据此熔断。</para>
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

                            var contractTypes = new[] { change.ContractType };
                            RegisterInternal(change.Service, contractTypes);

                            if (change.Service is IServiceLifecycle lifecycle)
                                lifecycle.Initialize(this);
                            break;
                        }

                        case PendingChangeKind.BindAdditionalContract:
                        {
                            if (change.Service == null) continue;
                            if (!_entriesByService.ContainsKey(change.Service)) continue;

                            // 契约可能已被队列中更早的 Register 占用——占用即跳过（幂等）
                            if (_servicesByContract.ContainsKey(change.ContractType.TypeHandle)) continue;

                            AttachContractCore(change.Service, change.ContractType);
                            break;
                        }

                        case PendingChangeKind.Unregister:
                        {
                            if (!_servicesByContract.TryGetValue(change.ContractType.TypeHandle, out var service))
                                continue;

                            if (service is IServiceLifecycle lifecycle)
                                lifecycle.Destroy();

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

        private void ShutdownService(IService service)
        {
            if (!_entriesByService.TryGetValue(service, out var entry)) return;

            // 幂等守卫：已处于关闭中或已销毁的服务不再重复关闭
            var state = GameServices.GetState(service);
            if (state >= EServiceState.ShuttingDown) return;

            GameServices.SetState(service, EServiceState.ShuttingDown);
            GameServices.InvokeShutdown(service);

            try { service.OnShutdown(); }
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

            // _registrationOrder 必须保持注册序——使用 List.Remove（O(n) 移位保序），
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
            PrepareDisposal();

            // 逆注册序关闭：依赖方（后注册）先关闭，被依赖方后关闭。
            // 循环依赖在 RegisterWithDependencies 的 s_InFlight 栈检测中即被阻止，此处无需再做环检测。
            for (int i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                if (service != null && _entriesByService.ContainsKey(service))
                    ShutdownService(service);
            }

            CompleteDisposal();
        }

        /// <summary>
        /// 销毁前置状态复位（同步/异步销毁共用）：退出迭代态、清空延迟队列、标记整体销毁中。
        /// </summary>
        private void PrepareDisposal()
        {
            _isIterating = false;
            _disposePending = false;
            _pendingChanges.Clear();

            // 标记正在整体销毁：ShutdownService 跳过逐项列表移除，
            // 由循环结束后统一 Clear() 清空全部列表——避免逆序遍历时 List.Remove 修改被遍历列表
            _isDisposing = true;
        }

        /// <summary>
        /// 销毁收尾（同步/异步销毁共用）：退出销毁标记、统一清空全部列表与注册表、置位 IsDisposed。
        /// </summary>
        private void CompleteDisposal()
        {
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
        /// 再调用同步 <c>Shutdown</c>。逆注册序执行。
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
                    PollAvgMs = entry.PollSamples > 0 ? entry.PollTotalMs / entry.PollSamples : 0f,
                    PollPeakMs = entry.PollPeakMs,
                    PollSamples = entry.PollSamples,
                });
            }
        }

        /// <summary>
        /// 清零本作用域全部服务的轮询耗时统计（不影响失败计数与熔断状态）。
        /// </summary>
        internal void ResetPollStatistics()
        {
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
        /// <para>实例方法：CreationIndex 从 <c>_entriesByService</c> 查找，需访问实例状态。</para>
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
        /// </summary>
        internal struct ServiceEntry
        {
            public RuntimeTypeHandle[] ContractHandles;
            public int CreationIndex;
            public int TickIndex;
            public int FixedTickIndex;
            public int LateTickIndex;
            public int GizmoIndex;

            // ── 轮询耗时统计（编辑器/开发构建写入；Release 下恒为 0，由 #if 门控）──

            public float PollTotalMs;
            public float PollPeakMs;
            public int PollSamples;

            // ── 各轮询类别的连续失败计数（异常熔断依据；对应类别成功一次即清零）──

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
