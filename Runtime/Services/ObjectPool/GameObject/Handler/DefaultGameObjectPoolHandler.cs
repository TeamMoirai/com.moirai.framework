using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Attributes;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 基于 Unity 原生 Instantiate/Destroy 的默认 GameObject 池处理器。
    /// <para><see cref="GameObjectPoolServiceHandler"/> 的内置实现：分页槽位存储 + 代系句柄校验 + 共享最小堆维护调度，PoolCatalog 数据驱动配置。</para>
    /// <para>由 <see cref="GameObjectPoolServiceSettings"/> 序列化配置，可替换为自定义对象池后端。</para>
    /// </summary>
    [Serializable]
    [UnityEngine.Scripting.Preserve]
    public sealed class DefaultGameObjectPoolHandler : GameObjectPoolServiceHandler
    {
        #region 常量 [CONSTANTS]

        private const int INITIAL_POOL_CAPACITY = 8;
        private const int INITIAL_GROUP_ROOT_CAPACITY = 4;

        #endregion

        #region 字段 [FIELDS]

        [Tooltip("池配置 ScriptableObject。为空时使用空配置（所有地址都会警告未注册）。")]
        [Expand]
        [SerializeField] private PoolConfigScriptableObject m_PoolConfig;

        // struct 哈希表/调度器必须存于可变字段（方法直接改写字段状态），禁止 readonly。
        [NonSerialized] private PoolMaintenanceScheduler _scheduler;
        [NonSerialized] private readonly IPrefabLoader _loader = new ResourcePrefabLoader();
        [NonSerialized] private readonly List<GameObjectPoolSnapshot> _debugSnapshots = new List<GameObjectPoolSnapshot>(16);
        [NonSerialized] private StringOpenHashMap _unregisteredWarned;
        [NonSerialized] private StringOpenHashMap _unhandledDespawnWarned;
        [NonSerialized] private StringOpenHashMap _groupRootMap;
        [NonSerialized] private StringOpenHashMap _poolByLocation;
        // struct 哈希表必须存于可变字段；Prefab 源热路径零字符串分配查找。
        [NonSerialized] private ReferenceOpenHashMap _poolByPrefab;
        [NonSerialized] private RuntimeGameObjectPool[] _pools;
        [NonSerialized] private int _poolCount;
        [NonSerialized] private PoolCompiledCatalog _catalog;
        [NonSerialized] private Transform _containerRoot;
        [NonSerialized] private Transform[] _groupRoots;
        [NonSerialized] private int _groupRootCount;
        [NonSerialized] private PooledInstanceRegistry _registry;

        private static readonly Comparison<GameObjectPoolSnapshot> s_SnapshotComparer = CompareSnapshot;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 处理器初始化（幂等：无条件重建全部运行时状态并加载目录配置）。
        /// </summary>
        protected override void OnInit()
        {
            _scheduler = new PoolMaintenanceScheduler();
            _pools = new RuntimeGameObjectPool[INITIAL_POOL_CAPACITY];
            _poolCount = 0;
            _catalog = PoolCompiledCatalog.Empty();
            _groupRoots = new Transform[INITIAL_GROUP_ROOT_CAPACITY];
            _groupRootCount = 0;
            _unregisteredWarned = new StringOpenHashMap(8);
            _unhandledDespawnWarned = new StringOpenHashMap(8);
            _groupRootMap = new StringOpenHashMap(8);
            _poolByLocation = new StringOpenHashMap(32);
            _poolByPrefab = new ReferenceOpenHashMap(16);
            _registry = new PooledInstanceRegistry(64);

            GameObject rootGo = new GameObject("[GameObjectPool]");
            UnityEngine.Object.DontDestroyOnLoad(rootGo);
            _containerRoot = rootGo.transform;

            Application.lowMemory += OnLowMemory;

            if (m_PoolConfig != null) LoadCatalog(m_PoolConfig);
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
            _registry?.Dispose();
            _registry = null;

            if (_containerRoot != null)
            {
                PoolDestroyUtility.Destroy(_containerRoot.gameObject);
                _containerRoot = null;
            }

            _poolByLocation.Dispose();
            _poolByPrefab.Dispose();
            _unregisteredWarned.Dispose();
            _unhandledDespawnWarned.Dispose();
            _groupRootMap.Dispose();
        }

        /// <summary>
        /// 每帧轮询——处理到期的维护操作。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            _scheduler.ProcessDue(Time.time);
        }

        #endregion

        #region 获取 [SPAWN]

        /// <summary>
        /// 同步获取游戏对象。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>游戏对象。</returns>
        public override GameObject Spawn(GameObjectPoolSource source, Transform parent)
        {
            RuntimeGameObjectPool pool = ResolveSourcePool(source);
            return pool == null ? null : pool.Spawn(parent);
        }

        /// <summary>
        /// 同步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件。</returns>
        public override T Spawn<T>(GameObjectPoolSource source, Transform parent)
        {
            GameObject instance = Spawn(source, parent);
            return instance == null ? null : instance.GetComponent<T>();
        }

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="instance">获取的游戏对象。</param>
        /// <returns>是否成功。</returns>
        public override bool TrySpawn(GameObjectPoolSource source, Transform parent, out GameObject instance)
        {
            instance = Spawn(source, parent);
            return instance != null;
        }

        /// <summary>
        /// 异步获取游戏对象。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>游戏对象。</returns>
        public override async UniTask<GameObject> SpawnAsync(GameObjectPoolSource source, Transform parent, CancellationToken cancellationToken)
        {
            RuntimeGameObjectPool pool = ResolveSourcePool(source);
            return pool == null ? null : await pool.SpawnAsync(parent, cancellationToken);
        }

        /// <summary>
        /// 异步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>组件。</returns>
        public override async UniTask<T> SpawnAsync<T>(GameObjectPoolSource source, Transform parent, CancellationToken cancellationToken)
        {
            GameObject instance = await SpawnAsync(source, parent, cancellationToken);
            return instance == null ? null : instance.GetComponent<T>();
        }

        #endregion

        #region 预制体与预热 [PREFAB & WARMUP]

        /// <summary>
        /// 同步加载预制体。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>预制体。</returns>
        public override GameObject LoadPrefab(string location)
        {
            RuntimeGameObjectPool pool = ResolvePool(location);
            return pool == null ? null : pool.LoadPrefab();
        }

        /// <summary>
        /// 异步加载预制体。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>预制体。</returns>
        public override async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken)
        {
            RuntimeGameObjectPool pool = ResolvePool(location);
            return pool == null ? null : await pool.LoadPrefabAsync(cancellationToken);
        }

        /// <summary>
        /// 异步预热指定来源的池。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="count">预热数量。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public override async UniTask WarmupAsync(GameObjectPoolSource source, int count, CancellationToken cancellationToken)
        {
            RuntimeGameObjectPool pool = ResolveSourcePool(source);
            if (pool != null)
            {
                await pool.WarmupAsync(count, cancellationToken);
            }
        }

        #endregion

        #region 回收与刷新 [DESPAWN & FLUSH]

        /// <summary>
        /// 回收游戏对象。仅外来（未注册）对象才会 Destroy；已注册但非 Active 的重复 Despawn 为安全 no-op。
        /// </summary>
        /// <param name="instance">游戏对象。</param>
        public override void Despawn(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            if (TryResolveInstance(instance, out RuntimeGameObjectPool pool, out int slotIndex))
            {
                switch (pool.ReleaseByInstance(slotIndex, instance))
                {
                    case PoolReleaseResult.Released:
                        return;
                    case PoolReleaseResult.NotActive:
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        LogUtility.Warning("[GameObjectPool] Despawn ignored: instance is not active (duplicate Despawn?): {0}", instance.name);
#endif
                        return;
                    default:
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        LogUtility.Warning("[GameObjectPool] Despawn ignored: instance does not own the resolved slot: {0}", instance.name);
#endif
                        return;
                }
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            WarnUnhandledDespawn(instance);
#endif
            PoolDestroyUtility.Destroy(instance);
        }

        /// <summary>
        /// 通过租约回收游戏对象。
        /// </summary>
        /// <param name="pooled">池化租约。</param>
        public override void Despawn(PooledGameObject pooled)
        {
            pooled?.Dispose();
        }

        /// <summary>
        /// 尝试解析实例身份。
        /// </summary>
        internal override bool TryResolveInstance(GameObject instance, out RuntimeGameObjectPool pool, out int slotIndex)
        {
            pool = null;
            slotIndex = -1;
            return _registry != null && _registry.TryResolve(instance, out pool, out slotIndex);
        }

        /// <summary>
        /// 刷新指定来源的池。
        /// </summary>
        /// <param name="source">池化来源。</param>
        public override void Flush(GameObjectPoolSource source)
        {
            if (!source.IsValid)
            {
                return;
            }

            RuntimeGameObjectPool pool = source.IsPrefab
                ? FindPrefabPool(source.Prefab)
                : FindPool(source.Location);
            pool?.Flush();
        }

        /// <summary>
        /// 刷新指定分组的所有池。
        /// </summary>
        /// <param name="group">分组名称。</param>
        public override void FlushGroup(string group)
        {
            string groupName = string.IsNullOrWhiteSpace(group) ? PoolEntry.DEFAULT_GROUP : group.Trim();
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeGameObjectPool pool = _pools[i];
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

        #endregion

        #region 目录 [CATALOG]

        /// <summary>
        /// 加载池配置（重建全部池）。
        /// </summary>
        /// <param name="config">配置 ScriptableObject；null 表示空配置。</param>
        public override void LoadCatalog(PoolConfigScriptableObject config)
        {
            ClearAllPools();
            _catalog.Dispose();
            _catalog = config == null ? PoolCompiledCatalog.Empty() : config.BuildCatalog();
        }

        /// <summary>
        /// 从资源地址加载池配置（重建全部池）。
        /// <para>租约在目录编译完成后立即释放——编译产物不持有资产引用。</para>
        /// </summary>
        /// <param name="poolConfigPath">池配置资源地址。</param>
        public override void LoadCatalog(string poolConfigPath)
        {
            ResourceAssetLease<PoolConfigScriptableObject> lease = ResourceService.LoadLease<PoolConfigScriptableObject>(poolConfigPath);
            using (lease)
            {
                LoadCatalog(lease.Asset);
            }
        }

        #endregion

        #region 调试接口 [DEBUG INTERFACE]

        /// <summary>
        /// 获取调试摘要。
        /// </summary>
        public override GameObjectPoolSummarySnapshot GetDebugSummary()
        {
            int loadedPrefabCount = 0;
            int totalInstanceCount = 0;
            int activeInstanceCount = 0;
            int inactiveInstanceCount = 0;
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeGameObjectPool pool = _pools[i];
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

            return new GameObjectPoolSummarySnapshot(
                true,
                _poolCount,
                loadedPrefabCount,
                totalInstanceCount,
                activeInstanceCount,
                inactiveInstanceCount,
                _scheduler.Count);
        }

        /// <summary>
        /// 获取调试快照。
        /// </summary>
        public override int GetDebugSnapshots(GameObjectPoolSnapshot[] snapshots)
        {
            if (snapshots == null || snapshots.Length == 0)
            {
                ReleaseDebugSnapshots();
                return 0;
            }

            ReleaseDebugSnapshots();
            for (int i = 0; i < _poolCount; i++)
            {
                RuntimeGameObjectPool pool = _pools[i];
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
        public override void FillDebugInstances(GameObjectPoolSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.location))
            {
                return;
            }

            FindPool(snapshot.location)?.FillInstances(snapshot);
        }

        #endregion

        #region 私有方法 — 池解析 [PRIVATE POOL RESOLUTION]

        private RuntimeGameObjectPool ResolveSourcePool(GameObjectPoolSource source)
        {
            if (!source.IsValid)
            {
                return null;
            }

            if (source.IsPrefab)
            {
                return ResolveOrCreatePrefabPool(source.Prefab, source.Group);
            }

            if (!string.IsNullOrEmpty(source.Group))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogUtility.Warning("[GameObjectPool] Group on location source is ignored; catalog rule decides group: {0}", source.Location);
#endif
            }

            return ResolvePool(source.Location);
        }

        private RuntimeGameObjectPool ResolvePool(string location)
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
                return GetOrCreatePoolWithRule(DefaultPoolRules.CreateLocationRule(normalized), normalized);
            }

            return GetOrCreatePool(ruleIndex, normalized);
        }

        private RuntimeGameObjectPool ResolveOrCreatePrefabPool(GameObject prefab, string group = null)
        {
            if (prefab == null)
            {
                return null;
            }

            if (_poolByPrefab.TryGetValue(prefab, out int poolIndex))
            {
                return _pools[poolIndex];
            }

            string location = DefaultPoolRules.GetPrefabPoolLocation(prefab);
            if (_poolByLocation.TryGetValue(location, out int existing))
            {
                _poolByPrefab.AddOrUpdate(prefab, existing);
                return _pools[existing];
            }

            PoolCompiledRule rule = DefaultPoolRules.CreateExternalRule(location, group);
            return GetOrCreateExternalPrefabPool(in rule, location, prefab);
        }

        private RuntimeGameObjectPool FindPool(string location)
        {
            // raw 优先：合成 Prefab 键可含 '.'，规范化会剥扩展名导致查找失败。
            if (!string.IsNullOrEmpty(location) && _poolByLocation.TryGetValue(location, out int rawIndex))
            {
                return _pools[rawIndex];
            }

            string normalized = PoolEntry.NormalizeLocation(location);
            return !string.IsNullOrEmpty(normalized) && _poolByLocation.TryGetValue(normalized, out int poolIndex)
                ? _pools[poolIndex]
                : null;
        }

        private RuntimeGameObjectPool FindPrefabPool(GameObject prefab)
        {
            if (prefab != null && _poolByPrefab.TryGetValue(prefab, out int poolIndex))
            {
                return _pools[poolIndex];
            }

            return prefab == null ? null : FindPool(DefaultPoolRules.GetPrefabPoolLocation(prefab));
        }

        private RuntimeGameObjectPool GetOrCreatePool(int ruleIndex, string location)
        {
            if (_poolByLocation.TryGetValue(location, out int existing))
            {
                return _pools[existing];
            }

            ref readonly PoolCompiledRule rule = ref _catalog.GetRule(ruleIndex);
            return GetOrCreatePoolWithRule(rule, location);
        }

        private RuntimeGameObjectPool GetOrCreatePoolWithRule(in PoolCompiledRule rule, string location)
        {
            if (_poolByLocation.TryGetValue(location, out int existing))
            {
                return _pools[existing];
            }

            EnsurePoolCapacity(_poolCount + 1);
            RuntimeGameObjectPool pool = MemoryPool.Acquire<RuntimeGameObjectPool>();
            pool.Initialize(_scheduler, rule, location, _loader, GetOrCreateGroupRoot(rule.Group), _registry);
            RegisterPool(pool, location);
            return pool;
        }

        private RuntimeGameObjectPool GetOrCreateExternalPrefabPool(in PoolCompiledRule rule, string location, GameObject prefab)
        {
            if (_poolByLocation.TryGetValue(location, out int existing))
            {
                _poolByPrefab.AddOrUpdate(prefab, existing);
                return _pools[existing];
            }

            EnsurePoolCapacity(_poolCount + 1);
            RuntimeGameObjectPool pool = MemoryPool.Acquire<RuntimeGameObjectPool>();
            pool.InitializeWithPrefab(_scheduler, rule, location, prefab, GetOrCreateGroupRoot(rule.Group), _registry);
            RegisterPool(pool, location);
            _poolByPrefab.AddOrUpdate(prefab, _poolCount - 1);
            return pool;
        }

        private void RegisterPool(RuntimeGameObjectPool pool, string location)
        {
            _pools[_poolCount] = pool;
            _poolByLocation.AddOrUpdate(location, _poolCount);
            _poolCount++;
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
                RuntimeGameObjectPool pool = _pools[i];
                if (pool == null)
                {
                    continue;
                }

                pool.Shutdown();
                MemoryPool.Release(pool);
                _pools[i] = null;
            }

            _poolCount = 0;
            _scheduler.Clear();
            _poolByLocation.Clear();
            _poolByPrefab.Clear();
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
                    PoolDestroyUtility.Destroy(root.gameObject);
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
            LogUtility.Warning("[GameObjectPool] Location is not in PoolConfig, using default rule: {0}", location);
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
            LogUtility.Warning("[GameObjectPool] Despawn target is not a registered pooled instance (foreign or already destroyed) and will be destroyed: {0}", name);
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

        #region 私有方法 — 工具 [PRIVATE UTILITIES]

        private void EnsurePoolCapacity(int required)
        {
            if (_pools.Length >= required)
            {
                return;
            }

            Array.Resize(ref _pools, Mathf.Max(required, _pools.Length << 1));
        }

        private void ReleaseDebugSnapshots()
        {
            for (int i = 0; i < _debugSnapshots.Count; i++)
            {
                MemoryPool.Release(_debugSnapshots[i]);
            }

            _debugSnapshots.Clear();
        }

        private static int CompareSnapshot(GameObjectPoolSnapshot left, GameObjectPoolSnapshot right)
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
