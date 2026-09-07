using System.Collections.Generic;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Save
{
    /// <summary>
    /// <see cref="SaveBlockComposer"/> 不可变块合并纯函数测试：查找、按键插入/替换（保持原位）、删除、源列表不被修改。
    /// </summary>
    public class SaveBlockComposerTests
    {
        private static SaveBlockEntry Entry(string key, int gold)
        {
            return new SaveBlockEntry(key, 1, ESaveBackend.Json, new[] { (byte)gold });
        }

        [Test]
        public void TryFind_HitsExistingKey()
        {
            var source = new List<SaveBlockEntry> { Entry("stats", 1), Entry("inventory", 2) };

            bool found = SaveBlockComposer.TryFind(source, "inventory", out SaveBlockEntry entry);

            Assert.IsTrue(found);
            Assert.AreEqual("inventory", entry.Key);
        }

        [Test]
        public void TryFind_MissesUnknownKey()
        {
            var source = new List<SaveBlockEntry> { Entry("stats", 1) };

            Assert.IsFalse(SaveBlockComposer.TryFind(source, "missing", out SaveBlockEntry entry));
            Assert.AreEqual(default, entry);
        }

        [Test]
        public void TryFind_NullSource_ReturnsFalse()
        {
            Assert.IsFalse(SaveBlockComposer.TryFind(null, "stats", out _));
        }

        [Test]
        public void Upsert_NewKey_AppendsTail()
        {
            var source = new List<SaveBlockEntry> { Entry("stats", 1) };

            List<SaveBlockEntry> merged = SaveBlockComposer.Upsert(source, Entry("inventory", 2));

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual("stats", merged[0].Key);
            Assert.AreEqual("inventory", merged[1].Key);
        }

        [Test]
        public void Upsert_ExistingKey_ReplacesInPlace()
        {
            var source = new List<SaveBlockEntry> { Entry("stats", 1), Entry("inventory", 2), Entry("world", 3) };

            List<SaveBlockEntry> merged = SaveBlockComposer.Upsert(source, Entry("inventory", 9));

            Assert.AreEqual(3, merged.Count, "同键替换不应增加块数");
            Assert.AreEqual("stats", merged[0].Key);
            Assert.AreEqual("inventory", merged[1].Key);
            Assert.AreEqual(9, merged[1].Bytes[0], "同键替换应采用新条目数据");
            Assert.AreEqual("world", merged[2].Key, "替换应保持原插入位置");
        }

        [Test]
        public void Upsert_DoesNotMutateSource()
        {
            var source = new List<SaveBlockEntry> { Entry("stats", 1) };

            SaveBlockComposer.Upsert(source, Entry("inventory", 2));

            Assert.AreEqual(1, source.Count, "源列表不可被修改（不可变合并契约）");
        }

        [Test]
        public void Remove_RemovesTargetKey_KeepsOrder()
        {
            var source = new List<SaveBlockEntry> { Entry("a", 1), Entry("b", 2), Entry("c", 3) };

            List<SaveBlockEntry> remaining = SaveBlockComposer.Remove(source, "b");

            Assert.AreEqual(2, remaining.Count);
            Assert.AreEqual("a", remaining[0].Key);
            Assert.AreEqual("c", remaining[1].Key);
        }

        [Test]
        public void Remove_UnknownKey_ReturnsEquivalentList()
        {
            var source = new List<SaveBlockEntry> { Entry("a", 1) };

            List<SaveBlockEntry> remaining = SaveBlockComposer.Remove(source, "missing");

            Assert.AreEqual(1, remaining.Count);
            Assert.AreEqual("a", remaining[0].Key);
        }
    }
}
