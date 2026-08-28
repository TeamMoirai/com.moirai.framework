using System;
using Moirai.Atropos.Resource;
using NUnit.Framework;

namespace Resource
{
    /// <summary>
    /// 测试用结构体键。
    /// </summary>
    internal readonly struct IntMapKey : IEquatable<IntMapKey>
    {
        public readonly int Value;

        public IntMapKey(int value)
        {
            Value = value;
        }

        public bool Equals(IntMapKey other)
        {
            return Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value;
        }
    }

    /// <summary>
    /// 资源索引映射内核测试：开放寻址哈希的插入、覆盖、删除与槽位复用语义。
    /// </summary>
    public sealed class ResourceIndexMapTests
    {
        [Test]
        public void EmptyMap_CountIsZero_TryGetValueFails_RemoveFails()
        {
            var map = new ResourceIndexMap<IntMapKey, int>();

            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue(new IntMapKey(1), out _));
            Assert.IsFalse(map.Remove(new IntMapKey(1)));
        }

        [Test]
        public void Set_NewEntry_RoundTripsValue()
        {
            var map = new ResourceIndexMap<IntMapKey, int>();

            map.Set(new IntMapKey(10), 100);

            Assert.AreEqual(1, map.Count);
            Assert.IsTrue(map.TryGetValue(new IntMapKey(10), out int value));
            Assert.AreEqual(100, value);
        }

        [Test]
        public void Set_SameKey_OverwritesWithoutGrowth()
        {
            var map = new ResourceIndexMap<IntMapKey, int>();

            map.Set(new IntMapKey(10), 100);
            map.Set(new IntMapKey(10), 200);

            Assert.AreEqual(1, map.Count);
            Assert.IsTrue(map.TryGetValue(new IntMapKey(10), out int value));
            Assert.AreEqual(200, value);
        }

        [Test]
        public void Remove_ExistingKey_TrueOnceThenFalse()
        {
            var map = new ResourceIndexMap<IntMapKey, int>();
            map.Set(new IntMapKey(20), 400);

            Assert.IsTrue(map.Remove(new IntMapKey(20)));
            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue(new IntMapKey(20), out _));
            Assert.IsFalse(map.Remove(new IntMapKey(20)));
        }

        [Test]
        public void Set_AfterRemove_ReusesFreeSlotAndRestoresCount()
        {
            var map = new ResourceIndexMap<IntMapKey, int>();
            map.Set(new IntMapKey(30), 300);
            map.Set(new IntMapKey(31), 310);
            Assert.AreEqual(2, map.Count);

            Assert.IsTrue(map.Remove(new IntMapKey(30)));
            Assert.AreEqual(1, map.Count);

            map.Set(new IntMapKey(32), 320);

            Assert.AreEqual(2, map.Count);
            Assert.IsTrue(map.TryGetValue(new IntMapKey(31), out int keptValue));
            Assert.AreEqual(310, keptValue);
            Assert.IsTrue(map.TryGetValue(new IntMapKey(32), out int reusedValue));
            Assert.AreEqual(320, reusedValue);
        }

        [Test]
        public void Clear_ResetsAllEntries()
        {
            var map = new ResourceIndexMap<IntMapKey, int>();
            for (int i = 0; i < 8; i++)
            {
                map.Set(new IntMapKey(i), i * 10);
            }

            map.Clear();

            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue(new IntMapKey(3), out _));

            map.Set(new IntMapKey(99), 990);
            Assert.IsTrue(map.TryGetValue(new IntMapKey(99), out int value));
            Assert.AreEqual(990, value);
        }

        [Test]
        public void Grow_BeyondDefaultCapacity_PreservesAllEntries()
        {
            var map = new ResourceIndexMap<IntMapKey, long>();
            const int count = 1000;

            for (int i = 0; i < count; i++)
            {
                map.Set(new IntMapKey(i), i * 1000L);
            }

            Assert.AreEqual(count, map.Count);
            Assert.IsTrue(map.TryGetValue(new IntMapKey(0), out long head));
            Assert.AreEqual(0L, head);
            Assert.IsTrue(map.TryGetValue(new IntMapKey(count - 1), out long tail));
            Assert.AreEqual((count - 1) * 1000L, tail);
            Assert.IsTrue(map.TryGetValue(new IntMapKey(count / 2), out long middle));
            Assert.AreEqual((count / 2) * 1000L, middle);
        }

        [Test]
        public void Remove_MiddleOfChain_KeepsTailReachable()
        {
            var map = new ResourceIndexMap<IntMapKey, int>();
            const int seedBase = 500000;

            for (int i = 0; i < 64; i++)
            {
                map.Set(new IntMapKey(seedBase + i * 256), i);
            }

            int removedFirstHalf = 0;
            for (int i = 0; i < 32; i++)
            {
                if (map.Remove(new IntMapKey(seedBase + i * 256)))
                {
                    removedFirstHalf++;
                }
            }

            Assert.AreEqual(32, removedFirstHalf);
            Assert.AreEqual(32, map.Count);

            for (int i = 32; i < 64; i++)
            {
                Assert.IsTrue(map.TryGetValue(new IntMapKey(seedBase + i * 256), out int value), "survivor {0} lost", i);
                Assert.AreEqual(i, value);
            }
        }
    }
}
