using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.ObjectPool;
using NUnit.Framework;
using UnityEngine;

namespace ObjectPoolTests
{
    /// <summary>
    /// GameObject 池回归测试：注入 fake IPrefabLoader 直测 RuntimeGameObjectPool 的
    /// Spawn/Despawn 往返、句柄代系校验、容量约束、Flush 裁剪与策略规划器。
    /// </summary>
    public sealed class GameObjectPoolTests
    {
        #region 测试桩 [TEST FAKE]

        private sealed class FakePrefabLoader : IPrefabLoader
        {
            public GameObject Prefab;
            public int LoadCount;
            public int UnloadCount;

            public FakePrefabLoader()
            {
                Prefab = new GameObject("FakePrefab");
            }

            public GameObject LoadPrefab(string location)
            {
                LoadCount++;
                return Prefab;
            }

            public UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
            {
                LoadCount++;
                return UniTask.FromResult(Prefab);
            }

            public void UnloadPrefab(GameObject prefab)
            {
                UnloadCount++;
            }
        }

        #endregion

        #region 基础设施 [INFRASTRUCTURE]

        private PoolMaintenanceScheduler _scheduler;
        private FakePrefabLoader _loader;
        private Transform _root;
        private RuntimeGameObjectPool _pool;

        [SetUp]
        public void SetUp()
        {
            _scheduler = new PoolMaintenanceScheduler();
            _loader = new FakePrefabLoader();
            _root = new GameObject("PoolRoot").transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (_pool != null)
            {
                _pool.Shutdown();
                _pool = null;
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root.gameObject);
                _root = null;
            }

            if (_loader != null && _loader.Prefab != null)
            {
                Object.DestroyImmediate(_loader.Prefab);
                _loader = null;
            }
        }

        private RuntimeGameObjectPool CreatePool(
            EPoolPolicy policy = EPoolPolicy.Burst,
            int minIdle = 0,
            int softCapacity = 4,
            int hardCapacity = 8,
            float idleSeconds = 15f,
            bool unloadPrefab = true)
        {
            PoolCompiledRule rule = new PoolCompiledRule(
                0, "TestEntry", PoolEntry.DEFAULT_GROUP, "Assets/Test/Fake",
                policy, minIdle, softCapacity, hardCapacity, idleSeconds, unloadPrefab, 0,
                PoolGlobMatcher.Compile("Assets/Test/Fake"));
            _pool = new RuntimeGameObjectPool();
            _pool.Initialize(_scheduler, rule, "Assets/Test/Fake", _loader, _root);
            return _pool;
        }

        private GameObject SpawnOne(RuntimeGameObjectPool pool)
        {
            GameObject prefab = pool.LoadPrefab();
            Assert.NotNull(prefab);
            return pool.Spawn(null);
        }

        private void DespawnOne(RuntimeGameObjectPool pool, GameObject instance)
        {
            Assert.IsTrue(instance.TryGetComponent(out GameObjectPoolHandle handle));
            Assert.IsTrue(handle.TryRelease());
        }

        #endregion

        #region Spawn / Despawn 往返 [SPAWN ROUND TRIP]

        [Test]
        public void Spawn_CreatesInstanceWithHandle()
        {
            RuntimeGameObjectPool pool = CreatePool();

            GameObject instance = SpawnOne(pool);

            Assert.NotNull(instance);
            Assert.IsTrue(instance.TryGetComponent(out GameObjectPoolHandle handle));
            Assert.AreEqual(1, pool.TotalCount);
            Assert.AreEqual(1, pool.ActiveCount);
            Assert.AreEqual(0, pool.InactiveCount);
        }

        [Test]
        public void Spawn_WithoutPrefabLoaded_ReturnsNull()
        {
            RuntimeGameObjectPool pool = CreatePool();

            GameObject instance = pool.Spawn(null);

            Assert.IsNull(instance);
        }

