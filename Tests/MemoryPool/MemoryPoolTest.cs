using System;
using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;

namespace GameTool
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
            int count = MemoryPool.Count;
            if (_infoBuffer.Length < count)
            {
                _infoBuffer = new MemoryPoolInfo[count];
            }

            int actual = MemoryPool.GetAllMemoryPoolInfos(_infoBuffer);
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
            MemoryPool.ClearAll();
            MemoryPool.ResetAllStats();
        }

        [TearDown]
        public void TearDown()
        {
            MemoryPool.ClearAll();
        }

        [Test]
        public void Acquire_ReturnsNewInstance()
        {
            var obj = MemoryPool.Acquire<TestMemory>();

            Assert.IsNotNull(obj);
            Assert.IsInstanceOf<TestMemory>(obj);

            MemoryPool.Release(obj);
        }

        [Test]
        public void Release_CallsClearOnObject()
        {
            var obj = MemoryPool.Acquire<TestMemory>();
            obj.Value = 42;

            MemoryPool.Release(obj);

            Assert.IsTrue(obj.WasCleared);
            Assert.AreEqual(0, obj.Value);
        }

        [Test]
        public void Acquire_AfterRelease_ReusesObject()
        {
            var first = MemoryPool.Acquire<TestMemory>();
            MemoryPool.Release(first);

            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            // Tick 会按 miss 水位补充空闲对象（新对象压在空闲链表头部），复用契约是
            // 「已释放对象仍在池中并被再次发放」，而非严格同实例。
            int unused = GetInfo(typeof(TestMemory)).UnusedCount;
            var acquired = new List<TestMemory>(unused);
            try
            {
                for (int i = 0; i < unused; i++)
                {
                    acquired.Add(MemoryPool.Acquire<TestMemory>());
                }

                Assert.Contains(first, acquired);
            }
            finally
            {
                for (int i = 0; i < acquired.Count; i++)
                {
                    MemoryPool.Release(acquired[i]);
                }
            }
        }

        [Test]
        public void Release_Null_Noop()
        {
            Assert.DoesNotThrow(() => MemoryPool.Release((MemoryObject)null));
        }

        [Test]
        public void Count_TracksRegisteredTypes()
        {
            // 注册表为全域类型缓存（ClearAll 不注销条目）——用独占类型 + 增量断言规避跨 fixture 污染；
            // 同域重复运行时类型已注册（增量为 0），用 registeredBefore 归一。
            bool aRegistered = IsRegistered(typeof(CountMemoryA));
            bool bRegistered = IsRegistered(typeof(CountMemoryB));
            int baseline = MemoryPool.Count;

            var first = MemoryPool.Acquire<CountMemoryA>();
            try
            {
                Assert.AreEqual(baseline + (aRegistered ? 0 : 1), MemoryPool.Count);

                var second = MemoryPool.Acquire<CountMemoryB>();
                try
                {
                    Assert.AreEqual(baseline + (aRegistered ? 0 : 1) + (bRegistered ? 0 : 1), MemoryPool.Count);
                }
                finally
                {
                    MemoryPool.Release(second);
                }
            }
            finally
            {
                MemoryPool.Release(first);
            }
        }

        [Test]
        public void ClearAll_RemovesAllCollections()
        {
            var a = MemoryPool.Acquire<TestMemory>();
            var b = MemoryPool.Acquire<OtherMemory>();
            MemoryPool.Release(a);
            MemoryPool.Release(b);

            int countBefore = MemoryPool.Count;
            MemoryPool.ClearAll();

            // ClearAll 清空所有池内容；类型注册表是域级缓存，条目保留。
            Assert.AreEqual(countBefore, MemoryPool.Count);
            Assert.AreEqual(0, GetInfo(typeof(TestMemory)).UnusedCount);
            Assert.AreEqual(0, GetInfo(typeof(TestMemory)).UsingCount);
            Assert.AreEqual(0, GetInfo(typeof(OtherMemory)).UnusedCount);
            Assert.AreEqual(0, GetInfo(typeof(OtherMemory)).UsingCount);
        }

        [Test]
        public void Add_PreAllocatesObjects()
        {
            MemoryPool.Add<TestMemory>(3);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            MemoryPoolInfo info = GetInfo(typeof(TestMemory));
            Assert.GreaterOrEqual(info.UnusedCount, 3);
        }

        [Test]
        public void Remove_RemovesPreAllocatedObjects()
        {
            MemoryPool.Add<TestMemory>(5);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);
            int unusedBefore = GetInfo(typeof(TestMemory)).UnusedCount;

            MemoryPool.Remove<TestMemory>(3);

            // Remove 即时收缩空闲量；活跃池随后会按水位目标回补，移除非永久语义（RemoveAll 才持久）。
            Assert.AreEqual(Math.Max(0, unusedBefore - 3), GetInfo(typeof(TestMemory)).UnusedCount);
        }

        [Test]
        public void Remove_MoreThanAvailable_ClampsToAvailable()
        {
            MemoryPool.Add<TestMemory>(2);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            MemoryPool.Remove<TestMemory>(10);

            Assert.AreEqual(0, GetInfo(typeof(TestMemory)).UnusedCount);
        }

        [Test]
        public void RemoveAll_ClearsAllFromType()
        {
            MemoryPool.Add<TestMemory>(5);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            MemoryPool.RemoveAll<TestMemory>();

            // RemoveAll 走 ClearAll 并反注册 Tick 调度，清空对后续 Tick 持久。
            Assert.AreEqual(0, GetInfo(typeof(TestMemory)).UnusedCount);
        }

        [Test]
        public void GetAllMemoryPoolInfos_ReturnsCorrectStats()
        {
            var a = MemoryPool.Acquire<TestMemory>();
            var b = MemoryPool.Acquire<TestMemory>();
            try
            {
                MemoryPool.Release(a);
                MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

                MemoryPoolInfo info = GetInfo(typeof(TestMemory));

                Assert.AreEqual(typeof(TestMemory), info.Type);
                Assert.AreEqual(2, info.AcquireCount);
                Assert.AreEqual(1, info.ReleaseCount);
                Assert.AreEqual(1, info.UsingCount);
            }
            finally
            {
                MemoryPool.Release(b);
            }
        }

        [Test]
        public void Acquire_ByType_ReturnsCorrectInstance()
        {
            MemoryObject obj = MemoryPool.Acquire(typeof(TestMemory));

            Assert.IsNotNull(obj);
            Assert.IsInstanceOf<TestMemory>(obj);

            MemoryPool.Release(obj);
        }

        [Test]
        public void DoubleRelease_ThrowsException()
        {
            var obj = MemoryPool.Acquire<TestMemory>();
            MemoryPool.Release(obj);

            Assert.Throws<InvalidOperationException>(() => MemoryPool.Release(obj));
        }

        [Test]
        public void Acquire_InvalidType_ThrowsException()
        {
            Assert.Throws<InvalidOperationException>(() => MemoryPool.Acquire(typeof(string)));
        }

        [Test]
        public void Acquire_AbstractType_ThrowsException()
        {
            Assert.Throws<InvalidOperationException>(() => MemoryPool.Acquire(typeof(IDisposable)));
        }

        [Test]
        public void Acquire_NullType_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => MemoryPool.Acquire(null));
        }
    }
}
