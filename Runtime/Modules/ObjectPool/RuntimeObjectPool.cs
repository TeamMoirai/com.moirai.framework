using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 运行时 GameObject 池，使用 SoA 分页 Slot + 侵入式链表实现零 GC 热路径。
    /// </summary>
    internal sealed class RuntimeObjectPool : MemoryObject
    {
        #region 常量 [CONSTANTS]

        private enum SlotState : byte
        {
            Free = 0,
            Inactive = 1,
            Active = 2
        }

        private const int PAGE_BITS = 7;
        private const int PAGE_SIZE = 1 << PAGE_BITS;
        private const int PAGE_MASK = PAGE_SIZE - 1;
        private const int INITIAL_PAGE_CAPACITY = 4;
        private const int WARMUP_CREATE_BATCH = 8;
        private const float WARMUP_FRAME_BUDGET_SECONDS = 0.001f;

        #endregion

        #region 结构体 [STRUCTS]

        private struct Slot
        {
            public GameObject Instance;
            public Transform Transform;
            public ObjectPoolHandle Handle;
            public IObjectPoolable[] Poolables;
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

        private ObjectPoolServiceHandler _service;
        private IPrefabLoader _loader;
        private PoolCompiledRule _rule;
        private int _poolIndex;
        private string _location;
        private Transform _root;
        private GameObject _prefab;
        private UniTaskCompletionSource<GameObject> _prefabLoadCompletionSource;
        private bool _prefabLoading;
        private bool _isShuttingDown;
        private int _loadVersion;
        private float _nextMaintenanceAt;
        private int _maintenanceHeapIndex;

        private Slot[][] _pages;
        private int[][] _pageFreeStacks;
        private int[] _pageAliveCounts;
        private int[] _pageFreeTops;
        private int _pageCount;
        private int[] _freePageStack;
        private int _freePageTop;

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
        private readonly List<IObjectPoolable> _poolableBuffer = new List<IObjectPoolable>(8);

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

        #region 初始化 [INITIALIZATION]

        /// <summary>
        /// 初始化池。
        /// </summary>
        public void Initialize(
            ObjectPoolServiceHandler service,
            int poolIndex,
            in PoolCompiledRule rule,
            string location,
            IPrefabLoader loader,
            Transform inactiveRoot)
        {
            _service = service;
            _poolIndex = poolIndex;
            _rule = rule;
            _location = location;
            _loader = loader;
            _root = inactiveRoot;
            _retainTarget = rule.MinIdle;
            _nextMaintenanceAt = float.MaxValue;
            _maintenanceHeapIndex = -1;
            _inactiveHead = -1;
            _inactiveTail = -1;
            _pages = SlotArrayPool<Slot[]>.Rent(INITIAL_PAGE_CAPACITY);
            _pageFreeStacks = SlotArrayPool<int[]>.Rent(INITIAL_PAGE_CAPACITY);
            _pageAliveCounts = SlotArrayPool<int>.Rent(INITIAL_PAGE_CAPACITY);
            _pageFreeTops = SlotArrayPool<int>.Rent(INITIAL_PAGE_CAPACITY);
            _freePageStack = SlotArrayPool<int>.Rent(INITIAL_PAGE_CAPACITY);
            Array.Clear(_pages, 0, INITIAL_PAGE_CAPACITY);
            Array.Clear(_pageFreeStacks, 0, INITIAL_PAGE_CAPACITY);
            Array.Clear(_pageAliveCounts, 0, INITIAL_PAGE_CAPACITY);
            Array.Clear(_pageFreeTops, 0, INITIAL_PAGE_CAPACITY);
            Array.Clear(_freePageStack, 0, INITIAL_PAGE_CAPACITY);
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
        public bool ReleaseFromHandle(ObjectPoolHandle handle)
        {
            if (handle == null || !IsValidIndex(handle.SlotIndex))
            {
                return false;
            }

            ref Slot slot = ref GetSlotRef(handle.SlotIndex);
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
            if (_isShuttingDown || !IsValidIndex(slotIndex))
            {
                return;
            }

            ref Slot slot = ref GetSlotRef(slotIndex);
            if (slot.Generation != generation)
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogUtility.Warning("[ObjectPool] Pooled object destroyed outside pool. Rule:{0}, Location:{1}",
                _rule.EntryName, _location);
#endif
            RemoveDestroyedSlot(slotIndex);
        }

        #endregion

        #region 公共方法 — 维护 [PUBLIC MAINTENANCE]

        /// <summary>
        /// 设置维护堆索引。
        /// </summary>
        public void SetMaintenanceHeapIndex(int heapIndex)
        {
            _maintenanceHeapIndex = heapIndex;
        }

        /// <summary>
        /// 执行维护操作。
        /// </summary>
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
            _service.RemoveMaintenance(ref _maintenanceHeapIndex);

            for (int page = 0; page < _pageCount; page++)
            {
                Slot[] pageSlots = _pages[page];
                if (pageSlots == null)
                {
                    continue;
                }

                for (int offset = 0; offset < PAGE_SIZE; offset++)
                {
                    ref Slot slot = ref pageSlots[offset];
                    if (slot.State == SlotState.Free && slot.Instance == null)
                    {
                        continue;
                    }

                    InvokeOnPooledDestroy(ref slot);
                    slot.Handle?.Detach();
                    if (slot.Instance != null)
                    {
                        UnityEngine.Object.Destroy(slot.Instance);
                    }

                    ClearSlot(ref slot);
                    _destroyCount++;
                }
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
        public ObjectPoolSnapshot CreateSnapshot(bool includeInstances)
        {
            float now = Time.time;
            ObjectPoolSnapshot snapshot = MemoryPool.Acquire<ObjectPoolSnapshot>();
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
        public void FillInstances(ObjectPoolSnapshot snapshot)
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
            _service = null;
            _loader = null;
            _rule = default;
            _poolIndex = 0;
            _location = null;
            _root = null;
            _prefab = null;
            _prefabLoading = false;
            _isShuttingDown = false;
            _loadVersion++;
            _nextMaintenanceAt = float.MaxValue;
            _maintenanceHeapIndex = -1;
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
            return GetSlotRef(slotIndex).Instance;
        }

        private void ActivateTrackedInstance(int slotIndex, Transform parent)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            slot.State = SlotState.Active;
            _activeCount++;
            slot.Transform.SetParent(parent, false);
            if (!slot.Instance.activeSelf)
            {
                slot.Instance.SetActive(true);
            }

            PoolSpawnContext context = new PoolSpawnContext(_location, _rule.Group, parent, (uint)Time.frameCount);
            InvokeOnSpawn(ref slot, in context);
        }

        private void ReleaseTrackedInstance(int slotIndex)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
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
            ref Slot slot = ref GetSlotRef(slotIndex);
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
                LogUtility.Warning("[ObjectPool] HardCapacity reached. Rule:{0}, Location:{1}, Hard:{2}",
                    _rule.EntryName, _location, _rule.HardCapacity);
#endif
                return -1;
            }

            int slotIndex = AllocSlot();
            ref Slot slot = ref GetSlotRef(slotIndex);
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

            ObjectPoolHandle handle = slot.Instance.GetComponent<ObjectPoolHandle>();
            if (handle == null)
            {
                handle = slot.Instance.AddComponent<ObjectPoolHandle>();
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
            ref Slot slot = ref GetSlotRef(slotIndex);
            RemoveFromInactive(slotIndex);
            if (slot.State == SlotState.Active)
            {
                _activeCount = Mathf.Max(0, _activeCount - 1);
            }

            InvokeOnPooledDestroy(ref slot);
            slot.Handle?.Detach();
            if (slot.Instance != null)
            {
                UnityEngine.Object.Destroy(slot.Instance);
            }

            ClearSlot(ref slot);
            FreeSlot(slotIndex);
            _totalCount = Mathf.Max(0, _totalCount - 1);
            _destroyCount++;
        }

        private void RemoveDestroyedSlot(int slotIndex)
        {
            ref Slot slot = ref GetSlotRef(slotIndex);
            RemoveFromInactive(slotIndex);
            if (slot.State == SlotState.Active)
            {
                _activeCount = Mathf.Max(0, _activeCount - 1);
            }

            InvokeOnPooledDestroy(ref slot);
            slot.Handle?.Detach();
            ClearSlot(ref slot);
            FreeSlot(slotIndex);
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

            return now - GetSlotRef(_inactiveHead).LastReleaseTime >= _rule.IdleSeconds;
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
                        : GetSlotRef(_inactiveHead).LastReleaseTime + _rule.IdleSeconds;
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
            _service.ScheduleMaintenance(_poolIndex, dueTime, ref _maintenanceHeapIndex);
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
            _prefabLoadCompletionSource = new UniTaskCompletionSource<GameObject>();
            RunPrefabLoadAsync(_loadVersion).Forget();
            await _prefabLoadCompletionSource.Task.AttachExternalCancellation(cancellationToken);
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
        /// 缓存实例上的 IObjectPoolable 组件列表。
        /// 使用预分配的 buffer 避免 GC 分配。
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

            slot.Poolables = SlotArrayPool<IObjectPoolable>.Rent(slot.PoolableCount);
            for (int i = 0; i < slot.PoolableCount; i++)
            {
                slot.Poolables[i] = _poolableBuffer[i];
            }
        }

        private static void InvokeOnSpawn(ref Slot slot, in PoolSpawnContext context)
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
            ref Slot slot = ref GetSlotRef(slotIndex);
            slot.PrevInactive = _inactiveTail;
            slot.NextInactive = -1;
            if (_inactiveTail >= 0)
            {
                GetSlotRef(_inactiveTail).NextInactive = slotIndex;
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
            ref Slot slot = ref GetSlotRef(slotIndex);
            if (slot.State != SlotState.Inactive)
            {
                return;
            }

            int prev = slot.PrevInactive;
            int next = slot.NextInactive;
            if (prev >= 0)
            {
                GetSlotRef(prev).NextInactive = next;
            }
            else
            {
                _inactiveHead = next;
            }

            if (next >= 0)
            {
                GetSlotRef(next).PrevInactive = prev;
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

        private void FillInstances(ObjectPoolSnapshot snapshot, float now)
        {
            snapshot.ClearInstances();
            for (int page = 0; page < _pageCount; page++)
            {
                Slot[] pageSlots = _pages[page];
                if (pageSlots == null)
                {
                    continue;
                }

                for (int offset = 0; offset < PAGE_SIZE; offset++)
                {
                    ref Slot slot = ref pageSlots[offset];
                    if (slot.State == SlotState.Free && slot.Instance == null)
                    {
                        continue;
                    }

                    ObjectPoolInstanceSnapshot instanceSnapshot = MemoryPool.Acquire<ObjectPoolInstanceSnapshot>();
                    instanceSnapshot.instanceName = slot.Instance == null ? "<destroyed>" : slot.Instance.name;
                    instanceSnapshot.isActive = slot.State == SlotState.Active;
                    instanceSnapshot.idleDuration = slot.State == SlotState.Active ? 0f : Mathf.Max(0f, now - slot.LastReleaseTime);
                    instanceSnapshot.lifeDuration = Mathf.Max(0f, now - slot.SpawnTime);
                    instanceSnapshot.gameObject = slot.Instance;
                    snapshot.instances.Add(instanceSnapshot);
                }
            }
        }

        #endregion

        #region 私有方法 — Slot 管理 [PRIVATE SLOT MANAGEMENT]

        private void ClearSlot(ref Slot slot)
        {
            if (slot.Poolables != null)
            {
                SlotArrayPool<IObjectPoolable>.Return(slot.Poolables, true);
            }

            slot = default;
            slot.PrevInactive = -1;
            slot.NextInactive = -1;
            slot.State = SlotState.Free;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Slot GetSlotRef(int index)
        {
            return ref _pages[index >> PAGE_BITS][index & PAGE_MASK];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValidIndex(int index)
        {
            return index >= 0 && (index >> PAGE_BITS) < _pageCount && _pages[index >> PAGE_BITS] != null;
        }

        private int AllocSlot()
        {
            if (_freePageTop <= 0)
            {
                AllocatePage();
            }

            int page = _freePageStack[_freePageTop - 1];
            int offset = _pageFreeStacks[page][--_pageFreeTops[page]];
            if (_pageFreeTops[page] <= 0)
            {
                _freePageTop--;
            }

            _pageAliveCounts[page]++;
            return (page << PAGE_BITS) | offset;
        }

        private void FreeSlot(int index)
        {
            int page = index >> PAGE_BITS;
            int offset = index & PAGE_MASK;
            if (_pageFreeTops[page] == 0)
            {
                _freePageStack[_freePageTop++] = page;
            }

            _pageFreeStacks[page][_pageFreeTops[page]++] = offset;
            _pageAliveCounts[page]--;
        }

        private void AllocatePage()
        {
            EnsurePageCapacity(_pageCount + 1);
            int page = _pageCount++;
            _pages[page] = SlotArrayPool<Slot>.Rent(PAGE_SIZE);
            _pageFreeStacks[page] = SlotArrayPool<int>.Rent(PAGE_SIZE);
            Array.Clear(_pages[page], 0, PAGE_SIZE);
            for (int i = 0; i < PAGE_SIZE; i++)
            {
                _pageFreeStacks[page][i] = PAGE_SIZE - 1 - i;
                _pages[page][i].PrevInactive = -1;
                _pages[page][i].NextInactive = -1;
            }

            _pageFreeTops[page] = PAGE_SIZE;
            _pageAliveCounts[page] = 0;
            _freePageStack[_freePageTop++] = page;
        }

        private void EnsurePageCapacity(int required)
        {
            if (_pages.Length >= required)
            {
                return;
            }

            int newCapacity = Mathf.Max(required, _pages.Length << 1);
            GrowArray(ref _pages, newCapacity);
            GrowArray(ref _pageFreeStacks, newCapacity);
            GrowArray(ref _pageAliveCounts, newCapacity);
            GrowArray(ref _pageFreeTops, newCapacity);
            GrowArray(ref _freePageStack, newCapacity);
        }

        private static void GrowArray<T>(ref T[] array, int newCapacity)
        {
            T[] grown = SlotArrayPool<T>.Rent(newCapacity);
            Array.Clear(grown, 0, newCapacity);
            if (array != null)
            {
                Array.Copy(array, 0, grown, 0, array.Length);
                SlotArrayPool<T>.Return(array, true);
            }

            array = grown;
        }

        private void ReturnStorage()
        {
            for (int page = 0; page < _pageCount; page++)
            {
                if (_pages[page] != null)
                {
                    for (int offset = 0; offset < PAGE_SIZE; offset++)
                    {
                        if (_pages[page][offset].Poolables != null)
                        {
                            SlotArrayPool<IObjectPoolable>.Return(_pages[page][offset].Poolables, true);
                        }
                    }

                    SlotArrayPool<Slot>.Return(_pages[page], true);
                    SlotArrayPool<int>.Return(_pageFreeStacks[page], true);
                }
            }

            SlotArrayPool<Slot[]>.Return(_pages, true);
            SlotArrayPool<int[]>.Return(_pageFreeStacks, true);
            SlotArrayPool<int>.Return(_pageAliveCounts, true);
            SlotArrayPool<int>.Return(_pageFreeTops, true);
            SlotArrayPool<int>.Return(_freePageStack, true);
            _pages = null;
            _pageFreeStacks = null;
            _pageAliveCounts = null;
            _pageFreeTops = null;
            _freePageStack = null;
            _pageCount = 0;
            _freePageTop = 0;
        }

        #endregion
    }
}
