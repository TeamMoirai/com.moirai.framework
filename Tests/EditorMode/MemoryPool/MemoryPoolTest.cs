using System;
using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;
using Mp = Moirai.Atropos.MemoryPool;

namespace Service.MemoryPool
{
    public class MemoryPoolTest
    {
        private class TestMemory : MemoryObject
        {
            public int Value { get; set; }
            public bool WasCleared { get; private set; }

            public override void Clear()
            {
                Value = 0;
                WasCleared = true;
            }
        }

        private class OtherMemory : MemoryObject
        {
            public override void Clear() { }
        }

        // 仅 Count 用例独占：保证测试运行时该类型必然未注册（增量断言的前提）
        private class CountMemoryA : MemoryObject
        {
            public override void Clear() { }
        }

        private class CountMemoryB : MemoryObject
        {
            public override void Clear() { }
        }

        private MemoryPoolInfo[] _infoBuffer = Array.Empty<MemoryPoolInfo>();

        private MemoryPoolInfo[] GetInfos()
        {
            int count = Mp.Count;
            if (_infoBuffer.Length < count)
            {
                _infoBuffer = new MemoryPoolInfo[count];
            }

            int actual = Mp.GetAllMemoryPoolInfos(_infoBuffer);
            MemoryPoolInfo[] result = new MemoryPoolInfo[actual];
            Array.Copy(_infoBuffer, result, actual);
            return result;
        }

        private MemoryPoolInfo GetInfo(Type targetType)
        {
            var infos = GetInfos();
            for (int i = 0; i < infos.Length; i++)
            {
                if (infos[i].Type == targetType)
                {
                    return infos[i];
                }
            }

            return default;
        }

        private bool IsRegistered(Type targetType)
        {
            var infos = GetInfos();
            for (int i = 0; i < infos.Length; i++)
            {
                if (infos[i].Type == targetType)
                {
                    return true;
                }
            }

            return false;
        }

        [SetUp]
        public void SetUp()
        {
            Mp.ClearAll();
            Mp.ResetAllStats();
        }

        [TearDown]
        public void TearDown()
        {
            Mp.ClearAll();
        }

        [Test]
        public void Acquire_ReturnsNewInstance()
        {
            var obj = Mp.Acquire<TestMemory>();

            Assert.IsNotNull(obj);
            Assert.IsInstanceOf<TestMemory>(obj);

            Mp.Release(obj);
        }

        [Test]
        public void Release_CallsClearOnObject()
        {
            var obj = Mp.Acquire<TestMemory>();
            obj.Value = 42;

            Mp.Release(obj);

            Assert.IsTrue(obj.WasCleared);
            Assert.AreEqual(0, obj.Value);
        }

        [Test]
        public void Acquire_AfterRelease_ReusesObject()
        {
            var first = Mp.Acquire<TestMemory>();
            Mp.Release(first);

            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            // Tick 会按 miss 水位补充空闲对象（新对象压在空闲链表头部），复用契约是
            // 「已释放对象仍在池中并被再次发放」，而非严格同实例。
            int unused = GetInfo(typeof(TestMemory)).UnusedCount;
            var acquired = new List<TestMemory>(unused);
            try
            {
                for (int i = 0; i < unused; i++)
                {
                    acquired.Add(Mp.Acquire<TestMemory>());
                }

                Assert.Contains(first, acquired);
            }
            finally
            {
                for (int i = 0; i < acquired.Count; i++)
                {
                    Mp.Release(acquired[i]);
                }
            }
        }

        [Test]
        public void Release_Null_Noop()
        {
            Assert.DoesNotThrow(() => Mp.Release((MemoryObject)null));
        }

        [Test]
        public void Count_TracksRegisteredTypes()
        {
            // 注册表为全域类型缓存（ClearAll 不注销条目）——用独占类型 + 增量断言规避跨 fixture 污染；
            // 同域重复运行时类型已注册（增量为 0），用 registeredBefore 归一。
            bool aRegistered = IsRegistered(typeof(CountMemoryA));
            bool bRegistered = IsRegistered(typeof(CountMemoryB));
            int baseline = Mp.Count;

            var first = Mp.Acquire<CountMemoryA>();
            try
            {
                Assert.AreEqual(baseline + (aRegistered ? 0 : 1), Mp.Count);

                var second = Mp.Acquire<CountMemoryB>();
                try
                {
                    Assert.AreEqual(baseline + (aRegistered ? 0 : 1) + (bRegistered ? 0 : 1), Mp.Count);
                }
                finally
                {
                    Mp.Release(second);
                }
            }
            finally
            {
                Mp.Release(first);
            }
        }

