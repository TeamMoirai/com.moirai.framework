using System;
using System.Collections.Generic;
using System.IO;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// 后端标识表用例：钉住 2 字节 ID 的落盘宽度、偏移与内建数值（存量存档可读全靠这三件事不变），并断其显示名收口。
    /// </summary>
    /// <remarks>
    /// 期望数字写死在本文件，不从被测类型读出，否则"改了数值"与"跟着改断言"无法区分； <br />
    /// 容器与文件头版本一并断为 2：块后端字段的偏移与宽度未变，任一侧升版都会使存量存档读不回。
    /// </remarks>
    public class SaveBackendWireIdTests
    {
        /// <summary>块 0 后端字段相对容器起点的字节偏移。</summary>
        private const int BACKEND_FIELD_OFFSET = 12 + 4 + 1 + 4; // 容器头(魔数+版本+块数) + 键长 + 键 + 模式版本

        /// <summary>
        /// 写单块容器并取回块 0 后端字段的落盘数值。
        /// </summary>
        /// <param name="backendId">写入的后端标识。</param>
        /// <returns>容器内该 2 字节字段的小端数值。</returns>
        private static ushort ReadWireBackend(ushort backendId)
        {
            var blocks = new List<SaveBlockEntry>
            {
                new SaveBlockEntry("k", 1, backendId, new byte[] { 0x01, 0x02 })
            };

            var buffer = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(buffer, blocks);
            return BitConverter.ToUInt16(buffer, BACKEND_FIELD_OFFSET);
        }

        [Test]
        public void WireBackendField_FiveBuiltInAndReservedIds_WriteAtFixedOffset()
        {
            Assert.AreEqual(0, ReadWireBackend(SaveBackendIds.JSON), "JSON 必须是 0：它兼作未配置时的回退值");
            Assert.AreEqual(1, ReadWireBackend(SaveBackendIds.MESSAGE_PACK));
            Assert.AreEqual(2, ReadWireBackend(SaveBackendIds.MEMORY_PACK));
            Assert.AreEqual(3, ReadWireBackend(SaveBackendIds.PROTOBUF));
            Assert.AreEqual(254, ReadWireBackend(SaveBackendIds.KEY_VALUE));
        }

        [Test]
        public void WireBackendField_StreamWriteOverload_UsesSameOffsetAndValue()
        {
            // 生产写盘走 Stream 重载（SaveServiceHandler 的段流路径），字段落在另一处写入点，必须单独钉住
            var blocks = new List<SaveBlockEntry>
            {
                new SaveBlockEntry("k", 1, SaveBackendIds.MEMORY_PACK, new byte[] { 0x01, 0x02 })
            };

            using var stream = new MemoryStream();
            SaveFileContainer.Write(stream, blocks);
            byte[] bytes = stream.ToArray();

            Assert.AreEqual(2, BitConverter.ToUInt16(bytes, BACKEND_FIELD_OFFSET), "Stream 路径的后端字段要与 Span 路径同偏移同值");
        }

        [Test]
        public void WireBackendField_RoundTripsThroughContainerRead()
        {
            var blocks = new List<SaveBlockEntry>
            {
                new SaveBlockEntry("k", 7, SaveBackendIds.PROTOBUF, new byte[] { 0x0A, 0x0B })
            };

            var buffer = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(buffer, blocks);

            Assert.AreEqual(SaveError.None, SaveFileContainer.Read(buffer, out List<SaveBlockEntry> parsed, out _));
            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("k", parsed[0].Key);
            Assert.AreEqual(7, parsed[0].DataVersion);
            Assert.AreEqual(3, parsed[0].Backend);
        }

        [Test]
        public void DisplayName_KnownIdsAndUnknown_ReturnWordsNotRawNumbers()
        {
            Assert.AreEqual("Json", SaveBackendIds.DisplayName(SaveBackendIds.JSON));
            Assert.AreEqual("MessagePack", SaveBackendIds.DisplayName(SaveBackendIds.MESSAGE_PACK));
            Assert.AreEqual("MemoryPack", SaveBackendIds.DisplayName(SaveBackendIds.MEMORY_PACK));
            Assert.AreEqual("Protobuf", SaveBackendIds.DisplayName(SaveBackendIds.PROTOBUF));
            Assert.AreEqual("KeyValue", SaveBackendIds.DisplayName(SaveBackendIds.KEY_VALUE));
            Assert.AreEqual("ID 1100", SaveBackendIds.DisplayName(1100), "未注册的 ID 没有名字，回送数值本身");
        }

        [Test]
        public void FormatVersions_ContainerAndHeader_StayAtTwo()
        {
            Assert.AreEqual(2, SaveFileContainer.CurrentVersion, "块后端字段偏移与数值未变，容器不得升版");
            Assert.AreEqual(2, SaveFileHeader.CurrentVersion, "文件头未增删字段，不得升版");
        }
    }
}
