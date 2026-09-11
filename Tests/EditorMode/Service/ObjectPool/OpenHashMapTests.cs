using Moirai.Atropos.ObjectPool;
using NUnit.Framework;

namespace Service.ObjectPool
{
    /// <summary>
    /// 内核三张开放寻址哈希表回归测试：增删改查/容量增长/清空/释放。
    /// </summary>
    public sealed class OpenHashMapTests
    {
        #region OpenHashMap<TKey> [GENERIC MAP]

        [Test]
        public void GenericMap_Empty_CountZeroAndMiss()
        {
            OpenHashMap<(int, string)> map = new OpenHashMap<(int, string)>(8);

            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue((1, "a"), out int value));
            Assert.AreEqual(-1, value);
            Assert.IsFalse(map.ContainsKey((1, "a")));
        }

        [Test]
        public void GenericMap_AddThenTryGetValue_RoundTrips()
        {
            OpenHashMap<(int, string)> map = new OpenHashMap<(int, string)>(8);
            (int, string) key = (1, "a");

            map.AddOrUpdate(key, 42);

            Assert.IsTrue(map.TryGetValue(key, out int value));
            Assert.AreEqual(42, value);
            Assert.AreEqual(1, map.Count);
        }

        [Test]
        public void GenericMap_UpdateExisting_OverwritesValue()
        {
            OpenHashMap<(int, string)> map = new OpenHashMap<(int, string)>(8);
            (int, string) key = (1, "a");

            map.AddOrUpdate(key, 1);
            map.AddOrUpdate(key, 2);

            Assert.AreEqual(1, map.Count);
            Assert.IsTrue(map.TryGetValue(key, out int value));
            Assert.AreEqual(2, value);
        }

