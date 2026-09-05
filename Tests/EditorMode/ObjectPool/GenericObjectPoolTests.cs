using Moirai.Atropos.ObjectPool;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ObjectPoolTests
{
    /// <summary>
    /// 通用对象池回归测试：注册/取用/归还、引用计数复用、锁定保护、容量裁剪、过期释放、销毁池。
    /// </summary>
    public sealed class GenericObjectPoolTests
    {
        #region 测试桩 [TEST FAKES]

        private sealed class TestObject : ObjectBase
        {
            public int SpawnCount;
            public int DespawnCount;
            public bool Released;
            public bool ReleaseWasShutdown;

            public TestObject()
            {
            }

            public TestObject(object target, string name = "")
            {
                Initialize(name, target);
            }

            protected internal override void OnSpawn()
            {
                SpawnCount++;
            }

            protected internal override void OnDespawn()
            {
                DespawnCount++;
            }

            protected internal override void Release(bool isShutdown)
            {
                Released = true;
                ReleaseWasShutdown = isShutdown;
            }

            public override void Clear()
            {
                // 测试桩：保留统计标志便于断言（不入 MemoryPool 循环）。
            }
        }

        private sealed class BlockedObject : ObjectBase
        {
            public bool Released;

            public BlockedObject()
            {
            }

            public BlockedObject(object target)
            {
                Initialize(target);
            }

            public override bool CustomCanReleaseFlag => false;

            protected internal override void Release(bool isShutdown)
            {
                Released = true;
            }

            public override void Clear()
            {
            }
        }

        #endregion

        #region 基础设施 [INFRASTRUCTURE]

        private DefaultObjectPoolHandler CreateHandler()
        {
            DefaultObjectPoolHandler handler = new DefaultObjectPoolHandler();
            handler.Internal_Init();
            return handler;
        }

        private static ObjectPoolBase GetPoolBase(DefaultObjectPoolHandler handler)
        {
            ObjectPoolBase[] results = new ObjectPoolBase[8];
            handler.GetAllObjectPools(false, results);
            return results[0];
        }

        #endregion

        #region 池管理 [POOL MANAGEMENT]

        [Test]
        public void GetOrCreatePool_SameKeyTwice_ReturnsSameInstance()
        {
            DefaultObjectPoolHandler handler = CreateHandler();

            IObjectPool<TestObject> a = handler.GetOrCreatePool<TestObject>(default);
            IObjectPool<TestObject> b = handler.GetOrCreatePool<TestObject>(default);

            Assert.AreSame(a, b);
            Assert.AreEqual(1, handler.Count);
        }

        [Test]
        public void GetOrCreatePool_DifferentNames_DistinctInstances()
        {
            DefaultObjectPoolHandler handler = CreateHandler();

            IObjectPool<TestObject> a = handler.GetOrCreatePool<TestObject>(new ObjectPoolCreateOptions("A"));
            IObjectPool<TestObject> b = handler.GetOrCreatePool<TestObject>(new ObjectPoolCreateOptions("B"));

            Assert.AreNotSame(a, b);
            Assert.AreEqual(2, handler.Count);
        }

        [Test]
        public void HasObjectPool_AfterCreate_True()
        {
            DefaultObjectPoolHandler handler = CreateHandler();

            Assert.IsFalse(handler.HasObjectPool<TestObject>(""));
            handler.GetOrCreatePool<TestObject>(default);
            Assert.IsTrue(handler.HasObjectPool<TestObject>(""));
            Assert.IsFalse(handler.HasObjectPool<TestObject>("other"));
        }

        [Test]
        public void DestroyObjectPool_ReleasesAllObjectsWithShutdownFlag()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject obj = new TestObject(new object());
            pool.Register(obj, false);

            bool destroyed = handler.DestroyObjectPool<TestObject>("");

            Assert.IsTrue(destroyed);
            Assert.IsTrue(obj.Released);
            Assert.IsTrue(obj.ReleaseWasShutdown);
            Assert.AreEqual(0, handler.Count);
            Assert.IsFalse(handler.HasObjectPool<TestObject>(""));
        }

        [Test]
        public void DestroyObjectPool_Missing_ReturnsFalse()
        {
            DefaultObjectPoolHandler handler = CreateHandler();

            Assert.IsFalse(handler.DestroyObjectPool<TestObject>(""));
        }

        #endregion

        #region 注册与取用 [REGISTER & SPAWN]

        [Test]
        public void Register_NotSpawned_ThenSpawnReturnsIt()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject obj = new TestObject(new object());

            bool registered = pool.Register(obj, false);

            Assert.IsTrue(registered);
            TestObject spawned = pool.Spawn();
            Assert.AreSame(obj, spawned);
            Assert.AreEqual(1, spawned.SpawnCount);
            Assert.AreEqual(1, pool.Count);
        }

        [Test]
        public void Register_SpawnedImmediately_InvokesSpawnCallback()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject obj = new TestObject(new object());

            bool registered = pool.Register(obj, true);

            Assert.IsTrue(registered);
            Assert.AreEqual(1, obj.SpawnCount);
        }

        [Test]
        public void Register_NullTarget_Fails()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject obj = new TestObject(new object());
            typeof(ObjectBase)
                .GetField("_target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(obj, null);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*"));

            bool registered = pool.Register(obj, false);

            Assert.IsFalse(registered);
        }

        [Test]
        public void Register_SameTargetTwice_FailsSecondTime()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            object target = new object();
            TestObject first = new TestObject(target);
            TestObject second = new TestObject(target);
            Assert.IsTrue(pool.Register(first, false));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*"));

            Assert.IsFalse(pool.Register(second, false));
        }

        [Test]
        public void Spawn_ByName_ReturnsMatchingAvailable()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject named = new TestObject(new object(), "weapon");
            TestObject unnamed = new TestObject(new object());
            pool.Register(named, false);
            pool.Register(unnamed, false);

            TestObject spawned = pool.Spawn("weapon");

            Assert.AreSame(named, spawned);
        }

        [Test]
        public void Spawn_NoAvailable_ReturnsNull()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);

            Assert.IsNull(pool.Spawn());
        }

        [Test]
        public void Despawn_ThenAvailableAgain()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject obj = new TestObject(new object());
            pool.Register(obj, true);

            pool.Despawn(obj);

            Assert.AreEqual(1, obj.DespawnCount);
            TestObject respawned = pool.Spawn();
            Assert.AreSame(obj, respawned);
        }

        [Test]
        public void DespawnTarget_ByTargetReference()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            object target = new object();
            TestObject obj = new TestObject(target);
            pool.Register(obj, true);

            pool.DespawnTarget(target);

            Assert.AreEqual(1, obj.DespawnCount);
        }

        #endregion

        #region 引用计数复用 [MULTI SPAWN]

        [Test]
        public void MultiSpawn_ReferenceCount_OnlyReleasesWhenFullyDespawned()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(
                new ObjectPoolCreateOptions(allowMultiSpawn: true));
            TestObject obj = new TestObject(new object());
            pool.Register(obj, false);

            TestObject first = pool.Spawn();
            TestObject second = pool.Spawn();

            Assert.AreSame(obj, first);
            Assert.AreSame(obj, second);
            Assert.AreEqual(2, obj.SpawnCount);

            pool.Despawn(obj);
            pool.ReleaseAllUnused();
            Assert.IsFalse(obj.Released, "spawnCount 1 remaining keeps object in use — not releasable");

            pool.Despawn(obj);
            pool.ReleaseAllUnused();
            Assert.IsTrue(obj.Released, "fully unspawned object is releasable");
        }

        [Test]
        public void MultiSpawn_SpawnWhileSpawned_ReturnsSameObject()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(
                new ObjectPoolCreateOptions(allowMultiSpawn: true));
            TestObject obj = new TestObject(new object());
            pool.Register(obj, false);

            TestObject first = pool.Spawn();
            TestObject second = pool.Spawn();

            Assert.AreSame(first, second);
            Assert.AreEqual(2, obj.SpawnCount);
            Assert.AreEqual(1, pool.Count);
        }

        [Test]
        public void MultiSpawn_ByName_SearchesAllNotJustAvailable()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(
                new ObjectPoolCreateOptions(allowMultiSpawn: true));
            TestObject obj = new TestObject(new object(), "fx");
            pool.Register(obj, false);
            pool.Spawn("fx");

            TestObject again = pool.Spawn("fx");

            Assert.AreSame(obj, again);
        }

        [Test]
        public void SingleSpawn_ByName_OnlyReturnsAvailable()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject obj = new TestObject(new object(), "fx");
            pool.Register(obj, false);
            pool.Spawn("fx");

            TestObject again = pool.Spawn("fx");

            Assert.IsNull(again);
        }

        #endregion

        #region 释放策略 [RELEASE POLICIES]

        [Test]
        public void ReleaseAllUnused_ReleasesUnusedButKeepsLockedAndInUse()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject unused = new TestObject(new object());
            TestObject locked = new TestObject(new object());
            TestObject inUse = new TestObject(new object());
            pool.Register(unused, false);
            pool.Register(locked, false);
            pool.Register(inUse, true);
            locked.Locked = true;

            pool.ReleaseAllUnused();

            Assert.IsTrue(unused.Released, "plain unused object should be released");
            Assert.IsFalse(locked.Released, "locked object must survive ReleaseAllUnused");
            Assert.IsFalse(inUse.Released, "in-use object must survive ReleaseAllUnused");
            Assert.AreEqual(2, pool.Count);
        }

        [Test]
        public void ReleaseAllUnused_CustomCanReleaseFlagFalse_BlocksRelease()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<BlockedObject> pool = handler.GetOrCreatePool<BlockedObject>(default);
            BlockedObject blocked = new BlockedObject(new object());
            pool.Register(blocked, false);

            pool.ReleaseAllUnused();

            Assert.IsFalse(blocked.Released);
        }

        [Test]
        public void Release_Count_ReleasesOldestUnusedFirst()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject first = new TestObject(new object());
            TestObject second = new TestObject(new object());
            TestObject third = new TestObject(new object());
            pool.Register(first, false);
            pool.Register(second, false);
            pool.Register(third, false);

            pool.Release(1);

            Assert.IsTrue(first.Released, "FIFO: oldest unused released first");
            Assert.IsFalse(second.Released);
            Assert.IsFalse(third.Released);
        }

        [Test]
        public void Release_LockedObjectsSkipped()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject lockedFirst = new TestObject(new object());
            TestObject plain = new TestObject(new object());
            pool.Register(lockedFirst, false);
            pool.Register(plain, false);
            lockedFirst.Locked = true;

            pool.Release(1);

            Assert.IsFalse(lockedFirst.Released);
            Assert.IsTrue(plain.Released);
        }

        #endregion

        #region 容量裁剪 [CAPACITY TRIM]

        [Test]
        public void Register_AtCapacity_ReleasesOneUnusedToMakeRoom()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(
                new ObjectPoolCreateOptions(capacity: 2));
            TestObject a = new TestObject(new object());
            TestObject b = new TestObject(new object());
            TestObject c = new TestObject(new object());
            pool.Register(a, false);
            pool.Register(b, false);

            bool registered = pool.Register(c, false);

            Assert.IsTrue(registered);
            Assert.IsTrue(a.Released, "oldest unused released to honor capacity");
            Assert.IsFalse(b.Released);
            Assert.IsFalse(c.Released);
            Assert.AreEqual(2, pool.Count);
        }

        [Test]
        public void CapacitySetter_Lowered_MarksExcessForRelease()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject a = new TestObject(new object());
            TestObject b = new TestObject(new object());
            pool.Register(a, false);
            pool.Register(b, false);

            pool.Capacity = 1;

            // 超容标记 → 维护唤醒释放；直接驱动 ExecuteMaintenance 模拟到期唤醒。
            ObjectPoolBase poolBase = GetPoolBase(handler);
            poolBase.ExecuteMaintenance(UnityEngine.Time.realtimeSinceStartup, false);

            Assert.AreEqual(1, pool.Count);
            Assert.IsTrue(a.Released);
            Assert.IsFalse(b.Released);
        }

        #endregion

        #region 过期释放 [EXPIRE]

        [Test]
        public void ExecuteMaintenance_PastExpiry_ReleasesExpiredUnused()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(
                new ObjectPoolCreateOptions(expireTime: 10f));
            TestObject a = new TestObject(new object());
            pool.Register(a, false);

            ObjectPoolBase poolBase = GetPoolBase(handler);
            float now = UnityEngine.Time.realtimeSinceStartup;
            poolBase.ExecuteMaintenance(now + 20f, false);

            Assert.IsTrue(a.Released, "object idle past ExpireTime should be released");
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void ExecuteMaintenance_BeforeExpiry_KeepsUnused()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(
                new ObjectPoolCreateOptions(expireTime: 600f));
            TestObject a = new TestObject(new object());
            pool.Register(a, false);

            ObjectPoolBase poolBase = GetPoolBase(handler);
            poolBase.ExecuteMaintenance(UnityEngine.Time.realtimeSinceStartup + 1f, false);

            Assert.IsFalse(a.Released);
            Assert.AreEqual(1, pool.Count);
        }

        [Test]
        public void ExecuteMaintenance_ExpiredButLocked_Keeps()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(
                new ObjectPoolCreateOptions(expireTime: 10f));
            TestObject a = new TestObject(new object());
            pool.Register(a, false);
            a.Locked = true;

            ObjectPoolBase poolBase = GetPoolBase(handler);
            poolBase.ExecuteMaintenance(UnityEngine.Time.realtimeSinceStartup + 20f, false);

            Assert.IsFalse(a.Released);
            Assert.AreEqual(1, pool.Count);
        }

        [Test]
        public void ExecuteMaintenance_LowMemory_ReleasesAllUnused()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject a = new TestObject(new object());
            TestObject locked = new TestObject(new object());
            pool.Register(a, false);
            pool.Register(locked, false);
            locked.Locked = true;

            ObjectPoolBase poolBase = GetPoolBase(handler);
            poolBase.ExecuteMaintenance(UnityEngine.Time.realtimeSinceStartup, true);

            Assert.IsTrue(a.Released);
            Assert.IsFalse(locked.Released);
            Assert.AreEqual(1, pool.Count);
        }

        #endregion

        #region 调试信息 [DEBUG INFO]

        [Test]
        public void GetAllObjectInfos_ReportsSpawnCounts()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            IObjectPool<TestObject> pool = handler.GetOrCreatePool<TestObject>(default);
            TestObject a = new TestObject(new object(), "alpha");
            TestObject b = new TestObject(new object(), "beta");
            pool.Register(a, false);
            pool.Register(b, true);

            ObjectPoolBase poolBase = GetPoolBase(handler);
            ObjectInfo[] infos = new ObjectInfo[8];
            int count = poolBase.GetAllObjectInfos(infos);

            Assert.AreEqual(2, count);
            bool foundSpawned = false;
            bool foundAvailable = false;
            for (int i = 0; i < count; i++)
            {
                if (infos[i].Name == "alpha" && infos[i].SpawnCount == 0)
                {
                    foundAvailable = true;
                }

                if (infos[i].Name == "beta" && infos[i].SpawnCount == 1)
                {
                    foundSpawned = true;
                }
            }

            Assert.IsTrue(foundAvailable);
            Assert.IsTrue(foundSpawned);
        }

        [Test]
        public void GetAllObjectPools_SortByPriority_Ascending()
        {
            DefaultObjectPoolHandler handler = CreateHandler();
            handler.GetOrCreatePool<TestObject>(new ObjectPoolCreateOptions("A", priority: 1));
            handler.GetOrCreatePool<TestObject>(new ObjectPoolCreateOptions("B", priority: 9));

            ObjectPoolBase[] results = new ObjectPoolBase[8];
            int count = handler.GetAllObjectPools(true, results);

            Assert.AreEqual(2, count);
            Assert.AreEqual("A", results[0].Name);
            Assert.AreEqual("B", results[1].Name);
        }

        #endregion
    }
}
