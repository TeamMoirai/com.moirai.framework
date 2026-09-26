using Moirai.Atropos;
using NUnit.Framework;

namespace DataStructure
{
    /// <summary>
    /// 验证 <see cref="GameDictionary{TKey, TValue}"/> 的添加、索引读写、按下标访问键值、包含性检查、移除与按插入序遍历等行为。
    /// </summary>
    public class GameDictionaryTests
    {
        [Test]
        public void NewDictionary_CountIsZero()
        {
            var dict = new GameDictionary<string, int>();
            Assert.AreEqual(0, dict.Count);
        }

        [Test]
        public void Add_IncreasesCount()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("a", 1);
            dict.Add("b", 2);

            Assert.AreEqual(2, dict.Count);
        }

        [Test]
        public void Indexer_Get_ReturnsCorrectValue()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("key", 42);

            Assert.AreEqual(42, dict["key"]);
        }

        [Test]
        public void Indexer_Set_ExistingKey_UpdatesValue()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("key", 1);
            dict["key"] = 99;

            Assert.AreEqual(99, dict["key"]);
            Assert.AreEqual(1, dict.Count);
        }

        [Test]
        public void Indexer_Set_NewKey_AddsEntry()
        {
            var dict = new GameDictionary<string, int>();
            dict["newKey"] = 10;

            Assert.AreEqual(10, dict["newKey"]);
            Assert.AreEqual(1, dict.Count);
        }

        [Test]
        public void GetValueByIndex_ReturnsCorrectValue()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("a", 10);
            dict.Add("b", 20);

            Assert.AreEqual(10, dict.GetValueByIndex(0));
            Assert.AreEqual(20, dict.GetValueByIndex(1));
        }

        [Test]
        public void SetValue_UpdatesByIndex()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("a", 10);
            dict.SetValue(0, 99);

            Assert.AreEqual(99, dict["a"]);
        }

        [Test]
        public void GetKey_ReturnsCorrectKey()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("first", 1);
            dict.Add("second", 2);

            Assert.AreEqual("first", dict.GetKey(0));
            Assert.AreEqual("second", dict.GetKey(1));
        }

        [Test]
        public void ContainsKey_ExistingKey_ReturnsTrue()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("hello", 1);

            Assert.IsTrue(dict.ContainsKey("hello"));
        }

        [Test]
        public void ContainsKey_MissingKey_ReturnsFalse()
        {
            var dict = new GameDictionary<string, int>();
            Assert.IsFalse(dict.ContainsKey("missing"));
        }

        [Test]
        public void TryGetValue_ExistingKey_ReturnsTrueAndValue()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("key", 42);

            bool found = dict.TryGetValue("key", out int val);

            Assert.IsTrue(found);
            Assert.AreEqual(42, val);
        }

        [Test]
        public void TryGetValue_MissingKey_ReturnsFalse()
        {
            var dict = new GameDictionary<string, int>();

            bool found = dict.TryGetValue("missing", out int val);

            Assert.IsFalse(found);
        }

        [Test]
        public void Remove_ExistingKey_ReturnsTrue()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("a", 1);
            dict.Add("b", 2);

            bool removed = dict.Remove("a");

            Assert.IsTrue(removed);
            Assert.AreEqual(1, dict.Count);
            Assert.IsFalse(dict.ContainsKey("a"));
        }

        [Test]
        public void Remove_NonExistentKey_ReturnsFalse()
        {
            var dict = new GameDictionary<string, int>();
            Assert.IsFalse(dict.Remove("missing"));
        }

        [Test]
        public void Clear_RemovesAllEntries()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("a", 1);
            dict.Add("b", 2);

            dict.Clear();

            Assert.AreEqual(0, dict.Count);
        }

        [Test]
        public void Keys_ReturnsInsertionOrder()
        {
            var dict = new GameDictionary<string, int>();
            dict.Add("c", 3);
            dict.Add("a", 1);
            dict.Add("b", 2);

            Assert.AreEqual("c", dict.Keys[0]);
            Assert.AreEqual("a", dict.Keys[1]);
            Assert.AreEqual("b", dict.Keys[2]);
        }
    }
}