        [Test]
        public void GenericMap_Remove_ThenMissing()
        {
            OpenHashMap<(int, string)> map = new OpenHashMap<(int, string)>(8);
            (int, string) key = (1, "a");
            map.AddOrUpdate(key, 42);

            bool removed = map.Remove(key);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue(key, out _));
        }

        [Test]
        public void GenericMap_RemoveMissingKey_ReturnsFalse()
        {
            OpenHashMap<(int, string)> map = new OpenHashMap<(int, string)>(8);
            map.AddOrUpdate((1, "a"), 1);

            Assert.IsFalse(map.Remove((2, "b")));
            Assert.AreEqual(1, map.Count);
            Assert.IsTrue(map.ContainsKey((1, "a")));
        }

        [Test]
        public void GenericMap_ContainsKey_TrueAfterAdd()
        {
            OpenHashMap<(int, string)> map = new OpenHashMap<(int, string)>(8);
            (int, string) key = (7, "x");

            Assert.IsFalse(map.ContainsKey(key));
            map.AddOrUpdate(key, 3);
            Assert.IsTrue(map.ContainsKey(key));
        }

        [Test]
        public void GenericMap_ManyEntries_GrowsAndKeepsAll()
        {
            OpenHashMap<(int, int)> map = new OpenHashMap<(int, int)>(8);
            const int count = 500;

            for (int i = 0; i < count; i++)
            {
                map.AddOrUpdate((i, i), i * 10);
            }

            Assert.AreEqual(count, map.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(map.TryGetValue((i, i), out int value), "missing key " + i);
                Assert.AreEqual(i * 10, value);
            }
        }

        [Test]
        public void GenericMap_RemoveThenAdd_ReusesSlot()
        {
            OpenHashMap<(int, string)> map = new OpenHashMap<(int, string)>(8);
            map.AddOrUpdate((1, "a"), 1);
            map.Remove((1, "a"));

            map.AddOrUpdate((2, "b"), 2);

            Assert.AreEqual(1, map.Count);
            Assert.IsTrue(map.TryGetValue((2, "b"), out int value));
            Assert.AreEqual(2, value);
        }

        [Test]
        public void GenericMap_Clear_ThenEmptyButReusable()
        {
            OpenHashMap<(int, string)> map = new OpenHashMap<(int, string)>(8);
            map.AddOrUpdate((1, "a"), 7);
            map.AddOrUpdate((2, "b"), 8);

            map.Clear();

            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue((1, "a"), out _));
            map.AddOrUpdate((3, "c"), 9);
            Assert.IsTrue(map.TryGetValue((3, "c"), out int value));
            Assert.AreEqual(9, value);
        }

        [Test]
        public void GenericMap_Dispose_ThenEmpty()
        {
            OpenHashMap<(int, string)> map = new OpenHashMap<(int, string)>(8);
            map.AddOrUpdate((1, "a"), 1);

            map.Dispose();

            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue((1, "a"), out _));
        }

        #endregion

        #region StringOpenHashMap [STRING MAP]

        [Test]
        public void StringMap_Empty_CountZeroAndMiss()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);

            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue("any", out _));
            Assert.IsFalse(map.ContainsKey("any"));
        }

        [Test]
        public void StringMap_NullKey_IsSafeNoOp()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);

            Assert.DoesNotThrow(() => map.AddOrUpdate(null, 1));
            Assert.IsFalse(map.TryGetValue(null, out int value));
            Assert.AreEqual(-1, value);
            Assert.IsFalse(map.ContainsKey(null));
            Assert.IsFalse(map.Remove(null));
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void StringMap_AddThenTryGetValue_RoundTrips()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);

            map.AddOrUpdate("apple", 7);

            Assert.IsTrue(map.TryGetValue("apple", out int value));
            Assert.AreEqual(7, value);
            Assert.AreEqual(1, map.Count);
        }

        [Test]
        public void StringMap_MissingKey_ReturnsFalse()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);

            Assert.IsFalse(map.TryGetValue("missing", out int value));
            Assert.AreEqual(-1, value);
        }

        [Test]
        public void StringMap_OrdinalEquality_DistinctKeys()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);
            map.AddOrUpdate("Apple", 1);
            map.AddOrUpdate("apple", 2);

            Assert.AreEqual(2, map.Count);
            Assert.IsTrue(map.TryGetValue("Apple", out int a));
            Assert.IsTrue(map.TryGetValue("apple", out int b));
            Assert.AreEqual(1, a);
            Assert.AreEqual(2, b);
        }

        [Test]
        public void StringMap_Remove_ThenMissing()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);
            map.AddOrUpdate("apple", 7);

            Assert.IsTrue(map.Remove("apple"));
            Assert.IsFalse(map.ContainsKey("apple"));
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void StringMap_RemoveMissingKey_ReturnsFalse()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);
            map.AddOrUpdate("apple", 7);

            Assert.IsFalse(map.Remove("banana"));
            Assert.AreEqual(1, map.Count);
            Assert.IsTrue(map.ContainsKey("apple"));
        }

        [Test]
        public void StringMap_Clear_ThenEmptyButReusable()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);
            map.AddOrUpdate("apple", 7);
            map.AddOrUpdate("banana", 8);

            map.Clear();

            Assert.AreEqual(0, map.Count);
            map.AddOrUpdate("cherry", 9);
            Assert.IsTrue(map.TryGetValue("cherry", out int value));
            Assert.AreEqual(9, value);
        }

        [Test]
        public void StringMap_ManyEntries_GrowsAndKeepsAll()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);
            const int count = 300;

            for (int i = 0; i < count; i++)
            {
                map.AddOrUpdate("key-" + i, i);
            }

            Assert.AreEqual(count, map.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(map.TryGetValue("key-" + i, out int value), "missing key-" + i);
                Assert.AreEqual(i, value);
            }
        }

        [Test]
        public void StringMap_Dispose_ThenEmpty()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);
            map.AddOrUpdate("apple", 7);

            map.Dispose();

            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue("apple", out _));
        }

        #endregion

        #region ReferenceOpenHashMap [REFERENCE MAP]

        [Test]
        public void ReferenceMap_Empty_CountZeroAndMiss()
        {
            ReferenceOpenHashMap map = new ReferenceOpenHashMap(8);
            object key = new object();

            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue(key, out int value));
            Assert.AreEqual(-1, value);
        }

        [Test]
        public void ReferenceMap_NullKey_IsSafeNoOp()
        {
            ReferenceOpenHashMap map = new ReferenceOpenHashMap(8);

            Assert.DoesNotThrow(() => map.AddOrUpdate(null, 1));
            Assert.IsFalse(map.TryGetValue(null, out int value));
            Assert.AreEqual(-1, value);
            Assert.IsFalse(map.Remove(null));
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void ReferenceMap_ReferenceIdentity_NotValueEquality()
        {
            ReferenceOpenHashMap map = new ReferenceOpenHashMap(8);
            object a = new string("same".ToCharArray());
            object b = new string("same".ToCharArray());
            map.AddOrUpdate(a, 1);

            Assert.IsTrue(map.TryGetValue(a, out int value));
            Assert.AreEqual(1, value);
            Assert.IsFalse(map.TryGetValue(b, out _), "equal-but-distinct references must not match");
        }

        [Test]
        public void ReferenceMap_UpdateExisting_Overwrites()
        {
            ReferenceOpenHashMap map = new ReferenceOpenHashMap(8);
            object key = new object();

            map.AddOrUpdate(key, 1);
            map.AddOrUpdate(key, 2);

            Assert.AreEqual(1, map.Count);
            Assert.IsTrue(map.TryGetValue(key, out int value));
            Assert.AreEqual(2, value);
        }

        [Test]
        public void ReferenceMap_Remove_ThenMissing()
        {
            ReferenceOpenHashMap map = new ReferenceOpenHashMap(8);
            object key = new object();
            map.AddOrUpdate(key, 1);

            Assert.IsTrue(map.Remove(key));
            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue(key, out _));
        }

        [Test]
        public void ReferenceMap_RemoveMissingKey_ReturnsFalse()
        {
            ReferenceOpenHashMap map = new ReferenceOpenHashMap(8);
            object key = new object();
            map.AddOrUpdate(key, 1);

            Assert.IsFalse(map.Remove(new object()));
            Assert.AreEqual(1, map.Count);
            Assert.IsTrue(map.TryGetValue(key, out _));
        }

        [Test]
        public void ReferenceMap_Clear_ThenEmptyButReusable()
        {
            ReferenceOpenHashMap map = new ReferenceOpenHashMap(8);
            object a = new object();
            object b = new object();
            map.AddOrUpdate(a, 7);
            map.AddOrUpdate(b, 8);

            map.Clear();

            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.TryGetValue(a, out _));
            map.AddOrUpdate(a, 9);
            Assert.IsTrue(map.TryGetValue(a, out int value));
            Assert.AreEqual(9, value);
        }

        [Test]
        public void ReferenceMap_ManyEntries_GrowsAndKeepsAll()
        {
            ReferenceOpenHashMap map = new ReferenceOpenHashMap(8);
            const int count = 300;
            object[] keys = new object[count];

            for (int i = 0; i < count; i++)
            {
                keys[i] = new object();
                map.AddOrUpdate(keys[i], i);
            }

            Assert.AreEqual(count, map.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(map.TryGetValue(keys[i], out int value), "missing key " + i);
                Assert.AreEqual(i, value);
            }
        }

        [Test]
        public void ReferenceMap_Dispose_ThenEmpty()
        {
            ReferenceOpenHashMap map = new ReferenceOpenHashMap(8);
            map.AddOrUpdate(new object(), 1);

            map.Dispose();

            Assert.AreEqual(0, map.Count);
        }

        #endregion

        #region Dispose 后写入 [DISPOSED WRITES]

        [Test]
        public void GenericMap_AddOrUpdateAfterDispose_IsSafeNoOp()
        {
            OpenHashMap<(int, string)> map = new OpenHashMap<(int, string)>(8);
            (int, string) key = (1, "a");
            map.AddOrUpdate(key, 1);
            map.Dispose();

            Assert.DoesNotThrow(() => map.AddOrUpdate(key, 2));
            Assert.IsFalse(map.TryGetValue(key, out _));
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void StringMap_AddOrUpdateAfterDispose_IsSafeNoOp()
        {
            StringOpenHashMap map = new StringOpenHashMap(8);
            map.AddOrUpdate("k", 1);
            map.Dispose();

            Assert.DoesNotThrow(() => map.AddOrUpdate("k", 2));
            Assert.IsFalse(map.TryGetValue("k", out _));
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void ReferenceMap_AddOrUpdateAfterDispose_IsSafeNoOp()
        {
            ReferenceOpenHashMap map = new ReferenceOpenHashMap(8);
            object key = new object();
            map.AddOrUpdate(key, 1);
            map.Dispose();

            Assert.DoesNotThrow(() => map.AddOrUpdate(key, 2));
            Assert.IsFalse(map.TryGetValue(key, out _));
            Assert.AreEqual(0, map.Count);
        }

        #endregion
    }
}
