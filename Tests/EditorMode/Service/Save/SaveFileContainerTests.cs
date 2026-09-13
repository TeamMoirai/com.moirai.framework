using System;
using System.Collections.Generic;
using System.Text;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// <see cref="SaveFileContainer"/> v2 手写二进制容器布局测试：多块往返、逐块 CRC32 部分恢复、
    /// 结构性损坏前缀保留、版本硬切（v1 拒载）与魔数/块数分型。
    /// </summary>
    public class SaveFileContainerTests
    {
        /// <summary>
        /// 构建三个测试块（ASCII / 二进制 / 非 ASCII 键各一）。
        /// </summary>
        private static List<SaveBlockEntry> BuildThreeBlocks()
        {
            return new List<SaveBlockEntry>
            {
                new SaveBlockEntry("stats", 3, ESaveBackend.Json, Encoding.UTF8.GetBytes("{\"Gold\":1}")),
                new SaveBlockEntry("inventory", 1, ESaveBackend.MessagePack, new byte[] { 0x93, 0x01, 0x02, 0x03 }),
                new SaveBlockEntry("unicode-键名⑵", 2, ESaveBackend.KeyValue, new byte[] { 0xAA, 0xBB }),
            };
        }

        /// <summary>
        /// 序列化容器（精确长度缓冲区）。
        /// </summary>
        private static byte[] WriteContainer(List<SaveBlockEntry> blocks)
        {
            byte[] buffer = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(buffer, blocks);
            return buffer;
        }

        [Test]
        public void Write_ThenRead_RoundTrips()
        {
            List<SaveBlockEntry> blocks = BuildThreeBlocks();
            byte[] buffer = WriteContainer(blocks);

            SaveError error = SaveFileContainer.Read(buffer, out List<SaveBlockEntry> parsed, out List<SaveBlockError> blockErrors);
            Assert.AreEqual(SaveError.None, error);
            Assert.IsNull(blockErrors, "健康容器不应产生坏块清单");
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
            byte[] buffer = WriteContainer(new List<SaveBlockEntry>());

            SaveError error = SaveFileContainer.Read(buffer, out List<SaveBlockEntry> parsed, out List<SaveBlockError> blockErrors);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(0, parsed.Count);
            Assert.IsNull(blockErrors);
        }

        [Test]
        public void Read_WrongMagic_ReturnsInvalidFormat()
        {
            byte[] buffer = new byte[] { 0x00, 0x01, 0x02, 0x03, 0, 0, 0, 0, 0, 0, 0, 0 };
            SaveError error = SaveFileContainer.Read(buffer, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors);
            Assert.AreEqual(SaveError.InvalidFormat, error);
            Assert.IsNull(blocks);
            Assert.IsNull(blockErrors);
        }

        [Test]
        public void Read_TooShort_ReturnsInvalidFormat()
        {
            SaveError error = SaveFileContainer.Read(new byte[8], out _, out _);
            Assert.AreEqual(SaveError.InvalidFormat, error);
        }

        [Test]
        public void Read_UnknownContainerVersion_ReturnsUnsupportedVersion()
        {
            byte[] buffer = WriteContainer(new List<SaveBlockEntry>());
            buffer[4] = 0x7F; // 容器版本 → 未知值

            SaveError error = SaveFileContainer.Read(buffer, out _, out _);
            Assert.AreEqual(SaveError.UnsupportedVersion, error);
        }

        [Test]
        public void Read_V1Container_ReturnsUnsupportedVersion()
        {
            // 容器 v1 旧档硬切作废（用户裁定，不做双格式兼容读）
            byte[] buffer = WriteContainer(new List<SaveBlockEntry>());
            buffer[4] = 1;

            SaveError error = SaveFileContainer.Read(buffer, out _, out _);
            Assert.AreEqual(SaveError.UnsupportedVersion, error, "v1 容器必须判别为 UnsupportedVersion");
        }

        [Test]
        public void Read_AbsurdBlockCount_ReturnsCorrupted()
        {
            byte[] buffer = WriteContainer(new List<SaveBlockEntry>());
            // 块数改写为超出合理性上限的巨大值（小端）
            buffer[8] = 0xFF;
            buffer[9] = 0xFF;
            buffer[10] = 0xFF;
            buffer[11] = 0x7F;

            SaveError error = SaveFileContainer.Read(buffer, out _, out _);
            Assert.AreEqual(SaveError.Corrupted, error);
        }

        #region 逐块部分恢复 [PER-BLOCK PARTIAL RECOVERY]

        /// <summary>
        /// 定位容器内指定块的起始偏移（键长字段；容器头 12B + 逐块遍历框架字段）。
        /// </summary>
        private static int LocateBlockStart(byte[] container, int blockIndex)
        {
            int offset = 12;
            for (int i = 0; i < blockIndex; i++)
            {
                int keyByteCount = BitConverter.ToInt32(container, offset);
                offset += 4 + keyByteCount + 4 + 2; // 键长 + 键 + 模式版本 + 后端
                int payloadLength = BitConverter.ToInt32(container, offset);
                offset += 4 + 4 + payloadLength; // 载荷长 + 载荷 CRC32 + 载荷
            }

            return offset;
        }

        /// <summary>
        /// 定位容器内指定块的载荷偏移。
        /// </summary>
        private static int LocateBlockPayloadOffset(byte[] container, int blockIndex)
        {
            int offset = LocateBlockStart(container, blockIndex);
            int keyByteCount = BitConverter.ToInt32(container, offset);
            return offset + 4 + keyByteCount + 4 + 2 + 4 + 4; // 键长 + 键 + 模式版本 + 后端 + 载荷长 + 载荷 CRC32
        }

        [Test]
        public void Read_PayloadBitFlip_SkipsBlock_OthersRecovered()
        {
            byte[] buffer = WriteContainer(BuildThreeBlocks());
            buffer[LocateBlockPayloadOffset(buffer, 1)] ^= 0xFF; // 翻转 inventory 载荷首字节

            SaveError error = SaveFileContainer.Read(buffer, out List<SaveBlockEntry> parsed, out List<SaveBlockError> blockErrors);

            Assert.AreEqual(SaveError.None, error, "单块损坏应部分恢复而非整档拒绝");
            Assert.AreEqual(2, parsed.Count, "健康块应全部可救");
            Assert.AreEqual("stats", parsed[0].Key);
            Assert.AreEqual("unicode-键名⑵", parsed[1].Key);

            Assert.AreEqual(1, blockErrors.Count);
            Assert.AreEqual("inventory", blockErrors[0].Key);
            Assert.AreEqual(SaveError.Corrupted, blockErrors[0].Error);
            Assert.IsTrue(blockErrors[0].HasMetadata, "CRC 坏块框架完好，元数据应可信");
            Assert.AreEqual(1, blockErrors[0].DataVersion);
            Assert.AreEqual(ESaveBackend.MessagePack, blockErrors[0].Backend);
            Assert.AreEqual(4, blockErrors[0].SizeBytes);
        }

        [Test]
        public void Read_StructuralDamage_PreservesPrefix_TerminalError()
        {
            byte[] buffer = WriteContainer(BuildThreeBlocks());
            // 破坏第 2 块（inventory）的键长字段为巨大值——框架越界属结构性损坏，后续块边界不可知
            int blockOneStart = LocateBlockStart(buffer, 1);
            buffer[blockOneStart] = 0xFF;
            buffer[blockOneStart + 1] = 0xFF;
            buffer[blockOneStart + 2] = 0xFF;
            buffer[blockOneStart + 3] = 0x7F;

            SaveError error = SaveFileContainer.Read(buffer, out List<SaveBlockEntry> parsed, out List<SaveBlockError> blockErrors);

            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(1, parsed.Count, "结构性损坏前的健康前缀应保留");
            Assert.AreEqual("stats", parsed[0].Key);
            Assert.AreEqual(1, blockErrors.Count, "结构性损坏只记一条终结记录");
            Assert.IsNull(blockErrors[0].Key, "键字段不可读时坏块键为 null");
            Assert.AreEqual(SaveError.Corrupted, blockErrors[0].Error);
            Assert.IsFalse(blockErrors[0].HasMetadata);
        }

        [Test]
        public void Read_TruncatedPayload_PreservesPrefix()
        {
            // 容器 v2：截断不再整档拒绝——已解析前缀保留 + 终结坏块（长度仍 < 定长头 12B 时判 InvalidFormat）
            byte[] buffer = WriteContainer(BuildThreeBlocks());
            int blockOnePayloadOffset = LocateBlockPayloadOffset(buffer, 1);
            byte[] truncated = new byte[blockOnePayloadOffset + 1]; // 截断在 inventory 载荷内
            Array.Copy(buffer, truncated, truncated.Length);

            SaveError error = SaveFileContainer.Read(truncated, out List<SaveBlockEntry> parsed, out List<SaveBlockError> blockErrors);

            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("stats", parsed[0].Key);
            Assert.AreEqual(1, blockErrors.Count);
            Assert.AreEqual("inventory", blockErrors[0].Key, "载荷截断时键已知");
            Assert.IsFalse(blockErrors[0].HasMetadata);
        }

        [Test]
        public void Read_TruncatedIntoHeader_ReturnsInvalidFormat()
        {
            byte[] buffer = WriteContainer(BuildThreeBlocks());
            for (int length = 0; length < 12; length++)
            {
                byte[] truncated = new byte[length];
                Array.Copy(buffer, truncated, length);
                SaveError error = SaveFileContainer.Read(truncated, out _, out _);
                Assert.AreEqual(SaveError.InvalidFormat, error, $"长度 {length} 低于定长头应判别为 InvalidFormat");
            }
        }

        [Test]
        public void Read_FirstBlockCorrupted_RestStillRecovered()
        {
            byte[] buffer = WriteContainer(BuildThreeBlocks());
            buffer[LocateBlockPayloadOffset(buffer, 0)] ^= 0xFF; // 翻转首块载荷

            SaveError error = SaveFileContainer.Read(buffer, out List<SaveBlockEntry> parsed, out List<SaveBlockError> blockErrors);

            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual("inventory", parsed[0].Key);
            Assert.AreEqual("unicode-键名⑵", parsed[1].Key);
            Assert.AreEqual(1, blockErrors.Count);
            Assert.AreEqual("stats", blockErrors[0].Key);
        }

        #endregion
    }
}
