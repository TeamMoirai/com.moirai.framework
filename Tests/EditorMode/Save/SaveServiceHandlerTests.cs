using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Save
{
    /// <summary>
    /// <see cref="SaveServiceHandler"/> 文件管线测试（经框架内置 <see cref="PlainSaveHandler"/> 消费同步核心路径）：
    /// 多块容器往返与合并、原子写入与覆盖、孤儿临时文件清扫、版本化文件头校验（v2 / v1 旧档作废）、
    /// 损坏兜底分型、块键与路径参数校验、删除与槽位/块枚举、外观兼容映射与降级契约。
    /// <para>经 <c>s_OverrideBasePath</c> 将存档根指向临时目录（<c>InternalsVisibleTo</c> 暴露 internal 管线入口），全流程真实文件 IO。</para>
    /// <para>错误日志断言经 <see cref="LogUtility.OnMessageLogged"/> 事件捕获（Handler 无关）；
    /// DefaultLogHandler 同步链路下另补 <c>LogAssert.Expect</c> 消除 UTF 的未预期日志拦截。</para>
    /// </summary>
    public class SaveServiceHandlerTests
    {
        [Serializable]
        private sealed class SaveData
        {
            public int Gold;
            public string PlayerName;
        }

        /// <summary>测试用存档文件夹名（在覆盖根目录下创建）。</summary>
        private const string TestFolder = "Slots";

        private PlainSaveHandler _handler;
        private string _rootPath;
        private List<(ELogLevel Level, string Message)> _capturedLogs;

        [SetUp]
        public void SetUp()
        {
            _handler = new PlainSaveHandler();
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-handler-tests-" + Guid.NewGuid().ToString("N"));
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
        /// 断言已记录包含指定片段的 Error 日志（运维可见性契约）。
        /// </summary>
        private void AssertErrorLogged(string fragment)
        {
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == ELogLevel.Error && entry.Message != null && entry.Message.Contains(fragment)),
                $"应记录含 '{fragment}' 的 Error 日志，实际捕获 {_capturedLogs.Count} 条");
        }

        /// <summary>
        /// 为随后一条 Error 日志声明 UTF 预期（仅 DefaultLogHandler 同步链路下 UTF 可见；异步链路下 UTF 不可见，跳过声明以免「预期未出现」误报）。
        /// </summary>
        private static void ExpectErrorLogForUtf()
        {
            if (LogUtility.Handler is DefaultLogHandler)
            {
                LogAssert.Expect(LogType.Error, new Regex(".*"));
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
        /// <see cref="TestFolder"/> 在磁盘上的完整目录（覆盖根 + Data 段）。
        /// </summary>
        private string TestFolderDirectory()
        {
            return Path.Combine(_rootPath, SaveServiceHandler.DataFolderName, TestFolder);
        }

        #region 写入与往返 [SAVE / ROUNDTRIP]

        [Test]
        public void SaveBlock_ThenTryLoad_RoundTrips()
        {
            var paths = Paths("slot");
            var data = new SaveData { Gold = 4321, PlayerName = "Moirai" };

            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, data, ESaveBackend.Json, 1, CancellationToken.None);

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(4321, loaded.Gold);
            Assert.AreEqual("Moirai", loaded.PlayerName);
        }

        [Test]
        public void LoadBlockCore_FailedError_ReturnsDefault()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 42, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);
            Assert.IsTrue(_handler.FileExists("slot", TestFolder));

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(42, loaded.Gold);
        }

        [Test]
        public void LoadBlockCore_Corrupted_ReturnsDefault()
        {
            // LoadBlockCore（Load 外观的核心）在损坏时记录错误日志并返回默认值——既有「损坏兜底」契约
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            fileBytes[^1] ^= 0xFF;
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();

            SaveData loaded = _handler.LoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey);

            AssertErrorLogged("Load failed");
            Assert.IsNull(loaded, "损坏档应兜底返回默认值");
        }

        [Test]
        public void SaveBlock_OverwritesExistingFile()
        {
            var paths = Paths("slot");

            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "first" }, ESaveBackend.Json, 1, CancellationToken.None);
            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 2, PlayerName = "second" }, ESaveBackend.Json, 1, CancellationToken.None);

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(2, loaded.Gold, "覆盖写入后应读到最新数据");
        }

        [Test]
        public void Save_LeavesNoTempFiles()
        {
            var paths = Paths("slot");

            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);
            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 2, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);

            string[] tempFiles = Directory.GetFiles(TestFolderDirectory(), "*.tmp-*", SearchOption.AllDirectories);
            Assert.IsEmpty(tempFiles, "成功写入后不应残留临时文件");
        }

        [Test]
        public void Save_FileHasVersionedHeader()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            Assert.GreaterOrEqual(fileBytes.Length, SaveFileHeader.Size, "文件应包含文件头");

            SaveError headerError = SaveFileHeader.Read(fileBytes, out SaveFileHeader header);
            Assert.AreEqual(SaveError.None, headerError, "文件头应可解析");
            Assert.AreEqual(SaveFileHeader.CurrentVersion, header.FormatVersion, "写入应为当前格式版本");
            Assert.AreEqual(0u, header.Flags, "未启用压缩时标志位应为 0");
            Assert.Greater(header.SavedAtUtcTicks, 0L, "文件头应携带保存时间");
            Assert.AreEqual(fileBytes.Length - SaveFileHeader.Size, header.PayloadLength, "载荷长度应与文件自洽");
        }

        [Test]
        public void SaveBlockAsync_NullObject_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => _handler.SaveBlockAsync<SaveData>(null, "slot", "stats"),
                "空存档对象应 fail-fast");
        }

        #endregion

        #region 孤儿临时文件清扫 [ORPHAN CLEANUP]

        [Test]
        public void CleanupOrphanTempFiles_RemovesTempOnly()
        {
            string orphanTemp = Path.Combine(_rootPath, "stale.sav" + ".tmp-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(orphanTemp, "leftover");
            string legitSave = Path.Combine(_rootPath, "legit" + SaveServiceSettings.SaveFileExtension);
            File.WriteAllText(legitSave, "keep me");

            SaveServiceHandler.CleanupOrphanTempFiles(_rootPath);

            Assert.IsFalse(File.Exists(orphanTemp), "孤儿临时文件应被清扫");
            Assert.IsTrue(File.Exists(legitSave), "正式存档不应被误删");
        }

        #endregion

        #region 损坏与格式校验兜底 [CORRUPTION / FORMAT]

        [Test]
        public void TryLoadBlockCore_MissingFile_ReturnsFileNotFound()
        {
            // 无档属正常业务流（静默返回 FileNotFound，不记录错误日志）
            SaveError error = _handler.TryLoadBlockCore<SaveData>(Paths("missing"), SaveServiceHandler.MainBlockKey, out SaveData loaded);

            Assert.AreEqual(SaveError.FileNotFound, error);
            Assert.IsNull(loaded);
        }

        [Test]
        public void TryLoad_LegacyHeaderlessFile_ReturnsInvalidFormat()
        {
            // 更旧格式（无文件头）存档按用户裁定作废——判别为格式非法并留痕
            var paths = Paths("legacy");
            Directory.CreateDirectory(paths.DirectoryPath);
            File.WriteAllBytes(paths.SaveFilePath, new byte[] { 0x7B, 0x22, 0x47, 0x6F, 0x6C, 0x64, 0x7D });

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.InvalidFormat, error);
            Assert.IsNull(loaded);
        }

        [Test]
        public void TryLoad_V1SingleBlockFile_ReturnsUnsupportedVersion()
        {
            // v1（28B 头单块旧格式）不作兼容读——判别为版本不支持（用户裁定作废，S+ 起仅接受 v2 多块容器）
            var paths = Paths("v1");
            Directory.CreateDirectory(paths.DirectoryPath);
            byte[] v1FileBytes = new byte[28 + 16];
            WriteV1HeaderBytes(v1FileBytes);
            File.WriteAllBytes(paths.SaveFilePath, v1FileBytes);

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.UnsupportedVersion, error, "v1 旧档应作废（UnsupportedVersion）");
            Assert.IsNull(loaded);
        }

        [Test]
        public void TryLoad_TruncatedFile_ReturnsInvalidFormat()
        {
            var paths = Paths("truncated");
            Directory.CreateDirectory(paths.DirectoryPath);
            File.WriteAllBytes(paths.SaveFilePath, new byte[] { (byte)'M', (byte)'R', (byte)'S', (byte)'A', 0x01 });

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.InvalidFormat, error);
        }

        [Test]
        public void TryLoad_UnsupportedVersion_ReturnsUnsupportedVersion()
        {
            var paths = Paths("future");
            Directory.CreateDirectory(paths.DirectoryPath);
            byte[] fileBytes = new byte[SaveFileHeader.Size + 16];
            WriteHeaderBytes(fileBytes, 99);
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.UnsupportedVersion, error, "高于当前版本的格式应明确拒绝（向前兼容保护）");
        }

        [Test]
        public void TryLoad_CrcMismatch_ReturnsCorrupted()
        {
            var paths = Paths("crc");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            fileBytes[^1] ^= 0xFF; // 翻转载荷末字节（CRC 校验必失败）
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.Corrupted, error, "存储损坏应被 CRC 拦截");
            Assert.IsNull(loaded);
        }

        [Test]
        public void TryLoad_PayloadLengthMismatch_ReturnsCorrupted()
        {
            var paths = Paths("length");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            using (var stream = new FileStream(paths.SaveFilePath, FileMode.Open, FileAccess.Write))
            {
                stream.SetLength(fileBytes.Length - 4); // 截断 4 字节载荷（长度与文件头不再自洽）
            }

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.Corrupted, error);
        }

        [Test]
        public void TryLoad_InvalidContainerPayload_ReturnsInvalidFormat()
        {
            // 文件头与 CRC 均合法、但载荷不是合法多块容器——容器解析失败应分型为格式非法
            var paths = Paths("badcontainer");
            Directory.CreateDirectory(paths.DirectoryPath);
            byte[] payload = { 0x00, 0x01, 0x02, 0x03 };
            byte[] fileBytes = new byte[SaveFileHeader.Size + payload.Length];
            WriteHeaderBytes(fileBytes, SaveFileHeader.CurrentVersion, payload.Length, Crc32.Compute(payload));
            Buffer.BlockCopy(payload, 0, fileBytes, SaveFileHeader.Size, payload.Length);
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MainBlockKey, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.InvalidFormat, error);
            Assert.IsNull(loaded);
        }

        [Test]
        public void TryLoad_MissingBlock_ReturnsFileNotFound()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);

            // 档存在但目标块不存在——块读取语义仍判别为 FileNotFound（无该块）
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, "inventory", out SaveData loaded);

            Assert.AreEqual(SaveError.FileNotFound, error);
            Assert.IsNull(loaded);
        }

        #endregion

        #region 多块语义 [MULTI-BLOCK SEMANTICS]

        [Test]
        public void SaveBlock_MultipleKeys_CoexistInOneFile()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "stats" }, ESaveBackend.Json, 1, CancellationToken.None);
            _handler.SaveBlockCore(paths, "inventory", new SaveData { Gold = 2, PlayerName = "inventory" }, ESaveBackend.Json, 1, CancellationToken.None);

            SaveError statsError = _handler.TryLoadBlockCore<SaveData>(paths, "stats", out SaveData stats);
            SaveError inventoryError = _handler.TryLoadBlockCore<SaveData>(paths, "inventory", out SaveData inventory);

            Assert.AreEqual(SaveError.None, statsError);
            Assert.AreEqual(SaveError.None, inventoryError);
            Assert.AreEqual("stats", stats.PlayerName, "多块应共存且互不覆盖");
            Assert.AreEqual("inventory", inventory.PlayerName);
        }

        [Test]
        public void SaveBlock_UpsertReplacesByKey_KeepsOthers()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "v1" }, ESaveBackend.Json, 1, CancellationToken.None);
            _handler.SaveBlockCore(paths, "inventory", new SaveData { Gold = 2, PlayerName = "inv" }, ESaveBackend.Json, 1, CancellationToken.None);
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 3, PlayerName = "v2" }, ESaveBackend.Json, 1, CancellationToken.None);

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, "stats", out SaveData stats);
            SaveError inventoryError = _handler.TryLoadBlockCore<SaveData>(paths, "inventory", out SaveData inventory);

            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(3, stats.Gold, "同键写入应替换旧块");
            Assert.AreEqual(SaveError.None, inventoryError, "替换不应影响其它块");
            Assert.AreEqual("inv", inventory.PlayerName);
        }

        [Test]
        public void GetBlockInfos_EnumeratesAllBlocks()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "stats" }, ESaveBackend.Json, 3, CancellationToken.None);
            _handler.SaveBlockCore(paths, "inventory", new SaveData { Gold = 2, PlayerName = "inv" }, ESaveBackend.Json, 1, CancellationToken.None);

            SaveBlockInfo[] infos = _handler.GetBlockInfos("slot", TestFolder);

            Assert.AreEqual(2, infos.Length, "应枚举出全部数据块");
            Assert.AreEqual("stats", infos[0].Key, "块应按插入顺序枚举");
            Assert.AreEqual(3, infos[0].DataVersion, "块元信息应携带模式版本");
            Assert.AreEqual(ESaveBackend.Json, infos[0].Backend, "块元信息应携带后端标识");
            Assert.Greater(infos[0].SizeBytes, 0, "块元信息应携带载荷大小");
            Assert.AreEqual("inventory", infos[1].Key);
        }

        [Test]
        public void GetBlockInfos_MissingFile_ReturnsEmpty()
        {
            SaveBlockInfo[] infos = _handler.GetBlockInfos("missing-blocks", TestFolder);
            Assert.IsEmpty(infos);
        }

        [Test]
        public void DeleteBlock_RemovesTargetBlock_KeepsOthers()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "stats" }, ESaveBackend.Json, 1, CancellationToken.None);
            _handler.SaveBlockCore(paths, "inventory", new SaveData { Gold = 2, PlayerName = "inv" }, ESaveBackend.Json, 1, CancellationToken.None);

            _handler.DeleteBlock("slot", "stats", TestFolder);

            Assert.IsTrue(File.Exists(paths.SaveFilePath), "仍有剩余块时文件应保留");
            Assert.AreEqual(SaveError.FileNotFound, _handler.TryLoadBlockCore<SaveData>(paths, "stats", out _), "目标块应被删除");
            Assert.AreEqual(SaveError.None, _handler.TryLoadBlockCore<SaveData>(paths, "inventory", out SaveData inventory));
            Assert.AreEqual("inv", inventory.PlayerName);
        }

        [Test]
        public void DeleteBlock_LastBlockRemovesFile()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "solo" }, ESaveBackend.Json, 1, CancellationToken.None);

            _handler.DeleteBlock("slot", "stats", TestFolder);

            Assert.IsFalse(File.Exists(paths.SaveFilePath), "删除最后一个块后整档应移除");
        }

        [Test]
        public void MetadataBlock_RoundTrips()
        {
            var paths = Paths("slot");
            var metadata = new SaveMetadata
            {
                GameVersion = "0.1.0",
                Custom = new System.Collections.Generic.Dictionary<string, string> { ["LevelName"] = "Sunken Temple" }
            };

            _handler.SaveBlockCore(paths, SaveServiceHandler.MetaBlockKey, metadata, ESaveBackend.Json, 1, CancellationToken.None);

            SaveError error = _handler.TryLoadBlockCore<SaveMetadata>(paths, SaveServiceHandler.MetaBlockKey, out SaveMetadata loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual("0.1.0", loaded.GameVersion);
            Assert.AreEqual("Sunken Temple", loaded.Custom["LevelName"]);
        }

        #endregion

        #region 块键校验 [BLOCK KEY VALIDATION]

        [Test]
        public void Validate_NullBlockKey_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _handler.SaveBlock(new SaveData(), "slot", null, TestFolder));
        }

        [Test]
        public void Validate_EmptyBlockKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => _handler.SaveBlock(new SaveData(), "slot", "  ", TestFolder));
        }

        [Test]
        public void Validate_ReservedPrefixBlockKey_Throws()
        {
            // "__" 前缀为框架保留块（__main__/__meta）专用——用户块键禁止使用
            Assert.Throws<ArgumentException>(() => _handler.SaveBlock(new SaveData(), "slot", "__user", TestFolder));
        }

        [Test]
        public void Validate_OversizeBlockKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => _handler.SaveBlock(new SaveData(), "slot", new string('k', SaveServiceHandler.MaxBlockKeyLength + 1), TestFolder));
        }

        [Test]
        public void Validate_IllegalCharsBlockKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => _handler.SaveBlock(new SaveData(), "slot", "stats/1", TestFolder));
            Assert.Throws<ArgumentException>(() => _handler.SaveBlock(new SaveData(), "slot", "stats:1", TestFolder));
        }

        [Test]
        public void Validate_MaxLengthBlockKey_Passes()
        {
            // 边界值：恰好达到长度上限的键应合法
            string key = new string('k', SaveServiceHandler.MaxBlockKeyLength);
            _handler.SaveBlock(new SaveData { Gold = 9 }, "slot", key, TestFolder);
            Assert.AreEqual(SaveError.None, _handler.TryLoadBlockCore<SaveData>(Paths("slot"), key, out SaveData loaded));
            Assert.AreEqual(9, loaded.Gold);
        }

        #endregion

        #region 同步块级 API [SYNC BLOCK FACADE]

        [Test]
        public void SaveBlock_Then_LoadBlock_RoundTrips()
        {
            _handler.SaveBlock(new SaveData { Gold = 777, PlayerName = "sync" }, "slot", "stats", TestFolder);
            Assert.IsTrue(_handler.FileExists("slot", TestFolder));

            SaveData loaded = _handler.LoadBlock<SaveData>("slot", "stats", TestFolder);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(777, loaded.Gold);
            Assert.AreEqual("sync", loaded.PlayerName);
        }

        [Test]
        public void TryLoadBlock_MissingBlock_ReturnsFileNotFound()
        {
            SaveResult<SaveData> result = _handler.TryLoadBlock<SaveData>("missing_sync", "stats", TestFolder);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveError.FileNotFound, result.Error);
            Assert.IsNull(result.Data);
        }

        [Test]
        public void SaveBlock_NullObject_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _handler.SaveBlock<SaveData>(null, "slot", "stats", TestFolder));
        }

        #endregion

        #region 外观兼容映射与降级 [FACADE COMPAT / DEGRADATION]

        /// <summary>
        /// 经反射设置生成的私有静态 s_Handler（生成的 Handler 属性 setter 拒绝 null，降级契约测试需要 null 态）。
        /// </summary>
        private static void SetHandler(object value)
        {
            typeof(SaveService)
                .GetField("s_Handler", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, value);
        }

        [Test]
        public void Facade_LegacySave_MapsToMainBlock()
        {
            // 旧单对象 API 映射保留块 __main__：facade 写入后块级读应命中，反之亦然
            SetHandler(_handler);
            try
            {
                SaveService.Save(new SaveData { Gold = 55, PlayerName = "legacy" }, "slot", TestFolder);

                SaveError mainError = _handler.TryLoadBlockCore<SaveData>(Paths("slot"), SaveServiceHandler.MainBlockKey, out SaveData mainBlock);
                Assert.AreEqual(SaveError.None, mainError);
                Assert.AreEqual(55, mainBlock.Gold);

                SaveData loaded = SaveService.Load<SaveData>("slot", TestFolder);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(55, loaded.Gold);

                SaveResult<SaveData> result = SaveService.TryLoad<SaveData>("slot", TestFolder);
                Assert.IsTrue(result.IsSuccess);
                Assert.AreEqual("legacy", result.Data.PlayerName);
            }
            finally
            {
                SetHandler(null);
            }
        }

        [Test]
        public void Facade_HandlerNotReady_TryLoadBlock_FailsWithHandlerNotReady()
        {
            // 降级契约：处理器未就绪时 TryLoadBlock 判别为 HandlerNotReady，查询降级为空数组
            SetHandler(null);

            SaveResult<SaveData> result = SaveService.TryLoadBlock<SaveData>("slot", "stats", TestFolder);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveError.HandlerNotReady, result.Error);

            Assert.IsEmpty(SaveService.GetBlockInfos("slot", TestFolder));
            Assert.IsFalse(SaveService.FileExists("slot", TestFolder));
        }

        #endregion

        #region 路径参数校验 [ARGUMENT VALIDATION]

        [Test]
        public void Validate_NullFileName_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => SaveServiceHandler.ResolveSavePaths(null, TestFolder));
        }

        [Test]
        public void Validate_EmptyFileName_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("", TestFolder));
        }

        [Test]
        public void Validate_WhitespaceFileName_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("   ", TestFolder));
        }

        [Test]
        public void Validate_PathTraversalFileName_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("../evil", TestFolder));
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths(@"sub\dir\evil", TestFolder));
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("sub/dir/evil", TestFolder));
        }

        [Test]
        public void Validate_ReservedDeviceName_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("CON", TestFolder));
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("com1.sav", TestFolder));
        }

        [Test]
        public void Validate_TempSuffixFileName_Throws()
        {
            // 文件名含临时后缀会被孤儿清扫误删——校验层拦截
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("slot.tmp-x", TestFolder));
        }

        [Test]
        public void Validate_IllegalCharsFileName_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("slot:1", TestFolder));
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("slot?", TestFolder));
        }

        [Test]
        public void Validate_NullFolderName_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => SaveServiceHandler.ResolveSavePaths("slot", null));
        }

        [Test]
        public void Validate_PathTraversalFolderName_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("slot", "../escape"));
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("slot", "a/b"));
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("slot", "a:b"));
            Assert.Throws<ArgumentException>(() => SaveServiceHandler.ResolveSavePaths("slot", "."));
        }

        [Test]
        public void Validate_EmptyFolderName_IsDataRoot()
        {
            // 空文件夹名 = 存档数据根目录（合法语义，路径即覆盖根 + 分隔符）
            SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths("slot", "");
            StringAssert.StartsWith(_rootPath, paths.SaveFilePath);
        }

        [Test]
        public void DeleteSaveFolder_EmptyName_Throws()
        {
            Assert.Throws<ArgumentException>(() => _handler.DeleteSaveFolder(""));
        }

        [Test]
        public void FileName_ExtensionIsNormalized()
        {
            // 传入带扩展名的文件名应被剥离后重新追加配置扩展名（既有契约）
            SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths("player_data.json", TestFolder);
            StringAssert.EndsWith(SaveServiceSettings.SaveFileExtension, paths.SaveFilePath);
            StringAssert.DoesNotEndWith(".json" + SaveServiceSettings.SaveFileExtension, paths.SaveFilePath);
        }

        #endregion

        #region 删除与槽位枚举 [DELETE / LISTING]

        [Test]
        public void DeleteSave_RemovesFile()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);
            Assert.IsTrue(File.Exists(paths.SaveFilePath));

            _handler.DeleteSave("slot", TestFolder);
            Assert.IsFalse(File.Exists(paths.SaveFilePath), "删除后存档应不存在");
        }

        [Test]
        public void DeleteSaveFolder_RemovesDirectoryTree()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);

            // 文件夹内嵌套子目录（验证递归删除）
            string nestedDirectory = Path.Combine(paths.DirectoryPath, "nested-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(nestedDirectory);
            File.WriteAllText(Path.Combine(nestedDirectory, "junk.tmp"), "x");

            _handler.DeleteSaveFolder(TestFolder);

            Assert.IsFalse(Directory.Exists(paths.DirectoryPath), "删除文件夹应移除整个目录树");
        }

        [Test]
        public void FileExists_ReflectsLifecycle()
        {
            var paths = Paths("slot");
            Assert.IsFalse(_handler.FileExists("slot", TestFolder));

            _handler.SaveBlockCore(paths, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);
            Assert.IsTrue(_handler.FileExists("slot", TestFolder));

            _handler.DeleteSave("slot", TestFolder);
            Assert.IsFalse(_handler.FileExists("slot", TestFolder));
        }

        [Test]
        public void GetSaveFiles_ListsSlots_NewestFirst()
        {
            var oldPath = Paths("slot_old");
            var newPath = Paths("slot_new");
            _handler.SaveBlockCore(oldPath, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "old" }, ESaveBackend.Json, 1, CancellationToken.None);
            _handler.SaveBlockCore(newPath, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 2, PlayerName = "new" }, ESaveBackend.Json, 1, CancellationToken.None);

            // 控制时间戳使顺序确定（SaveCore 间隔过短时文件系统时间精度不足）
            File.SetLastWriteTimeUtc(oldPath.SaveFilePath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newPath.SaveFilePath, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            SaveFileInfo[] files = _handler.GetSaveFiles(TestFolder);
            Assert.AreEqual(2, files.Length, "应枚举出两个存档槽位");
            Assert.AreEqual("slot_new", files[0].FileName, "应按最后写入时间倒序（最近优先）");
            Assert.AreEqual("slot_old", files[1].FileName);
            Assert.Greater(files[0].SizeBytes, 0L, "元数据应携带文件大小");
            Assert.AreEqual(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), files[0].LastWriteTimeUtc);
        }

        [Test]
        public void GetSaveFiles_MissingFolder_ReturnsEmpty()
        {
            SaveFileInfo[] files = _handler.GetSaveFiles("nonexistent-folder-" + Guid.NewGuid().ToString("N"));
            Assert.AreEqual(0, files.Length);
        }

        [Test]
        public void GetSaveFiles_FiltersByExactExtension()
        {
            // Windows GetFiles 的 8.3 通配符怪癖：*.sav 会命中 *.saveall——必须按扩展名精确过滤
            var realPath = Paths("real");
            _handler.SaveBlockCore(realPath, SaveServiceHandler.MainBlockKey, new SaveData { Gold = 1, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);
            File.WriteAllText(realPath.SaveFilePath + "all", "decoy");

            SaveFileInfo[] files = _handler.GetSaveFiles(TestFolder);
            Assert.AreEqual(1, files.Length, "同前缀不同扩展名的文件不应计入");
            Assert.AreEqual("real", files[0].FileName);
        }

        #endregion

        /// <summary>
        /// 向缓冲区写入 v1 旧格式（28 字节）文件头字节。
        /// </summary>
        private static void WriteV1HeaderBytes(byte[] destination)
        {
            destination[0] = (byte)'M';
            destination[1] = (byte)'R';
            destination[2] = (byte)'S';
            destination[3] = (byte)'A';
            // 版本 = 1（小端）
            destination[4] = 1;
            for (int i = 8; i < 16; i++)
            {
                destination[i] = 0x01; // 正 ticks
            }

            // 载荷长度 = 缓冲区剩余（28B 头之后），CRC 填 0（v1 判别发生在长度/CRC 校验前）
            int payloadLength = destination.Length - 28;
            destination[16] = (byte)payloadLength;
            destination[17] = (byte)(payloadLength >> 8);
            destination[18] = (byte)(payloadLength >> 16);
            destination[19] = (byte)(payloadLength >> 24);
        }

        /// <summary>
        /// 向缓冲区写入指定版本号的文件头字节（长度/CRC 由参数控制）。
        /// </summary>
        private static void WriteHeaderBytes(byte[] destination, int version)
        {
            WriteHeaderBytes(destination, version, destination.Length - SaveFileHeader.Size, 0u);
        }

        /// <summary>
        /// 向缓冲区写入完整文件头字节（v2 32B 布局）。
        /// </summary>
        private static void WriteHeaderBytes(byte[] destination, int version, int payloadLength, uint payloadCrc)
        {
            destination[0] = (byte)'M';
            destination[1] = (byte)'R';
            destination[2] = (byte)'S';
            destination[3] = (byte)'A';
            destination[4] = (byte)version;
            destination[5] = (byte)(version >> 8);
            destination[6] = (byte)(version >> 16);
            destination[7] = (byte)(version >> 24);
            for (int i = 8; i < 16; i++)
            {
                destination[i] = 0x01; // 正 ticks
            }

            destination[16] = (byte)payloadLength;
            destination[17] = (byte)(payloadLength >> 8);
            destination[18] = (byte)(payloadLength >> 16);
            destination[19] = (byte)(payloadLength >> 24);
            destination[20] = (byte)payloadCrc;
            destination[21] = (byte)(payloadCrc >> 8);
            destination[22] = (byte)(payloadCrc >> 16);
            destination[23] = (byte)(payloadCrc >> 24);
            destination[28] = 0; // flags
            destination[29] = 0;
            destination[30] = 0;
            destination[31] = 0;
        }
    }
}
