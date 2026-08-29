using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        #endregion

        #region 结构体 [STRUCTS]

        private struct Slot
        {
            public GameObject Instance;
            public Transform Transform;
            public GameObjectPoolHandle Handle;
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
        private IPrefabLoader _loader;
        private PoolCompiledRule _rule;
        private string _location;
        private Transform _root;
        private GameObject _prefab;
        private UniTaskCompletionSource<GameObject> _prefabLoadCompletionSource;
        private bool _prefabLoading;
        private bool _isShuttingDown;
        private int _loadVersion;
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
        /// 获取预制体是否已加载。
        /// </summary>
        public bool IsPrefabLoaded => _prefab != null;

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
        /// 初始化池。
        /// </summary>
        /// <param name="scheduler">所属服务的维护调度器。</param>
        /// <param name="rule">编译后的池规则。</param>
        /// <param name="location">资源地址。</param>
        /// <param name="loader">预制体加载器。</param>
        /// <param name="inactiveRoot">非活跃对象挂载根。</param>
        public void Initialize(
            PoolMaintenanceScheduler scheduler,
            in PoolCompiledRule rule,
            string location,
            IPrefabLoader loader,
            Transform inactiveRoot)
        {
            _scheduler = scheduler;
            _rule = rule;
            _location = location;
            _loader = loader;
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
        /// 同步获取对象。需预制体已加载。
        /// </summary>
        public GameObject Spawn(Transform parent)
        {
            if (_prefab == null)
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
            if (!await EnsurePrefabLoadedAsync(cancellationToken))
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

            if (!await EnsurePrefabLoadedAsync(cancellationToken))
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
            return EnsurePrefabLoaded() ? _prefab : null;
        }

        /// <summary>
        /// 异步加载预制体。
        /// </summary>
        public async UniTask<GameObject> LoadPrefabAsync(CancellationToken cancellationToken)
        {
            return await EnsurePrefabLoadedAsync(cancellationToken) ? _prefab : null;
        }

        #endregion

        #region 公共方法 — 回收 [PUBLIC DESPAWN]

        /// <summary>
        /// 通过句柄回收对象。
        /// </summary>
        public bool ReleaseFromHandle(GameObjectPoolHandle handle)
        {
            if (handle == null || !_storage.IsValidIndex(handle.SlotIndex))
            {
                return false;
            }

            ref Slot slot = ref _storage.GetSlotRef(handle.SlotIndex);
            if (slot.Handle != handle || slot.Generation != handle.Generation || slot.State != SlotState.Active)
            {
                return false;
            }

            ReleaseTrackedInstance(handle.SlotIndex);
            return true;
        }

        /// <summary>
        /// 通知句柄已销毁（GameObject 被外部 Destroy）。
        /// </summary>
        public void NotifyHandleDestroyed(int slotIndex, uint generation)
        {
            if (_isShuttingDown || !_storage.IsValidIndex(slotIndex))
            {
                return;
            }

            ref Slot slot = ref _storage.GetSlotRef(slotIndex);
            if (slot.Generation != generation)
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogUtility.Warning("[GameObjectPool] Pooled object destroyed outside pool. Rule:{0}, Location:{1}",
                _rule.EntryName, _location);
#endif
            RemoveDestroyedSlot(slotIndex);
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
            PoolRecyclePlan plan = PoolPolicyPlanner.Plan(in _rule, _totalCount, lowMemory);
            _retainTarget = Mathf.Clamp(plan.RetainTarget, _rule.MinIdle, _rule.HardCapacity);

            int budget = Mathf.Max(1, plan.TrimBudget);
            while (_inactiveHead >= 0 && budget > 0 && ShouldTrimHead(now, in plan))
            {
                DestroyTrackedInstance(_inactiveHead);
                budget--;
            }

            if (_prefab != null && _totalCount == 0 && plan.UnloadPrefab)
            {
                _loader.UnloadPrefab(_prefab);
                _prefab = null;
                _prefabLoading = false;
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
            _isShuttingDown = true;
            _loadVersion++;
            _prefabLoading = false;
            _prefabLoadCompletionSource?.TrySetCanceled();
            _prefabLoadCompletionSource = null;
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
                slot.Handle?.Detach();
                if (slot.Instance != null)
                {
                    PoolDestroyUtility.Destroy(slot.Instance);
                }

                ClearSlot(ref slot);
                _destroyCount++;
            }

            _inactiveHead = -1;
            _inactiveTail = -1;
            _activeCount = 0;
            _inactiveCount = 0;
            _totalCount = 0;
            if (_prefab != null)
            {
                _loader.UnloadPrefab(_prefab);
                _prefab = null;
            }
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
            snapshot.prefabLoaded = _prefab != null;
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
            _prefabLoadCompletionSource?.TrySetCanceled();
            _prefabLoadCompletionSource = null;
            _scheduler = null;
            _loader = null;
            _rule = default;
            _location = null;
            _root = null;
            _prefab = null;
            _prefabLoading = false;
            _isShuttingDown = false;
            _loadVersion++;
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
            _generationCounter = 0;
        }

        #endregion

        #region 私有方法 — Spawn 核心 [PRIVATE SPAWN CORE]

        private GameObject SpawnPrepared(Transform parent)
        {
            _spawnCount++;
            int slotIndex;
            if (_inactiveTail >= 0)
            {
                slotIndex = _inactiveTail;
                RemoveFromInactive(slotIndex);
                _hitCount++;
            }
            else
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
            _activeCount++;
            slot.Transform.SetParent(parent, false);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogUtility.Warning("[GameObjectPool] HardCapacity reached. Rule:{0}, Location:{1}, Hard:{2}",
                    _rule.EntryName, _location, _rule.HardCapacity);
#endif
                return -1;
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
            slot.Instance = UnityEngine.Object.Instantiate(_prefab);
            slot.Transform = slot.Instance.transform;
#if UNITY_EDITOR
            slot.Instance.name = StringUtility.Format("{0}[Pool]", _prefab.name);
#endif
            slot.Transform.SetParent(_root, false);
            if (slot.Instance.activeSelf)
            {
                slot.Instance.SetActive(false);
            }

            GameObjectPoolHandle handle = slot.Instance.GetComponent<GameObjectPoolHandle>();
            if (handle == null)
            {
                handle = slot.Instance.AddComponent<GameObjectPoolHandle>();
            }

            handle.Bind(this, slotIndex, slot.Generation);
            slot.Handle = handle;
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
            slot.Handle?.Detach();
            if (slot.Instance != null)
            {
                PoolDestroyUtility.Destroy(slot.Instance);
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
            slot.Handle?.Detach();
            ClearSlot(ref slot);
            _storage.FreeSlot(slotIndex);
            _totalCount = Mathf.Max(0, _totalCount - 1);
            _destroyCount++;
            RefreshMaintenance();
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
                else if (_prefab != null && _totalCount == 0 && _rule.UnloadPrefab)
                {
                    due = _rule.Policy == EPoolPolicy.Burst ? now + _rule.IdleSeconds : now;
                }
            }

            ScheduleMaintenance(due);
        }

        private void ScheduleMaintenance(float dueTime)
        {
            _nextMaintenanceAt = dueTime;
            _scheduler.Schedule(this, dueTime);
        }

        #endregion

        #region 私有方法 — 预制体加载 [PRIVATE PREFAB LOADING]

        private bool EnsurePrefabLoaded()
        {
            if (_prefab != null)
            {
                return true;
            }

            if (_prefabLoading)
            {
                return false;
            }

            _prefab = _loader.LoadPrefab(_location);
            return _prefab != null;
        }

        private async UniTask<bool> EnsurePrefabLoadedAsync(CancellationToken cancellationToken)
        {
            if (_prefab != null)
            {
                return true;
            }

            if (_prefabLoading)
            {
                await _prefabLoadCompletionSource.Task.AttachExternalCancellation(cancellationToken);
                return _prefab != null;
            }

            _prefabLoading = true;
            // 先捕获局部引用再启动加载——同步完成的加载器会立刻消费并置空字段，直接 await 字段将 NRE。
            UniTaskCompletionSource<GameObject> completionSource = new UniTaskCompletionSource<GameObject>();
            _prefabLoadCompletionSource = completionSource;
            RunPrefabLoadAsync(_loadVersion).Forget();
            await completionSource.Task.AttachExternalCancellation(cancellationToken);
            return _prefab != null;
        }

        private async UniTaskVoid RunPrefabLoadAsync(int loadVersion)
        {
            GameObject loaded = null;
            try
            {
                loaded = await _loader.LoadPrefabAsync(_location);
            }
            catch
            {
                loaded = null;
            }

            if (_isShuttingDown || loadVersion != _loadVersion)
            {
                if (loaded != null)
                {
                    _loader.UnloadPrefab(loaded);
                }

                _prefabLoading = false;
                _prefabLoadCompletionSource?.TrySetCanceled();
                _prefabLoadCompletionSource = null;
                return;
            }

            _prefab = loaded;
            _prefabLoading = false;
            UniTaskCompletionSource<GameObject> completionSource = _prefabLoadCompletionSource;
            _prefabLoadCompletionSource = null;
            completionSource?.TrySetResult(_prefab);
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
