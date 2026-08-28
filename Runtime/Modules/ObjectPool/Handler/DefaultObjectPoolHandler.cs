using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 基于 Unity 原生 Instantiate/Destroy 的默认游戏对象池处理器。
    /// <para><see cref="ObjectPoolServiceHandler"/> 的内置实现，使用最小堆调度维护，PoolCatalog 数据驱动配置。</para>
    /// <para>由 <see cref="ObjectPoolServiceSettings"/> 序列化配置，可替换为自定义对象池后端。</para>
    /// </summary>
    [Serializable]
    public sealed class DefaultObjectPoolHandler : ObjectPoolServiceHandler
    {
        #region 常量 [CONSTANTS]

        // 每帧用于池维护操作的最大时间预算（秒），超出则延迟到下一帧，防止卡顿。
        // 单个池耗时微秒级，1ms 足够处理几十个到期池。
        private const float MAINTENANCE_FRAME_BUDGET_SECONDS = 0.001f;

        #endregion

        #region 内部结构 [INTERNAL STRUCTS]

        private struct MaintenanceNode
        {
            public float dueTime;
            public int poolIndex;
        }

        #endregion

        #region 常量 [CONSTANTS]

        private static readonly Comparison<ObjectPoolSnapshot> s_SnapshotComparer = CompareSnapshot;

        #endregion

        #region 字段 [FIELDS]

        [NonSerialized] private readonly IPrefabLoader _loader = new ResourcePrefabLoader();
        [NonSerialized] private Transform _containerRoot;
        [NonSerialized] private readonly List<ObjectPoolSnapshot> _debugSnapshots = new List<ObjectPoolSnapshot>(16);
        [NonSerialized] private readonly StringOpenHashMap _unregisteredWarned = new StringOpenHashMap(8);
        [NonSerialized] private readonly StringOpenHashMap _unhandledDespawnWarned = new StringOpenHashMap(8);
        [NonSerialized] private readonly StringOpenHashMap _groupRootMap = new StringOpenHashMap(8);
        [NonSerialized] private readonly StringOpenHashMap _poolByLocation = new StringOpenHashMap(32);

        [NonSerialized] private RuntimeObjectPool[] _pools = new RuntimeObjectPool[8];
        [NonSerialized] private int _poolCount;
        [NonSerialized] private PoolCompiledCatalog _catalog = PoolCompiledCatalog.Empty();
        [NonSerialized] private Transform[] _groupRoots = new Transform[4];
        [NonSerialized] private int _groupRootCount;
        [NonSerialized] private MaintenanceNode[] _maintenanceHeap = new MaintenanceNode[8];
        [NonSerialized] private int _maintenanceCount;
        [NonSerialized] private bool _enabled;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 处理器初始化。
        /// </summary>
        protected override void OnInit()
        {
            GameObject rootGo = new GameObject("[ObjectPool]");
            UnityEngine.Object.DontDestroyOnLoad(rootGo);
            _containerRoot = rootGo.transform;

            Application.lowMemory += OnLowMemory;
        }

        /// <summary>
        /// 处理器关闭。
        /// </summary>
        protected override void OnShutdown()
        {
            Application.lowMemory -= OnLowMemory;
            ClearAllPools();
            _catalog.Dispose();
            _catalog = null;
            _unregisteredWarned.Dispose();
            _unhandledDespawnWarned.Dispose();
            _groupRootMap.Dispose();
            _poolByLocation.Dispose();
            if (_containerRoot != null)
            {
                UnityEngine.Object.Destroy(_containerRoot.gameObject);
                _containerRoot = null;
            }
        }

        /// <summary>
        /// 每帧 Tick，处理到期的维护操作。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            if (!_enabled)
            {
                return;
            }

            ProcessDueMaintenance(Time.time);
            _enabled = _maintenanceCount > 0;
        }

        #endregion

        #region 对象池操作 [POOL OPERATIONS]

        /// <summary>
        /// 同步获取游戏对象。
        /// </summary>
        public override GameObject Spawn(string location, Transform parent)
        {
            RuntimeObjectPool pool = ResolvePool(location);
            return pool == null ? null : pool.Spawn(parent);
        }

        /// <summary>
        /// 同步获取组件。
        /// </summary>
        public override T Spawn<T>(string location, Transform parent)
        {
            GameObject instance = Spawn(location, parent);
            return instance == null ? null : instance.GetComponent<T>();
        }

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        public override bool TrySpawn(string location, Transform parent, out GameObject instance)
        {
            instance = Spawn(location, parent);
            return instance != null;
        }

        /// <summary>
        /// 异步获取游戏对象。
        /// </summary>
        public override async UniTask<GameObject> SpawnAsync(string location, Transform parent, CancellationToken cancellationToken)
        {
            RuntimeObjectPool pool = ResolvePool(location);
            return pool == null ? null : await pool.SpawnAsync(parent, cancellationToken);
        }

        /// <summary>
        /// 异步获取组件。
        /// </summary>
        public override async UniTask<T> SpawnAsync<T>(string location, Transform parent, CancellationToken cancellationToken)
        {
            GameObject instance = await SpawnAsync(location, parent, cancellationToken);
            return instance == null ? null : instance.GetComponent<T>();
        }

        /// <summary>
        /// 异步预热。
        /// </summary>
        public override async UniTask WarmupAsync(string location, int count, CancellationToken cancellationToken)
        {
            RuntimeObjectPool pool = ResolvePool(location);
            if (pool != null)
            {
                await pool.WarmupAsync(count, cancellationToken);
            }
        }

        /// <summary>
        /// 回收游戏对象。
        /// </summary>
        public override void Despawn(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            if (instance.TryGetComponent(out ObjectPoolHandle handle) && handle.TryRelease())
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            WarnUnhandledDespawn(instance);
#endif
            UnityEngine.Object.Destroy(instance);
        }

        /// <summary>
        /// 通过句柄回收游戏对象。
        /// </summary>
        public override void Despawn(ObjectPoolHandle handle)
        {
            if (handle == null || handle.TryRelease())
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            WarnUnhandledDespawn(handle.gameObject);
#endif
            if (handle != null)
            {
                UnityEngine.Object.Destroy(handle.gameObject);
            }
        }

        /// <summary>
        /// 刷新指定地址的池。
        /// </summary>
        public override void Flush(string location)
        {
            RuntimeObjectPool pool = FindPool(location);
            pool?.Flush();
        }

        /// <summary>
        /// 刷新指定分组的所有池。
        /// </summary>
        public override void FlushGroup(string group)
        {
            string groupName = string.IsNullOrWhiteSpace(group) ? PoolEntry.DEFAULT_GROUP : group.Trim();
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeObjectPool pool = _pools[i];
                if (pool != null && string.Equals(pool.Group, groupName, StringComparison.Ordinal))
                {
                    pool.Flush();
                }
            }
        }

        /// <summary>
        /// 刷新所有池。
        /// </summary>
        public override void FlushAll()
        {
            for (int i = 0; i < _poolCount; i++)
            {
                _pools[i]?.Flush();
            }
        }

        /// <summary>
        /// 加载池配置。
        /// </summary>
        public override void LoadCatalog(PoolConfigScriptableObject config)
        {
            ClearAllPools();
            _catalog.Dispose();
            _catalog = config == null ? PoolCompiledCatalog.Empty() : config.BuildCatalog();
            _enabled = false;
        }

        #endregion

        #region 调试接口 [DEBUG INTERFACE]

        /// <summary>
        /// 获取调试摘要。
        /// </summary>
        public override ObjectPoolSummarySnapshot GetDebugSummary()
        {
            int loadedPrefabCount = 0;
            int totalInstanceCount = 0;
            int activeInstanceCount = 0;
            int inactiveInstanceCount = 0;
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeObjectPool pool = _pools[i];
                if (pool == null)
                {
                    continue;
                }

                if (pool.IsPrefabLoaded)
                {
                    loadedPrefabCount++;
                }

                totalInstanceCount += pool.TotalCount;
                activeInstanceCount += pool.ActiveCount;
                inactiveInstanceCount += pool.InactiveCount;
            }

            return new ObjectPoolSummarySnapshot(
                true,
                _poolCount,
                loadedPrefabCount,
                totalInstanceCount,
                activeInstanceCount,
                inactiveInstanceCount,
                _maintenanceCount);
        }

        /// <summary>
        /// 获取调试快照。
        /// </summary>
        public override int GetDebugSnapshots(ObjectPoolSnapshot[] snapshots)
        {
            if (snapshots == null || snapshots.Length == 0)
            {
                ReleaseDebugSnapshots();
                return 0;
            }

            ReleaseDebugSnapshots();
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeObjectPool pool = _pools[i];
                if (pool != null)
                {
                    _debugSnapshots.Add(pool.CreateSnapshot(false));
                }
            }

            _debugSnapshots.Sort(s_SnapshotComparer);
            int copyCount = Mathf.Min(snapshots.Length, _debugSnapshots.Count);
            for (int i = 0; i < copyCount; i++)
            {
                snapshots[i] = _debugSnapshots[i];
            }

            return copyCount;
        }

        /// <summary>
        /// 填充实例级调试快照。
        /// </summary>
        public override void FillDebugInstances(ObjectPoolSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.location))
            {
                return;
            }

            FindPool(snapshot.location)?.FillInstances(snapshot);
        }

        #endregion

        #region 内部方法 — 维护调度 [INTERNAL MAINTENANCE SCHEDULING]

        internal override void ScheduleMaintenance(int poolIndex, float dueTime, ref int heapIndex)
        {
            if (dueTime >= float.MaxValue)
            {
                RemoveMaintenance(ref heapIndex);
                return;
            }

            if (heapIndex >= 0)
            {
                _maintenanceHeap[heapIndex].dueTime = dueTime;
                _maintenanceHeap[heapIndex].poolIndex = poolIndex;
                SiftMaintenanceUp(heapIndex);
                SiftMaintenanceDown(heapIndex);
                _enabled = true;
                return;
            }

            EnsureMaintenanceCapacity(_maintenanceCount + 1);
            int insertIndex = _maintenanceCount++;
            _maintenanceHeap[insertIndex].dueTime = dueTime;
            _maintenanceHeap[insertIndex].poolIndex = poolIndex;
            heapIndex = insertIndex;
            _pools[poolIndex].SetMaintenanceHeapIndex(insertIndex);
            SiftMaintenanceUp(insertIndex);
            _enabled = true;
        }

        internal override void RemoveMaintenance(ref int heapIndex)
        {
            if (heapIndex < 0 || heapIndex >= _maintenanceCount)
            {
                heapIndex = -1;
                return;
            }

            RemoveMaintenanceAt(heapIndex);
            heapIndex = -1;
        }

        #endregion

        #region 私有方法 — 池解析 [PRIVATE POOL RESOLUTION]

        private RuntimeObjectPool ResolvePool(string location)
        {
            string normalized = PoolEntry.NormalizeLocation(location);
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            if (_poolByLocation.TryGetValue(normalized, out int poolIndex))
            {
                return _pools[poolIndex];
            }

            int ruleIndex = _catalog.Resolve(normalized);
            if (ruleIndex < 0)
            {
                WarnUnregistered(normalized);
                return null;
            }

            return GetOrCreatePool(ruleIndex, normalized);
        }

        private RuntimeObjectPool FindPool(string location)
        {
            string normalized = PoolEntry.NormalizeLocation(location);
            return !string.IsNullOrEmpty(normalized) && _poolByLocation.TryGetValue(normalized, out int poolIndex)
                ? _pools[poolIndex]
                : null;
        }

        private RuntimeObjectPool GetOrCreatePool(int ruleIndex, string location)
        {
            if (_poolByLocation.TryGetValue(location, out int existing))
            {
                return _pools[existing];
            }

            EnsurePoolCapacity(_poolCount + 1);
            ref readonly PoolCompiledRule rule = ref _catalog.GetRule(ruleIndex);
            RuntimeObjectPool pool = MemoryPool.Acquire<RuntimeObjectPool>();
            pool.Initialize(this, _poolCount, rule, location, _loader, GetOrCreateGroupRoot(rule.Group));
            _pools[_poolCount] = pool;
            _poolByLocation.AddOrUpdate(location, _poolCount);
            _poolCount++;
            return pool;
        }

        private Transform GetOrCreateGroupRoot(string group)
        {
            string groupName = string.IsNullOrWhiteSpace(group) ? PoolEntry.DEFAULT_GROUP : group.Trim();
            if (_groupRootMap.TryGetValue(groupName, out int groupIndex))
            {
                Transform existing = _groupRoots[groupIndex];
                if (existing != null)
                {
                    return existing;
                }
            }

            if (_groupRootCount >= _groupRoots.Length)
            {
                Array.Resize(ref _groupRoots, _groupRoots.Length << 1);
            }

            GameObject rootObject = new GameObject(StringUtility.Concat("[", groupName, "]"));
            Transform root = rootObject.transform;
            root.SetParent(_containerRoot, false);
            int newIndex = _groupRootCount++;
            _groupRoots[newIndex] = root;
            _groupRootMap.AddOrUpdate(groupName, newIndex);
            return root;
        }

        #endregion

        #region 私有方法 — 清理 [PRIVATE CLEANUP]

        private void ClearAllPools()
        {
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeObjectPool pool = _pools[i];
                if (pool == null)
                {
                    continue;
                }

                pool.Shutdown();
                MemoryPool.Release(pool);
                _pools[i] = null;
            }

            _poolCount = 0;
            _maintenanceCount = 0;
            _poolByLocation.Clear();
            _unregisteredWarned.Clear();
            ClearGroupRoots();
            ReleaseDebugSnapshots();
        }

        private void ClearGroupRoots()
        {
            for (int i = 0; i < _groupRootCount; i++)
            {
                Transform root = _groupRoots[i];
                if (root != null)
                {
                    UnityEngine.Object.Destroy(root.gameObject);
                    _groupRoots[i] = null;
                }
            }

            _groupRootCount = 0;
            _groupRootMap.Clear();
        }

        #endregion

        #region 私有方法 — 警告 [PRIVATE WARNINGS]

        private void WarnUnregistered(string location)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_unregisteredWarned.TryGetValue(location, out _))
            {
                return;
            }

            _unregisteredWarned.AddOrUpdate(location, 1);
            LogUtility.Error("[ObjectPool] Location is not in PoolConfig: {0}", location);
