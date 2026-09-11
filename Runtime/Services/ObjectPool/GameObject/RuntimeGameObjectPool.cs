using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 运行时 GameObject 池，使用分页 Slot 存储 + 侵入式链表实现零 GC 热路径，维护经共享调度器驱动。
    /// </summary>
    internal sealed class RuntimeGameObjectPool : MemoryObject, IPoolMaintenanceItem
    {
        #region 常量 [CONSTANTS]

        private enum SlotState : byte
        {
            Free = 0,
            Inactive = 1,
            Active = 2
        }

        private const int WARMUP_CREATE_BATCH = 8;
        private const float WARMUP_FRAME_BUDGET_SECONDS = 0.001f;

        /// <summary>
        /// 兜底僵尸清扫间隔秒数——池无自发到期维护且有实例时按此周期扫描外部销毁的槽位。
        /// </summary>
        private const float ZOMBIE_SWEEP_SECONDS = 30f;

        #endregion

        #region 结构体 [STRUCTS]

        private struct Slot
        {
            public GameObject Instance;
            public Transform Transform;
            public object UserData;
            public IGameObjectPoolable[] Poolables;
            public int PoolableCount;
            public float SpawnTime;
            public float LastReleaseTime;
            public int PrevInactive;
            public int NextInactive;
            public uint Generation;
            public SlotState State;
        }

        #endregion

        #region 字段 [FIELDS]

        private PoolMaintenanceScheduler _scheduler;
        private readonly GameObjectPrefabSource _prefabSource = new GameObjectPrefabSource();
        private PooledInstanceRegistry _registry;
        private PoolCompiledRule _rule;
        private string _location;
        private Transform _root;
        private float _nextMaintenanceAt;

        private PoolSlotStorage<Slot> _storage;

        private int _inactiveHead;
        private int _inactiveTail;
        private int _activeCount;
        private int _inactiveCount;
        private int _totalCount;
        private int _retainTarget;
        private int _spawnCount;
        private int _despawnCount;
        private int _hitCount;
        private int _missCount;
        private int _expandCount;
        private int _destroyCount;
        private int _peakActive;
        private uint _generationCounter;
        private readonly List<IGameObjectPoolable> _poolableBuffer = new List<IGameObjectPoolable>(8);

        private static readonly Comparison<GameObjectPoolInstanceSnapshot> s_InstanceComparer = CompareInstanceSnapshot;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取资源地址。
        /// </summary>
        public string Location => _location;

        /// <summary>
        /// 获取分组名称。
        /// </summary>
        public string Group => _rule.Group;

        /// <summary>
        /// 获取对象总数。
        /// </summary>
        public int TotalCount => _totalCount;

        /// <summary>
        /// 获取活跃对象数量。
        /// </summary>
        public int ActiveCount => _activeCount;

        /// <summary>
        /// 获取非活跃对象数量。
        /// </summary>
        public int InactiveCount => _inactiveCount;

        /// <summary>
        /// 获取预制体是否已就绪（Location 源需已加载；External 源取决于 prefab 是否有效）。
        /// </summary>
        public bool IsPrefabLoaded => _prefabSource.IsReady;

        /// <summary>
        /// 获取下次维护时间。
        /// </summary>
        public float NextMaintenanceAt => _nextMaintenanceAt;

        #endregion

        #region 维护调度 [MAINTENANCE SCHEDULING]

        /// <summary>
        /// 维护堆索引——由 <see cref="PoolMaintenanceScheduler"/> 独占维护。
        /// </summary>
        public int MaintenanceHeapIndex { get; set; } = -1;

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <summary>
        /// 初始化池（资源地址加载预制体）。
        /// </summary>
        /// <param name="scheduler">所属服务的维护调度器。</param>
        /// <param name="rule">编译后的池规则。</param>
        /// <param name="location">资源地址。</param>
        /// <param name="loader">预制体加载器。</param>
        /// <param name="inactiveRoot">非活跃对象挂载根。</param>
        /// <param name="registry">实例注册表。</param>
        public void Initialize(
            PoolMaintenanceScheduler scheduler,
            in PoolCompiledRule rule,
            string location,
            IPrefabLoader loader,
            Transform inactiveRoot,
            PooledInstanceRegistry registry)
        {
            InitializeCore(scheduler, rule, location, inactiveRoot, registry);
            _prefabSource.InitializeLocation(loader);
        }

        /// <summary>
        /// 初始化池（外部预制体引用，池不负责卸载预制体）。
        /// </summary>
        /// <param name="scheduler">所属服务的维护调度器。</param>
        /// <param name="rule">编译后的池规则。</param>
        /// <param name="location">池键（合成地址）。</param>
        /// <param name="prefab">外部预制体引用。</param>
        /// <param name="inactiveRoot">非活跃对象挂载根。</param>
        /// <param name="registry">实例注册表。</param>
        public void InitializeWithPrefab(
            PoolMaintenanceScheduler scheduler,
            in PoolCompiledRule rule,
            string location,
            GameObject prefab,
            Transform inactiveRoot,
            PooledInstanceRegistry registry)
        {
            InitializeCore(scheduler, rule, location, inactiveRoot, registry);
            _prefabSource.InitializeExternal(prefab);
        }

        private void InitializeCore(
            PoolMaintenanceScheduler scheduler,
            in PoolCompiledRule rule,
            string location,
            Transform inactiveRoot,
            PooledInstanceRegistry registry)
        {
            _scheduler = scheduler;
            _rule = rule;
            _location = location;
            _registry = registry;
            _root = inactiveRoot;
            _retainTarget = rule.MinIdle;
            _nextMaintenanceAt = float.MaxValue;
            _inactiveHead = -1;
            _inactiveTail = -1;
            _storage.Initialize();
            ScheduleMaintenance(float.MaxValue);
        }

        #endregion

        #region 公共方法 — Spawn [PUBLIC SPAWN]

        /// <summary>
        /// 同步获取对象。
        /// <para>Location 源：需预制体已加载（请先 LoadPrefab / Warmup / SpawnAsync），否则返回 null。</para>
        /// <para>External 源：需 prefab 引用仍有效。</para>
        /// </summary>
        public GameObject Spawn(Transform parent)
        {
            if (!_prefabSource.IsExternal && !_prefabSource.IsReady)
            {
                return null;
            }

            if (!_prefabSource.EnsureLoaded(_location))
            {
                return null;
            }

            return SpawnPrepared(parent);
        }

        /// <summary>
        /// 异步获取对象。
        /// </summary>
        public async UniTask<GameObject> SpawnAsync(Transform parent, CancellationToken cancellationToken)
        {
            if (!await _prefabSource.EnsureLoadedAsync(_location, cancellationToken))
            {
                return null;
            }

            return SpawnPrepared(parent);
        }

        /// <summary>
        /// 异步预热指定数量的对象。
        /// </summary>
        public async UniTask WarmupAsync(int count, CancellationToken cancellationToken)
        {
            int target = Mathf.Min(Mathf.Max(0, count), _rule.HardCapacity);
            if (target <= 0 || _inactiveCount >= target)
            {
                return;
            }

            if (!await _prefabSource.EnsureLoadedAsync(_location, cancellationToken))
            {
                return;
            }

            int createdThisFrame = 0;
            float frameStart = Time.realtimeSinceStartup;
            while (_inactiveCount < target && _totalCount < _rule.HardCapacity)
            {
                int slotIndex = CreateTrackedInstance();
                if (slotIndex < 0)
                {
                    break;
                }

                ParkInactive(slotIndex);
                createdThisFrame++;
                if (createdThisFrame >= WARMUP_CREATE_BATCH || Time.realtimeSinceStartup - frameStart >= WARMUP_FRAME_BUDGET_SECONDS)
                {
                    createdThisFrame = 0;
                    frameStart = Time.realtimeSinceStartup;
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }

            RefreshMaintenance();
        }

        #endregion

        #region 公共方法 — 预制体加载 [PUBLIC PREFAB LOADING]

        /// <summary>
        /// 同步加载预制体。
        /// </summary>
        public GameObject LoadPrefab()
        {
            return _prefabSource.EnsureLoaded(_location) ? _prefabSource.Prefab : null;
        }

        /// <summary>
        /// 异步加载预制体。
        /// </summary>
        public async UniTask<GameObject> LoadPrefabAsync(CancellationToken cancellationToken)
        {
            return await _prefabSource.EnsureLoadedAsync(_location, cancellationToken) ? _prefabSource.Prefab : null;
        }

        #endregion

        #region 公共方法 — 回收与租约 [PUBLIC DESPAWN & LEASE]

        /// <summary>
        /// 按槽位与租期代系回收活跃实例。
        /// </summary>
        public bool TryRelease(int slotIndex, uint generation)
        {
            if (!_storage.IsValidIndex(slotIndex))
            {
                return false;
            }

            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            if (slot.Generation != generation || slot.State != SlotState.Active)
            {
                return false;
            }

            ReleaseTrackedInstance(slotIndex);
            return true;
        }

        /// <summary>
        /// 按槽位与实例引用回收（代系由 Slot 独占校验）。
        /// </summary>
        public PoolReleaseResult ReleaseByInstance(int slotIndex, GameObject instance)
        {
            if (!_storage.IsValidIndex(slotIndex))
            {
                return PoolReleaseResult.NotOwned;
            }

            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            if (!ReferenceEquals(slot.Instance, instance))
            {
                return PoolReleaseResult.NotOwned;
            }

            if (slot.State != SlotState.Active)
            {
                return PoolReleaseResult.NotActive;
            }

            ReleaseTrackedInstance(slotIndex);
            return PoolReleaseResult.Released;
        }

        /// <summary>
        /// 尝试绑定租约：仅 Active 且实例有效时返回租期代系与引用。
        /// </summary>
        public bool TryBindLease(int slotIndex, out uint generation, out GameObject instance, out Transform transform)
        {
            generation = 0;
            instance = null;
            transform = null;
            if (!_storage.IsValidIndex(slotIndex))
            {
                return false;
            }

            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            if (slot.State != SlotState.Active || slot.Instance == null)
            {
                return false;
            }

            generation = slot.Generation;
            instance = slot.Instance;
            transform = slot.Transform;
            return true;
        }

        /// <summary>
        /// 槽位是否仍指向有效 Active 实例。
        /// </summary>
        public bool IsAlive(int slotIndex, uint generation)
        {
            if (!_storage.IsValidIndex(slotIndex))
            {
                return false;
            }

            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            return slot.Generation == generation && slot.State == SlotState.Active && slot.Instance != null;
        }

        /// <summary>
        /// 获取实例与 Transform（代系校验）。
        /// </summary>
        public bool TryGetInstance(int slotIndex, uint generation, out GameObject instance, out Transform transform)
        {
            instance = null;
            transform = null;
            if (!IsAlive(slotIndex, generation))
            {
                return false;
            }

            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            instance = slot.Instance;
            transform = slot.Transform;
            return true;
        }

        /// <summary>
        /// 获取用户数据。
        /// </summary>
        public T GetUserData<T>(int slotIndex) where T : class =>
            _storage.IsValidIndex(slotIndex) ? _storage.GetSlotRef(slotIndex).UserData as T : null;

        /// <summary>
        /// 获取或创建用户数据。
        /// </summary>
        public T GetOrAddUserData<T>(int slotIndex) where T : class, new()
        {
            if (!_storage.IsValidIndex(slotIndex))
            {
                return null;
            }

            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            if (slot.UserData is T typed)
            {
                return typed;
            }

            if (slot.UserData != null)
            {
                return null;
            }

            typed = new T();
            slot.UserData = typed;
            return typed;
        }

        /// <summary>
        /// 覆盖用户数据。
        /// </summary>
        public void SetUserData(int slotIndex, object value)
        {
            if (_storage.IsValidIndex(slotIndex))
            {
                _storage.GetSlotRef(slotIndex).UserData = value;
            }
        }

        #endregion

        #region 公共方法 — 维护 [PUBLIC MAINTENANCE]

        /// <summary>
        /// 执行维护操作（由调度器到期唤醒或服务低内存遍历调用）。
        /// </summary>
        /// <param name="now">当前缩放时钟。</param>
        /// <param name="lowMemory">是否为低内存强制维护。</param>
        public void ExecuteMaintenance(float now, bool lowMemory)
        {
            SweepDestroyedInstances();
            PoolRecyclePlan plan = PoolPolicyPlanner.Plan(in _rule, _totalCount, lowMemory);
            _retainTarget = Mathf.Clamp(plan.RetainTarget, _rule.MinIdle, _rule.HardCapacity);

            int budget = Mathf.Max(1, plan.TrimBudget);
            while (_inactiveHead >= 0 && budget > 0 && ShouldTrimHead(now, in plan))
            {
                DestroyTrackedInstance(_inactiveHead);
                budget--;
            }

            if (_prefabSource.IsReady && _totalCount == 0 && plan.UnloadPrefab && !_prefabSource.IsExternal)
            {
                _prefabSource.UnloadIfOwned();
            }

            RefreshMaintenance();
        }

        /// <summary>
        /// 刷新池（强制回收所有非活跃对象）。
        /// </summary>
        public void Flush()
        {
            ExecuteMaintenance(Time.time, true);
        }

        #endregion

        #region 公共方法 — 关闭 [PUBLIC SHUTDOWN]

        /// <summary>
        /// 关闭池，销毁所有对象。
        /// </summary>
        public void Shutdown()
        {
            _prefabSource.Shutdown();
            _scheduler.Remove(this);

            int slotCount = _storage.SlotCount;
            for (int i = 0; i < slotCount; i++)
            {
                ref Slot slot = ref _storage.GetSlotRef(i);
                if (slot.State == SlotState.Free && slot.Instance == null)
                {
                    continue;
                }

                InvokeOnPooledDestroy(ref slot);
                GameObject instance = slot.Instance;
                _registry?.Unregister(instance);
                if (instance != null)
                {
                    PoolDestroyUtility.Destroy(instance);
                }

                ClearSlot(ref slot);
                _destroyCount++;
            }

            _inactiveHead = -1;
            _inactiveTail = -1;
            _activeCount = 0;
            _inactiveCount = 0;
            _totalCount = 0;

            // 页数组立即归还，不依赖后续 MemoryPool.Clear 配对。
            ReturnStorage();
        }

        #endregion

        #region 公共方法 — 快照 [PUBLIC SNAPSHOT]

        /// <summary>
        /// 创建池快照。
        /// </summary>
        public GameObjectPoolSnapshot CreateSnapshot(bool includeInstances)
        {
            float now = Time.time;
            GameObjectPoolSnapshot snapshot = MemoryPool.Acquire<GameObjectPoolSnapshot>();
            snapshot.entryName = _rule.EntryName;
            snapshot.group = _rule.Group;
            snapshot.location = _location;
            snapshot.policy = _rule.Policy;
            snapshot.minIdle = _rule.MinIdle;
            snapshot.retainTarget = _retainTarget;
            snapshot.softCapacity = _rule.SoftCapacity;
            snapshot.hardCapacity = _rule.HardCapacity;
            snapshot.unloadPrefab = _rule.UnloadPrefab;
            snapshot.totalCount = _totalCount;
            snapshot.activeCount = _activeCount;
            snapshot.inactiveCount = _inactiveCount;
            snapshot.prefabLoaded = _prefabSource.IsReady;
            snapshot.nextMaintenanceIn = _nextMaintenanceAt >= float.MaxValue ? -1f : Mathf.Max(0f, _nextMaintenanceAt - now);
            snapshot.spawnCount = _spawnCount;
            snapshot.despawnCount = _despawnCount;
            snapshot.hitCount = _hitCount;
            snapshot.missCount = _missCount;
            snapshot.expandCount = _expandCount;
            snapshot.destroyCount = _destroyCount;
            snapshot.peakActive = _peakActive;
            if (includeInstances)
            {
                FillInstances(snapshot, now);
            }

            return snapshot;
        }

        /// <summary>
        /// 填充实例快照。
        /// </summary>
        public void FillInstances(GameObjectPoolSnapshot snapshot)
        {
            FillInstances(snapshot, Time.time);
        }

        #endregion

        #region MemoryObject 重写 [MEMORY OBJECT OVERRIDE]

        /// <summary>
        /// 清理池状态。
        /// </summary>
        public override void Clear()
        {
            ReturnStorage();
            _prefabSource.Clear();
            _scheduler = null;
            _registry = null;
            _rule = default;
            _location = null;
            _root = null;
            _nextMaintenanceAt = float.MaxValue;
            _inactiveHead = -1;
            _inactiveTail = -1;
            _activeCount = 0;
            _inactiveCount = 0;
            _totalCount = 0;
            _retainTarget = 0;
            _spawnCount = 0;
            _despawnCount = 0;
            _hitCount = 0;
            _missCount = 0;
            _expandCount = 0;
            _destroyCount = 0;
            _peakActive = 0;
            // 代系计数在池对象 CLR 生命期内单调递增，避免回收再用时与陈旧租约碰撞。
        }

        #endregion

        #region 私有方法 — Spawn 核心 [PRIVATE SPAWN CORE]

        private GameObject SpawnPrepared(Transform parent)
        {
            _spawnCount++;
            int slotIndex = -1;
            while (_inactiveTail >= 0)
            {
                slotIndex = _inactiveTail;
                RemoveFromInactive(slotIndex);
                // Sticky 池可能长期不排维护：外部 Destroy 的槽位在此惰性清扫。
                if (_storage.GetSlotRef(slotIndex).Instance == null)
                {
                    RemoveDestroyedSlot(slotIndex);
                    slotIndex = -1;
                    continue;
                }

                _hitCount++;
                break;
            }

            if (slotIndex < 0)
            {
                _missCount++;
                slotIndex = CreateTrackedInstance();
                if (slotIndex < 0)
                {
                    RefreshMaintenance();
                    return null;
                }
            }

            ActivateTrackedInstance(slotIndex, parent);
            if (_activeCount > _peakActive)
            {
                _peakActive = _activeCount;
            }

            RefreshMaintenance();
            return _storage.GetSlotRef(slotIndex).Instance;
        }

        private void ActivateTrackedInstance(int slotIndex, Transform parent)
        {
            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            slot.State = SlotState.Active;
            // 租期级代系：每次激活递增，使旧租约在槽位复用后必然失效。
            slot.Generation = ++_generationCounter;
            _activeCount++;
            slot.Transform.SetParent(parent, false);
            if (_prefabSource.Prefab != null)
            {
                // 与 Object.Instantiate(prefab, parent) 对齐：复用实例重置到预制体局部姿态。
                Transform prefabTransform = _prefabSource.Prefab.transform;
                slot.Transform.localPosition = prefabTransform.localPosition;
                slot.Transform.localRotation = prefabTransform.localRotation;
                slot.Transform.localScale = prefabTransform.localScale;
            }

            if (!slot.Instance.activeSelf)
            {
                slot.Instance.SetActive(true);
            }

            GameObjectPoolSpawnContext context = new GameObjectPoolSpawnContext(_location, _rule.Group, parent, (uint)Time.frameCount);
            InvokeOnSpawn(ref slot, in context);
        }

        private void ReleaseTrackedInstance(int slotIndex)
        {
            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            if (slot.State != SlotState.Active)
            {
                return;
            }

            // 帧末延迟销毁窗口：外部 Destroy 后实例已假空，禁止再访问 .activeSelf。
            if (slot.Instance == null)
            {
                RemoveDestroyedSlot(slotIndex);
                return;
            }

            _despawnCount++;
            _activeCount = Mathf.Max(0, _activeCount - 1);
            InvokeOnDespawn(ref slot);
            if (slot.Instance.activeSelf)
            {
                slot.Instance.SetActive(false);
            }

            ParkInactive(slotIndex);
            RefreshMaintenance();
        }

        private void ParkInactive(int slotIndex)
        {
            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            slot.State = SlotState.Inactive;
            slot.LastReleaseTime = Time.time;
            slot.Transform.SetParent(_root, false);
            AddToInactiveTail(slotIndex);
        }

        #endregion

        #region 私有方法 — 实例管理 [PRIVATE INSTANCE MANAGEMENT]

        private int CreateTrackedInstance()
        {
            if (_totalCount >= _rule.HardCapacity)
            {
                // 硬顶自愈：优先清扫外部销毁的僵尸槽位（Active / Inactive 均可），腾出容量后再判定。
                SweepDestroyedInstances();
                if (_totalCount >= _rule.HardCapacity)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    LogUtility.Warning("[GameObjectPool] HardCapacity reached. Rule:{0}, Location:{1}, Hard:{2}",
                        _rule.EntryName, _location, _rule.HardCapacity);
#endif
                    return -1;
                }
            }

            int slotIndex = _storage.AllocSlot();
            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            slot = default;
            slot.Generation = ++_generationCounter;
            slot.State = SlotState.Inactive;
            slot.SpawnTime = Time.time;
            slot.LastReleaseTime = Time.time;
            slot.PrevInactive = -1;
            slot.NextInactive = -1;
            slot.Instance = UnityEngine.Object.Instantiate(_prefabSource.Prefab, _root, false);
            slot.Transform = slot.Instance.transform;
#if UNITY_EDITOR
            slot.Instance.name = StringUtility.Format("{0}[Pool]", _prefabSource.Prefab.name);
#endif
            if (slot.Instance.activeSelf)
            {
                slot.Instance.SetActive(false);
            }

            _registry?.Register(slot.Instance, this, slotIndex);
            CachePoolables(ref slot);
            _totalCount++;
            _expandCount++;
            return slotIndex;
        }

        private void DestroyTrackedInstance(int slotIndex)
        {
            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            RemoveFromInactive(slotIndex);
            if (slot.State == SlotState.Active)
            {
                _activeCount = Mathf.Max(0, _activeCount - 1);
            }

            InvokeOnPooledDestroy(ref slot);
            GameObject instance = slot.Instance;
            _registry?.Unregister(instance);
            if (instance != null)
            {
                PoolDestroyUtility.Destroy(instance);
            }

            ClearSlot(ref slot);
            _storage.FreeSlot(slotIndex);
            _totalCount = Mathf.Max(0, _totalCount - 1);
            _destroyCount++;
        }

        private void RemoveDestroyedSlot(int slotIndex)
        {
            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            RemoveFromInactive(slotIndex);
            if (slot.State == SlotState.Active)
            {
                _activeCount = Mathf.Max(0, _activeCount - 1);
            }

            InvokeOnPooledDestroy(ref slot);
            _registry?.Unregister(slot.Instance);
            ClearSlot(ref slot);
            _storage.FreeSlot(slotIndex);
            _totalCount = Mathf.Max(0, _totalCount - 1);
            _destroyCount++;
            RefreshMaintenance();
        }

        private void SweepDestroyedInstances()
        {
            int slotCount = _storage.SlotCount;
            for (int i = 0; i < slotCount; i++)
            {
                ref Slot slot = ref _storage.GetSlotRef(i);
                if (slot.State != SlotState.Free && slot.Instance == null)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    LogUtility.Warning("[GameObjectPool] Pooled object destroyed outside pool. Rule:{0}, Location:{1}",
                        _rule.EntryName, _location);
#endif
                    RemoveDestroyedSlot(i);
                }
            }
        }

        #endregion

        #region 私有方法 — 维护调度 [PRIVATE MAINTENANCE]

        private bool ShouldTrimHead(float now, in PoolRecyclePlan plan)
        {
            if (_inactiveHead < 0 || _totalCount <= plan.RetainTarget)
            {
                return false;
            }

            if (plan.ForceTrim || _rule.Policy == EPoolPolicy.Fixed || _totalCount > _rule.SoftCapacity)
            {
                return true;
            }

            if (_rule.Policy == EPoolPolicy.Sticky)
            {
                return false;
            }

            return now - _storage.GetSlotRef(_inactiveHead).LastReleaseTime >= _rule.IdleSeconds;
        }

        private void RefreshMaintenance()
        {
            float now = Time.time;
            float due = float.MaxValue;
            if (_rule.Policy != EPoolPolicy.Sticky)
            {
                int retain = Mathf.Max(_rule.MinIdle, _retainTarget);
                if (_inactiveHead >= 0 && _totalCount > retain)
                {
                    due = _rule.Policy == EPoolPolicy.Fixed
                        ? now
                        : _storage.GetSlotRef(_inactiveHead).LastReleaseTime + _rule.IdleSeconds;
                }
                else if (!_prefabSource.IsExternal && _prefabSource.IsReady && _totalCount == 0 && _rule.UnloadPrefab)
                {
                    due = _rule.Policy == EPoolPolicy.Burst ? now + _rule.IdleSeconds : now;
                }
            }

            // 兜底清扫排期：仍有实例但无自发到期维护（Sticky 池 / 全活跃池）时按周期扫描
            // 外部销毁的僵尸槽位——保证回收有上界并触发告警，不再依赖 Flush / 低内存。
            if (due >= float.MaxValue && _totalCount > 0)
            {
                due = now + ZOMBIE_SWEEP_SECONDS;
            }

            ScheduleMaintenance(due);
        }

        private void ScheduleMaintenance(float dueTime)
        {
            _nextMaintenanceAt = dueTime;
            _scheduler.Schedule(this, dueTime);
        }

        #endregion

        #region 私有方法 — Poolable 回调 [PRIVATE POOLABLE CALLBACKS]

        /// <summary>
        /// 缓存实例上的 IGameObjectPoolable 组件列表。
        /// <para>使用预分配的 buffer 避免 GC 分配。</para>
        /// </summary>
        private void CachePoolables(ref Slot slot)
        {
            _poolableBuffer.Clear();
            slot.Instance.GetComponentsInChildren(true, _poolableBuffer);
            slot.PoolableCount = _poolableBuffer.Count;
            if (slot.PoolableCount == 0)
            {
                slot.Poolables = null;
                return;
            }

            slot.Poolables = SlotArrayPool<IGameObjectPoolable>.Rent(slot.PoolableCount);
            for (int i = 0; i < slot.PoolableCount; i++)
            {
                slot.Poolables[i] = _poolableBuffer[i];
            }
        }

        private static void InvokeOnSpawn(ref Slot slot, in GameObjectPoolSpawnContext context)
        {
            for (int i = 0; i < slot.PoolableCount; i++)
            {
                slot.Poolables[i].OnSpawn(in context);
            }
        }

        private static void InvokeOnDespawn(ref Slot slot)
        {
            for (int i = 0; i < slot.PoolableCount; i++)
            {
                slot.Poolables[i].OnDespawn();
            }
        }

        private static void InvokeOnPooledDestroy(ref Slot slot)
        {
            for (int i = 0; i < slot.PoolableCount; i++)
            {
                slot.Poolables[i].OnPooledDestroy();
            }
        }

        #endregion

        #region 私有方法 — 侵入式链表 [PRIVATE LINKED LIST]

        private void AddToInactiveTail(int slotIndex)
        {
            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            slot.PrevInactive = _inactiveTail;
            slot.NextInactive = -1;
            if (_inactiveTail >= 0)
            {
                _storage.GetSlotRef(_inactiveTail).NextInactive = slotIndex;
            }
            else
            {
                _inactiveHead = slotIndex;
            }

            _inactiveTail = slotIndex;
            _inactiveCount++;
        }

        private void RemoveFromInactive(int slotIndex)
        {
            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            if (slot.State != SlotState.Inactive)
            {
                return;
            }

            int prev = slot.PrevInactive;
            int next = slot.NextInactive;
            if (prev >= 0)
            {
                _storage.GetSlotRef(prev).NextInactive = next;
            }
            else
            {
                _inactiveHead = next;
            }

            if (next >= 0)
            {
                _storage.GetSlotRef(next).PrevInactive = prev;
            }
            else
            {
                _inactiveTail = prev;
            }

            slot.PrevInactive = -1;
            slot.NextInactive = -1;
            _inactiveCount = Mathf.Max(0, _inactiveCount - 1);
        }

        #endregion

        #region 私有方法 — 快照填充 [PRIVATE SNAPSHOT FILL]

        private void FillInstances(GameObjectPoolSnapshot snapshot, float now)
        {
            snapshot.ClearInstances();
            int slotCount = _storage.SlotCount;
            for (int i = 0; i < slotCount; i++)
            {
                ref Slot slot = ref _storage.GetSlotRef(i);
                if (slot.State == SlotState.Free && slot.Instance == null)
                {
                    continue;
                }

                GameObjectPoolInstanceSnapshot instanceSnapshot = MemoryPool.Acquire<GameObjectPoolInstanceSnapshot>();
                instanceSnapshot.instanceName = slot.Instance == null ? "<destroyed>" : slot.Instance.name;
                instanceSnapshot.isActive = slot.State == SlotState.Active;
                instanceSnapshot.idleDuration = slot.State == SlotState.Active ? 0f : Mathf.Max(0f, now - slot.LastReleaseTime);
                instanceSnapshot.lifeDuration = Mathf.Max(0f, now - slot.SpawnTime);
                instanceSnapshot.gameObject = slot.Instance;
                snapshot.instances.Add(instanceSnapshot);
            }

            snapshot.instances.Sort(s_InstanceComparer);
        }

        private static int CompareInstanceSnapshot(GameObjectPoolInstanceSnapshot left, GameObjectPoolInstanceSnapshot right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int state = right.isActive.CompareTo(left.isActive);
            return state != 0 ? state : string.CompareOrdinal(left.instanceName, right.instanceName);
        }

        #endregion

        #region 私有方法 — Slot 管理 [PRIVATE SLOT MANAGEMENT]

        private void ClearSlot(ref Slot slot)
        {
            if (slot.Poolables != null)
            {
                SlotArrayPool<IGameObjectPoolable>.Return(slot.Poolables, true);
            }

            slot = default;
            slot.PrevInactive = -1;
            slot.NextInactive = -1;
            slot.State = SlotState.Free;
        }

        private void ReturnStorage()
        {
            int slotCount = _storage.SlotCount;
            for (int i = 0; i < slotCount; i++)
            {
                if (_storage.GetSlotRef(i).Poolables != null)
                {
                    SlotArrayPool<IGameObjectPoolable>.Return(_storage.GetSlotRef(i).Poolables, true);
                }
            }

            _storage.ReturnStorage();
        }

        #endregion
    }
}
