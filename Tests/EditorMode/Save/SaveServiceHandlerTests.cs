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

namespace Save
{
    /// <summary>
    /// <see cref="SaveServiceHandler"/> 文件管线测试（经框架内置 <see cref="JsonSaveHandler"/> 消费同步核心路径）：
    /// 原子写入与覆盖、孤儿临时文件清扫、版本化文件头校验、损坏兜底分型、路径参数校验、删除与槽位枚举。
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

        private JsonSaveHandler _handler;
        private string _rootPath;
        private List<(ELogLevel Level, string Message)> _capturedLogs;

        [SetUp]
        public void SetUp()
        {
            _handler = new JsonSaveHandler();
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
        public void Save_ThenTryLoad_RoundTrips()
        {
            var paths = Paths("slot");
            var data = new SaveData { Gold = 4321, PlayerName = "Moirai" };

            _handler.SaveCore(paths, data, CancellationToken.None);

            SaveError error = _handler.TryLoadCore<SaveData>(paths, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(4321, loaded.Gold);
            Assert.AreEqual("Moirai", loaded.PlayerName);
        }

        [Test]
        public void LoadCore_FailedError_ReturnsDefault()
        {
            var paths = Paths("slot");
            _handler.SaveCore(paths, new SaveData { Gold = 42, PlayerName = "Moirai" }, CancellationToken.None);
            Assert.IsTrue(_handler.FileExists("slot", TestFolder));

            SaveError error = _handler.TryLoadCore<SaveData>(paths, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(42, loaded.Gold);
        }

        [Test]
        public void LoadCore_Corrupted_ReturnsDefault()
        {
            // LoadCore（Load 外观的核心）在损坏时记录错误日志并返回默认值——既有「损坏兜底」契约
            var paths = Paths("slot");
            _handler.SaveCore(paths, new SaveData { Gold = 1, PlayerName = "Moirai" }, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            fileBytes[^1] ^= 0xFF;
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();

            SaveData loaded = _handler.LoadCore<SaveData>(paths);

            AssertErrorLogged("Load failed");
            Assert.IsNull(loaded, "损坏档应兜底返回默认值");
        }

        [Test]
        public void Save_OverwritesExistingFile()
        {
            var paths = Paths("slot");

            _handler.SaveCore(paths, new SaveData { Gold = 1, PlayerName = "first" }, CancellationToken.None);
            _handler.SaveCore(paths, new SaveData { Gold = 2, PlayerName = "second" }, CancellationToken.None);

            SaveError error = _handler.TryLoadCore<SaveData>(paths, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(2, loaded.Gold, "覆盖写入后应读到最新数据");
        }

        [Test]
        public void Save_LeavesNoTempFiles()
        {
            var paths = Paths("slot");

            _handler.SaveCore(paths, new SaveData { Gold = 1, PlayerName = "Moirai" }, CancellationToken.None);
            _handler.SaveCore(paths, new SaveData { Gold = 2, PlayerName = "Moirai" }, CancellationToken.None);

            string[] tempFiles = Directory.GetFiles(TestFolderDirectory(), "*.tmp-*", SearchOption.AllDirectories);
            Assert.IsEmpty(tempFiles, "成功写入后不应残留临时文件");
        }

        [Test]
        public void Save_FileHasVersionedHeader()
        {
            var paths = Paths("slot");
            _handler.SaveCore(paths, new SaveData { Gold = 1, PlayerName = "Moirai" }, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            Assert.GreaterOrEqual(fileBytes.Length, SaveFileHeader.Size, "文件应包含文件头");

            SaveError headerError = SaveFileHeader.Read(fileBytes, out SaveFileHeader header);
            Assert.AreEqual(SaveError.None, headerError, "文件头应可解析");
            Assert.AreEqual(SaveFileHeader.CurrentVersion, header.FormatVersion, "写入应为当前格式版本");
            Assert.Greater(header.SavedAtUtcTicks, 0L, "文件头应携带保存时间");
            Assert.AreEqual(fileBytes.Length - SaveFileHeader.Size, header.PayloadLength, "载荷长度应与文件自洽");
        }

        [Test]
        public void SaveAsync_NullObject_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => _handler.SaveAsync<SaveData>(null, "slot"),
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
        public void TryLoadCore_MissingFile_ReturnsFileNotFound()
        {
            // 无档属正常业务流（静默返回 FileNotFound，不记录错误日志）
            SaveError error = _handler.TryLoadCore<SaveData>(Paths("missing"), out SaveData loaded);

            Assert.AreEqual(SaveError.FileNotFound, error);
            Assert.IsNull(loaded);
        }

        [Test]
        public void TryLoad_LegacyHeaderlessFile_ReturnsInvalidFormat()
        {
            // 旧格式（无文件头）存档按用户裁定作废——判别为格式非法并留痕
            var paths = Paths("legacy");
            Directory.CreateDirectory(paths.DirectoryPath);
            File.WriteAllBytes(paths.SaveFilePath, new byte[] { 0x7B, 0x22, 0x47, 0x6F, 0x6C, 0x64, 0x7D });

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadCore<SaveData>(paths, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.InvalidFormat, error);
            Assert.IsNull(loaded);
        }

        [Test]
        public void TryLoad_TruncatedFile_ReturnsInvalidFormat()
        {
            var paths = Paths("truncated");
            Directory.CreateDirectory(paths.DirectoryPath);
            File.WriteAllBytes(paths.SaveFilePath, new byte[] { (byte)'M', (byte)'R', (byte)'S', (byte)'A', 0x01 });

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadCore<SaveData>(paths, out SaveData loaded);

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

            SaveError error = _handler.TryLoadCore<SaveData>(paths, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.UnsupportedVersion, error, "高于当前版本的格式应明确拒绝（向前兼容保护）");
        }

        [Test]
        public void TryLoad_CrcMismatch_ReturnsCorrupted()
        {
            var paths = Paths("crc");
            _handler.SaveCore(paths, new SaveData { Gold = 1, PlayerName = "Moirai" }, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            fileBytes[^1] ^= 0xFF; // 翻转载荷末字节（CRC 校验必失败）
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadCore<SaveData>(paths, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.Corrupted, error, "存储损坏应被 CRC 拦截");
            Assert.IsNull(loaded);
        }

        [Test]
        public void TryLoad_PayloadLengthMismatch_ReturnsCorrupted()
        {
            var paths = Paths("length");
            _handler.SaveCore(paths, new SaveData { Gold = 1, PlayerName = "Moirai" }, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            using (var stream = new FileStream(paths.SaveFilePath, FileMode.Open, FileAccess.Write))
            {
                stream.SetLength(fileBytes.Length - 4); // 截断 4 字节载荷（长度与文件头不再自洽）
            }

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadCore<SaveData>(paths, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.Corrupted, error);
        }

        [Test]
        public void TryLoad_InvalidJsonPayload_ReturnsSerializationFailed()
        {
            // 文件头与 CRC 均合法、但载荷不是合法 JSON——反序列化失败应分型
            var paths = Paths("badjson");
            Directory.CreateDirectory(paths.DirectoryPath);
            byte[] payload = { 0x00, 0x01, 0x02, 0x03 };
            byte[] fileBytes = new byte[SaveFileHeader.Size + payload.Length];
            WriteHeaderBytes(fileBytes, SaveFileHeader.CurrentVersion, payload.Length, Crc32.Compute(payload));
            Buffer.BlockCopy(payload, 0, fileBytes, SaveFileHeader.Size, payload.Length);
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadCore<SaveData>(paths, out SaveData loaded);

            AssertErrorLogged("Deserialize save failed");
            Assert.AreEqual(SaveError.SerializationFailed, error);
            Assert.IsNull(loaded);
        }

        #endregion

        #region 同步外观 API [SYNC FACADE]

        [Test]
        public void Save_Then_Load_RoundTrips()
        {
            _handler.Save(new SaveData { Gold = 777, PlayerName = "sync" }, "slot", TestFolder);
            Assert.IsTrue(_handler.FileExists("slot", TestFolder));

            SaveData loaded = _handler.Load<SaveData>("slot", TestFolder);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(777, loaded.Gold);
            Assert.AreEqual("sync", loaded.PlayerName);
        }

        [Test]
        public void Save_Overwrites_Then_TryLoad_ReflectsState()
        {
            _handler.Save(new SaveData { Gold = 1, PlayerName = "first" }, "slot", TestFolder);
            _handler.Save(new SaveData { Gold = 2, PlayerName = "second" }, "slot", TestFolder);

            SaveResult<SaveData> result = _handler.TryLoad<SaveData>("slot", TestFolder);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(2, result.Data.Gold, "覆盖写入后应读到最新数据");
            Assert.AreEqual("second", result.Data.PlayerName);
        }

        [Test]
        public void TryLoad_MissingFile_ReturnsFileNotFound()
        {
            SaveResult<SaveData> result = _handler.TryLoad<SaveData>("missing_sync", TestFolder);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveError.FileNotFound, result.Error);
            Assert.IsNull(result.Data);
        }

        [Test]
        public void Save_NullObject_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _handler.Save<SaveData>(null, "slot", TestFolder));
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
            _handler.SaveCore(paths, new SaveData { Gold = 1, PlayerName = "Moirai" }, CancellationToken.None);
            Assert.IsTrue(File.Exists(paths.SaveFilePath));

            _handler.DeleteSave("slot", TestFolder);
            Assert.IsFalse(File.Exists(paths.SaveFilePath), "删除后存档应不存在");
        }

        [Test]
        public void DeleteSaveFolder_RemovesDirectoryTree()
        {
            var paths = Paths("slot");
            _handler.SaveCore(paths, new SaveData { Gold = 1, PlayerName = "Moirai" }, CancellationToken.None);

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

            _handler.SaveCore(paths, new SaveData { Gold = 1, PlayerName = "Moirai" }, CancellationToken.None);
            Assert.IsTrue(_handler.FileExists("slot", TestFolder));

            _handler.DeleteSave("slot", TestFolder);
            Assert.IsFalse(_handler.FileExists("slot", TestFolder));
        }

        [Test]
        public void GetSaveFiles_ListsSlots_NewestFirst()
        {
            var oldPath = Paths("slot_old");
            var newPath = Paths("slot_new");
            _handler.SaveCore(oldPath, new SaveData { Gold = 1, PlayerName = "old" }, CancellationToken.None);
            _handler.SaveCore(newPath, new SaveData { Gold = 2, PlayerName = "new" }, CancellationToken.None);

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
            _handler.SaveCore(realPath, new SaveData { Gold = 1, PlayerName = "Moirai" }, CancellationToken.None);
            File.WriteAllText(realPath.SaveFilePath + "all", "decoy");

            SaveFileInfo[] files = _handler.GetSaveFiles(TestFolder);
            Assert.AreEqual(1, files.Length, "同前缀不同扩展名的文件不应计入");
            Assert.AreEqual("real", files[0].FileName);
        }

        #endregion

        /// <summary>
        /// 向缓冲区写入指定版本号的文件头字节（默认长度/CRC 由参数控制）。
        /// </summary>
        private static void WriteHeaderBytes(byte[] destination, int version)
        {
            WriteHeaderBytes(destination, version, destination.Length - SaveFileHeader.Size, 0u);
        }

        /// <summary>
        /// 向缓冲区写入完整文件头字节。
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
        }
    }
}
