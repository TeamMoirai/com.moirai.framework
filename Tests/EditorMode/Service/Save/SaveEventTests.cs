using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Save
{
    /// <summary>
    /// V3-P4 事件 API 测试：静态事件触发时机/次数/参数、失败事件阶段分型、后台线程派发主线程化、进度批次判定。
    /// <para>全部经 internal 同步核心路径在主线程内联派发断言（EditMode 主线程 = <c>MainThreadDispatcher.IsMainThread</c>）；
    /// 后台派发用 <c>Task.Run</c> + <c>MainThreadDispatcher.Pump()</c> 确定性验证入队路径。</para>
    /// <para>错误日志断言经 <see cref="LogUtility.OnMessageLogged"/> 事件捕获（Handler 无关）；
    /// DefaultLogHandler 同步链路下另补 <c>LogAssert.Expect</c> 消除 UTF 的未预期日志拦截。</para>
    /// </summary>
    public class SaveEventTests
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

        private List<SaveSlotChangedArgs> _slotEvents;
        private List<SaveBlockChangedArgs> _blockSavedEvents;
        private List<SaveBlockChangedArgs> _blockDeletedEvents;
        private List<SaveFailedArgs> _saveFailedEvents;
        private List<SaveFailedArgs> _loadFailedEvents;

        [SetUp]
        public void SetUp()
        {
            _handler = new PlainSaveHandler();
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-event-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;

            _capturedLogs = new List<(ELogLevel, string)>();
            LogUtility.OnMessageLogged += CaptureLog;

            _slotEvents = new List<SaveSlotChangedArgs>();
            _blockSavedEvents = new List<SaveBlockChangedArgs>();
            _blockDeletedEvents = new List<SaveBlockChangedArgs>();
            _saveFailedEvents = new List<SaveFailedArgs>();
            _loadFailedEvents = new List<SaveFailedArgs>();
            SaveService.SlotChanged += OnSlotChanged;
            SaveService.BlockSaved += OnBlockSaved;
            SaveService.BlockDeleted += OnBlockDeleted;
            SaveService.SaveFailed += OnSaveFailed;
            SaveService.LoadFailed += OnLoadFailed;
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.SlotChanged -= OnSlotChanged;
            SaveService.BlockSaved -= OnBlockSaved;
            SaveService.BlockDeleted -= OnBlockDeleted;
            SaveService.SaveFailed -= OnSaveFailed;
            SaveService.LoadFailed -= OnLoadFailed;
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

        private void OnSlotChanged(SaveSlotChangedArgs args) => _slotEvents.Add(args);
        private void OnBlockSaved(SaveBlockChangedArgs args) => _blockSavedEvents.Add(args);
        private void OnBlockDeleted(SaveBlockChangedArgs args) => _blockDeletedEvents.Add(args);
        private void OnSaveFailed(SaveFailedArgs args) => _saveFailedEvents.Add(args);
        private void OnLoadFailed(SaveFailedArgs args) => _loadFailedEvents.Add(args);

        /// <summary>
        /// 为随后一条 Error 日志声明 UTF 预期（仅 DefaultLogHandler 同步链路下 UTF 可见）。
        /// </summary>
        private static void ExpectErrorLogForUtf()
        {
            UtfLogExpect.Error();
        }

        /// <summary>
        /// 为随后一条 Warning 日志声明 UTF 预期（仅 DefaultLogHandler 同步链路下 UTF 可见）。
        /// </summary>
        private static void ExpectWarningLogForUtf()
        {
            UtfLogExpect.Warning();
        }

        /// <summary>
        /// 在 <see cref="TestFolder"/> 下解析目标存档路径。
        /// </summary>
        private SaveServiceHandler.SavePaths Paths(string fileName)
        {
            return SaveServiceHandler.ResolveSavePaths(fileName, TestFolder);
        }

        #region 块与槽位事件 [BLOCK / SLOT EVENTS]

        [Test]
        public void SaveBlockCore_FiresBlockSaved_AndSlotSaved()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 5, PlayerName = "Moirai" }, ESaveBackend.Json, 1, CancellationToken.None);

            Assert.AreEqual(1, _blockSavedEvents.Count, "块保存事件应恰好触发一次");
            SaveBlockChangedArgs blockArgs = _blockSavedEvents[0];
            Assert.AreEqual("slot", blockArgs.FileName);
            Assert.AreEqual(TestFolder, blockArgs.FolderName);
            Assert.AreEqual("stats", blockArgs.Key);
            Assert.AreEqual(ESaveBackend.Json, blockArgs.Backend);
            Assert.Greater(blockArgs.SizeBytes, 0);

            Assert.AreEqual(1, _slotEvents.Count, "槽位写入事件应恰好触发一次");
            Assert.AreEqual(ESaveSlotChangeKind.Saved, _slotEvents[0].Kind);
            Assert.AreEqual("slot", _slotEvents[0].FileName);
            Assert.AreEqual(TestFolder, _slotEvents[0].FolderName);
        }

        [Test]
        public void DeleteBlockCore_ExistingKey_FiresBlockDeleted()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "a" }, ESaveBackend.Json, 1, CancellationToken.None);
            _handler.SaveBlockCore(paths, "inventory", new SaveData { Gold = 2, PlayerName = "b" }, ESaveBackend.Json, 1, CancellationToken.None);
            _blockSavedEvents.Clear();
            _slotEvents.Clear();

            _handler.DeleteBlockCore(paths, "stats", CancellationToken.None);

            Assert.AreEqual(1, _blockDeletedEvents.Count);
            Assert.AreEqual("stats", _blockDeletedEvents[0].Key);
            Assert.AreEqual("slot", _blockDeletedEvents[0].FileName);
            Assert.AreEqual(ESaveBackend.Json, _blockDeletedEvents[0].Backend);
            Assert.AreEqual(0, _slotEvents.Count, "残余块仍在——槽位不应报删除");
        }

        [Test]
        public void DeleteBlockCore_MissingKey_FiresNothing()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "a" }, ESaveBackend.Json, 1, CancellationToken.None);
            _blockSavedEvents.Clear();
            _slotEvents.Clear();

            _handler.DeleteBlockCore(paths, "missing", CancellationToken.None);

            Assert.AreEqual(0, _blockDeletedEvents.Count, "幂等空删不触发事件");
            Assert.AreEqual(0, _slotEvents.Count);
        }

        [Test]
        public void DeleteBlockCore_LastKey_FiresBlockDeleted_AndSlotDeleted()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "a" }, ESaveBackend.Json, 1, CancellationToken.None);
            _blockSavedEvents.Clear();
            _slotEvents.Clear();

            _handler.DeleteBlockCore(paths, "stats", CancellationToken.None);

            Assert.AreEqual(1, _blockDeletedEvents.Count);
            Assert.AreEqual(1, _slotEvents.Count);
            Assert.AreEqual(ESaveSlotChangeKind.Deleted, _slotEvents[0].Kind, "最后一块删除后整档移除，槽位应报删除");
            Assert.IsFalse(File.Exists(paths.SaveFilePath));
        }

        [Test]
        public void DeleteSave_ExistingFile_FiresSlotDeleted()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "a" }, ESaveBackend.Json, 1, CancellationToken.None);
            _slotEvents.Clear();

            _handler.DeleteSave("slot", TestFolder);

            Assert.AreEqual(1, _slotEvents.Count);
            Assert.AreEqual(ESaveSlotChangeKind.Deleted, _slotEvents[0].Kind);
            Assert.AreEqual("slot", _slotEvents[0].FileName);
        }

        [Test]
        public void DeleteSave_MissingFile_FiresNothing()
        {
            _handler.DeleteSave("never-existed", TestFolder);
            Assert.AreEqual(0, _slotEvents.Count, "幂等删除不存在目标不触发事件");
        }

        [Test]
        public void BackupRoundTrip_FiresBackupCreated_AndBackupRestored()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "a" }, ESaveBackend.Json, 1, CancellationToken.None);
            _slotEvents.Clear();

            _handler.CreateBackup("slot", TestFolder);
            Assert.AreEqual(1, _slotEvents.Count);
            Assert.AreEqual(ESaveSlotChangeKind.BackupCreated, _slotEvents[0].Kind);

            _handler.RestoreBackup("slot", TestFolder);
            Assert.AreEqual(2, _slotEvents.Count);
            Assert.AreEqual(ESaveSlotChangeKind.BackupRestored, _slotEvents[1].Kind);
        }

        #endregion

        #region 失败事件 [FAILURE EVENTS]

        [Test]
        public void SaveBlockCore_UnregisteredBackend_FiresSaveFailed()
        {
            var paths = Paths("slot");

            Assert.Throws<GameException>(() =>
                _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "a" }, (ESaveBackend)99, 1, CancellationToken.None));

            Assert.AreEqual(1, _saveFailedEvents.Count);
            SaveFailedArgs args = _saveFailedEvents[0];
            Assert.AreEqual(ESaveFailureStage.Serialize, args.Stage);
            Assert.AreEqual(SaveError.SerializationFailed, args.Error);
            Assert.AreEqual("stats", args.Key);
            Assert.AreEqual("slot", args.FileName);
        }

        [Test]
        public void TryLoadBlockCore_CorruptedFile_FiresLoadFailed_HeaderValidation()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "a" }, ESaveBackend.Json, 1, CancellationToken.None);
            _slotEvents.Clear();
            _blockSavedEvents.Clear();

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            fileBytes[fileBytes.Length - 1] ^= 0xFF; // 翻转载荷末字节（整档 CRC 必失败，头校验优先整档拒绝）
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, "stats", out _);

            Assert.AreEqual(SaveError.Corrupted, error);
            Assert.AreEqual(1, _loadFailedEvents.Count);
            Assert.AreEqual(ESaveFailureStage.HeaderValidation, _loadFailedEvents[0].Stage);
            Assert.AreEqual(SaveError.Corrupted, _loadFailedEvents[0].Error);
            Assert.IsNull(_loadFailedEvents[0].Key, "整档级失败不带块键");
        }

        [Test]
        public void TryLoadBlockCore_CorruptedBlockKey_FiresLoadFailed_ContainerParse()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "a" }, ESaveBackend.Json, 1, CancellationToken.None);
            _handler.SaveBlockCore(paths, "inventory", new SaveData { Gold = 2, PlayerName = "b" }, ESaveBackend.Json, 1, CancellationToken.None);
            _slotEvents.Clear();
            _blockSavedEvents.Clear();

            // 翻转 stats 块载荷并重算头 CRC——头校验放行，逐块 CRC 隔离层拦截
            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            int offset = SaveFileHeader.Size + 12;
            int keyByteCount = BitConverter.ToInt32(fileBytes, offset);
            offset += 4 + keyByteCount + 4 + 2 + 4 + 4;
            fileBytes[offset] ^= 0xFF;
            uint patchedCrc = Crc32.Compute(fileBytes.AsSpan(SaveFileHeader.Size));
            fileBytes[20] = (byte)patchedCrc;
            fileBytes[21] = (byte)(patchedCrc >> 8);
            fileBytes[22] = (byte)(patchedCrc >> 16);
            fileBytes[23] = (byte)(patchedCrc >> 24);
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectWarningLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, "stats", out _);

            Assert.AreEqual(SaveError.Corrupted, error);
            Assert.AreEqual(1, _loadFailedEvents.Count);
            Assert.AreEqual(ESaveFailureStage.ContainerParse, _loadFailedEvents[0].Stage);
            Assert.AreEqual("stats", _loadFailedEvents[0].Key, "逐块失败事件带块键");
        }

        [Test]
        public void TryLoadBlockCore_MissingKey_FiresNothing()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "stats", new SaveData { Gold = 1, PlayerName = "a" }, ESaveBackend.Json, 1, CancellationToken.None);
            _slotEvents.Clear();
            _blockSavedEvents.Clear();

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, "missing", out _);

            Assert.AreEqual(SaveError.FileNotFound, error);
            Assert.AreEqual(0, _loadFailedEvents.Count, "确无块属正常业务流，不产生失败事件");
        }

        [Test]
        public void TryLoadBlockCore_MissingFile_FiresNothing()
        {
            SaveError error = _handler.TryLoadBlockCore<SaveData>(Paths("missing"), "stats", out _);

            Assert.AreEqual(SaveError.FileNotFound, error);
            Assert.AreEqual(0, _loadFailedEvents.Count, "缺档属正常业务流，不产生失败事件");
        }

        [Test]
        public void TryLoadBlockCore_DeserializeFailure_FiresLoadFailed_Deserialize()
        {
            // 手工构造合法容器 + 文件头，块载荷为非法 JSON（Gold 字符串无法入 int）
            var paths = Paths("slot");
            Directory.CreateDirectory(paths.DirectoryPath);
            byte[] garbagePayload = Encoding.UTF8.GetBytes("{\"Gold\":\"not-a-number\"}");
            var blocks = new List<SaveBlockEntry>
            {
                new SaveBlockEntry("stats", 1, ESaveBackend.Json, garbagePayload),
            };
            byte[] container = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(container, blocks);
            byte[] fileBytes = new byte[SaveFileHeader.Size + container.Length];
            SaveFileHeader.Write(fileBytes.AsSpan(0, SaveFileHeader.Size), container.Length, Crc32.Compute(container), 0u, 0u);
            Buffer.BlockCopy(container, 0, fileBytes, SaveFileHeader.Size, container.Length);
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, "stats", out _);

            Assert.AreEqual(SaveError.SerializationFailed, error);
            Assert.AreEqual(1, _loadFailedEvents.Count);
            Assert.AreEqual(ESaveFailureStage.Deserialize, _loadFailedEvents[0].Stage);
            Assert.AreEqual(SaveError.SerializationFailed, _loadFailedEvents[0].Error);
            Assert.AreEqual("stats", _loadFailedEvents[0].Key);
        }

        #endregion

        #region 派发与进度 [DISPATCH / PROGRESS]

        [Test]
        public void Raise_FromBackgroundThread_DispatchesOnMainThread()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int eventThreadId = -1;
            void Handler(SaveBlockChangedArgs args) => eventThreadId = Thread.CurrentThread.ManagedThreadId;
            SaveService.BlockSaved += Handler;
            try
            {
                // 后台线程触发：事件不内联（入队等待主线程泵）
                Task raiseTask = Task.Run(() => SaveService.RaiseBlockSaved("slot", TestFolder, "stats", ESaveBackend.Json, 8));
                raiseTask.Wait(TimeSpan.FromSeconds(10));
                Assert.AreEqual(-1, eventThreadId, "后台线程触发时应入队而非内联");

                MainThreadDispatcher.Pump();
                Assert.AreEqual(mainThreadId, eventThreadId, "入队事件应在主线程泵内派发");
            }
            finally
            {
                SaveService.BlockSaved -= Handler;
            }
        }

        [Test]
        public void Raise_OnMainThread_DispatchesInline()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int eventThreadId = -1;
            void Handler(SaveSlotChangedArgs args) => eventThreadId = Thread.CurrentThread.ManagedThreadId;
            SaveService.SlotChanged += Handler;
            try
            {
                SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Saved, "slot", TestFolder);
                Assert.AreEqual(mainThreadId, eventThreadId, "主线程触发应内联同步派发");
            }
            finally
            {
                SaveService.SlotChanged -= Handler;
            }
        }

        [Test]
        public void ShouldReportProgress_Batches_AndAlwaysReportsFinal()
        {
            Assert.IsTrue(SaveService.ShouldReportProgress(8, 20), "满批应报");
            Assert.IsFalse(SaveService.ShouldReportProgress(9, 20), "批间不报");
            Assert.IsTrue(SaveService.ShouldReportProgress(20, 20), "最终一批必报");
            Assert.IsTrue(SaveService.ShouldReportProgress(3, 3), "总数不足一批时最终必报");
            Assert.IsFalse(SaveService.ShouldReportProgress(1, 3));
        }

        #endregion
    }
}
