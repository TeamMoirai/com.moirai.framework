using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 本地文件存储后端（默认）：存档以文件形式落于磁盘目录树。
    /// </summary>
    /// <remarks>
    /// 写入为「临时文件 + <c>Flush(true)</c> 强制落盘 + 原子替换」（NTFS <see cref="File.Replace"/> 元数据级原子，平台不支持时转 <see cref="FallbackReplace"/>：旧档先改名到 <c>.journal</c>，再把临时文件改名到位，两步之间崩溃由 <see cref="RecoverInterruptedWrites"/> 在下次初始化抬回）。
    /// 删除带退避重试（应对云同步/杀毒软件短时锁文件）；备份为单档 <c>.bak</c> 副本（项目侧手动备份位，与写入用的 <c>.journal</c> 互不占用），恢复经临时文件原子替换回源路径。
    /// 提供孤儿临时文件清扫与中断恢复；无状态纯 .NET 实现，可在任意线程调用；共享实例 <see cref="s_Default"/> 供未配置后端时回退。
    /// </remarks>
    [Serializable]
    public class FileSaveStorageBackend : SaveStorageBackend
    {
        /// <summary>临时文件唯一后缀（实际形如 <c>xxx.sav.tmp-3f2a…</c>，避免并发写入互撞）。</summary>
        internal const string TEMP_FILE_SUFFIX = ".tmp-";

        /// <summary>单槽备份文件后缀（实际形如 <c>xxx.sav.bak</c>）。</summary>
        private const string BACKUP_FILE_SUFFIX = ".bak";

        /// <summary>
        /// 回退替换的中转日志后缀（实际形如 <c>xxx.sav.journal</c>）。
        /// </summary>
        /// <remarks>与 <see cref="BACKUP_FILE_SUFFIX"/> 分开：后者是项目侧 <c>CreateBackup</c>/<c>RestoreBackup</c> 的持久备份位，回退若借它中转，玩家手动恢复会捞到一份写入中途的快照。</remarks>
        internal const string JOURNAL_FILE_SUFFIX = ".journal";

        /// <summary>删除操作的退避重试次数（应对云同步/杀毒软件的短时文件锁）。</summary>
        private const int DELETE_RETRY_COUNT = 3;

        /// <summary>
        /// 共享默认实例（无状态后端，未配置存储后端时回退使用；任意线程安全）。
        /// </summary>
        internal static readonly FileSaveStorageBackend s_Default = new FileSaveStorageBackend();

        /// <summary>
        /// 后端能力自描述（本地文件：无半写窗口且中断后旧档可恢复、无线程池外真异步、不设尺寸上限、非易失、同步读权威）。
        /// </summary>
        public override SaveStorageCapabilities Capabilities => new SaveStorageCapabilities(
            supportsAtomicRename: true,
            supportsTrueAsyncIO: false,
            maxRecommendedSize: -1L,
            volatileStorage: false,
            syncReadsAuthoritative: true);

        #region 同步原语 [SYNC PRIMITIVES]

        /// <summary>
        /// 是否存在目标文件。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public override bool Exists(string filePath)
        {
            return File.Exists(filePath);
        }

        /// <summary>
        /// 是否存在目标目录。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public override bool DirectoryExists(string directoryPath)
        {
            return Directory.Exists(directoryPath);
        }

        /// <summary>
        /// 查询目标文件的最后写入时间（经 <see cref="FileInfo.Refresh()"/> 强制刷新元数据以规避缓存滞后值）。
        /// </summary>
        /// <remarks><see cref="File.GetLastWriteTimeUtc(string)"/> 在 Windows 上可能命中目录枚举缓存，写后立即读取会拿到滞后值；会话级增量守卫要求新鲜元数据。</remarks>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="writeTimeUtc">成功时的最后写入时间（UTC）。</param>
        /// <returns>文件存在返回 <c>true</c>；缺档/查询失败返回 <c>false</c>。</returns>
        public override bool TryGetWriteTimeUtc(string filePath, out DateTime writeTimeUtc)
        {
            if (!File.Exists(filePath))
            {
                writeTimeUtc = default;
                return false;
            }

            try
            {
                var info = new FileInfo(filePath);
                info.Refresh();
                writeTimeUtc = info.LastWriteTimeUtc;
                return true;
            }
            catch (Exception)
            {
                writeTimeUtc = default;
                return false;
            }
        }

        /// <summary>
        /// 读取文件全部字节（缺档返回 <see cref="SaveError.FileNotFound"/> 不记日志；IO 失败记录详细日志后返回 <see cref="SaveError.IoFailed"/>）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="bytes">成功时的文件字节。</param>
        /// <returns>错误码。</returns>
        public override SaveError TryReadAllBytes(string filePath, out byte[] bytes)
        {
            bytes = null;
            if (!File.Exists(filePath))
            {
                return SaveError.FileNotFound;
            }

            try
            {
                bytes = File.ReadAllBytes(filePath);
                return SaveError.None;
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Read save file failed, path: {0}, exception: {1}.", filePath, exception.GetType().Name);
                return SaveError.IoFailed;
            }
        }

        /// <summary>
        /// 原子写入：完整文件字节写临时文件 → 强制落盘 → 原子替换目标（失败抛 <see cref="GameException"/> 并清理临时文件）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="bytes">完整文件字节（含文件头）。</param>
        /// <param name="cancellationToken">取消令牌（替换前检查）。</param>
        public override void WriteAtomic(string filePath, byte[] bytes, CancellationToken cancellationToken)
        {
            WriteAtomic(filePath, ReadOnlySpan<byte>.Empty, bytes.AsSpan(), cancellationToken);
        }

        /// <summary>
        /// 原子写入（两段式）：头部与载荷分段写临时文件（零拼接分配）→ 强制落盘 → 原子替换目标（失败抛 <see cref="GameException"/> 并清理临时文件）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="head">文件头部字节（先写入）。</param>
        /// <param name="payload">载荷字节（头部之后写入）。</param>
        /// <param name="cancellationToken">取消令牌（替换前检查）。</param>
        public override void WriteAtomic(string filePath, ReadOnlySpan<byte> head, ReadOnlySpan<byte> payload, CancellationToken cancellationToken)
        {
            string tempFilePath = filePath + TEMP_FILE_SUFFIX + Guid.NewGuid().ToString("N");
            try
            {
                EnsureDirectory(Path.GetDirectoryName(filePath));
                WriteToTempFile(tempFilePath, head, payload, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                AtomicReplace(tempFilePath, filePath);
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tempFilePath);
                throw;
            }
            catch (Exception exception)
            {
                TryDeleteFile(tempFilePath);
                throw new GameException(StringUtility.Format("Save write failed, path: {0}, exception: {1}.", filePath, exception.GetType().Name), exception);
            }
        }

        /// <summary>
        /// 流式原子写入：临时文件流（可寻址）交委托写入全部内容 → 强制落盘 → 原子替换目标。
        /// </summary>
        /// <remarks>
        /// 失败抛 <c>GameException</c> 并清理临时文件；委托抛 <c>GameException</c>/<see cref="OperationCanceledException"/> 原样上抛，其余异常归一为 <c>GameException</c>（含路径上下文）。
        /// </remarks>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="writeFile">写入委托（收到的临时文件流生命周期仅限本次调用）。</param>
        /// <param name="cancellationToken">取消令牌（替换前检查）。</param>
        public override void WriteAtomic(string filePath, Action<Stream> writeFile, CancellationToken cancellationToken)
        {
            if (writeFile == null)
            {
                throw new ArgumentNullException(nameof(writeFile));
            }

            string tempFilePath = filePath + TEMP_FILE_SUFFIX + Guid.NewGuid().ToString("N");
            try
            {
                EnsureDirectory(Path.GetDirectoryName(filePath));
                using (FileStream stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.None))
                {
                    writeFile(stream);
                    FlushToDisk(stream);
                }

                cancellationToken.ThrowIfCancellationRequested();
                AtomicReplace(tempFilePath, filePath);
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tempFilePath);
                throw;
            }
            catch (GameException)
            {
                TryDeleteFile(tempFilePath);
                throw;
            }
            catch (Exception exception)
            {
                TryDeleteFile(tempFilePath);
                throw new GameException(StringUtility.Format("Save write failed, path: {0}, exception: {1}.", filePath, exception.GetType().Name), exception);
            }
        }

        /// <summary>
        /// 流式读：打开目标文件只读流（顺序扫描优化；可寻址）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="stream">成功时的只读流（生命周期由调用方管理）。</param>
        /// <returns>错误码（缺档不记日志；IO 失败记录详细日志）。</returns>
        public override SaveError TryOpenRead(string filePath, out Stream stream)
        {
            if (!File.Exists(filePath))
            {
                stream = null;
                return SaveError.FileNotFound;
            }

            try
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
                return SaveError.None;
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Open save file for streaming read failed, path: {0}, exception: {1}.", filePath, exception.GetType().Name);
                stream = null;
                return SaveError.IoFailed;
            }
        }

        /// <summary>
        /// 删除文件（幂等：不存在视为成功；带退避重试）。
        /// </summary>
        /// <remarks>
        /// 须先清同路径中转日志（<see cref="JOURNAL_FILE_SUFFIX"/>）再删主档：反序会在 journal 被云同步/杀软锁住时留下「主档已没、journal 尚存」的形态，令已删槽位在下次初始化被 <see cref="RecoverInterruptedWrites"/> 抬回。
        /// </remarks>
        /// <param name="filePath">文件完整路径。</param>
        public override void DeleteFile(string filePath)
        {
            DeleteFileWithRetry(filePath + JOURNAL_FILE_SUFFIX);
            DeleteFileWithRetry(filePath);
        }

        /// <summary>
        /// 递归删除目录树（幂等：不存在视为成功；只读文件先清除只读属性；带退避重试）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        public override void DeleteDirectory(string directoryPath)
        {
            DeleteDirectoryWithRetry(directoryPath);
        }

        /// <summary>
        /// 枚举目录内指定扩展名的文件（按最后写入时间倒序；目录不存在返回空数组；扩展名精确匹配规避 8.3 通配符怪癖）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <param name="extension">扩展名（含点）。</param>
        /// <returns>文件元信息数组。</returns>
        public override SaveFileInfo[] EnumerateFiles(string directoryPath, string extension)
        {
            if (!Directory.Exists(directoryPath))
            {
                return Array.Empty<SaveFileInfo>();
            }

            FileInfo[] files = new DirectoryInfo(directoryPath).GetFiles("*" + extension, SearchOption.TopDirectoryOnly);

            // Windows GetFiles 的 8.3 通配符怪癖：*.sav 会命中 *.saveall——按扩展名精确过滤
            List<SaveFileInfo> results = new List<SaveFileInfo>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                if (!string.Equals(files[i].Extension, extension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(new SaveFileInfo(Path.GetFileNameWithoutExtension(files[i].Name), files[i].Length, files[i].LastWriteTimeUtc));
            }

            if (results.Count == 0)
            {
                return Array.Empty<SaveFileInfo>();
            }

            results.Sort((left, right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));
            return results.ToArray();
        }

        /// <summary>
        /// 创建单档备份（<c>.bak</c> 后缀，覆盖旧备份；源不存在抛 <see cref="GameException"/>）。
        /// </summary>
        /// <param name="filePath">源文件完整路径。</param>
        public override void CreateBackup(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new GameException(StringUtility.Format("Save file not found for backup, path: {0}.", filePath));
            }

            File.Copy(filePath, filePath + BACKUP_FILE_SUFFIX, overwrite: true);
        }

        /// <summary>
        /// 从单档备份恢复（备份经临时文件原子替换回源路径；备份不存在抛 <see cref="GameException"/>）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        public override void RestoreBackup(string filePath)
        {
            string backupFilePath = filePath + BACKUP_FILE_SUFFIX;
            if (!File.Exists(backupFilePath))
            {
                throw new GameException(StringUtility.Format("Backup file not found, path: {0}.", backupFilePath));
            }

            string tempFilePath = filePath + TEMP_FILE_SUFFIX + Guid.NewGuid().ToString("N");
            try
            {
                EnsureDirectory(Path.GetDirectoryName(filePath));
                File.Copy(backupFilePath, tempFilePath, overwrite: true);
                AtomicReplace(tempFilePath, filePath);
            }
            catch (Exception exception)
            {
                TryDeleteFile(tempFilePath);
                throw new GameException(StringUtility.Format("Backup restore failed, path: {0}, exception: {1}.", filePath, exception.GetType().Name), exception);
            }
        }

        /// <summary>
        /// 清扫指定根目录树内的孤儿临时文件（上次写入中断残留；尽力而为，失败仅告警）。
        /// </summary>
        /// <param name="rootDirectory">存档数据根目录。</param>
        public override void CleanupOrphanTempFiles(string rootDirectory)
        {
            try
            {
                if (!Directory.Exists(rootDirectory))
                {
                    return;
                }

                foreach (string tempFilePath in Directory.EnumerateFiles(rootDirectory, "*" + TEMP_FILE_SUFFIX + "*", SearchOption.AllDirectories))
                {
                    TryDeleteFile(tempFilePath);
                }
            }
            catch (Exception exception)
            {
                LogUtility.Warning("[SaveService] Cleanup orphan temp files failed, directory: {0}, exception: {1}.", rootDirectory, exception.GetType().Name);
            }
        }

        /// <summary>
        /// 抬回上次写入中断留下的日志档（<see cref="FallbackReplace"/> 两步之间崩溃即属此类）：主档不在而日志档在则改名回主档，主档在则删除日志档且不覆盖新档。
        /// </summary>
        /// <remarks>尽力而为，失败仅告警；须在 <see cref="CleanupOrphanTempFiles"/> 之前调用（先抬回主档，再扫临时残留）。</remarks>
        /// <param name="rootDirectory">存档数据根目录。</param>
        public override void RecoverInterruptedWrites(string rootDirectory)
        {
            try
            {
                if (!Directory.Exists(rootDirectory))
                {
                    return;
                }

                foreach (string journalFilePath in Directory.EnumerateFiles(rootDirectory, "*" + JOURNAL_FILE_SUFFIX, SearchOption.AllDirectories))
                {
                    // 通配符匹配在 Windows 上有 8.3 短名怪癖（同 EnumerateFiles 的后置过滤理由），砍长度前必须复核真后缀
                    if (!journalFilePath.EndsWith(JOURNAL_FILE_SUFFIX, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string saveFilePath = journalFilePath.Substring(0, journalFilePath.Length - JOURNAL_FILE_SUFFIX.Length);

                    try
                    {
                        if (File.Exists(saveFilePath))
                        {
                            if (new FileInfo(saveFilePath).Length > 0L)
                            {
                                TryDeleteFile(journalFilePath);
                            }
                            else
                            {
                                // 空主档是删不掉的残迹，journal 才是唯一可读副本——不清 journal，改告警等人/上层裁决
                                LogUtility.Warning("[SaveService] Primary is an empty remnant, journal kept as the only readable copy: {0}.", saveFilePath);
                            }
                        }
                        else
                        {
                            File.Move(journalFilePath, saveFilePath);
                            LogUtility.Warning("[SaveService] Interrupted save restored from journal, path: {0}.", saveFilePath);
                        }
                    }
                    catch (Exception exception)
                    {
                        LogUtility.Warning("[SaveService] Journal restore failed at init, path: {0}, exception: {1}.", journalFilePath, exception.GetType().Name);
                    }
                }
            }
            catch (Exception exception)
            {
                LogUtility.Warning("[SaveService] Recover interrupted writes failed, directory: {0}, exception: {1}.", rootDirectory, exception.GetType().Name);
            }
        }

        #endregion

        #region 文件原语 [FILE PRIMITIVES]

        /// <summary>
        /// 确保目标目录存在（幂等）。
        /// </summary>
        /// <param name="directoryPath">目标目录。</param>
        private static void EnsureDirectory(string directoryPath)
        {
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        /// <summary>
        /// 将头部与载荷分段写入临时文件并强制落盘（跨度直写流，零拼接分配）。
        /// </summary>
        /// <param name="tempFilePath">临时文件路径。</param>
        /// <param name="head">文件头部字节（先写入）。</param>
        /// <param name="payload">载荷字节（头部之后写入）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private static void WriteToTempFile(string tempFilePath, ReadOnlySpan<byte> head, ReadOnlySpan<byte> payload, CancellationToken cancellationToken)
        {
            using (FileStream stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
            {
                if (!head.IsEmpty)
                {
                    stream.Write(head);
                }

                stream.Write(payload);
                FlushToDisk(stream);
            }
        }

        /// <summary>
        /// 强制刷新到物理磁盘（断电/崩溃安全）；平台不支持 fsync 时退化为常规刷新。
        /// </summary>
        /// <param name="stream">目标文件流。</param>
        private static void FlushToDisk(FileStream stream)
        {
            try
            {
                stream.Flush(true);
            }
            catch (Exception exception) when (exception is IOException || exception is PlatformNotSupportedException)
            {
                stream.Flush();
            }
        }

        /// <summary>
        /// 原子替换目标文件：优先 <see cref="File.Replace"/>（NTFS 元数据级原子，无丢失窗口），平台不支持时转 <see cref="FallbackReplace"/>；目标不存在时直接改名。
        /// </summary>
        /// <param name="tempFilePath">临时文件路径。</param>
        /// <param name="saveFilePath">目标存档路径。</param>
        private static void AtomicReplace(string tempFilePath, string saveFilePath)
        {
            if (File.Exists(saveFilePath))
            {
                try
                {
                    File.Replace(tempFilePath, saveFilePath, null);
                    return;
                }
                catch (Exception exception) when (exception is PlatformNotSupportedException || exception is NotImplementedException)
                {
                    FallbackReplace(tempFilePath, saveFilePath);
                }

                return;
            }

            File.Move(tempFilePath, saveFilePath);
        }

        /// <summary>
        /// 无原子替换能力的平台（Android / iOS / WebGL 等 POSIX 语义）下的回退写法：旧档先改名到日志位，临时文件再改名到位，成功后清掉日志。
        /// </summary>
        /// <remarks>
        /// 不写成「删掉旧档再改名」——那两步之间崩溃或断电等于存档消失；到位失败时转 <see cref="RollbackJournal"/> 抬回，进程崩在两步之间时旧档完整留在日志位，由 <see cref="RecoverInterruptedWrites"/> 在下次初始化抬回。
        /// 不复用 <see cref="BACKUP_FILE_SUFFIX"/>（项目侧手动备份的持久单槽位），借它中转会让玩家「恢复上一版」捞到写入中途的快照。
        /// </remarks>
        /// <param name="tempFilePath">已落盘的临时文件路径（本次要写入的新内容）。</param>
        /// <param name="saveFilePath">目标存档路径。</param>
        internal static void FallbackReplace(string tempFilePath, string saveFilePath)
        {
            if (!File.Exists(saveFilePath))
            {
                File.Move(tempFilePath, saveFilePath);
                return;
            }

            string journalFilePath = saveFilePath + JOURNAL_FILE_SUFFIX;
            TryDeleteFile(journalFilePath);
            File.Move(saveFilePath, journalFilePath);

            try
            {
                File.Move(tempFilePath, saveFilePath);
            }
            catch (Exception)
            {
                RollbackJournal(journalFilePath, saveFilePath);
                throw;
            }

            TryDeleteFile(journalFilePath);
        }

        /// <summary>
        /// 到位失败后的回滚：把 journal 抬回主档位置，原异常由调用方继续上抛。
        /// </summary>
        /// <remarks>
        /// 只在 journal 确实还在时才动主档——主档位置上可能是并发恢复刚抬回来的旧档，无判据地删掉它就删了唯一可读副本。
        /// 本类第一不变量：任一时刻主档或 journal 至少一处可读，回滚路径自己不得破坏。
        /// 抬回失败时保留 journal 与原样主档残迹，交给下次初始化的 <see cref="RecoverInterruptedWrites"/> 按「主档非空才算已提交」裁决。
        /// </remarks>
        /// <param name="journalFilePath">中转日志文件路径（旧档内容）。</param>
        /// <param name="saveFilePath">目标存档路径。</param>
        internal static void RollbackJournal(string journalFilePath, string saveFilePath)
        {
            if (!File.Exists(journalFilePath))
            {
                LogUtility.Warning("[SaveService] Journal missing during in-session rollback, primary left untouched: {0}.", saveFilePath);
                return;
            }

            try
            {
                TryDeleteFile(saveFilePath);
                File.Move(journalFilePath, saveFilePath);
            }
            catch (Exception restoreException)
            {
                LogUtility.Warning("[SaveService] Journal restore failed during in-session rollback, path: {0}, exception: {1}.", saveFilePath, restoreException.GetType().Name);
            }
        }

        /// <summary>
        /// 删除文件（带退避重试，应对云同步/杀毒软件短时锁文件）。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        private static void DeleteFileWithRetry(string filePath)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    return;
                }
                catch (Exception exception) when ((exception is IOException || exception is UnauthorizedAccessException) && attempt < DELETE_RETRY_COUNT)
                {
                    Thread.Sleep(10 * attempt);
                }
            }
        }

        /// <summary>
        /// 尽力删除文件（失败仅告警，用于临时文件清理等容错路径）。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception exception)
            {
                LogUtility.Warning("[SaveService] Cleanup temp file failed, path: {0}, exception: {1}.", filePath, exception.GetType().Name);
            }
        }

        /// <summary>
        /// 删除目录（带退避重试；只读文件先清除只读属性）。
        /// </summary>
        /// <param name="targetDirectory">目标目录。</param>
        private static void DeleteDirectoryWithRetry(string targetDirectory)
        {
            if (!Directory.Exists(targetDirectory))
            {
                return;
            }

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Delete(targetDirectory, true);
                    return;
                }
                catch (Exception exception) when ((exception is IOException || exception is UnauthorizedAccessException) && attempt < DELETE_RETRY_COUNT)
                {
                    if (exception is UnauthorizedAccessException)
                    {
                        ClearReadOnlyAttributes(targetDirectory);
                    }

                    Thread.Sleep(10 * attempt);
                }
            }
        }

        /// <summary>
        /// 递归清除目录树内全部只读属性（<see cref="Directory.Delete"/> 不处理只读文件）。
        /// </summary>
        /// <param name="targetDirectory">目标目录。</param>
        private static void ClearReadOnlyAttributes(string targetDirectory)
        {
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(targetDirectory, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }

                foreach (string directoryPath in Directory.EnumerateDirectories(targetDirectory, "*", SearchOption.AllDirectories))
                {
                    FileAttributes attributes = File.GetAttributes(directoryPath);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(directoryPath, attributes & ~FileAttributes.ReadOnly);
                    }
                }
            }
            catch (Exception exception)
            {
                LogUtility.Warning("[SaveService] Clear read-only attributes failed, directory: {0}, exception: {1}.", targetDirectory, exception.GetType().Name);
            }
        }

        #endregion
    }
}
