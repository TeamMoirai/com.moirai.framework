using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Save
{
    /// <summary>
    /// V3-P9 云存档一体测试：写双发、读策略裁决矩阵（Latest/LocalWins/CloudWins/Custom）、单侧对齐、
    /// 离线降级、待回传 backfill 重放、枚举并集、同步原语镜像直通、云端键规范化。
    /// <para>远端为内存 Fake（含时钟偏移与故障注入；internal + 非 [Serializable]，避免 ProviderDropdown 扫描污染）；
    /// 本地镜像落临时目录（<c>s_OverrideBasePath</c>），时间戳经 <c>File.SetLastWriteTimeUtc</c> 显式控制。</para>
    /// </summary>
    public class SaveCloudStorageBackendTests
    {
        /// <summary>
        /// 内存 Fake 远端 KV（时钟偏移模拟云端权威时钟偏差；故障注入模拟离线/远端失败）。
        /// </summary>
        private sealed class FakeCloudKvStore : CloudSaveKvStore
        {
            public readonly Dictionary<string, CloudKvEntry> Entries = new Dictionary<string, CloudKvEntry>(StringComparer.Ordinal);
            public TimeSpan ClockOffset;
            public bool Offline;
            public int FailNextOperations;

            private DateTime Now => DateTime.UtcNow + ClockOffset;

            private void ThrowIfUnavailable()
            {
                if (FailNextOperations > 0)
                {
                    FailNextOperations--;
                    throw new IOException("fake cloud failure (injected)");
                }

                if (Offline)
                {
                    throw new IOException("fake cloud offline");
                }
            }

            public void Seed(string key, byte[] bytes, DateTime lastWriteTimeUtc)
            {
                Entries[key] = new CloudKvEntry(bytes, lastWriteTimeUtc);
            }

            public override UniTask<CloudKvEntry?> ReadAsync(string key, CancellationToken cancellationToken)
            {
                ThrowIfUnavailable();
                return UniTask.FromResult(Entries.TryGetValue(key, out CloudKvEntry entry) ? (CloudKvEntry?)entry : null);
            }

            public override UniTask<bool> ExistsAsync(string key, CancellationToken cancellationToken)
            {
                ThrowIfUnavailable();
                return UniTask.FromResult(Entries.ContainsKey(key));
            }

            public override UniTask WriteAsync(string key, byte[] bytes, CancellationToken cancellationToken)
            {
                ThrowIfUnavailable();
                Entries[key] = new CloudKvEntry(bytes, Now);
                return UniTask.CompletedTask;
            }

            public override UniTask DeleteAsync(string key, CancellationToken cancellationToken)
            {
                ThrowIfUnavailable();
                Entries.Remove(key);
                return UniTask.CompletedTask;
            }

            public override UniTask<CloudKvEntryInfo[]> EnumerateAsync(CancellationToken cancellationToken)
            {
                ThrowIfUnavailable();
                var infos = new List<CloudKvEntryInfo>(Entries.Count);
                foreach (KeyValuePair<string, CloudKvEntry> pair in Entries)
                {
                    infos.Add(new CloudKvEntryInfo(pair.Key, pair.Value.Bytes.LongLength, pair.Value.LastWriteTimeUtc));
                }

                return UniTask.FromResult(infos.ToArray());
            }
        }

        /// <summary>
        /// 测试裁决器（固定决策注入）。
        /// </summary>
        private sealed class FixedResolver : SaveSyncConflictResolver
        {
            public ESaveSyncDecision Decision;

            public override ESaveSyncDecision Resolve(string key, SaveSyncEntryInfo localEntry, SaveSyncEntryInfo remoteEntry)
            {
                return Decision;
            }
        }

        private const string TestFolder = "Slots";
        private static readonly byte[] s_LocalBytes = { 1, 2, 3, 4 };
        private static readonly byte[] s_RemoteBytes = { 9, 8, 7, 6, 5 };
        private static readonly DateTime s_TimeOld = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_TimeNew = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        private CloudSaveStorageBackend _backend;
        private FakeCloudKvStore _remote;
        private string _rootPath;
        private string _directoryPath;
        private List<(ELogLevel Level, string Message)> _capturedLogs;

        [SetUp]
        public void SetUp()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-cloud-tests-" + Guid.NewGuid().ToString("N"));
            _directoryPath = Path.Combine(_rootPath, SaveServiceHandler.DATA_FOLDER_NAME, TestFolder) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(_directoryPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;

            _remote = new FakeCloudKvStore();
            _backend = new CloudSaveStorageBackend
            {
                RemoteStore = _remote,
                Policy = ESaveSyncPolicy.Latest,
            };

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
        /// 目标槽位的本地完整路径。
        /// </summary>
        private string PathFor(string fileName)
        {
            return Path.Combine(_directoryPath, fileName + ".sav");
        }

        /// <summary>
        /// 目标槽位的云端键。
        /// </summary>
        private static string KeyFor(string fileName)
        {
            return TestFolder + "/" + fileName + ".sav";
        }

        /// <summary>
        /// 写入本地镜像并显式盖章时间戳。
        /// </summary>
        private void WriteMirror(string fileName, byte[] bytes, DateTime lastWriteTimeUtc)
        {
            string path = PathFor(fileName);
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        }

        #region 键规范化 [KEY NORMALIZATION]

        [Test]
        public void ToCloudKey_DataRootedPath_ReturnsRelativeSlashKey()
        {
            string path = Path.Combine(_rootPath, "Data", "Slots", "slot1.sav");
            Assert.AreEqual("Slots/slot1.sav", CloudSaveStorageBackend.ToCloudKey(path));
        }

        [Test]
        public void ToCloudKey_NonRootedPath_NormalizesSeparators()
        {
            Assert.AreEqual("tmp/x.sav", CloudSaveStorageBackend.ToCloudKey("tmp\\x.sav"));
        }

        #endregion

        #region 写双发与回传 [DUAL WRITE / BACKFILL]

        [Test]
        public async Task WriteAtomicAsync_Online_DualWrites()
        {
            await _backend.WriteAtomicAsync(PathFor("slot1"), s_LocalBytes, CancellationToken.None);

            Assert.IsTrue(File.Exists(PathFor("slot1")));
            Assert.IsTrue(_remote.Entries.ContainsKey(KeyFor("slot1")));
            CollectionAssert.AreEqual(s_LocalBytes, _remote.Entries[KeyFor("slot1")].Bytes);
            Assert.AreEqual(0, _backend.PendingUploadCount);
        }

        [Test]
        public async Task WriteAtomicAsync_Offline_CommitsLocalAndBackfillsOnRecovery()
        {
            _remote.Offline = true;
            ExpectWarningLogForUtf();
            await _backend.WriteAtomicAsync(PathFor("slot1"), s_LocalBytes, CancellationToken.None);

            Assert.IsTrue(File.Exists(PathFor("slot1")));
            Assert.IsFalse(_remote.Entries.ContainsKey(KeyFor("slot1")));
            Assert.AreEqual(1, _backend.PendingUploadCount);
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == ELogLevel.Warning));

            _remote.Offline = false;
            // 任意一次远端可达操作触发回传重放（读路径远端缺失 → 上传 + 重放待传集合）
            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            CollectionAssert.AreEqual(s_LocalBytes, read);
            Assert.IsTrue(_remote.Entries.ContainsKey(KeyFor("slot1")));
            Assert.AreEqual(0, _backend.PendingUploadCount);
        }

        [Test]
        public async Task DeleteFileAsync_Offline_PendsDeleteAndReplaysOnRecovery()
        {
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            _remote.Seed(KeyFor("slot1"), s_LocalBytes, s_TimeNew);

            _remote.Offline = true;
            ExpectWarningLogForUtf();
            await _backend.DeleteFileAsync(PathFor("slot1"), CancellationToken.None);
            Assert.IsFalse(File.Exists(PathFor("slot1")));
            Assert.IsTrue(_remote.Entries.ContainsKey(KeyFor("slot1")));
            Assert.AreEqual(1, _backend.PendingDeleteCount);

            _remote.Offline = false;
            await _backend.WriteAtomicAsync(PathFor("slot2"), s_LocalBytes, CancellationToken.None);
            Assert.IsFalse(_remote.Entries.ContainsKey(KeyFor("slot1")));
            Assert.AreEqual(0, _backend.PendingDeleteCount);
        }

        #endregion

        #region 读策略裁决 [READ ARBITRATION]

        [Test]
        public async Task ReadAllBytesAsync_Latest_RemoteNewer_ReturnsRemoteAndRefreshesMirror()
        {
            WriteMirror("slot1", s_LocalBytes, s_TimeOld);
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeNew);

            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            CollectionAssert.AreEqual(s_RemoteBytes, read);
            CollectionAssert.AreEqual(s_RemoteBytes, File.ReadAllBytes(PathFor("slot1")));
            Assert.AreEqual(s_TimeNew, File.GetLastWriteTimeUtc(PathFor("slot1")));
        }

        [Test]
        public async Task ReadAllBytesAsync_Latest_LocalNewer_KeepsLocalAndUploads()
        {
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeOld);

            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            CollectionAssert.AreEqual(s_LocalBytes, read);
            CollectionAssert.AreEqual(s_LocalBytes, _remote.Entries[KeyFor("slot1")].Bytes);
        }

        [Test]
        public async Task ReadAllBytesAsync_LocalWins_IgnoresNewerRemote()
        {
            _backend.Policy = ESaveSyncPolicy.LocalWins;
            WriteMirror("slot1", s_LocalBytes, s_TimeOld);
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeNew);

            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            CollectionAssert.AreEqual(s_LocalBytes, read);
            CollectionAssert.AreEqual(s_LocalBytes, File.ReadAllBytes(PathFor("slot1")));
        }

        [Test]
        public async Task ReadAllBytesAsync_CloudWins_ReturnsRemoteAndRefreshesMirror()
        {
            _backend.Policy = ESaveSyncPolicy.CloudWins;
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeOld);

            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            CollectionAssert.AreEqual(s_RemoteBytes, read);
            CollectionAssert.AreEqual(s_RemoteBytes, File.ReadAllBytes(PathFor("slot1")));
        }

        [Test]
        public async Task ReadAllBytesAsync_CloudWins_Offline_FallsBackToMirror()
        {
            _backend.Policy = ESaveSyncPolicy.CloudWins;
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeNew);
            _remote.Offline = true;

            ExpectWarningLogForUtf();
            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            CollectionAssert.AreEqual(s_LocalBytes, read);
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == ELogLevel.Warning));
        }

        [Test]
        public async Task ReadAllBytesAsync_Custom_ResolverDecides()
        {
            _backend.Policy = ESaveSyncPolicy.Custom;
            var resolver = new FixedResolver { Decision = ESaveSyncDecision.UseRemote };
            _backend.ConflictResolver = resolver;
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeOld);

            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            CollectionAssert.AreEqual(s_RemoteBytes, read);

            resolver.Decision = ESaveSyncDecision.UseLocal;
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            CollectionAssert.AreEqual(s_LocalBytes, read);
        }

        [Test]
        public async Task ReadAllBytesAsync_CustomWithoutResolver_FallsBackToLatestWithWarning()
        {
            _backend.Policy = ESaveSyncPolicy.Custom;
            WriteMirror("slot1", s_LocalBytes, s_TimeOld);
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeNew);

            ExpectWarningLogForUtf();
            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            CollectionAssert.AreEqual(s_RemoteBytes, read);
        }

        [Test]
        public async Task ReadAllBytesAsync_RemoteOnly_DownloadsToMirror()
        {
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeNew);

            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            CollectionAssert.AreEqual(s_RemoteBytes, read);
            Assert.IsTrue(File.Exists(PathFor("slot1")));
            Assert.AreEqual(s_TimeNew, File.GetLastWriteTimeUtc(PathFor("slot1")));
        }

        [Test]
        public async Task ReadAllBytesAsync_BothMissing_ReturnsNull()
        {
            byte[] read = await _backend.ReadAllBytesAsync(PathFor("missing"), CancellationToken.None);
            Assert.IsNull(read);
        }

        #endregion

        #region 枚举与同步原语 [ENUMERATION / SYNC PRIMITIVES]

        [Test]
        public async Task EnumerateFilesAsync_Latest_UnionPicksNewer()
        {
            WriteMirror("slot-local-only", s_LocalBytes, s_TimeOld);
            WriteMirror("slot-both", s_LocalBytes, s_TimeOld);
            _remote.Seed(KeyFor("slot-both"), s_RemoteBytes, s_TimeNew);
            _remote.Seed(KeyFor("slot-remote-only"), s_RemoteBytes, s_TimeNew);

            SaveFileInfo[] files = await _backend.EnumerateFilesAsync(_directoryPath, ".sav", CancellationToken.None);
            Assert.AreEqual(3, files.Length);
            // 按时间倒序：两个 New 在前（次序不定），Old 殿后
            Assert.AreEqual(s_TimeOld, files[2].LastWriteTimeUtc);
            var names = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                names.Add(files[i].FileName);
            }

            CollectionAssert.AreEquivalent(new[] { "slot-local-only", "slot-both", "slot-remote-only" }, names);
        }

        [Test]
        public async Task EnumerateFilesAsync_Offline_FallsBackToMirror()
        {
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            _remote.Seed(KeyFor("slot2"), s_RemoteBytes, s_TimeNew);
            _remote.Offline = true;

            ExpectWarningLogForUtf();
            SaveFileInfo[] files = await _backend.EnumerateFilesAsync(_directoryPath, ".sav", CancellationToken.None);
            Assert.AreEqual(1, files.Length);
            Assert.AreEqual("slot1", files[0].FileName);
        }

        [Test]
        public void SyncPrimitives_OperateOnMirrorOnly()
        {
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);

            Assert.IsTrue(_backend.Exists(PathFor("slot1")));
            Assert.AreEqual(SaveError.None, _backend.TryReadAllBytes(PathFor("slot1"), out byte[] bytes));
            CollectionAssert.AreEqual(s_LocalBytes, bytes);

            // 同步写只落镜像、不触远端
            _backend.WriteAtomic(PathFor("slot2"), s_LocalBytes, CancellationToken.None);
            Assert.IsTrue(File.Exists(PathFor("slot2")));
            Assert.IsFalse(_remote.Entries.ContainsKey(KeyFor("slot2")));
        }

        #endregion
    }
}
