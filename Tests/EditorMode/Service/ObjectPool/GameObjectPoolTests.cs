using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.ObjectPool;
using NUnit.Framework;
using UnityEngine;
using Mp = Moirai.Atropos.MemoryPool;

namespace Service.GameObjectPool
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
        private PooledInstanceRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _scheduler = new PoolMaintenanceScheduler();
            _loader = new FakePrefabLoader();
            _root = new GameObject("PoolRoot").transform;
            _registry = new PooledInstanceRegistry(16);
        }

        [TearDown]
        public void TearDown()
        {
            if (_pool != null)
            {
                _pool.Shutdown();
                _pool = null;
            }

            _registry?.Dispose();
            _registry = null;

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
            _pool.Initialize(_scheduler, rule, "Assets/Test/Fake", _loader, _root, _registry);
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
            Assert.IsTrue(_registry.TryResolve(instance, out RuntimeGameObjectPool owner, out int slotIndex));
            Assert.AreSame(pool, owner);
            Assert.AreEqual(PoolReleaseResult.Released, pool.ReleaseByInstance(slotIndex, instance));
        }

        #endregion

        #region Spawn / Despawn 往返 [SPAWN ROUND TRIP]

        [Test]
        public void Spawn_CreatesInstanceAndRegisters()
        {
            RuntimeGameObjectPool pool = CreatePool();

            GameObject instance = SpawnOne(pool);

            Assert.NotNull(instance);
            Assert.IsTrue(_registry.TryResolve(instance, out _, out _));
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
        public void TryRelease_Twice_SecondFails()
        {
            RuntimeGameObjectPool pool = CreatePool();
            GameObject instance = SpawnOne(pool);
            Assert.IsTrue(_registry.TryResolve(instance, out _, out int slotIndex));

            Assert.AreEqual(PoolReleaseResult.Released, pool.ReleaseByInstance(slotIndex, instance));
            Assert.AreEqual(PoolReleaseResult.NotActive, pool.ReleaseByInstance(slotIndex, instance));
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
                Mp.Release(snapshot);
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

        #region 外部 Prefab [EXTERNAL PREFAB]

        [Test]
        public void Spawn_ExternalPrefab_WithoutLoader_CreatesInstance()
        {
            GameObject prefab = new GameObject("ExtPrefab");
            try
            {
                PoolCompiledRule rule = DefaultPoolRules.CreateExternalRule("Prefab:ExtPrefab:1");
                _pool = new RuntimeGameObjectPool();
                _pool.InitializeWithPrefab(_scheduler, rule, "Prefab:ExtPrefab:1", prefab, _root, _registry);

                GameObject instance = _pool.Spawn(null);

                Assert.NotNull(instance);
                Assert.AreNotSame(prefab, instance, "must clone the external prefab");
                Assert.IsTrue(_registry.TryResolve(instance, out _, out _));
                Assert.AreEqual(1, _pool.TotalCount);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void Spawn_ExternalPrefab_AfterDespawn_ReusesInstance()
        {
            GameObject prefab = new GameObject("ExtPrefab");
            try
            {
                PoolCompiledRule rule = DefaultPoolRules.CreateExternalRule("Prefab:ExtPrefab:2");
                _pool = new RuntimeGameObjectPool();
                _pool.InitializeWithPrefab(_scheduler, rule, "Prefab:ExtPrefab:2", prefab, _root, _registry);

                GameObject first = _pool.Spawn(null);
                Assert.IsTrue(_registry.TryResolve(first, out _, out int slotIndex));
                Assert.AreEqual(PoolReleaseResult.Released, _pool.ReleaseByInstance(slotIndex, first));

                GameObject second = _pool.Spawn(null);
                Assert.AreSame(first, second);
                Assert.AreEqual(1, _pool.TotalCount);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void UserData_SurvivesDespawnReuse()
        {
            GameObject prefab = new GameObject("ExtPrefab");
            try
            {
                PoolCompiledRule rule = DefaultPoolRules.CreateExternalRule("Prefab:ExtPrefab:3");
                _pool = new RuntimeGameObjectPool();
                _pool.InitializeWithPrefab(_scheduler, rule, "Prefab:ExtPrefab:3", prefab, _root, _registry);

                GameObject first = _pool.Spawn(null);
                Assert.IsTrue(_registry.TryResolve(first, out _, out int slotIndex));
                _pool.SetUserData(slotIndex, "cached");
                Assert.AreEqual(PoolReleaseResult.Released, _pool.ReleaseByInstance(slotIndex, first));

                GameObject second = _pool.Spawn(null);
                Assert.AreSame(first, second);
                Assert.IsTrue(_registry.TryResolve(second, out _, out int slot2));
                Assert.AreEqual("cached", _pool.GetUserData<string>(slot2));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        #endregion

        #region 租期代系与 Despawn 语义 [LEASE GENERATION & DESPAWN]

        [Test]
        public void Generation_Recycle_InvalidatesOldLease()
        {
            RuntimeGameObjectPool pool = CreatePool();
            GameObject first = SpawnOne(pool);
            Assert.IsTrue(_registry.TryResolve(first, out _, out int slotIndex));
            Assert.IsTrue(pool.TryBindLease(slotIndex, out uint gen1, out _, out _));

            Assert.AreEqual(PoolReleaseResult.Released, pool.ReleaseByInstance(slotIndex, first));
            GameObject second = SpawnOne(pool);
            Assert.AreSame(first, second);
            Assert.IsTrue(_registry.TryResolve(second, out _, out int slot2));
            Assert.IsTrue(pool.TryBindLease(slot2, out uint gen2, out _, out _));
            Assert.AreNotEqual(gen1, gen2, "lease generation must bump on re-activate");
            Assert.IsFalse(pool.TryRelease(slotIndex, gen1), "stale lease must fail");
            Assert.IsTrue(pool.IsAlive(slot2, gen2));
        }

        [Test]
        public void Lease_IsValid_FalseAfterDespawn()
        {
            RuntimeGameObjectPool pool = CreatePool();
            GameObject instance = SpawnOne(pool);
            Assert.IsTrue(_registry.TryResolve(instance, out _, out int slotIndex));
            Assert.IsTrue(pool.TryBindLease(slotIndex, out uint generation, out GameObject bound, out Transform tr));
            Assert.IsTrue(pool.IsAlive(slotIndex, generation));

            Assert.AreEqual(PoolReleaseResult.Released, pool.ReleaseByInstance(slotIndex, instance));
            Assert.IsFalse(pool.IsAlive(slotIndex, generation), "lease identity dies with Active state");
        }

        [Test]
        public void Wrap_InactiveInstance_ReturnsNull()
        {
            RuntimeGameObjectPool pool = CreatePool();
            GameObject instance = SpawnOne(pool);
            DespawnOne(pool, instance);

            Assert.IsTrue(_registry.TryResolve(instance, out _, out int slotIndex));
            Assert.IsFalse(pool.TryBindLease(slotIndex, out _, out _, out _), "inactive must not bind lease");
        }

        [Test]
        public void Despawn_Twice_DoesNotDestroyInactiveInstance()
        {
            DefaultGameObjectPoolHandler handler = new DefaultGameObjectPoolHandler();
            GameObject root = null;
            try
            {
                handler.Internal_Init();
                GameObject prefab = new GameObject("DupDespawnPrefab");
                GameObject instance = handler.Spawn(prefab, null);
                Assert.NotNull(instance);

                handler.Despawn(instance);
                Assert.IsNotNull(instance, "first despawn parks instance");

                handler.Despawn(instance);
                Assert.IsNotNull(instance, "duplicate despawn must not destroy inactive instance");
            }
            finally
            {
                handler.Internal_Shutdown();
            }
        }

        [Test]
        public void Despawn_ForeignInstance_Destroys()
        {
            DefaultGameObjectPoolHandler handler = new DefaultGameObjectPoolHandler();
            try
            {
                handler.Internal_Init();
                GameObject foreign = new GameObject("ForeignInstance");
                handler.Despawn(foreign);
                Assert.IsTrue(foreign == null, "foreign instance must be destroyed");
            }
            finally
            {
                handler.Internal_Shutdown();
            }
        }

        [Test]
        public void Dispose_ActiveInstanceExternallyDestroyed_DoesNotThrow()
        {
            RuntimeGameObjectPool pool = CreatePool();
            GameObject instance = SpawnOne(pool);
            Assert.IsTrue(_registry.TryResolve(instance, out _, out int slotIndex));
            Assert.IsTrue(pool.TryBindLease(slotIndex, out uint generation, out _, out _));

            Object.DestroyImmediate(instance);

            Assert.DoesNotThrow(() => pool.TryRelease(slotIndex, generation));
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [Test]
        public void Sticky_LazySweep_ReclaimsDestroyedSlot()
        {
            RuntimeGameObjectPool pool = CreatePool(policy: EPoolPolicy.Sticky);
            GameObject first = SpawnOne(pool);
            Object.DestroyImmediate(first);

            GameObject second = pool.Spawn(null);

            Assert.NotNull(second);
            Assert.AreEqual(1, pool.TotalCount, "destroyed slot must be lazily reclaimed");
        }

        [Test]
        public void ExternalDestroy_UnregistersRegistryEntry()
        {
            RuntimeGameObjectPool pool = CreatePool();
            GameObject instance = SpawnOne(pool);
            Assert.IsTrue(_registry.TryResolve(instance, out _, out int slotIndex));

            Object.DestroyImmediate(instance);
            pool.ExecuteMaintenance(Time.time, true);

            Assert.IsFalse(_registry.TryResolve(instance, out _, out _), "destroyed instance must leave registry");
            Assert.AreEqual(0, pool.TotalCount);
        }

        [Test]
        public void HardCap_ZombieSelfHeal_RetrySucceeds()
        {
            RuntimeGameObjectPool pool = CreatePool(softCapacity: 1, hardCapacity: 1);
            GameObject first = SpawnOne(pool);
            Object.DestroyImmediate(first);

            // 活跃实例外部销毁占住硬顶：Spawn 撞顶时先 sweep 自愈再重试分配。
            GameObject second = pool.Spawn(null);

            Assert.IsNotNull(second, "hardcap must self-heal via zombie sweep");
            Assert.AreEqual(1, pool.TotalCount);
            Assert.AreNotSame(first, second);
        }

        [Test]
        public void Sticky_AllActive_ZombieSweepGetsScheduled()
        {
            RuntimeGameObjectPool pool = CreatePool(policy: EPoolPolicy.Sticky, hardCapacity: 4);
            GameObject instance = SpawnOne(pool);
            Assert.AreEqual(1, pool.TotalCount);

            // 全活跃 + Sticky 无自发到期维护：兜底清扫必须已排期，而非永挂 MaxValue。
            Object.DestroyImmediate(instance);
            Assert.Less(pool.NextMaintenanceAt, float.MaxValue, "zombie sweep must be scheduled");

            _scheduler.ProcessDue(pool.NextMaintenanceAt);
            Assert.AreEqual(0, pool.TotalCount, "zombie slot must be reclaimed by fallback sweep");
        }

        [Test]
        public void Catalog_PrefabPrefixPattern_MatchesSyntheticKey()
        {
            PoolEntry entry = new PoolEntry
            {
                entryName = "外部预制体定制",
                pattern = "Prefab:Fake*",
                softCapacity = 32,
                hardCapacity = 128
            };
            entry.Normalize();

            PoolCompiledCatalog catalog = PoolCompiledCatalog.Build(new[] { entry });
            try
            {
                GameObject prefab = new GameObject("FakePrefab_Catalog");
                try
                {
                    int ruleIndex = catalog.Resolve(DefaultPoolRules.GetPrefabPoolLocation(prefab));
                    Assert.GreaterOrEqual(ruleIndex, 0, "Prefab: pattern must match synthetic pool key");
                    Assert.AreEqual(128, catalog.GetRule(ruleIndex).HardCapacity);
                }
                finally
                {
                    Object.DestroyImmediate(prefab);
                }
            }
            finally
            {
                catalog.Dispose();
            }
        }

        [Test]
        public void PooledComponent_AccessAfterDespawn_IsNullSafe()
        {
            // Component 属性在 Cache 为空时返回 null，不 NRE。
            Pooled<MeshFilter> lease = new Pooled<MeshFilter>();
            Assert.IsNull(lease.Component);
            lease.Dispose();
        }

        #endregion

        private sealed class UserDataProbe
        {
        }

        #region Source 与姿态 [SOURCE & POSE]

        [Test]
        public void Source_ImplicitConversion_Roundtrip()
        {
            GameObjectPoolSource fromLocation = "Assets/X";
            Assert.IsTrue(fromLocation.IsValid);
            Assert.IsFalse(fromLocation.IsPrefab);
            Assert.AreEqual("Assets/X", fromLocation.Location);

            GameObject prefab = new GameObject("SrcPrefab");
            try
            {
                GameObjectPoolSource fromPrefab = prefab;
                Assert.IsTrue(fromPrefab.IsValid);
                Assert.IsTrue(fromPrefab.IsPrefab);
                Assert.AreSame(prefab, fromPrefab.Prefab);

                Assert.IsFalse(default(GameObjectPoolSource).IsValid);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ExternalSpawn_ResetsPrefabLocalPose()
        {
            GameObject prefab = new GameObject("PosePrefab");
            try
            {
                prefab.transform.localPosition = new Vector3(1f, 2f, 3f);
                prefab.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
                prefab.transform.localScale = new Vector3(2f, 2f, 2f);

                PoolCompiledRule rule = DefaultPoolRules.CreateExternalRule("Prefab:PosePrefab:9");
                _pool = new RuntimeGameObjectPool();
                _pool.InitializeWithPrefab(_scheduler, rule, "Prefab:PosePrefab:9", prefab, _root, _registry);

                GameObject first = _pool.Spawn(null);
                first.transform.localPosition = Vector3.one * 99f;
                Assert.IsTrue(_registry.TryResolve(first, out _, out int slotIndex));
                Assert.AreEqual(PoolReleaseResult.Released, _pool.ReleaseByInstance(slotIndex, first));

                GameObject second = _pool.Spawn(null);
                Assert.AreSame(first, second);
                Assert.AreEqual(prefab.transform.localPosition, second.transform.localPosition);
                Assert.AreEqual(prefab.transform.localRotation.eulerAngles.x, second.transform.localRotation.eulerAngles.x, 0.1f);
                Assert.AreEqual(prefab.transform.localScale, second.transform.localScale);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void UserData_AlienOccupancy_DoesNotOverwrite()
        {
            GameObject prefab = new GameObject("UserDataAlien");
            try
            {
                PoolCompiledRule rule = DefaultPoolRules.CreateExternalRule("Prefab:UserDataAlien:1");
                _pool = new RuntimeGameObjectPool();
                _pool.InitializeWithPrefab(_scheduler, rule, "Prefab:UserDataAlien:1", prefab, _root, _registry);

                GameObject instance = _pool.Spawn(null);
                Assert.IsTrue(_registry.TryResolve(instance, out _, out int slotIndex));
                _pool.SetUserData(slotIndex, "alien");

                Assert.IsNull(_pool.GetOrAddUserData<UserDataProbe>(slotIndex));
                Assert.AreEqual("alien", _pool.GetUserData<string>(slotIndex));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        #endregion

        #region 零 GC 热路径 [ZERO GC HOT PATH]

        [Test]
        public void HotPath_ZeroGcAlloc_WarmPoolRoundtrip()
        {
            RuntimeGameObjectPool pool = CreatePool(hardCapacity: 32);
            pool.LoadPrefab();
            pool.WarmupAsync(8, CancellationToken.None).GetAwaiter().GetResult();

            // 预热 JIT / 池扩容
            for (int i = 0; i < 32; i++)
            {
                GameObject warm = pool.Spawn(null);
                Assert.IsTrue(_registry.TryResolve(warm, out _, out int warmSlot));
                pool.ReleaseByInstance(warmSlot, warm);
            }

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                GameObject instance = pool.Spawn(null);
                if (_registry.TryResolve(instance, out _, out int slotIndex))
                {
                    pool.ReleaseByInstance(slotIndex, instance);
                }
            }

            long after = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.LessOrEqual(after - before, 0L, "warm spawn/despawn roundtrip must be zero GC");
        }

        #endregion
    }
}