        [Test]
        public void Despawn_ParksInactiveUnderRoot()
        {
            RuntimeGameObjectPool pool = CreatePool();
            GameObject instance = SpawnOne(pool);

            DespawnOne(pool, instance);

            Assert.AreEqual(1, pool.TotalCount);
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(1, pool.InactiveCount);
            Assert.IsFalse(instance.activeSelf);
            Assert.AreEqual(_root, instance.transform.parent);
        }

        [Test]
        public void Spawn_AfterDespawn_ReusesSameInstance()
        {
            RuntimeGameObjectPool pool = CreatePool();
            GameObject first = SpawnOne(pool);
            DespawnOne(pool, first);

            GameObject second = SpawnOne(pool);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, pool.TotalCount, "reuse must not expand the pool");
            Assert.AreEqual(1, pool.ActiveCount);
        }

        [Test]
        public void Handle_TryReleaseTwice_SecondFails()
        {
            RuntimeGameObjectPool pool = CreatePool();
            GameObject instance = SpawnOne(pool);
            GameObjectPoolHandle handle = instance.GetComponent<GameObjectPoolHandle>();

            Assert.IsTrue(handle.TryRelease());
            Assert.IsFalse(handle.TryRelease(), "second release of same generation must fail");
            Assert.AreEqual(1, pool.InactiveCount);
        }

        [Test]
        public void Pool_ImplementsIPoolMaintenanceItem_WithHeapIndex()
        {
            RuntimeGameObjectPool pool = CreatePool();

            Assert.AreEqual(-1, pool.MaintenanceHeapIndex);
        }

        #endregion

        #region 容量约束 [CAPACITY]

        [Test]
        public void Spawn_BeyondHardCapacity_ReturnsNull()
        {
            RuntimeGameObjectPool pool = CreatePool(hardCapacity: 3);

            GameObject a = SpawnOne(pool);
            GameObject b = SpawnOne(pool);
            GameObject c = SpawnOne(pool);
            GameObject d = pool.Spawn(null);

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.NotNull(c);
            Assert.IsNull(d);
            Assert.AreEqual(3, pool.TotalCount);
        }

        [Test]
        public void Despawn_BeyondHardCapacity_DespanwedObjectsRecycledOnNextSpawn()
        {
            RuntimeGameObjectPool pool = CreatePool(hardCapacity: 2);
            GameObject a = SpawnOne(pool);
            GameObject b = SpawnOne(pool);
            DespawnOne(pool, a);

            GameObject c = SpawnOne(pool);

            Assert.AreSame(a, c);
            Assert.AreEqual(2, pool.TotalCount);
        }

        #endregion

        #region 维护与裁剪 [MAINTENANCE & TRIM]

        [Test]
        public void ExecuteMaintenance_LowMemory_TrimsToMinIdle()
        {
            RuntimeGameObjectPool pool = CreatePool(policy: EPoolPolicy.Burst, minIdle: 1, softCapacity: 8, hardCapacity: 16);
            GameObject a = SpawnOne(pool);
            GameObject b = SpawnOne(pool);
            DespawnOne(pool, a);
            DespawnOne(pool, b);

            pool.ExecuteMaintenance(Time.time, true);

            Assert.AreEqual(1, pool.TotalCount, "low memory trims to minIdle");
            Assert.AreEqual(1, pool.InactiveCount);
            Assert.AreEqual(0, _loader.UnloadCount, "totalCount > 0 keeps prefab loaded");
        }

        [Test]
        public void Flush_EmptyPool_UnloadsPrefab()
        {
            RuntimeGameObjectPool pool = CreatePool(unloadPrefab: true);
            GameObject a = SpawnOne(pool);
            DespawnOne(pool, a);

            pool.Flush();

            Assert.AreEqual(0, pool.TotalCount);
            Assert.AreEqual(1, _loader.UnloadCount);
            Assert.IsFalse(pool.IsPrefabLoaded);
        }

        [Test]
        public void ExecuteMaintenance_BurstIdleNotElapsed_KeepsInstances()
        {
            RuntimeGameObjectPool pool = CreatePool(policy: EPoolPolicy.Burst, minIdle: 0, softCapacity: 8, hardCapacity: 16, idleSeconds: 100f);
            GameObject a = SpawnOne(pool);
            DespawnOne(pool, a);

            pool.ExecuteMaintenance(Time.time + 1f, false);

            Assert.AreEqual(1, pool.TotalCount, "idle not elapsed → keep");
        }

        [Test]
        public void ExecuteMaintenance_BurstIdleElapsed_Trims()
        {
            RuntimeGameObjectPool pool = CreatePool(policy: EPoolPolicy.Burst, minIdle: 0, softCapacity: 8, hardCapacity: 16, idleSeconds: 10f);
            GameObject a = SpawnOne(pool);
            DespawnOne(pool, a);

            pool.ExecuteMaintenance(Time.time + 20f, false);

            Assert.AreEqual(0, pool.TotalCount, "idle elapsed past IdleSeconds → trim to retain target 0");
        }

        [Test]
        public void ExecuteMaintenance_StickyPolicy_DoesNotTrimWithoutLowMemory()
        {
            RuntimeGameObjectPool pool = CreatePool(policy: EPoolPolicy.Sticky, minIdle: 0, softCapacity: 4, hardCapacity: 16, idleSeconds: 1f);
            GameObject a = SpawnOne(pool);
            DespawnOne(pool, a);

            pool.ExecuteMaintenance(Time.time + 100f, false);

            Assert.AreEqual(1, pool.TotalCount, "sticky never trims by idle");
        }

        [Test]
        public void ExecuteMaintenance_StickyPolicy_LowMemoryTrims()
        {
            RuntimeGameObjectPool pool = CreatePool(policy: EPoolPolicy.Sticky, minIdle: 0, softCapacity: 4, hardCapacity: 16, idleSeconds: 1f);
            GameObject a = SpawnOne(pool);
            DespawnOne(pool, a);

            pool.ExecuteMaintenance(Time.time, true);

            Assert.AreEqual(0, pool.TotalCount, "sticky trims under low memory");
        }

        [Test]
        public void ExecuteMaintenance_BurstKeepsMinIdle()
        {
            RuntimeGameObjectPool pool = CreatePool(policy: EPoolPolicy.Burst, minIdle: 2, softCapacity: 8, hardCapacity: 16, idleSeconds: 10f);
            GameObject a = SpawnOne(pool);
            GameObject b = SpawnOne(pool);
            GameObject c = SpawnOne(pool);
            DespawnOne(pool, a);
            DespawnOne(pool, b);
            DespawnOne(pool, c);

            pool.ExecuteMaintenance(Time.time + 100f, false);

            Assert.AreEqual(2, pool.TotalCount, "idle trim respects minIdle");
        }

        #endregion

        #region 预制体加载 [PREFAB LOADING]

        [Test]
        public void LoadPrefab_LoadsOnceViaLoader()
        {
            RuntimeGameObjectPool pool = CreatePool();

            GameObject prefab = pool.LoadPrefab();

            Assert.AreSame(_loader.Prefab, prefab);
            Assert.AreEqual(1, _loader.LoadCount);
            pool.LoadPrefab();
            Assert.AreEqual(1, _loader.LoadCount, "second load reuses cached prefab");
        }

        [Test]
        public void SpawnAsync_LoadsPrefabThenSpawns()
        {
            RuntimeGameObjectPool pool = CreatePool();

            // 伪加载器同步完成——整条链路无真实挂起，直接阻塞取结果（NUnit 无法 await UniTask）。
            GameObject instance = pool.SpawnAsync(null, CancellationToken.None).GetAwaiter().GetResult();

            Assert.NotNull(instance);
            Assert.AreEqual(1, _loader.LoadCount);
            Assert.AreEqual(1, pool.ActiveCount);
        }

        [Test]
        public void WarmupAsync_CreatesTargetInstances()
        {
            RuntimeGameObjectPool pool = CreatePool(hardCapacity: 16);

            // 预热量小于单帧批量阈值——无真实 yield，直接阻塞取结果。
            pool.WarmupAsync(5, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(5, pool.InactiveCount);
            Assert.AreEqual(5, pool.TotalCount);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        #endregion

        #region 快照 [SNAPSHOT]

        [Test]
        public void CreateSnapshot_ReportsPoolStatistics()
        {
            RuntimeGameObjectPool pool = CreatePool();
            GameObject a = SpawnOne(pool);
            GameObject b = SpawnOne(pool);
            DespawnOne(pool, a);

            GameObjectPoolSnapshot snapshot = pool.CreateSnapshot(false);
            try
            {
                Assert.AreEqual("Assets/Test/Fake", snapshot.location);
                Assert.AreEqual(2, snapshot.totalCount);
                Assert.AreEqual(1, snapshot.activeCount);
                Assert.AreEqual(1, snapshot.inactiveCount);
                Assert.IsTrue(snapshot.prefabLoaded);
                Assert.AreEqual(2, snapshot.spawnCount);
                Assert.AreEqual(1, snapshot.despawnCount);
                Assert.AreEqual(0, snapshot.hitCount, "both spawns were misses (new instances)");
                Assert.AreEqual(2, snapshot.missCount);
                Assert.AreEqual(2, snapshot.expandCount);
                Assert.AreEqual(2, snapshot.peakActive);
            }
            finally
            {
                MemoryPool.Release(snapshot);
            }
        }

        #endregion

        #region 策略规划器 [POLICY PLANNER]

        [Test]
        public void Planner_FixedPolicy_RetainClampedToSoftCapacity()
        {
            PoolCompiledRule rule = new PoolCompiledRule(
                0, "E", "G", "P", EPoolPolicy.Fixed, minIdle: 5, softCapacity: 3, hardCapacity: 8,
                idleSeconds: 0f, unloadPrefab: true, 0, default);

            PoolRecyclePlan plan = PoolPolicyPlanner.Plan(in rule, totalCount: 10, lowMemory: false);

            Assert.AreEqual(3, plan.RetainTarget);
        }

        [Test]
        public void Planner_StickyPolicy_RetainsAll()
        {
            PoolCompiledRule rule = new PoolCompiledRule(
                0, "E", "G", "P", EPoolPolicy.Sticky, minIdle: 1, softCapacity: 3, hardCapacity: 8,
                idleSeconds: 0f, unloadPrefab: true, 0, default);

            PoolRecyclePlan plan = PoolPolicyPlanner.Plan(in rule, totalCount: 10, lowMemory: false);

            Assert.AreEqual(10, plan.RetainTarget);
            Assert.IsFalse(plan.UnloadPrefab, "sticky policy never unloads prefab without low memory");
        }

        [Test]
        public void Planner_LowMemory_ForceTrimAndUnload()
        {
            PoolCompiledRule rule = new PoolCompiledRule(
                0, "E", "G", "P", EPoolPolicy.Sticky, minIdle: 1, softCapacity: 3, hardCapacity: 8,
                idleSeconds: 0f, unloadPrefab: true, 0, default);

            PoolRecyclePlan plan = PoolPolicyPlanner.Plan(in rule, totalCount: 10, lowMemory: true);

            Assert.AreEqual(1, plan.RetainTarget, "low memory trims to minIdle");
            Assert.IsTrue(plan.ForceTrim);
            Assert.IsTrue(plan.UnloadPrefab);
        }

        [Test]
        public void Planner_BurstPolicy_RetainClampedAndUnload()
        {
            PoolCompiledRule rule = new PoolCompiledRule(
                0, "E", "G", "P", EPoolPolicy.Burst, minIdle: 5, softCapacity: 3, hardCapacity: 8,
                idleSeconds: 10f, unloadPrefab: true, 0, default);

            PoolRecyclePlan plan = PoolPolicyPlanner.Plan(in rule, totalCount: 10, lowMemory: false);

            Assert.AreEqual(3, plan.RetainTarget);
            Assert.IsTrue(plan.UnloadPrefab);
        }

        #endregion
    }
}
