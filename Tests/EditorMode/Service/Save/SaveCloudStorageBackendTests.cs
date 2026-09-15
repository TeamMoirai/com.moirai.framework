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

            /// <summary>单调修订号发生器（远端写入递增；版本通道裁决依据）。</summary>
            public long NextVersion;

            /// <summary>远端写调用计数（冷却抑制断言用）。</summary>
            public int WriteCallCount;

            /// <summary>仅写路径离线（读可达——镜像独有回传场景注入）。</summary>
            public bool WritesOffline;

            /// <summary>最近一次前缀枚举请求（前缀下推断言用；null = 未走前缀重载）。</summary>
            public string LastEnumeratePrefix;

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

            public void Seed(string key, byte[] bytes, DateTime lastWriteTimeUtc, long version = 0L)
            {
                Entries[key] = new CloudKvEntry(bytes, lastWriteTimeUtc, version);
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

            public override UniTask<long> WriteAsync(string key, byte[] bytes, CancellationToken cancellationToken)
            {
                WriteCallCount++;
                if (WritesOffline)
                {
                    throw new IOException("fake cloud write offline");
                }

                ThrowIfUnavailable();
                long version = ++NextVersion;
                Entries[key] = new CloudKvEntry(bytes, Now, version);
                return UniTask.FromResult(version);
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
                    infos.Add(new CloudKvEntryInfo(pair.Key, pair.Value.Bytes.LongLength, pair.Value.LastWriteTimeUtc, pair.Value.Version));
                }

                return UniTask.FromResult(infos.ToArray());
            }

            public override UniTask<CloudKvEntryInfo[]> EnumerateAsync(string prefix, CancellationToken cancellationToken)
            {
                LastEnumeratePrefix = prefix;
                return base.EnumerateAsync(prefix, cancellationToken);
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

        /// <summary>
        /// 写入镜像版本 sidecar（<c>{file}.cloudver</c>）并把 sidecar mtime 与镜像 mtime 对齐（干净态）。
        /// </summary>
        private void WriteMirrorVersionSidecar(string fileName, long version)
        {
            string path = PathFor(fileName);
            string sidecarPath = path + ".cloudver";
            File.WriteAllBytes(sidecarPath, BitConverter.GetBytes(version));
            File.SetLastWriteTimeUtc(sidecarPath, File.GetLastWriteTimeUtc(path));
        }

        /// <summary>
        /// 读取镜像版本 sidecar（不存在返回 0）。
        /// </summary>
        private static long ReadMirrorVersionSidecar(string filePath)
        {
            string sidecarPath = filePath + ".cloudver";
            return File.Exists(sidecarPath) ? BitConverter.ToInt64(File.ReadAllBytes(sidecarPath), 0) : 0L;
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

        #region 版本通道裁决（去时钟化） [VERSION-CHANNEL RESOLUTION]

        [Test]
        public async Task ReadAllBytesAsync_Latest_RemoteNewerByVersion_Downloads_DespiteLocalClockAhead()
        {
            // 远端 v5（旧远端时间戳）；镜像时间戳被前拨（客户端时钟超前）——时间戳裁决会误判本地新，版本通道必须纠正
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeOld, version: 5L);
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            WriteMirrorVersionSidecar("slot1", 4L);

            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);

            CollectionAssert.AreEqual(s_RemoteBytes, read, "远端 v5 > 镜像已同步 v4 → 下载（客户端时钟偏移不参与裁决）");
            Assert.AreEqual(5L, ReadMirrorVersionSidecar(PathFor("slot1")), "下载后镜像版本推进到 v5");
        }

        [Test]
        public async Task ReadAllBytesAsync_Latest_InSyncVersion_CleanMirror_NoTransfer()
        {
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeNew, version: 5L);
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            WriteMirrorVersionSidecar("slot1", 5L);

            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);

            CollectionAssert.AreEqual(s_LocalBytes, read, "版本相等且镜像干净 → 不下载");
            Assert.AreEqual(5L, _remote.Entries[KeyFor("slot1")].Version, "镜像干净 → 不上传（远端版本不变）");
            CollectionAssert.AreEqual(s_RemoteBytes, _remote.Entries[KeyFor("slot1")].Bytes);
        }

        [Test]
        public async Task ReadAllBytesAsync_Latest_InSyncVersion_DirtyMirror_Uploads()
        {
            // 干净对齐 v5 后镜像被本地改写（同步原语直通不更新 sidecar → mtime 失配 = 脏）
            byte[] newLocalBytes = { 7, 7, 7 };
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeNew, version: 5L);
            _remote.NextVersion = 5L; // 修订号发生器与已播种版本对齐（下次远端写入分配 6）
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            WriteMirrorVersionSidecar("slot1", 5L);
            string path = PathFor("slot1");
            File.WriteAllBytes(path, newLocalBytes);
            File.SetLastWriteTimeUtc(path, s_TimeNew.AddSeconds(1));

            byte[] read = await _backend.ReadAllBytesAsync(path, CancellationToken.None);

            CollectionAssert.AreEqual(newLocalBytes, read, "脏镜像为本地事实——读返回本地内容");
            CollectionAssert.AreEqual(newLocalBytes, _remote.Entries[KeyFor("slot1")].Bytes, "脏镜像应回传远端");
            Assert.AreEqual(6L, _remote.Entries[KeyFor("slot1")].Version, "远端分配新修订号");
            Assert.AreEqual(6L, ReadMirrorVersionSidecar(path), "上传后镜像版本推进");
        }

        [Test]
        public async Task ReadAllBytesAsync_Latest_NoVersionChannel_FallsBackToTimestamp()
        {
            // 远端不提供版本号（0）——回退时间戳裁决（远端时间戳新者优先）
            _remote.Seed(KeyFor("slot1"), s_RemoteBytes, s_TimeNew);
            WriteMirror("slot1", s_LocalBytes, s_TimeOld);

            byte[] read = await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);

            CollectionAssert.AreEqual(s_RemoteBytes, read, "无版本通道时保持时间戳裁决语义");
            Assert.AreEqual(0L, ReadMirrorVersionSidecar(PathFor("slot1")), "无版本后端不产生 sidecar");
        }

        #endregion

        #region 前缀下推与回传冷却 [PREFIX PUSH-DOWN / UPLOAD COOLDOWN]

        [Test]
        public async Task EnumerateFilesAsync_PassesDirectoryPrefixToStore()
        {
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            _remote.Seed(KeyFor("slot2"), s_RemoteBytes, s_TimeNew);

            await _backend.EnumerateFilesAsync(_directoryPath, ".sav", CancellationToken.None);

            Assert.AreEqual(TestFolder + "/", _remote.LastEnumeratePrefix, "枚举必须下推目录前缀（服务端过滤接入点）");
        }

        [Test]
        public async Task ReadAllBytesAsync_MirrorOnly_FailingUpload_CooldownSuppressesRetryStorm()
        {
            // 镜像独有 + 远端读可达但写持续失败：冷却期内读不再额外触发镜像独有补传
            // （每次读仍各有一次 backfill 重放尝试——重放是独立机制，冷却只抑制读触发的补传）
            WriteMirror("slot1", s_LocalBytes, s_TimeNew);
            _remote.WritesOffline = true;

            ExpectWarningLogForUtf();
            await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            int afterFirstRead = _remote.WriteCallCount;
            Assert.AreEqual(1, afterFirstRead, "首次读经镜像独有分支触发一次补传尝试");
            Assert.AreEqual(1, _backend.PendingUploadCount);

            // 冷却期内（默认 30s）：读只经 backfill 重放尝试一次，镜像独有补传被抑制
            await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            int afterSecondRead = _remote.WriteCallCount;
            Assert.AreEqual(afterFirstRead + 1, afterSecondRead, "冷却期内读不再额外触发镜像独有补传（仅重放一次）");

            // 冷却过期：读恢复「重放 + 镜像独有补传」两次尝试
            _backend.UploadRetryCooldown = TimeSpan.Zero;
            await _backend.ReadAllBytesAsync(PathFor("slot1"), CancellationToken.None);
            Assert.AreEqual(afterSecondRead + 2, _remote.WriteCallCount, "冷却过期后恢复镜像独有补传尝试");
        }

        #endregion
    }
}
