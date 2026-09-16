using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 云存档存储后端：本地镜像 + 远端 KV 双写，读按 <see cref="ESaveSyncPolicy"/> 裁决（CrystalSave 模式）。
    /// <para>写入双发（本地镜像原子写提交后远端跟随；远端失败不阻断本地提交——记入待回传集合，下次远端操作成功时 backfill 回传）；
    /// 删除同理（待删集合回传）；离线（远端抛异常）降级为本地镜像直通并记告警。</para>
    /// <para>冲突裁决去时钟化：远端提供单调修订号（<see cref="CloudKvEntry.Version"/> &gt; 0）时按版本号裁决——
    /// 镜像已同步修订号记录于 <c>{file}.cloudver</c> sidecar，镜像脏判定用镜像与 sidecar 的本地 mtime 失配（同为本地时钟）；
    /// 客户端时钟与远端时钟的偏移不参与裁决。后端不提供版本号时回退时间戳比较（远端权威时钟 vs 镜像戳——下载已转写远端戳）。</para>
    /// <para>同步原语仅作用于本地镜像（同步 API 不见远端；远端同步由异步 API 族驱动）；
    /// 单槽备份（<c>.bak</c>）为本地概念，不随云同步。</para>
    /// <para>目录级删除对远端按前缀尽力删除（失败记待删前缀，回传时重放）。</para>
    /// </summary>
    [Serializable]
    public class CloudSaveStorageBackend : SaveStorageBackend
    {
        [Tooltip("远端 KV 存储：云存档的远端下沉目标（具体后端随项目接入——Unity Cloud Save / 自定义 REST 等）。空 = 纯本地镜像（退化为本地文件后端）。")]
        [ProviderDropdown]
        [SerializeReference] private CloudSaveKvStore m_RemoteStore;

        [Tooltip("同步冲突策略：读裁决与回传规则（Latest 时间戳新者优先 / LocalWins 本地权威 / CloudWins 云端权威 / Custom 逐键裁决）。")]
        [SerializeField] private ESaveSyncPolicy m_Policy = ESaveSyncPolicy.Latest;

        [ShowIf(nameof(IsCustomPolicy))]
        [Tooltip("自定义冲突裁决器（Custom 策略时生效；未配置时回退 Latest 并记告警）。")]
        [ProviderDropdown]
        [SerializeReference] private SaveSyncConflictResolver m_ConflictResolver;

        /// <summary>镜像版本 sidecar 后缀（记录镜像已同步的远端修订号——版本通道裁决依据）。</summary>
        private const string MIRROR_VERSION_SUFFIX = ".cloudver";

        /// <summary>待回传/待删集合锁（并发远端操作共享；取改极快，非热路径）。</summary>
        [NonSerialized] private readonly object _syncLock = new object();

        /// <summary>镜像独有回传的上次尝试时间（云端键 → UTC ticks；远端持续失败时冷却期内读路径不再重试——积压由 backfill 重放兜底）。</summary>
        [NonSerialized] private readonly Dictionary<string, long> _uploadLastAttemptUtcTicks = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>镜像独有回传的失败冷却时长（默认 30s；测试可注入零值强制立即重试）。</summary>
        [NonSerialized] internal TimeSpan UploadRetryCooldown = TimeSpan.FromSeconds(30);

        /// <summary>待回传上传（云端键 → 本地镜像路径；远端写失败时登记，下次远端操作成功时重放）。</summary>
        [NonSerialized] private readonly Dictionary<string, string> _pendingUploads = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>待回传单键删除（远端删失败时登记）。</summary>
        [NonSerialized] private readonly HashSet<string> _pendingDeletes = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>待回传前缀删除（目录级删除的远端枚举/删除失败时登记）。</summary>
        [NonSerialized] private readonly List<string> _pendingDeletePrefixes = new List<string>();

        /// <summary>回传重放入口守卫（防并发重入）。</summary>
        [NonSerialized] private bool _replaying;

        private bool IsCustomPolicy => m_Policy == ESaveSyncPolicy.Custom;

        /// <summary>本地镜像（无状态共享实例；任意线程安全）。</summary>
        private static FileSaveStorageBackend Mirror => FileSaveStorageBackend.Default;

        /// <summary>
        /// 后端能力自描述（镜像原子写 + 远端整值替换视为原子；远端网络 IO 为真异步；不设尺寸上限；非易失；
        /// 同步读非权威——同步原语仅作用本地镜像，远端内容须经异步 API 族裁决获取）。
        /// </summary>
        public override SaveStorageCapabilities Capabilities => new SaveStorageCapabilities(
            supportsAtomicRename: true,
            supportsTrueAsyncIO: true,
            maxRecommendedSize: -1L,
            volatileStorage: false,
            syncReadsAuthoritative: false);

        /// <summary>远端存储注入点（测试/代码装配用；Inspector 配置走序列化字段）。</summary>
        internal CloudSaveKvStore RemoteStore
        {
            get => m_RemoteStore;
            set => m_RemoteStore = value;
        }

        /// <summary>冲突策略注入点（测试/代码装配用）。</summary>
        internal ESaveSyncPolicy Policy
        {
            get => m_Policy;
            set => m_Policy = value;
        }

        /// <summary>自定义裁决器注入点（测试/代码装配用）。</summary>
        internal SaveSyncConflictResolver ConflictResolver
        {
            get => m_ConflictResolver;
            set => m_ConflictResolver = value;
        }

        /// <summary>待回传上传数（测试观测用）。</summary>
        internal int PendingUploadCount
        {
            get { lock (_syncLock) { return _pendingUploads.Count; } }
        }

        /// <summary>待回传删除数（单键 + 前缀；测试观测用）。</summary>
        internal int PendingDeleteCount
        {
            get { lock (_syncLock) { return _pendingDeletes.Count + _pendingDeletePrefixes.Count; } }
        }

        /// <summary>
        /// 本地路径转云端键（取存档数据根目录 <c>Data/</c> 之后的相对路径，<c>/</c> 分隔；根外路径退化为全路径规范化）。
        /// </summary>
        /// <param name="filePath">本地完整路径。</param>
        /// <returns>云端键。</returns>
        internal static string ToCloudKey(string filePath)
        {
            string normalized = filePath.Replace('\\', '/');
            string marker = "/" + SaveServiceHandler.DATA_FOLDER_NAME + "/";
            int index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? normalized.Substring(index + marker.Length) : normalized.TrimStart('/');
        }

        #region 同步原语（镜像直通） [SYNC PRIMITIVES — MIRROR ONLY]

        /// <summary>
        /// 是否存在目标文件（仅查本地镜像；远端状态不影响同步 API）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <returns>镜像存在返回 <c>true</c>。</returns>
        public override bool Exists(string filePath)
        {
            return Mirror.Exists(filePath);
        }

        /// <summary>
        /// 是否存在目标目录（仅查本地镜像；远端状态不影响同步 API）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <returns>镜像存在返回 <c>true</c>。</returns>
        public override bool DirectoryExists(string directoryPath)
        {
            return Mirror.DirectoryExists(directoryPath);
        }

        /// <summary>
        /// 查询本地镜像的最后写入时间（远端状态不影响同步 API——镜像时间是下载/本地提交时间，非远端权威时钟）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="writeTimeUtc">成功时的最后写入时间（UTC）。</param>
        /// <returns>镜像存在返回 <c>true</c>；缺档/查询失败返回 <c>false</c>。</returns>
        public override bool TryGetWriteTimeUtc(string filePath, out DateTime writeTimeUtc)
        {
            return Mirror.TryGetWriteTimeUtc(filePath, out writeTimeUtc);
        }

        /// <summary>
        /// 读取本地镜像字节（远端不参与同步读；策略裁决在 <see cref="ReadAllBytesAsync"/>）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="bytes">成功时的文件字节。</param>
        /// <returns>错误码（镜像直通）。</returns>
        public override SaveError TryReadAllBytes(string filePath, out byte[] bytes)
        {
            return Mirror.TryReadAllBytes(filePath, out bytes);
        }

        /// <summary>
        /// 原子写入本地镜像（不同步远端——远端同步由异步 API 族驱动；语义见类注释）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="bytes">完整文件字节。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public override void WriteAtomic(string filePath, byte[] bytes, CancellationToken cancellationToken)
        {
            Mirror.WriteAtomic(filePath, bytes, cancellationToken);
        }

        /// <summary>
        /// 原子写入本地镜像（两段式：镜像为文件后端，真分段落盘零拼接；不同步远端）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="head">文件头部字节（先写入）。</param>
        /// <param name="payload">载荷字节（头部之后写入）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public override void WriteAtomic(string filePath, ReadOnlySpan<byte> head, ReadOnlySpan<byte> payload, CancellationToken cancellationToken)
        {
            Mirror.WriteAtomic(filePath, head, payload, cancellationToken);
        }

        /// <summary>
        /// 删除本地镜像文件（幂等；远端删除由异步 API 族驱动）。
        /// <para>级联清理版本 sidecar——残留版本记录会让同名新档的裁决误用陈旧修订号。</para>
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        public override void DeleteFile(string filePath)
        {
            Mirror.DeleteFile(filePath);
            DeleteMirrorVersion(filePath);
        }

        /// <summary>
        /// 递归删除本地镜像目录树（幂等）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        public override void DeleteDirectory(string directoryPath)
        {
            Mirror.DeleteDirectory(directoryPath);
        }

        /// <summary>
        /// 枚举本地镜像目录内文件（按最后写入时间倒序；远端枚举见 <see cref="EnumerateFilesAsync"/>）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <param name="extension">扩展名（含点，精确匹配）。</param>
        /// <returns>镜像文件元信息数组。</returns>
        public override SaveFileInfo[] EnumerateFiles(string directoryPath, string extension)
        {
            return Mirror.EnumerateFiles(directoryPath, extension);
        }

        /// <summary>
        /// 创建单槽备份（本地概念，不随云同步；覆盖旧备份）。
        /// </summary>
        /// <param name="filePath">源文件完整路径。</param>
        public override void CreateBackup(string filePath)
        {
            Mirror.CreateBackup(filePath);
        }

        /// <summary>
        /// 从单槽备份恢复（本地概念，不随云同步）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        public override void RestoreBackup(string filePath)
        {
            Mirror.RestoreBackup(filePath);
        }

        /// <summary>
        /// 清扫本地镜像孤儿临时文件（远端无临时文件语义）。
        /// </summary>
        /// <param name="rootDirectory">存档数据根目录。</param>
        public override void CleanupOrphanTempFiles(string rootDirectory)
        {
            Mirror.CleanupOrphanTempFiles(rootDirectory);
        }

        #endregion

        #region 异步覆盖（镜像 + 远端协调） [ASYNC OVERRIDES — CLOUD COORDINATION]

        /// <summary>
        /// 异步存在性检查（镜像命中即真；镜像缺失时查远端——远端失败降级返回镜像结论并记告警）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public override async UniTask<bool> ExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (Mirror.Exists(filePath))
            {
                return true;
            }

            CloudSaveKvStore remote = m_RemoteStore;
            if (remote == null)
            {
                return false;
            }

            try
            {
                bool exists = await remote.ExistsAsync(ToCloudKey(filePath), cancellationToken);
                await ReplayPendingAsync(remote, cancellationToken);
                return exists;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogRemoteFailure("Exists", filePath, exception);
                return false;
            }
        }

        /// <summary>
        /// 异步读取（策略裁决：LocalWins 读镜像 / CloudWins 读远端 / Latest 取新 / Custom 逐键裁决；远端失败降级镜像并记告警）。
        /// <para>单侧存在时自动对齐另一侧（远端独有 → 下载刷新镜像；镜像独有 → 回传补传远端）。</para>
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件字节；双侧均缺档或镜像失败时为 <c>null</c>。</returns>
        public override async UniTask<byte[]> ReadAllBytesAsync(string filePath, CancellationToken cancellationToken = default)
        {
            SaveError localError = Mirror.TryReadAllBytes(filePath, out byte[] localBytes);
            CloudSaveKvStore remote = m_RemoteStore;
            if (remote == null)
            {
                return localError == SaveError.None ? localBytes : null;
            }

            string cloudKey = ToCloudKey(filePath);
            CloudKvEntry? remoteEntry;
            try
            {
                remoteEntry = await remote.ReadAsync(cloudKey, cancellationToken);
                await ReplayPendingAsync(remote, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogRemoteFailure("Read", filePath, exception);
                return localError == SaveError.None ? localBytes : null;
            }

            bool hasLocal = localError == SaveError.None;
            if (!remoteEntry.HasValue && !hasLocal)
            {
                return null;
            }

            if (!remoteEntry.HasValue)
            {
                // 镜像独有 → 回传远端（离线落本地后的恢复路径同样走这里；失败冷却期内不重试——积压由 backfill 重放兜底）
                if (!IsUploadCoolingDown(cloudKey))
                {
                    await UploadAsync(remote, cloudKey, filePath, localBytes, cancellationToken);
                }

                return localBytes;
            }

            if (!hasLocal)
            {
                // 远端独有 → 下载刷新镜像
                RefreshMirror(filePath, remoteEntry.Value);
                return remoteEntry.Value.Bytes;
            }

            DateTime localTime = File.GetLastWriteTimeUtc(filePath);
            switch (m_Policy)
            {
                case ESaveSyncPolicy.LocalWins:
                    if (ShouldUpload(filePath, localTime, remoteEntry.Value))
                    {
                        await UploadAsync(remote, cloudKey, filePath, localBytes, cancellationToken);
                    }

                    return localBytes;

                case ESaveSyncPolicy.CloudWins:
                    RefreshMirror(filePath, remoteEntry.Value);
                    return remoteEntry.Value.Bytes;

                case ESaveSyncPolicy.Custom:
                    if (ResolveCustom(cloudKey, filePath, localTime, localBytes.LongLength, remoteEntry.Value.LastWriteTimeUtc, remoteEntry.Value.Bytes?.LongLength ?? 0L, remoteEntry.Value.Version) == ESaveSyncDecision.UseRemote)
                    {
                        RefreshMirror(filePath, remoteEntry.Value);
                        return remoteEntry.Value.Bytes;
                    }

                    if (ShouldUpload(filePath, localTime, remoteEntry.Value))
                    {
                        await UploadAsync(remote, cloudKey, filePath, localBytes, cancellationToken);
                    }

                    return localBytes;

                default:
                    // Latest：远端提供版本号且镜像有同步记录时按修订号裁决（去时钟化），否则回退时间戳比较
                    if (ShouldDownload(filePath, localTime, remoteEntry.Value))
                    {
                        RefreshMirror(filePath, remoteEntry.Value);
                        return remoteEntry.Value.Bytes;
                    }

                    if (ShouldUpload(filePath, localTime, remoteEntry.Value))
                    {
                        await UploadAsync(remote, cloudKey, filePath, localBytes, cancellationToken);
                    }

                    return localBytes;
            }
        }

        /// <summary>
        /// 异步原子写入（本地镜像原子提交后双发远端；远端失败不阻断——记待回传，下次远端操作成功时 backfill）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="bytes">完整文件字节。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>写入完成的异步任务；本地镜像失败抛 <see cref="GameException"/>。</returns>
        public override async UniTask WriteAtomicAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
        {
            Mirror.WriteAtomic(filePath, bytes, cancellationToken);
            CloudSaveKvStore remote = m_RemoteStore;
            if (remote == null)
            {
                return;
            }

            string cloudKey = ToCloudKey(filePath);
            try
            {
                long version = await remote.WriteAsync(cloudKey, bytes, cancellationToken);
                WriteMirrorVersion(filePath, version);
                await ReplayPendingAsync(remote, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lock (_syncLock)
                {
                    _pendingUploads[cloudKey] = filePath;
                }

                LogUtility.Warning("[SaveService] Cloud write deferred (local committed, will backfill), key: {0}, exception: {1}.", cloudKey, exception.GetType().Name);
            }
        }

        /// <summary>
        /// 异步删除（镜像幂等删除后远端跟随；远端失败记待删，下次远端操作成功时回传重放）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public override async UniTask DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Mirror.DeleteFile(filePath);
            DeleteMirrorVersion(filePath);
            CloudSaveKvStore remote = m_RemoteStore;
            if (remote == null)
            {
                return;
            }

            string cloudKey = ToCloudKey(filePath);
            try
            {
                await remote.DeleteAsync(cloudKey, cancellationToken);
                await ReplayPendingAsync(remote, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lock (_syncLock)
                {
                    _pendingDeletes.Add(cloudKey);
                }

                LogUtility.Warning("[SaveService] Cloud delete deferred (local deleted, will backfill), key: {0}, exception: {1}.", cloudKey, exception.GetType().Name);
            }
        }

        /// <summary>
        /// 异步递归删除目录树（镜像幂等删除 + 远端按前缀尽力删除；远端失败记待删前缀，回传时重放）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public override async UniTask DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            Mirror.DeleteDirectory(directoryPath);
            CloudSaveKvStore remote = m_RemoteStore;
            if (remote == null)
            {
                return;
            }

            string prefix = ToCloudKey(directoryPath);
            try
            {
                await DeleteRemotePrefixAsync(remote, prefix, cancellationToken);
                await ReplayPendingAsync(remote, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lock (_syncLock)
                {
                    _pendingDeletePrefixes.Add(prefix);
                }

                LogUtility.Warning("[SaveService] Cloud directory delete deferred (local deleted, will backfill), prefix: {0}, exception: {1}.", prefix, exception.GetType().Name);
            }
        }

        #endregion

        #region 枚举与裁决辅助 [ENUMERATION / DECISION HELPERS]

        /// <summary>
        /// 异步枚举目录内文件（LocalWins/未配远端 = 镜像直通；CloudWins = 远端清单（离线降级镜像）；Latest/Custom = 双侧并集按策略取舍；远端失败降级镜像并记告警）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <param name="extension">扩展名（含点，精确匹配）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件元信息数组（按最后写入时间倒序）。</returns>
        public override async UniTask<SaveFileInfo[]> EnumerateFilesAsync(string directoryPath, string extension, CancellationToken cancellationToken = default)
        {
            SaveFileInfo[] localFiles = Mirror.EnumerateFiles(directoryPath, extension);
            CloudSaveKvStore remote = m_RemoteStore;
            if (remote == null || m_Policy == ESaveSyncPolicy.LocalWins)
            {
                return localFiles;
            }

            string prefix = ToCloudKey(directoryPath);
            CloudKvEntryInfo[] remoteEntries;
            try
            {
                remoteEntries = await remote.EnumerateAsync(prefix, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogRemoteFailure("Enumerate", directoryPath, exception);
                return localFiles;
            }

            List<RemoteFileEntry> remoteFiles = FilterRemoteEntries(remoteEntries, prefix, extension);
            if (m_Policy == ESaveSyncPolicy.CloudWins)
            {
                var cloudWinsFiles = new List<SaveFileInfo>(remoteFiles.Count);
                for (int i = 0; i < remoteFiles.Count; i++)
                {
                    cloudWinsFiles.Add(remoteFiles[i].Info);
                }

                cloudWinsFiles.Sort(CompareNewestFirst);
                return cloudWinsFiles.ToArray();
            }

            // Latest / Custom：双侧并集按名取舍
            var merged = new Dictionary<string, SaveFileInfo>(StringComparer.Ordinal);
            for (int i = 0; i < localFiles.Length; i++)
            {
                merged[localFiles[i].FileName] = localFiles[i];
            }

            for (int i = 0; i < remoteFiles.Count; i++)
            {
                RemoteFileEntry remoteFile = remoteFiles[i];
                if (!merged.TryGetValue(remoteFile.Info.FileName, out SaveFileInfo localFile))
                {
                    merged[remoteFile.Info.FileName] = remoteFile.Info;
                    continue;
                }

                bool useRemote = m_Policy == ESaveSyncPolicy.Custom
                    ? ResolveCustom(prefix + remoteFile.Info.FileName + extension, directoryPath + remoteFile.Info.FileName + extension, localFile.LastWriteTimeUtc, localFile.SizeBytes, remoteFile.Info.LastWriteTimeUtc, remoteFile.Info.SizeBytes, remoteFile.Version) == ESaveSyncDecision.UseRemote
                    : ShouldDownload(directoryPath + remoteFile.Info.FileName + extension, localFile.LastWriteTimeUtc, new CloudKvEntry(null, remoteFile.Info.LastWriteTimeUtc, remoteFile.Version));
                if (useRemote)
                {
                    merged[remoteFile.Info.FileName] = remoteFile.Info;
                }
            }

            var results = new List<SaveFileInfo>(merged.Values);
            results.Sort(CompareNewestFirst);
            return results.Count == 0 ? Array.Empty<SaveFileInfo>() : results.ToArray();
        }

        /// <summary>
        /// 最新者优先比较器（按最后写入时间倒序）。
        /// </summary>
        private static int CompareNewestFirst(SaveFileInfo left, SaveFileInfo right)
        {
            return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
        }

        /// <summary>
        /// 远端文件条目（枚举裁决用：本地同构的文件元信息 + 远端修订号）。
        /// </summary>
        private readonly struct RemoteFileEntry
        {
            /// <summary>文件元信息（与本地同构）。</summary>
            internal readonly SaveFileInfo Info;

            /// <summary>远端单调修订号（0 = 后端不提供版本号）。</summary>
            internal readonly long Version;

            internal RemoteFileEntry(SaveFileInfo info, long version)
            {
                Info = info;
                Version = version;
            }
        }

        /// <summary>
        /// 过滤远端清单为目录内存档条目（前缀匹配 + 扩展名精确匹配 + 不深入子目录；文件名去扩展名与本地同构；携带远端修订号）。
        /// </summary>
        /// <param name="entries">远端条目清单。</param>
        /// <param name="prefix">目录云端前缀（<c>/</c> 结尾）。</param>
        /// <param name="extension">扩展名（含点）。</param>
        /// <returns>过滤后的远端文件条目列表。</returns>
        private static List<RemoteFileEntry> FilterRemoteEntries(CloudKvEntryInfo[] entries, string prefix, string extension)
        {
            var results = new List<RemoteFileEntry>();
            for (int i = 0; i < entries.Length; i++)
            {
                string key = entries[i].Key;
                if (string.IsNullOrEmpty(key) || !key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string relativeName = key.Substring(prefix.Length);
                if (relativeName.Length == 0 || relativeName.IndexOf('/') >= 0 || !relativeName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fileName = relativeName.Substring(0, relativeName.Length - extension.Length);
                results.Add(new RemoteFileEntry(new SaveFileInfo(fileName, entries[i].SizeBytes, entries[i].LastWriteTimeUtc), entries[i].Version));
            }

            return results;
        }

        /// <summary>
        /// 自定义裁决（未配置裁决器时回退 Latest 并记告警）。裁决条目携带版本号（远端修订号 / 镜像已同步修订号），无版本信息时为 0。
        /// <para>远端尺寸由调用方显式传入——读路径取载荷长度，枚举路径取清单 SizeBytes（枚举元信息无载荷，不得从 Bytes 推导）。</para>
        /// </summary>
        /// <param name="cloudKey">云端键。</param>
        /// <param name="filePath">本地镜像完整路径（镜像版本 sidecar 读取依据）。</param>
        /// <param name="localTime">本地镜像最后写入时间（UTC）。</param>
        /// <param name="localSize">本地镜像大小（字节）。</param>
        /// <param name="remoteTime">远端最后写入时间（UTC）。</param>
        /// <param name="remoteSize">远端条目大小（字节；枚举元信息可用）。</param>
        /// <param name="remoteVersion">远端单调修订号（0 = 无版本通道）。</param>
        /// <returns>取舍结果。</returns>
        private ESaveSyncDecision ResolveCustom(string cloudKey, string filePath, DateTime localTime, long localSize, DateTime remoteTime, long remoteSize, long remoteVersion)
        {
            SaveSyncConflictResolver resolver = m_ConflictResolver;
            if (resolver == null)
            {
                LogUtility.Warning("[SaveService] Sync policy is Custom but no conflict resolver is configured, falling back to Latest.");
                return ShouldDownload(filePath, localTime, new CloudKvEntry(null, remoteTime, remoteVersion)) ? ESaveSyncDecision.UseRemote : ESaveSyncDecision.UseLocal;
            }

            TryReadMirrorVersion(filePath, out long localVersion);
            var localInfo = new SaveSyncEntryInfo(true, localTime, localSize, localVersion);
            var remoteInfo = new SaveSyncEntryInfo(true, remoteTime, remoteSize, remoteVersion);
            return resolver.Resolve(cloudKey, localInfo, remoteInfo);
        }

        /// <summary>
        /// 下载远端条目并刷新本地镜像（原子写 + 保留远端时间戳——后续时间戳回退比较以远端权威时钟为准；
        /// 远端提供版本号时记录镜像已同步修订号 sidecar，0 则清除陈旧 sidecar）。
        /// </summary>
        /// <param name="filePath">本地镜像完整路径。</param>
        /// <param name="remoteEntry">远端条目。</param>
        private static void RefreshMirror(string filePath, CloudKvEntry remoteEntry)
        {
            Mirror.WriteAtomic(filePath, remoteEntry.Bytes, CancellationToken.None);
            if (remoteEntry.LastWriteTimeUtc > DateTime.MinValue)
            {
                try
                {
                    File.SetLastWriteTimeUtc(filePath, remoteEntry.LastWriteTimeUtc);
                }
                catch (Exception exception)
                {
                    LogUtility.Warning("[SaveService] Failed to stamp mirror timestamp, path: {0}, exception: {1}.", filePath, exception.GetType().Name);
                }
            }

            WriteMirrorVersion(filePath, remoteEntry.Version);
        }

        /// <summary>
        /// 回传本地镜像到远端（失败登记待回传并记告警——本地镜像始终为已提交事实；成功记录镜像已同步修订号）。
        /// </summary>
        /// <param name="remote">远端存储。</param>
        /// <param name="cloudKey">云端键。</param>
        /// <param name="localPath">本地镜像完整路径。</param>
        /// <param name="bytes">载荷字节。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>上传尝试完成的异步任务。</returns>
        private async UniTask UploadAsync(CloudSaveKvStore remote, string cloudKey, string localPath, byte[] bytes, CancellationToken cancellationToken)
        {
            try
            {
                long version = await remote.WriteAsync(cloudKey, bytes, cancellationToken);
                WriteMirrorVersion(localPath, version);
                lock (_syncLock)
                {
                    _uploadLastAttemptUtcTicks.Remove(cloudKey);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lock (_syncLock)
                {
                    _pendingUploads[cloudKey] = localPath;
                    _uploadLastAttemptUtcTicks[cloudKey] = DateTime.UtcNow.Ticks;
                }

                LogUtility.Warning("[SaveService] Cloud upload deferred (will backfill), key: {0}, exception: {1}.", cloudKey, exception.GetType().Name);
            }
        }

        /// <summary>
        /// 镜像独有回传冷却判定（远端持续失败时抑制读路径重试风暴；冷却过期自动恢复）。
        /// </summary>
        /// <param name="cloudKey">云端键。</param>
        /// <returns>冷却期内返回 <c>true</c>。</returns>
        private bool IsUploadCoolingDown(string cloudKey)
        {
            lock (_syncLock)
            {
                return _uploadLastAttemptUtcTicks.TryGetValue(cloudKey, out long lastAttemptTicks)
                    && DateTime.UtcNow.Ticks - lastAttemptTicks < UploadRetryCooldown.Ticks;
            }
        }

        /// <summary>
        /// 删除远端前缀下全部条目（尽力逐键删除；经前缀枚举下推减少流量）。
        /// <para>本地二次前缀校验不是性能冗余——自定义后端若错误返回前缀外的键，此处拦截，防止误删远端数据。</para>
        /// </summary>
        /// <param name="remote">远端存储。</param>
        /// <param name="prefix">目录云端前缀。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        private static async UniTask DeleteRemotePrefixAsync(CloudSaveKvStore remote, string prefix, CancellationToken cancellationToken)
        {
            CloudKvEntryInfo[] entries = await remote.EnumerateAsync(prefix, cancellationToken);
            for (int i = 0; i < entries.Length; i++)
            {
                // 安全护栏：仅删除前缀命中键——后端枚举实现有误时不得扩大删除面
                if (entries[i].Key != null && entries[i].Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    await remote.DeleteAsync(entries[i].Key, cancellationToken);
                }
            }
        }

        /// <summary>
        /// 回传重放（待删前缀 → 待删单键 → 待传上传，逐项弹出；任一步远端失败即停，余项保留待下次重放；重入直接返回）。
        /// </summary>
        /// <param name="remote">远端存储。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>重放完成的异步任务。</returns>
        private async UniTask ReplayPendingAsync(CloudSaveKvStore remote, CancellationToken cancellationToken)
        {
            lock (_syncLock)
            {
                if (_replaying)
                {
                    return;
                }

                _replaying = true;
            }

            try
            {
                for (;;)
                {
                    string deletePrefix = null;
                    string deleteKey = null;
                    KeyValuePair<string, string> upload = default;
                    bool hasUpload = false;
                    lock (_syncLock)
                    {
                        if (_pendingDeletePrefixes.Count > 0)
                        {
                            deletePrefix = _pendingDeletePrefixes[0];
                        }
                        else if (_pendingDeletes.Count > 0)
                        {
                            foreach (string key in _pendingDeletes)
                            {
                                deleteKey = key;
                                break;
                            }
                        }
                        else if (_pendingUploads.Count > 0)
                        {
                            foreach (KeyValuePair<string, string> pair in _pendingUploads)
                            {
                                upload = pair;
                                hasUpload = true;
                                break;
                            }
                        }
                    }

                    if (deletePrefix != null)
                    {
                        await DeleteRemotePrefixAsync(remote, deletePrefix, cancellationToken);
                        lock (_syncLock)
                        {
                            _pendingDeletePrefixes.RemoveAt(0);
                        }
                    }
                    else if (deleteKey != null)
                    {
                        await remote.DeleteAsync(deleteKey, cancellationToken);
                        lock (_syncLock)
                        {
                            _pendingDeletes.Remove(deleteKey);
                        }
                    }
                    else if (hasUpload)
                    {
                        SaveError readError = Mirror.TryReadAllBytes(upload.Value, out byte[] bytes);
                        if (readError == SaveError.None)
                        {
                            long version = await remote.WriteAsync(upload.Key, bytes, cancellationToken);
                            WriteMirrorVersion(upload.Value, version);
                        }

                        // 镜像已消失（如先写后删的交错时序）→ 丢弃该待传项
                        lock (_syncLock)
                        {
                            _pendingUploads.Remove(upload.Key);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogUtility.Warning("[SaveService] Cloud backfill interrupted (retries on next cloud operation), exception: {0}.", exception.GetType().Name);
            }
            finally
            {
                lock (_syncLock)
                {
                    _replaying = false;
                }
            }
        }

        /// <summary>
        /// 记录远端失败告警（降级本地镜像）。
        /// </summary>
        /// <param name="operation">操作名。</param>
        /// <param name="path">目标路径。</param>
        /// <param name="exception">远端异常。</param>
        private static void LogRemoteFailure(string operation, string path, Exception exception)
        {
            LogUtility.Warning("[SaveService] Cloud {0} failed, falling back to local mirror, path: {1}, exception: {2}.", operation, path, exception.GetType().Name);
        }

        #endregion

        #region 版本裁决辅助 [VERSION RESOLUTION]

        /// <summary>
        /// 下载判定：远端修订号领先镜像已同步修订号（版本通道）；版本不可用回退时间戳比较（远端新于镜像）。
        /// </summary>
        /// <param name="filePath">本地镜像完整路径。</param>
        /// <param name="localTime">本地镜像最后写入时间（UTC，回退通道用）。</param>
        /// <param name="remoteEntry">远端条目（枚举场景 Bytes 为 <c>null</c> 的元信息存根）。</param>
        /// <returns>应下载返回 <c>true</c>。</returns>
        private static bool ShouldDownload(string filePath, DateTime localTime, CloudKvEntry remoteEntry)
        {
            if (remoteEntry.Version > 0L && TryReadMirrorVersion(filePath, out long mirrorVersion))
            {
                return remoteEntry.Version > mirrorVersion;
            }

            return remoteEntry.LastWriteTimeUtc > localTime;
        }

        /// <summary>
        /// 上传判定（去时钟化优先）：版本通道下「远端落后（镜像领先，远端回滚等异常态以镜像为权威）」
        /// 或「版本相等但镜像脏（本地改写未同步）」；版本不可用回退时间戳比较（镜像新于远端）。
        /// </summary>
        /// <param name="filePath">本地镜像完整路径。</param>
        /// <param name="localTime">本地镜像最后写入时间（UTC，回退通道用）。</param>
        /// <param name="remoteEntry">远端条目。</param>
        /// <returns>应上传返回 <c>true</c>。</returns>
        private static bool ShouldUpload(string filePath, DateTime localTime, CloudKvEntry remoteEntry)
        {
            if (remoteEntry.Version > 0L && TryReadMirrorVersion(filePath, out long mirrorVersion))
            {
                if (remoteEntry.Version < mirrorVersion)
                {
                    return true;
                }

                return remoteEntry.Version == mirrorVersion && IsMirrorDirty(filePath);
            }

            return localTime > remoteEntry.LastWriteTimeUtc;
        }

        /// <summary>
        /// 镜像脏判定：镜像 mtime 与版本 sidecar mtime 失配即同步后被本地改写（同步原语直通不更新 sidecar）。
        /// <para>两者均为本地文件——同一时钟比较，客户端与远端的时钟偏移不参与；读取失败保守判脏（触发回传对齐）。</para>
        /// </summary>
        /// <param name="filePath">本地镜像完整路径。</param>
        /// <returns>镜像已脏返回 <c>true</c>。</returns>
        private static bool IsMirrorDirty(string filePath)
        {
            try
            {
                return File.GetLastWriteTimeUtc(filePath) != File.GetLastWriteTimeUtc(ResolveMirrorVersionPath(filePath));
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// 镜像版本 sidecar 路径（<c>{file}.cloudver</c>，8 字节小端修订号）。
        /// </summary>
        /// <param name="filePath">本地镜像完整路径。</param>
        /// <returns>sidecar 完整路径。</returns>
        private static string ResolveMirrorVersionPath(string filePath)
        {
            return filePath + MIRROR_VERSION_SUFFIX;
        }

        /// <summary>
        /// 读取镜像已同步的远端修订号（sidecar 缺失/损坏/非正版本均判无版本通道——回退时间戳裁决）。
        /// </summary>
        /// <param name="filePath">本地镜像完整路径。</param>
        /// <param name="version">读到的修订号。</param>
        /// <returns>存在有效版本记录返回 <c>true</c>。</returns>
        private static bool TryReadMirrorVersion(string filePath, out long version)
        {
            version = 0L;
            try
            {
                string sidecarPath = ResolveMirrorVersionPath(filePath);
                if (!File.Exists(sidecarPath))
                {
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(sidecarPath);
                if (bytes.Length != 8)
                {
                    return false;
                }

                version = BitConverter.ToInt64(bytes, 0);
                return version > 0L;
            }
            catch (Exception)
            {
                version = 0L;
                return false;
            }
        }

        /// <summary>
        /// 写入镜像已同步修订号（sidecar mtime 与镜像 mtime 对齐——脏判定基准；&lt;= 0 清除陈旧记录）。
        /// <para>写失败仅降级裁决精度（回退时间戳），不阻断同步主流程。</para>
        /// </summary>
        /// <param name="filePath">本地镜像完整路径。</param>
        /// <param name="version">远端分配的修订号（<c>0</c> = 后端不提供版本号）。</param>
        private static void WriteMirrorVersion(string filePath, long version)
        {
            if (version <= 0L)
            {
                DeleteMirrorVersion(filePath);
                return;
            }

            string sidecarPath = ResolveMirrorVersionPath(filePath);
            try
            {
                File.WriteAllBytes(sidecarPath, BitConverter.GetBytes(version));
                File.SetLastWriteTimeUtc(sidecarPath, File.GetLastWriteTimeUtc(filePath));
            }
            catch (Exception exception)
            {
                LogUtility.Warning("[SaveService] Failed to write mirror version sidecar, path: {0}, exception: {1}.", sidecarPath, exception.GetType().Name);
            }
        }

        /// <summary>
        /// 删除镜像版本 sidecar（幂等；失败仅告警——陈旧 sidecar 会降级为时间戳裁决，不丢数据）。
        /// </summary>
        /// <param name="filePath">本地镜像完整路径。</param>
        private static void DeleteMirrorVersion(string filePath)
        {
            try
            {
                string sidecarPath = ResolveMirrorVersionPath(filePath);
                if (File.Exists(sidecarPath))
                {
                    File.Delete(sidecarPath);
                }
            }
            catch (Exception exception)
            {
                LogUtility.Warning("[SaveService] Failed to delete mirror version sidecar, path: {0}, exception: {1}.", ResolveMirrorVersionPath(filePath), exception.GetType().Name);
            }
        }

        #endregion
    }
}