        [Test]
        public void ClearAll_RemovesAllCollections()
        {
            var a = Mp.Acquire<TestMemory>();
            var b = Mp.Acquire<OtherMemory>();
            Mp.Release(a);
            Mp.Release(b);

            int countBefore = Mp.Count;
            Mp.ClearAll();

            // ClearAll 清空所有池内容；类型注册表是域级缓存，条目保留。
            Assert.AreEqual(countBefore, Mp.Count);
            Assert.AreEqual(0, GetInfo(typeof(TestMemory)).UnusedCount);
            Assert.AreEqual(0, GetInfo(typeof(TestMemory)).UsingCount);
            Assert.AreEqual(0, GetInfo(typeof(OtherMemory)).UnusedCount);
            Assert.AreEqual(0, GetInfo(typeof(OtherMemory)).UsingCount);
        }

        [Test]
        public void Add_PreAllocatesObjects()
        {
            Mp.Add<TestMemory>(3);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            MemoryPoolInfo info = GetInfo(typeof(TestMemory));
            Assert.GreaterOrEqual(info.UnusedCount, 3);
        }

        [Test]
        public void Remove_RemovesPreAllocatedObjects()
        {
            Mp.Add<TestMemory>(5);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);
            int unusedBefore = GetInfo(typeof(TestMemory)).UnusedCount;

            Mp.Remove<TestMemory>(3);

            // Remove 即时收缩空闲量；活跃池随后会按水位目标回补，移除非永久语义（RemoveAll 才持久）。
            Assert.AreEqual(Math.Max(0, unusedBefore - 3), GetInfo(typeof(TestMemory)).UnusedCount);
        }

        [Test]
        public void Remove_MoreThanAvailable_ClampsToAvailable()
        {
            Mp.Add<TestMemory>(2);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            Mp.Remove<TestMemory>(10);

            Assert.AreEqual(0, GetInfo(typeof(TestMemory)).UnusedCount);
        }

        [Test]
        public void RemoveAll_ClearsAllFromType()
        {
            Mp.Add<TestMemory>(5);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            Mp.RemoveAll<TestMemory>();

            // RemoveAll 走 ClearAll 并反注册 Tick 调度，清空对后续 Tick 持久。
            Assert.AreEqual(0, GetInfo(typeof(TestMemory)).UnusedCount);
        }

        [Test]
        public void GetAllMemoryPoolInfos_ReturnsCorrectStats()
        {
            var a = Mp.Acquire<TestMemory>();
            var b = Mp.Acquire<TestMemory>();
            try
            {
                Mp.Release(a);
                MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

                MemoryPoolInfo info = GetInfo(typeof(TestMemory));

                Assert.AreEqual(typeof(TestMemory), info.Type);
                Assert.AreEqual(2, info.AcquireCount);
                Assert.AreEqual(1, info.ReleaseCount);
                Assert.AreEqual(1, info.UsingCount);
            }
            finally
            {
                Mp.Release(b);
            }
        }

        [Test]
        public void Acquire_ByType_ReturnsCorrectInstance()
        {
            MemoryObject obj = Mp.Acquire(typeof(TestMemory));

            Assert.IsNotNull(obj);
            Assert.IsInstanceOf<TestMemory>(obj);

            Mp.Release(obj);
        }

        [Test]
        public void DoubleRelease_ThrowsException()
        {
            var obj = Mp.Acquire<TestMemory>();
            Mp.Release(obj);

            Assert.Throws<InvalidOperationException>(() => Mp.Release(obj));
        }

        [Test]
        public void Acquire_InvalidType_ThrowsException()
        {
            Assert.Throws<InvalidOperationException>(() => Mp.Acquire(typeof(string)));
        }

        [Test]
        public void Acquire_AbstractType_ThrowsException()
        {
            Assert.Throws<InvalidOperationException>(() => Mp.Acquire(typeof(IDisposable)));
        }

        [Test]
        public void Acquire_NullType_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => Mp.Acquire(null));
        }
    }
}
