using System;
using System.Collections.Generic;
using System.Text;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// <see cref="SaveFileContainer"/> 手写二进制容器布局测试：多块往返、块序保持、边界截断与版本/魔数分型。
    /// </summary>
    public class SaveFileContainerTests
    {
        [Test]
        public void Write_ThenRead_RoundTrips()
        {
            var blocks = new List<SaveBlockEntry>
            {
                new SaveBlockEntry("stats", 3, ESaveBackend.Json, Encoding.UTF8.GetBytes("{\"Gold\":1}")),
                new SaveBlockEntry("inventory", 1, ESaveBackend.MessagePack, new byte[] { 0x93, 0x01, 0x02, 0x03 }),
                new SaveBlockEntry("unicode-键名⑵", 2, ESaveBackend.KeyValue, new byte[] { 0xAA, 0xBB }),
            };

            byte[] buffer = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(buffer, blocks);

            SaveError error = SaveFileContainer.Read(buffer, out List<SaveBlockEntry> parsed);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(3, parsed.Count);
            Assert.AreEqual("stats", parsed[0].Key);
            Assert.AreEqual(3, parsed[0].DataVersion);
            Assert.AreEqual(ESaveBackend.Json, parsed[0].Backend);
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("{\"Gold\":1}"), parsed[0].Bytes);
            Assert.AreEqual("inventory", parsed[1].Key);
            Assert.AreEqual(ESaveBackend.MessagePack, parsed[1].Backend);
            Assert.AreEqual("unicode-键名⑵", parsed[2].Key, "块键应支持非 ASCII");
            CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, parsed[2].Bytes);
        }

        [Test]
        public void Write_EmptyBlockList_RoundTrips()
        {
            var blocks = new List<SaveBlockEntry>();
            byte[] buffer = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(buffer, blocks);

            SaveError error = SaveFileContainer.Read(buffer, out List<SaveBlockEntry> parsed);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(0, parsed.Count);
        }

        [Test]
        public void Read_WrongMagic_ReturnsInvalidFormat()
        {
            byte[] buffer = new byte[] { 0x00, 0x01, 0x02, 0x03, 0, 0, 0, 0, 0, 0, 0, 0 };
            SaveError error = SaveFileContainer.Read(buffer, out List<SaveBlockEntry> blocks);
            Assert.AreEqual(SaveError.InvalidFormat, error);
            Assert.IsNull(blocks);
        }

        [Test]
        public void Read_TooShort_ReturnsInvalidFormat()
        {
            SaveError error = SaveFileContainer.Read(new byte[8], out _);
            Assert.AreEqual(SaveError.InvalidFormat, error);
        }

        [Test]
        public void Read_UnknownContainerVersion_ReturnsUnsupportedVersion()
        {
            var blocks = new List<SaveBlockEntry>();
            byte[] buffer = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(buffer, blocks);
            buffer[4] = 0x7F; // 容器版本 → 未知值

            SaveError error = SaveFileContainer.Read(buffer, out _);
            Assert.AreEqual(SaveError.UnsupportedVersion, error);
        }

        [Test]
        public void Read_TruncatedBlockBytes_ReturnsCorrupted()
        {
            var blocks = new List<SaveBlockEntry>
            {
                new SaveBlockEntry("stats", 1, ESaveBackend.Json, new byte[] { 0x01, 0x02, 0x03, 0x04 }),
            };
            byte[] buffer = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(buffer, blocks);

            // 逐字节截断：长度仍 ≥ 定长头（12B）时块解析越界判 Corrupted；低于头长判 InvalidFormat
            for (int cut = 1; cut < buffer.Length; cut++)
            {
                byte[] truncated = new byte[buffer.Length - cut];
                Array.Copy(buffer, truncated, truncated.Length);
                SaveError error = SaveFileContainer.Read(truncated, out _);
                SaveError expected = truncated.Length < 12 ? SaveError.InvalidFormat : SaveError.Corrupted;
                Assert.AreEqual(expected, error, $"截断 {cut} 字节应判别为 {expected}");
            }
        }

        [Test]
        public void Read_AbsurdBlockCount_ReturnsCorrupted()
        {
            var blocks = new List<SaveBlockEntry>();
            byte[] buffer = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(buffer, blocks);
            // 块数改写为超出合理性上限的巨大值（小端）
            buffer[8] = 0xFF;
            buffer[9] = 0xFF;
            buffer[10] = 0xFF;
            buffer[11] = 0x7F;

            SaveError error = SaveFileContainer.Read(buffer, out _);
            Assert.AreEqual(SaveError.Corrupted, error);
        }
    }
}
