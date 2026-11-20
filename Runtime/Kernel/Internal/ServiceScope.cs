using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务作用域容器，管理单个作用域内服务的注册表、轮询列表和迭代安全机制。
    /// </summary>
    /// <remarks>
    /// 所有权：注册/注销由 <see cref="ServiceWorld"/> 驱动，外部代码不直接操作本类。 <br />
    /// 两阶段构建：<see cref="RegisterDeferred"/> 仅入注册表（不驱动生命周期、不加入轮询列表）； <br />
    /// 世界 <see cref="ServiceWorld.Initialize"/> 拓扑排序后逐服务 <see cref="ActivateService"/> 补齐轮询列表并驱动 OnInit，同时记录激活完成序。 <br />
    /// 关闭按逆激活序（= 逆初始化序，依赖方先关闭）执行；未初始化服务归入兜底桶按逆注册序关闭。 <br />
    /// 线程契约：所有方法仅限 Unity 主线程调用。
    /// </remarks>
    internal sealed partial class ServiceScope : IDisposable
    {
        #region 常量与字段 [CONSTANTS & FIELDS]

        // --- 服务存储 ---

        private readonly ServiceWorld _world;
        private readonly Dictionary<RuntimeTypeHandle, IService> _servicesByContract =
            new Dictionary<RuntimeTypeHandle, IService>(ContractHandleComparer.Instance);
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

        // ── Tick 异常分级：开发期 Fatal 后上抛（第一时间暴露缺陷），发布期隔离续跑（单服务故障不拖垮整帧）──
        // const 门控：JIT 裁剪死分支，Release 零运行时成本。
        internal const bool RETHROW_TICK_EXCEPTIONS =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                true;
#else
                false;
#endif

        // ── Tick 异常熔断：同一服务在同一轮询类别连续失败达到阈值即摘出对应轮询列表并汇总告警一次。
        // 阈值是世界级配置，见 ServiceWorld.TickFailureTripThreshold。──

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

                            DestroyService(service);

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





        #region 内部数据结构 [INTERNAL STRUCTURES]

        /// <summary>
        /// 服务的注册元数据。struct 以消除堆分配；字典回写模式（<c>_entriesByService[svc] = e</c>）更新字段。
        /// </summary>
        /// <remarks>
        /// 轮询耗时统计字段仅编辑器/开发构建存在（编译期裁剪，Release 零内存成本）。
        /// </remarks>
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
