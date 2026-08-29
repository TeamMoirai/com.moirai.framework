using System;
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

        [SetUp]
        public void SetUp()
        {
            MemoryPool.ClearAll();
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

            var second = MemoryPool.Acquire<TestMemory>();

            Assert.AreSame(first, second);
        }

        [Test]
        public void Release_Null_Noop()
        {
            Assert.DoesNotThrow(() => MemoryPool.Release((MemoryObject)null));
        }

        [Test]
        public void Count_TracksRegisteredTypes()
        {
            MemoryPool.Acquire<TestMemory>();
            int countAfterFirst = MemoryPool.Count;

            MemoryPool.Acquire<OtherMemory>();
            int countAfterSecond = MemoryPool.Count;

            Assert.AreEqual(1, countAfterFirst);
            Assert.AreEqual(2, countAfterSecond);
        }

        [Test]
        public void ClearAll_RemovesAllCollections()
        {
            MemoryPool.Acquire<TestMemory>();
            MemoryPool.Acquire<OtherMemory>();

            MemoryPool.ClearAll();

            Assert.AreEqual(0, MemoryPool.Count);
        }

        [Test]
        public void Add_PreAllocatesObjects()
        {
            MemoryPool.Add<TestMemory>(3);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            var infos = GetInfos();
            Assert.AreEqual(1, infos.Length);
            Assert.GreaterOrEqual(infos[0].UnusedCount, 3);
        }

        [Test]
        public void Remove_RemovesPreAllocatedObjects()
        {
            MemoryPool.Add<TestMemory>(5);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);
            MemoryPool.Remove<TestMemory>(3);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount + 1);

            var infos = GetInfos();
            Assert.LessOrEqual(infos[0].UnusedCount, 2);
        }

        [Test]
        public void Remove_MoreThanAvailable_ClampsToAvailable()
        {
            MemoryPool.Add<TestMemory>(2);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);
            MemoryPool.Remove<TestMemory>(10);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount + 1);

            var infos = GetInfos();
            Assert.AreEqual(0, infos[0].UnusedCount);
        }

        [Test]
        public void RemoveAll_ClearsAllFromType()
        {
            MemoryPool.Add<TestMemory>(5);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            MemoryPool.RemoveAll<TestMemory>();

            var infos = GetInfos();
            Assert.AreEqual(0, infos[0].UnusedCount);
        }

        [Test]
        public void GetAllMemoryPoolInfos_ReturnsCorrectStats()
        {
            var a = MemoryPool.Acquire<TestMemory>();
            var b = MemoryPool.Acquire<TestMemory>();
            MemoryPool.Release(a);
            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            var infos = GetInfos();

            Assert.AreEqual(1, infos.Length);
            Assert.AreEqual(typeof(TestMemory), infos[0].Type);
            Assert.AreEqual(2, infos[0].AcquireCount);
            Assert.AreEqual(1, infos[0].ReleaseCount);
            Assert.AreEqual(1, infos[0].UsingCount);
        }

        [Test]
        public void Acquire_ByType_ReturnsCorrectInstance()
        {
            MemoryObject obj = MemoryPool.Acquire(typeof(TestMemory));

            Assert.IsNotNull(obj);
            Assert.IsInstanceOf<TestMemory>(obj);
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
