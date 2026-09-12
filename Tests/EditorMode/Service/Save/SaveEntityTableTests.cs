using System.Collections.Generic;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// 实体表 KVT 读写测试：生成记录/销毁列表往返、可空字段、空表、未知记录容错。
    /// </summary>
    public class SaveEntityTableTests
    {
        [Test]
        public void WriteRead_RoundTripsSpawnsAndDestroyed()
        {
            var spawns = new List<SaveSpawnRecord>
            {
                new SaveSpawnRecord("id-1", "enemy", "Main", null),
                new SaveSpawnRecord("id-2", "chest", "Dungeon[1]", "id-1"),
            };
            var destroyed = new List<string> { "preset-1", "preset-2" };

            byte[] bytes = SaveEntityTable.Write(spawns, destroyed);
            SaveEntityTable.Read(bytes, out List<SaveSpawnRecord> readSpawns, out List<string> readDestroyed);

            Assert.AreEqual(2, readSpawns.Count);
            Assert.AreEqual("id-1", readSpawns[0].EntityId);
            Assert.AreEqual("enemy", readSpawns[0].PrefabKey);
            Assert.AreEqual("Main", readSpawns[0].SceneName);
            Assert.IsNull(readSpawns[0].ParentId, "null 父级应往返为 null");
            Assert.AreEqual("id-2", readSpawns[1].EntityId);
            Assert.AreEqual("Dungeon[1]", readSpawns[1].SceneName);
            Assert.AreEqual("id-1", readSpawns[1].ParentId);
            CollectionAssert.AreEqual(new[] { "preset-1", "preset-2" }, readDestroyed);
        }

        [Test]
        public void Read_EmptyBytes_ReturnsEmptyTables()
        {
            SaveEntityTable.Read(null, out List<SaveSpawnRecord> spawns, out List<string> destroyed);
            Assert.IsEmpty(spawns);
            Assert.IsEmpty(destroyed);

            SaveEntityTable.Read(new byte[0], out spawns, out destroyed);
            Assert.IsEmpty(spawns);
            Assert.IsEmpty(destroyed);
        }

        [Test]
        public void WriteRead_EmptyTables_RoundTrips()
        {
            byte[] bytes = SaveEntityTable.Write(new List<SaveSpawnRecord>(), new List<string>());
            SaveEntityTable.Read(bytes, out List<SaveSpawnRecord> spawns, out List<string> destroyed);
            Assert.IsEmpty(spawns);
            Assert.IsEmpty(destroyed);
        }

        [Test]
        public void Read_UnknownRecords_Skipped()
        {
            // 未来版本字段/记录容错：额外标量记录与未知序列均跳过
            var writer = new SaveKeyValueWriter(128);
            writer.WriteInt32("future_version", 7);
            writer.BeginSequence("spawns", 0);
            writer.EndNested();
            writer.BeginSequence("unknown_seq", 1);
            writer.WriteInt32Element(1);
            writer.EndNested();

            SaveEntityTable.Read(writer.ToArray(), out List<SaveSpawnRecord> spawns, out List<string> destroyed);
            Assert.IsEmpty(spawns);
            Assert.IsEmpty(destroyed);
        }
    }
}
