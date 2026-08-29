using Moirai.Atropos.ObjectPool;
using NUnit.Framework;

namespace ObjectPoolTests
{
    /// <summary>
    /// 分页槽位存储回归测试：分配/释放/有效性/容量增长。
    /// </summary>
    public sealed class PoolSlotStorageTests
    {
        #region 结构体 [STRUCTS]

        private struct TestSlot
        {
            public int Value;
        }

        #endregion

        #region 分配与释放 [ALLOC & FREE]

        [Test]
        public void Initialize_SlotCountIsZero()
        {
            PoolSlotStorage<TestSlot> storage = default;
            storage.Initialize();

            Assert.AreEqual(0, storage.SlotCount);
            Assert.IsFalse(storage.IsValidIndex(0));
        }

        [Test]
        public void AllocSlot_FirstPage_CreatesOneHundredTwentyEightSlots()
        {
            PoolSlotStorage<TestSlot> storage = default;
            storage.Initialize();

            int index = storage.AllocSlot();

            Assert.AreEqual(128, storage.SlotCount);
            Assert.IsTrue(storage.IsValidIndex(index));
            Assert.AreEqual(0, index);
        }

        [Test]
        public void AllocSlot_SequentialWithinPage_FirstPageOffsetsAscend()
        {
            PoolSlotStorage<TestSlot> storage = default;
            storage.Initialize();

            int first = storage.AllocSlot();
            int second = storage.AllocSlot();

            Assert.AreEqual(0, first);
            Assert.AreEqual(1, second);
            Assert.AreEqual(128, storage.SlotCount);
        }

        [Test]
        public void AllocSlot_OverPageSize_CreatesSecondPage()
        {
            PoolSlotStorage<TestSlot> storage = default;
            storage.Initialize();

            int lastOfFirstPage = -1;
            for (int i = 0; i < 128; i++)
            {
                lastOfFirstPage = storage.AllocSlot();
            }

            Assert.AreEqual(127, lastOfFirstPage);
            Assert.AreEqual(128, storage.SlotCount);

            int firstOfSecondPage = storage.AllocSlot();

            Assert.AreEqual(128, firstOfSecondPage);
            Assert.AreEqual(256, storage.SlotCount);
            Assert.IsTrue(storage.IsValidIndex(128));
            Assert.IsFalse(storage.IsValidIndex(256));
        }

        [Test]
        public void FreeSlot_ThenAllocSlot_ReusesIndex()
        {
            PoolSlotStorage<TestSlot> storage = default;
            storage.Initialize();

            int a = storage.AllocSlot();
            int b = storage.AllocSlot();
            storage.FreeSlot(b);

            int reused = storage.AllocSlot();

            Assert.AreEqual(b, reused);
            Assert.AreEqual(a, a);
        }

        [Test]
        public void GetSlotRef_WritesThrough()
        {
            PoolSlotStorage<TestSlot> storage = default;
            storage.Initialize();

            int index = storage.AllocSlot();
            ref TestSlot slot = ref storage.GetSlotRef(index);
            slot.Value = 42;

            Assert.AreEqual(42, storage.GetSlotRef(index).Value);
        }

        [Test]
        public void IsValidIndex_NegativeAndBeyondPages_False()
        {
            PoolSlotStorage<TestSlot> storage = default;
            storage.Initialize();

            Assert.IsFalse(storage.IsValidIndex(-1));
            Assert.IsFalse(storage.IsValidIndex(0));
            storage.AllocSlot();
            Assert.IsTrue(storage.IsValidIndex(0));
            Assert.IsFalse(storage.IsValidIndex(128));
        }

        [Test]
        public void FreeSlot_WholePage_ReuseDoesNotAllocateNewPage()
        {
            PoolSlotStorage<TestSlot> storage = default;
            storage.Initialize();

            int[] indices = new int[128];
            for (int i = 0; i < 128; i++)
            {
                indices[i] = storage.AllocSlot();
            }

            for (int i = 0; i < 128; i++)
            {
                storage.FreeSlot(indices[i]);
            }

            int reused = storage.AllocSlot();

            Assert.AreEqual(128, storage.SlotCount);
            Assert.IsTrue(reused >= 0 && reused < 128);
        }

        [Test]
        public void ReturnStorage_ClearsCapacity()
        {
            PoolSlotStorage<TestSlot> storage = default;
            storage.Initialize();
            storage.AllocSlot();
            storage.AllocSlot();

            storage.ReturnStorage();

            Assert.AreEqual(0, storage.SlotCount);
            Assert.IsFalse(storage.IsValidIndex(0));
        }

        [Test]
        public void ReturnStorage_ThenReinitialize_Works()
        {
            PoolSlotStorage<TestSlot> storage = default;
            storage.Initialize();
            storage.AllocSlot();
            storage.ReturnStorage();

            storage.Initialize();
            int index = storage.AllocSlot();

            Assert.AreEqual(0, index);
            Assert.AreEqual(128, storage.SlotCount);
        }

        #endregion
    }
}
