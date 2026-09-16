using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 本地文件存储后端（默认）：存档以文件形式落于磁盘目录树。
    /// <para>写入为「临时文件 + Flush(true) 强制落盘 + 原子替换」（NTFS <see cref="File.Replace"/> 元数据级原子，
    /// 平台不支持时退化为删除+改名）；删除带退避重试（应对云同步/杀毒软件短时锁文件）；
    /// 备份为单档 <c>.bak</c> 副本，恢复经临时文件原子替换回源路径；提供孤儿临时文件清扫。</para>
    /// <para>无状态纯 .NET 实现，可在任意线程调用；共享实例 <see cref="Default"/> 供未配置后端时回退。</para>
    /// </summary>
    [Serializable]
    public class FileSaveStorageBackend : SaveStorageBackend
    {
        /// <summary>临时文件唯一后缀（实际形如 <c>xxx.sav.tmp-3f2a…</c>，避免并发写入互撞）。</summary>
        internal const string TempFileSuffix = ".tmp-";

        /// <summary>单槽备份文件后缀（实际形如 <c>xxx.sav.bak</c>）。</summary>
        private const string BackupFileSuffix = ".bak";

        /// <summary>删除操作的退避重试次数（应对云同步/杀毒软件的短时文件锁）。</summary>
        private const int DeleteRetryCount = 3;

        /// <summary>
        /// 共享默认实例（无状态后端，未配置存储后端时回退使用；任意线程安全）。
        /// </summary>
        internal static readonly FileSaveStorageBackend Default = new FileSaveStorageBackend();

        /// <summary>
        /// 后端能力自描述（本地文件：原子改名、无线程池外真异步、不设尺寸上限、非易失、同步读权威）。
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
        /// 查询目标文件的最后写入时间（文件系统元数据直查）。
        /// </summary>
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
                writeTimeUtc = File.GetLastWriteTimeUtc(filePath);
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
            string tempFilePath = filePath + TempFileSuffix + Guid.NewGuid().ToString("N");
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
        /// 流式原子写入：临时文件流（可寻址）交委托写入全部内容 → 强制落盘 → 原子替换目标（失败抛 <see cref="GameException"/> 并清理临时文件）。
        /// <para>委托抛 <see cref="GameException"/>/<see cref="OperationCanceledException"/> 原样上抛，其余异常归一为 <see cref="GameException"/>（含路径上下文）。</para>
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="writeFile">写入委托（收到的临时文件流生命周期仅限本次调用）。</param>
        /// <param name="cancellationToken">取消令牌（替换前检查）。</param>
        public override void WriteAtomic(string filePath, Action<Stream> writeFile, CancellationToken cancellationToken)
        {
            if (writeFile == null)
            {
                throw new ArgumentNullException(nameof(writeFile));
            }

            string tempFilePath = filePath + TempFileSuffix + Guid.NewGuid().ToString("N");
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
        /// <param name="filePath">文件完整路径。</param>
        public override void DeleteFile(string filePath)
        {
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

            File.Copy(filePath, filePath + BackupFileSuffix, overwrite: true);
        }

        /// <summary>
        /// 从单档备份恢复（备份经临时文件原子替换回源路径；备份不存在抛 <see cref="GameException"/>）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        public override void RestoreBackup(string filePath)
        {
            string backupFilePath = filePath + BackupFileSuffix;
            if (!File.Exists(backupFilePath))
            {
                throw new GameException(StringUtility.Format("Backup file not found, path: {0}.", backupFilePath));
            }

            string tempFilePath = filePath + TempFileSuffix + Guid.NewGuid().ToString("N");
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

                foreach (string tempFilePath in Directory.EnumerateFiles(rootDirectory, "*" + TempFileSuffix + "*", SearchOption.AllDirectories))
                {
                    TryDeleteFile(tempFilePath);
                }
            }
            catch (Exception exception)
            {
                LogUtility.Warning("[SaveService] Cleanup orphan temp files failed, directory: {0}, exception: {1}.", rootDirectory, exception.GetType().Name);
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
        /// 原子替换：目标存在时优先 <see cref="File.Replace"/>（NTFS 元数据级原子，无丢失窗口），
        /// 平台不支持时退化为删除+改名；目标不存在时直接改名。
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
                    DeleteFileWithRetry(saveFilePath);
                }
            }

            File.Move(tempFilePath, saveFilePath);
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
                catch (Exception exception) when ((exception is IOException || exception is UnauthorizedAccessException) && attempt < DeleteRetryCount)
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
                catch (Exception exception) when ((exception is IOException || exception is UnauthorizedAccessException) && attempt < DeleteRetryCount)
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
