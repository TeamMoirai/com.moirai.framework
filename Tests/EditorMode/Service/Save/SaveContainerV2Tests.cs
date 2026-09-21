using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Save
{
    /// <summary>
    /// V3-P3 容器 v2 Handler 级测试：逐块 CRC32 部分恢复（头 CRC 重算放行后坏块隔离）、
    /// 整档 CRC 坏仍整档拒绝（头校验优先）、v1 容器硬切拒载、坏块列报与写回收留。
    /// <para>错误日志断言经 <see cref="LogUtility.OnMessageLogged"/> 事件捕获（Handler 无关）；
    /// DefaultLogHandler 同步链路下另补 <c>LogAssert.Expect</c> 消除 UTF 的未预期日志拦截。</para>
    /// </summary>
    public class SaveContainerV2Tests
    {
        [Serializable]
        private sealed class SaveData
        {
            public int Gold;
            public string PlayerName;
        }

        private const string TestFolder = "Slots";

        private PlainSaveHandler _handler;
        private string _rootPath;
        private List<(ELogLevel Level, string Message)> _capturedLogs;

        [SetUp]
        public void SetUp()
        {
            _handler = new PlainSaveHandler();
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-container-v2-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;

            _capturedLogs = new List<(ELogLevel, string)>();
            LogUtility.OnMessageLogged += CaptureLog;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= CaptureLog;
            SaveServiceHandler.s_OverrideBasePath = null;
            try
            {
                if (Directory.Exists(_rootPath))
                {
                    Directory.Delete(_rootPath, true);
                }
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响测试结论
            }
        }

        private void CaptureLog(ELogLevel level, string message, Exception exception)
        {
            _capturedLogs.Add((level, message));
        }

        /// <summary>
        /// 断言已记录包含指定片段的 Error 日志。
        /// </summary>
        private void AssertErrorLogged(string fragment)
        {
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == ELogLevel.Error && entry.Message != null && entry.Message.Contains(fragment)),
                $"应记录含 '{fragment}' 的 Error 日志，实际捕获 {_capturedLogs.Count} 条");
        }

        /// <summary>
        /// 断言已记录包含指定片段的 Warning 日志。
        /// </summary>
        private void AssertWarningLogged(string fragment)
        {
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == ELogLevel.Warning && entry.Message != null && entry.Message.Contains(fragment)),
                $"应记录含 '{fragment}' 的 Warning 日志，实际捕获 {_capturedLogs.Count} 条");
        }

        /// <summary>
        /// 为随后一条 Error 日志声明 UTF 预期（仅 DefaultLogHandler 同步链路下 UTF 可见）。
        /// </summary>
        private static void ExpectErrorLogForUtf()
        {
            if (LogUtility.Handler is not UnityLoggingHandler)
            {
                LogAssert.Expect(LogType.Error, new Regex(".*"));
            }
        }

        /// <summary>
        /// 为随后一条 Warning 日志声明 UTF 预期（仅 DefaultLogHandler 同步链路下 UTF 可见）。
        /// </summary>
        private static void ExpectWarningLogForUtf()
        {
            if (LogUtility.Handler is not UnityLoggingHandler)
            {
                LogAssert.Expect(LogType.Warning, new Regex(".*"));
            }
        }

        /// <summary>
        /// 在 <see cref="TestFolder"/> 下解析目标存档路径。
        /// </summary>
        private SaveServiceHandler.SavePaths Paths(string fileName)
        {
            return SaveServiceHandler.ResolveSavePaths(fileName, TestFolder);
        }

        /// <summary>
        /// 写入含 stats/inventory 两个健康块的存档。
        /// </summary>
        private SaveServiceHandler.SavePaths WriteTwoBlockSave(string fileName)
        {
            var paths = Paths(fileName);
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 100, PlayerName = "stats" }, ESaveBackend.Json, 1, CancellationToken.None);
            _handler.SaveBlockCore(paths, "inventory", new SaveData { Gold = 200, PlayerName = "inventory" }, ESaveBackend.Json, 1, CancellationToken.None);
            return paths;
        }

        /// <summary>
        /// 定位存档文件内指定容器块的起始偏移（文件头 32B + 容器头 12B + 逐块遍历框架字段）。
        /// </summary>
        private static int LocateBlockStart(byte[] fileBytes, int blockIndex)
        {
            int offset = SaveFileHeader.Size + 12;
            for (int i = 0; i < blockIndex; i++)
            {
                int keyByteCount = BitConverter.ToInt32(fileBytes, offset);
                offset += 4 + keyByteCount + 4 + 2; // 键长 + 键 + 模式版本 + 后端
                int payloadLength = BitConverter.ToInt32(fileBytes, offset);
                offset += 4 + 4 + payloadLength; // 载荷长 + 载荷 CRC32 + 载荷
            }

            return offset;
        }

        /// <summary>
        /// 定位存档文件内指定容器块的载荷偏移。
        /// </summary>
        private static int LocateBlockPayloadOffset(byte[] fileBytes, int blockIndex)
        {
            int offset = LocateBlockStart(fileBytes, blockIndex);
            int keyByteCount = BitConverter.ToInt32(fileBytes, offset);
            return offset + 4 + keyByteCount + 4 + 2 + 4 + 4; // 键长 + 键 + 模式版本 + 后端 + 载荷长 + 载荷 CRC32
        }

        /// <summary>
        /// 覆写文件头载荷 CRC（offset 20-23；头字段改动无需其他联动）。
        /// </summary>
        private static void PatchHeaderCrc(byte[] fileBytes)
        {
            uint patchedCrc = Crc32.Compute(fileBytes.AsSpan(SaveFileHeader.Size));
            fileBytes[20] = (byte)patchedCrc;
            fileBytes[21] = (byte)(patchedCrc >> 8);
            fileBytes[22] = (byte)(patchedCrc >> 16);
            fileBytes[23] = (byte)(patchedCrc >> 24);
        }

        /// <summary>
        /// 翻转指定块载荷首字节并重算头 CRC——头校验放行，直达容器逐块 CRC 隔离层。
        /// </summary>
        private static void FlipBlockPayloadByte(string filePath, int blockIndex)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            fileBytes[LocateBlockPayloadOffset(fileBytes, blockIndex)] ^= 0xFF;
            PatchHeaderCrc(fileBytes);
            File.WriteAllBytes(filePath, fileBytes);
        }

        #region 部分恢复 [PARTIAL RECOVERY]

        [Test]
        public void TryLoad_BlockBitFlip_ReturnsCorrupted_OthersLoad()
        {
            var paths = WriteTwoBlockSave("slot");
            FlipBlockPayloadByte(paths.SaveFilePath, 0); // stats 块载荷损坏

            // 部分恢复路径只记坏块 Warning（ReadContainerOrEmpty 统一留痕），不再记 Error——勿声明 Error 预期
            ExpectWarningLogForUtf();
            SaveError statsError = _handler.TryLoadBlockCore<SaveData>(paths, "stats", out SaveData stats);
            Assert.AreEqual(SaveError.Corrupted, statsError, "坏块应分型为 Corrupted 而非 FileNotFound");
            Assert.IsNull(stats);
            AssertWarningLogged("corrupted");

            SaveError inventoryError = _handler.TryLoadBlockCore<SaveData>(paths, "inventory", out SaveData inventory);
            Assert.AreEqual(SaveError.None, inventoryError, "健康块不受坏块影响");
            Assert.AreEqual(200, inventory.Gold);
            Assert.AreEqual("inventory", inventory.PlayerName);
        }

        [Test]
        public void LoadBlock_BlockBitFlip_ReturnsDefault()
        {
            var paths = WriteTwoBlockSave("slot");
            FlipBlockPayloadByte(paths.SaveFilePath, 0);

            ExpectWarningLogForUtf();
            SaveData stats = _handler.LoadBlockCore<SaveData>(paths, "stats");
            Assert.IsNull(stats, "坏块经 LoadBlock 兜底为 default");
        }

        [Test]
        public void TryLoad_HeaderCrcBad_WholeFileRejected()
        {
            // 头 CRC 未重算的存储损坏——头校验优先，所有块一律整档拒绝（不进入逐块部分恢复）
            var paths = WriteTwoBlockSave("slot");
            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            fileBytes[LocateBlockPayloadOffset(fileBytes, 0)] ^= 0xFF;
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();
            SaveError statsError = _handler.TryLoadBlockCore<SaveData>(paths, "stats", out _);
            Assert.AreEqual(SaveError.Corrupted, statsError);

            ExpectErrorLogForUtf();
            SaveError inventoryError = _handler.TryLoadBlockCore<SaveData>(paths, "inventory", out _);
            Assert.AreEqual(SaveError.Corrupted, inventoryError, "整档 CRC 失败时健康块同样拒绝（头校验优先于逐块隔离）");
            AssertErrorLogged("Corrupted");
        }

        [Test]
        public void TryLoad_V1ContainerFile_ReturnsUnsupportedVersion()
        {
            // 容器版本字节改写为 1（文件头 CRC 重算放行）——v1 容器硬切作废
            var paths = WriteTwoBlockSave("slot");
            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            fileBytes[SaveFileHeader.Size + 4] = 1;
            PatchHeaderCrc(fileBytes);
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, "stats", out SaveData loaded);
            Assert.AreEqual(SaveError.UnsupportedVersion, error, "v1 容器必须判别为 UnsupportedVersion");
            Assert.IsNull(loaded);
            AssertErrorLogged("UnsupportedVersion");
        }

        [Test]
        public void GetBlockInfos_ListsCorruptedBlockWithMetadata()
        {
            var paths = WriteTwoBlockSave("slot");
            FlipBlockPayloadByte(paths.SaveFilePath, 0);

            ExpectWarningLogForUtf();
            SaveBlockInfo[] infos = _handler.GetBlockInfos("slot", TestFolder);

            Assert.AreEqual(2, infos.Length, "健康块与坏块都应列入清单");
            Assert.AreEqual("inventory", infos[0].Key, "健康块列前");
            Assert.AreEqual(SaveError.None, infos[0].Error);
            Assert.IsTrue(infos[0].HasMetadata);

            Assert.AreEqual("stats", infos[1].Key, "坏块列后");
            Assert.AreEqual(SaveError.Corrupted, infos[1].Error);
            Assert.IsTrue(infos[1].HasMetadata, "CRC 坏块框架完好，元数据可信");
            Assert.Greater(infos[1].SizeBytes, 0);
        }

        [Test]
        public void SaveBlock_OnCorruptedFile_PreservesHealthyDropsCorrupted()
        {
            var paths = WriteTwoBlockSave("slot");
            FlipBlockPayloadByte(paths.SaveFilePath, 0);

            ExpectWarningLogForUtf();
            _handler.SaveBlockCore(paths, "settings", new SaveData { Gold = 300, PlayerName = "settings" }, ESaveBackend.Json, 1, CancellationToken.None);

            SaveError statsError = _handler.TryLoadBlockCore<SaveData>(paths, "stats", out _);
            Assert.AreEqual(SaveError.FileNotFound, statsError, "坏块在写回时已剔除（数据本不可读），不再报损坏");

            Assert.AreEqual(SaveError.None, _handler.TryLoadBlockCore<SaveData>(paths, "inventory", out SaveData inventory));
            Assert.AreEqual(200, inventory.Gold);
            Assert.AreEqual(SaveError.None, _handler.TryLoadBlockCore<SaveData>(paths, "settings", out SaveData settings));
            Assert.AreEqual(300, settings.Gold);
        }

        [Test]
        public void DeleteBlock_CorruptedKey_SilentNoOp()
        {
            var paths = WriteTwoBlockSave("slot");
            FlipBlockPayloadByte(paths.SaveFilePath, 0);

            ExpectWarningLogForUtf();
            Assert.DoesNotThrow(() => _handler.DeleteBlockCore(paths, "stats", CancellationToken.None),
                "删除坏块键按幂等契约静默返回（坏块对写路径不可见，下次写回自然剔除）");
        }

        #endregion
    }
}
