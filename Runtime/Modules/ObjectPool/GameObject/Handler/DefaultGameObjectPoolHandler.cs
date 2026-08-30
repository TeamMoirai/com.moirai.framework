using System;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 默认 GameObject 池后端配置（分页槽位 + 代系句柄 + 最小堆维护调度）。
    /// </summary>
    [Serializable]
    public sealed class DefaultGameObjectPoolHandlerConfig : GameObjectPoolServiceHandlerConfig
    {
        [Tooltip("池配置 ScriptableObject。为空时使用空配置（所有地址都会警告未注册）。")]
        [SerializeField] private PoolConfigScriptableObject m_PoolConfig;

        /// <summary>
        /// 池配置 ScriptableObject。
        /// </summary>
        public PoolConfigScriptableObject PoolConfig
        {
            get => m_PoolConfig;
            set => m_PoolConfig = value;
        }

        /// <inheritdoc />
        public override GameObjectPoolServiceHandler CreateHandler()
        {
            return new DefaultGameObjectPoolHandler(this);
        }
    }

    /// <summary>
    /// 基于 Unity 原生 Instantiate/Destroy 的默认 GameObject 池处理器。
    /// <para><see cref="GameObjectPoolServiceHandler"/> 的内置实现：分页槽位存储 + 代系句柄校验 + 共享最小堆维护调度，PoolCatalog 数据驱动配置。</para>
    /// <para>由 <see cref="DefaultGameObjectPoolHandlerConfig"/> 工厂创建（普通运行时类，不参与序列化——运行时字段无需 [NonSerialized] 标注）。</para>
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public sealed class DefaultGameObjectPoolHandler : GameObjectPoolServiceHandler
    {
        #region 常量 [CONSTANTS]

        private const int INITIAL_POOL_CAPACITY = 8;
        private const int INITIAL_GROUP_ROOT_CAPACITY = 4;

        #endregion

        #region 字段 [FIELDS]

        /// <summary>后端配置（组合持有，不参与序列化）。</summary>
        private readonly DefaultGameObjectPoolHandlerConfig m_Config;

        // struct 哈希表/调度器必须存于可变字段（方法直接改写字段状态），禁止 readonly。
        private PoolMaintenanceScheduler _scheduler;
        private readonly IPrefabLoader _loader = new ResourcePrefabLoader();
        private readonly List<GameObjectPoolSnapshot> _debugSnapshots = new List<GameObjectPoolSnapshot>(16);
        private StringOpenHashMap _unregisteredWarned;
        private StringOpenHashMap _unhandledDespawnWarned;
        private StringOpenHashMap _groupRootMap;
        private StringOpenHashMap _poolByLocation;
        private RuntimeGameObjectPool[] _pools;
        private int _poolCount;
        private PoolCompiledCatalog _catalog;
        private Transform _containerRoot;
        private Transform[] _groupRoots;
        private int _groupRootCount;

        private static readonly Comparison<GameObjectPoolSnapshot> s_SnapshotComparer = CompareSnapshot;

        /// <summary>
        /// 以指定配置创建处理器。
        /// </summary>
        /// <param name="config">默认 GameObject 池配置。</param>
        public DefaultGameObjectPoolHandler(DefaultGameObjectPoolHandlerConfig config)
        {
            m_Config = config ?? new DefaultGameObjectPoolHandlerConfig();
        }

        /// <summary>
        /// 以默认配置创建处理器（供测试与默认值场景使用）。
        /// </summary>
        public DefaultGameObjectPoolHandler() : this(new DefaultGameObjectPoolHandlerConfig())
        {
        }

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

            GameObject rootGo = new GameObject("[GameObjectPool]");
            UnityEngine.Object.DontDestroyOnLoad(rootGo);
            _containerRoot = rootGo.transform;

            Application.lowMemory += OnLowMemory;

            if (m_Config.PoolConfig != null)
            {
                LoadCatalog(m_Config.PoolConfig);
            }
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

            if (_containerRoot != null)
            {
                PoolDestroyUtility.Destroy(_containerRoot.gameObject);
                _containerRoot = null;
            }
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
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>游戏对象。</returns>
        public override GameObject Spawn(string location, Transform parent)
        {
            RuntimeGameObjectPool pool = ResolvePool(location);
            return pool == null ? null : pool.Spawn(parent);
        }

        /// <summary>
        /// 同步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件。</returns>
        public override T Spawn<T>(string location, Transform parent)
        {
            GameObject instance = Spawn(location, parent);
            return instance == null ? null : instance.GetComponent<T>();
        }

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="instance">获取的游戏对象。</param>
        /// <returns>是否成功。</returns>
        public override bool TrySpawn(string location, Transform parent, out GameObject instance)
        {
            instance = Spawn(location, parent);
            return instance != null;
        }

        /// <summary>
        /// 异步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>游戏对象。</returns>
        public override async UniTask<GameObject> SpawnAsync(string location, Transform parent, CancellationToken cancellationToken)
        {
            RuntimeGameObjectPool pool = ResolvePool(location);
            return pool == null ? null : await pool.SpawnAsync(parent, cancellationToken);
        }

        /// <summary>
        /// 异步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>组件。</returns>
        public override async UniTask<T> SpawnAsync<T>(string location, Transform parent, CancellationToken cancellationToken)
        {
            GameObject instance = await SpawnAsync(location, parent, cancellationToken);
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
        /// 异步预热。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="count">预热数量。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public override async UniTask WarmupAsync(string location, int count, CancellationToken cancellationToken)
        {
            RuntimeGameObjectPool pool = ResolvePool(location);
            if (pool != null)
            {
                await pool.WarmupAsync(count, cancellationToken);
            }
        }

        #endregion

        #region 回收与刷新 [DESPAWN & FLUSH]

        /// <summary>
        /// 回收游戏对象。
        /// </summary>
        /// <param name="instance">游戏对象。</param>
        public override void Despawn(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            if (instance.TryGetComponent(out GameObjectPoolHandle handle) && handle.TryRelease())
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            WarnUnhandledDespawn(instance);
#endif
            PoolDestroyUtility.Destroy(instance);
        }

        /// <summary>
        /// 通过句柄回收游戏对象。
        /// </summary>
        /// <param name="handle">句柄。</param>
        public override void Despawn(GameObjectPoolHandle handle)
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
                PoolDestroyUtility.Destroy(handle.gameObject);
            }
        }

        /// <summary>
        /// 刷新指定地址的池。
        /// </summary>
        /// <param name="location">资源地址。</param>
        public override void Flush(string location)
        {
            RuntimeGameObjectPool pool = FindPool(location);
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
                return null;
            }

            return GetOrCreatePool(ruleIndex, normalized);
        }

        private RuntimeGameObjectPool FindPool(string location)
        {
            string normalized = PoolEntry.NormalizeLocation(location);
            return !string.IsNullOrEmpty(normalized) && _poolByLocation.TryGetValue(normalized, out int poolIndex)
                ? _pools[poolIndex]
                : null;
        }

        private RuntimeGameObjectPool GetOrCreatePool(int ruleIndex, string location)
        {
            if (_poolByLocation.TryGetValue(location, out int existing))
            {
                return _pools[existing];
            }

            EnsurePoolCapacity(_poolCount + 1);
            ref readonly PoolCompiledRule rule = ref _catalog.GetRule(ruleIndex);
            RuntimeGameObjectPool pool = MemoryPool.Acquire<RuntimeGameObjectPool>();
            pool.Initialize(_scheduler, rule, location, _loader, GetOrCreateGroupRoot(rule.Group));
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
            LogUtility.Error("[GameObjectPool] Location is not in PoolConfig: {0}", location);
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
            LogUtility.Warning("[GameObjectPool] Despawn target is not a pooled instance and will be destroyed: {0}", name);
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