#endif
        }

        private void WarnUnhandledDespawn(GameObject instance)
        {
            string name = instance == null ? "<null>" : instance.name;
            if (_unhandledDespawnWarned.TryGetValue(name, out _))
            {
                return;
            }

            _unhandledDespawnWarned.AddOrUpdate(name, 1);
            LogUtility.Warning("[ObjectPool] Despawn target is not a pooled instance and will be destroyed: {0}", name);
        }

        #endregion

        #region 私有方法 — 低内存 [PRIVATE LOW MEMORY]

        private void OnLowMemory()
        {
            float now = Time.time;
            for (int i = 0; i < _poolCount; i++)
            {
                _pools[i]?.ExecuteMaintenance(now, true);
            }
        }

        #endregion

        #region 私有方法 — 最小堆 [PRIVATE MIN-HEAP]

        private void ProcessDueMaintenance(float now)
        {
            float frameStart = Time.realtimeSinceStartup;
            while (_maintenanceCount > 0)
            {
                MaintenanceNode node = _maintenanceHeap[0];
                if (node.dueTime > now)
                {
                    return;
                }

                if (Time.realtimeSinceStartup - frameStart >= MAINTENANCE_FRAME_BUDGET_SECONDS)
                {
                    _enabled = true;
                    return;
                }

                RemoveMaintenanceAt(0);
                _pools[node.poolIndex]?.ExecuteMaintenance(now, false);
            }
        }

        private void EnsurePoolCapacity(int required)
        {
            if (_pools.Length >= required)
            {
                return;
            }

            Array.Resize(ref _pools, Mathf.Max(required, _pools.Length << 1));
        }

        private void EnsureMaintenanceCapacity(int required)
        {
            if (_maintenanceHeap.Length >= required)
            {
                return;
            }

            Array.Resize(ref _maintenanceHeap, Mathf.Max(required, _maintenanceHeap.Length << 1));
        }

        private void RemoveMaintenanceAt(int heapIndex)
        {
            MaintenanceNode removed = _maintenanceHeap[heapIndex];
            _pools[removed.poolIndex]?.SetMaintenanceHeapIndex(-1);
            int lastIndex = _maintenanceCount - 1;
            if (heapIndex != lastIndex)
            {
                MaintenanceNode moved = _maintenanceHeap[lastIndex];
                _maintenanceHeap[heapIndex] = moved;
                _pools[moved.poolIndex]?.SetMaintenanceHeapIndex(heapIndex);
            }

            _maintenanceHeap[lastIndex] = default;
            _maintenanceCount = lastIndex;
            if (heapIndex < _maintenanceCount)
            {
                SiftMaintenanceUp(heapIndex);
                SiftMaintenanceDown(heapIndex);
            }
        }

        private void SiftMaintenanceUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (_maintenanceHeap[parent].dueTime <= _maintenanceHeap[index].dueTime)
                {
                    break;
                }

                SwapMaintenance(parent, index);
                index = parent;
            }
        }

        private void SiftMaintenanceDown(int index)
        {
            while (true)
            {
                int left = (index << 1) + 1;
                if (left >= _maintenanceCount)
                {
                    return;
                }

                int right = left + 1;
                int smallest = right < _maintenanceCount && _maintenanceHeap[right].dueTime < _maintenanceHeap[left].dueTime
                    ? right
                    : left;
                if (_maintenanceHeap[index].dueTime <= _maintenanceHeap[smallest].dueTime)
                {
                    return;
                }

                SwapMaintenance(index, smallest);
                index = smallest;
            }
        }

        private void SwapMaintenance(int left, int right)
        {
            MaintenanceNode temp = _maintenanceHeap[left];
            _maintenanceHeap[left] = _maintenanceHeap[right];
            _maintenanceHeap[right] = temp;
            _pools[_maintenanceHeap[left].poolIndex]?.SetMaintenanceHeapIndex(left);
            _pools[_maintenanceHeap[right].poolIndex]?.SetMaintenanceHeapIndex(right);
        }

        #endregion

        #region 私有方法 — 调试快照 [PRIVATE DEBUG SNAPSHOTS]

        private void ReleaseDebugSnapshots()
        {
            for (int i = 0; i < _debugSnapshots.Count; i++)
            {
                MemoryPool.Release(_debugSnapshots[i]);
            }

            _debugSnapshots.Clear();
        }

        private static int CompareSnapshot(ObjectPoolSnapshot left, ObjectPoolSnapshot right)
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

            int groupCompare = string.CompareOrdinal(left.group, right.group);
            return groupCompare != 0 ? groupCompare : string.CompareOrdinal(left.location, right.location);
        }

        #endregion
    }
}
