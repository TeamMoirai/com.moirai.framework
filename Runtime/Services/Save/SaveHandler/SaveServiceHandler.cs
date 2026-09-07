using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档处理器抽象基类（策略模式抽象策略）。
    /// <para>承载完整文件管线：路径解析与参数校验、版本化文件头、临时文件 + Flush(true) 落盘 + 原子替换、
    /// 删除退避重试、孤儿临时文件清扫、槽位枚举；文件 IO 与序列化在工作线程执行，
    /// 避免大存档阻塞主线程（<see cref="CancellationToken"/> 协作式取消贯穿写入与读取）。</para>
    /// <para>子类只需实现 <see cref="Serialize"/> / <see cref="Deserialize{T}"/> 序列化策略（纯 .NET 字节通路，
    /// 在工作线程调用，禁止触达 Unity 主线程 API）。</para>
    /// <para>由 <see cref="SaveServiceSettings"/> 序列化持有实例，经 <see cref="SaveService"/> 静态外观访问。</para>
    /// </summary>
    public abstract class SaveServiceHandler : FrameworkHandler
    {
        /// <summary>存档数据根目录名（persistentDataPath 下）。</summary>
        public const string DataFolderName = "Data";

        /// <summary>默认存档文件夹名。</summary>
        public const string DEFAULT_FOLDER_NAME = "Save";

        /// <summary>临时文件唯一后缀（实际形如 <c>xxx.sav.tmp-3f2a…</c>，避免并发写入互撞）。</summary>
        private const string TempFileSuffix = ".tmp-";

        /// <summary>删除操作的退避重试次数（应对云同步/杀毒软件的短时文件锁）。</summary>
        private const int DeleteRetryCount = 3;

        private static readonly char[] s_PathSeparators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        private static readonly char[] s_InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private static readonly HashSet<string> s_ReservedDeviceNames = BuildReservedDeviceNames();

        /// <summary>存档根路径缓存（persistentDataPath 为 Unity API，仅在主线程首次解析）。</summary>
        [NonSerialized] private static string s_BasePath;

        /// <summary>存档根路径覆盖点（仅供测试注入；非 null 时优先于 persistentDataPath）。</summary>
        [NonSerialized] internal static string s_OverrideBasePath;

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化存档处理器。由容器在构建期调用。
        /// </summary>
        protected override void OnInit()
        {
            // 后台清扫上次写入中断残留的孤儿临时文件；根目录须在主线程解析（persistentDataPath 为 Unity API）
            string rootDirectory = BuildDataRootDirectory();
            _ = UniTask.RunOnThreadPool(() => CleanupOrphanTempFiles(rootDirectory));
        }

        #endregion

        #region 存档读写 [SAVE / LOAD]

        /// <summary>
        /// 将存档对象写入磁盘（临时文件 + 落盘刷新 + 原子替换），IO 在工作线程执行。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="saveObject">存档对象。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="cancellationToken">取消令牌（协作式：在序列化前后与替换前检查）。</param>
        /// <returns>写入完成的异步任务；失败抛出 <see cref="GameException"/>（含路径上下文）。</returns>
        public UniTask Save<T>(T saveObject, string fileName, string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            if (saveObject is null)
            {
                throw new ArgumentNullException(nameof(saveObject));
            }

            SavePaths paths = ResolveSavePaths(fileName, folderName);
            return UniTask.RunOnThreadPool(() => SaveCore(paths, saveObject, cancellationToken), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 从磁盘加载存档，IO 在工作线程执行。
        /// <para>文件不存在返回 <c>default</c>（既有契约）；损坏/解密失败/反序列化失败记录错误日志后同样返回 <c>default</c>——需要错误判别时使用 <see cref="TryLoad{T}"/>。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>反序列化后的存档对象；失败返回默认值。</returns>
        public UniTask<T> Load<T>(string fileName, string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            return UniTask.RunOnThreadPool(() => LoadCore<T>(paths), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 从磁盘加载存档并返回完整错误判别结果，IO 在工作线程执行。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>加载结果（<see cref="SaveResult{T}"/> 区分无档/损坏/解密失败等错误类别）。</returns>
        public UniTask<SaveResult<T>> TryLoad<T>(string fileName, string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            return UniTask.RunOnThreadPool(() =>
            {
                SaveError error = TryLoadCore<T>(paths, out T data);
                return error == SaveError.None ? SaveResult<T>.Success(data) : SaveResult<T>.Failure(error);
            }, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 写入核心（同步，工作线程调用）：序列化 → 临时文件 → 原子替换。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="saveObject">存档对象。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        internal void SaveCore(SavePaths paths, object saveObject, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] payload;
            try
            {
                payload = Serialize(saveObject);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new GameException(StringUtility.Format("Save serialization failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name), exception);
            }

            uint payloadCrc = Crc32.Compute(payload);
            string tempFilePath = paths.SaveFilePath + TempFileSuffix + Guid.NewGuid().ToString("N");

            try
            {
                EnsureDirectory(paths.DirectoryPath);
                WriteToTempFile(tempFilePath, payload, payloadCrc, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                AtomicReplace(tempFilePath, paths.SaveFilePath);
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tempFilePath);
                throw;
            }
            catch (Exception exception)
            {
                TryDeleteFile(tempFilePath);
                throw new GameException(StringUtility.Format("Save write failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name), exception);
            }
        }

        /// <summary>
        /// 读取核心（同步，工作线程调用）：错误时返回默认值（错误已记录日志）。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="paths">已解析的路径集合。</param>
        /// <returns>存档对象或默认值。</returns>
        internal T LoadCore<T>(SavePaths paths)
        {
            SaveError error = TryLoadCore<T>(paths, out T data);
            return error == SaveError.None ? data : default;
        }

        /// <summary>
        /// 读取核心（同步，工作线程调用）：读文件 → 校验文件头 → 校验 CRC → 反序列化，全错误分类返回。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="data">成功时的存档对象。</param>
        /// <returns>错误码。</returns>
        internal SaveError TryLoadCore<T>(SavePaths paths, out T data)
        {
            data = default;
            if (!File.Exists(paths.SaveFilePath))
            {
                return SaveError.FileNotFound;
            }

            byte[] fileBytes;
            try
            {
                fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Read save file failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name);
                return SaveError.IoFailed;
            }

            SaveError headerError = SaveFileHeader.Read(fileBytes, out SaveFileHeader header);
            if (headerError != SaveError.None)
            {
                LogLoadFailure(paths.SaveFilePath, headerError);
                return headerError;
            }

            int payloadLength = fileBytes.Length - SaveFileHeader.Size;
            if (payloadLength != header.PayloadLength
                || Crc32.Compute(fileBytes.AsSpan(SaveFileHeader.Size, payloadLength)) != header.PayloadCrc)
            {
                LogLoadFailure(paths.SaveFilePath, SaveError.Corrupted);
                return SaveError.Corrupted;
            }

            byte[] payload = new byte[payloadLength];
            Buffer.BlockCopy(fileBytes, SaveFileHeader.Size, payload, 0, payloadLength);
            fileBytes = null;

            try
            {
                data = Deserialize<T>(payload);
                return SaveError.None;
            }
            catch (SaveOperationException exception)
            {
                LogLoadFailure(paths.SaveFilePath, exception.Error);
                return exception.Error;
            }
            catch (CryptographicException exception)
            {
                LogUtility.Error("[SaveService] Load failed, path: {0}, error: {1}, exception: {2}.", paths.SaveFilePath, SaveError.DecryptionFailed, exception.GetType().Name);
                return SaveError.DecryptionFailed;
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Deserialize save failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name);
                return SaveError.SerializationFailed;
            }
        }

        #endregion

        #region 序列化策略 [SERIALIZATION STRATEGY]

        /// <summary>
        /// 将存档对象序列化为载荷字节。由子类实现具体格式（JSON / 加密 JSON 等）。
        /// <para>在工作线程调用；实现必须为纯 .NET 逻辑，禁止触达 Unity 主线程 API。</para>
        /// </summary>
        /// <param name="saveObject">存档对象。</param>
        /// <returns>载荷字节。</returns>
        protected internal abstract byte[] Serialize(object saveObject);

        /// <summary>
        /// 从载荷字节反序列化对象。由子类实现具体格式。
        /// <para>在工作线程调用；实现必须为纯 .NET 逻辑，禁止触达 Unity 主线程 API。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="payload">载荷字节。</param>
        /// <returns>反序列化后的对象。</returns>
        protected internal abstract T Deserialize<T>(byte[] payload);

        #endregion

        #region 存档删除 [DELETE]

        /// <summary>
        /// 从磁盘中删除单个存档。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        public void DeleteSave(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            DeleteFileWithRetry(paths.SaveFilePath);
        }

        /// <summary>
        /// 删除整个存档文件夹（含其中全部文件与子目录）。
        /// </summary>
        /// <param name="folderName">文件夹名称；不允许为空（清空全部请用 <see cref="DeleteAllSaveFiles"/>）。</param>
        public void DeleteSaveFolder(string folderName = DEFAULT_FOLDER_NAME)
        {
            ValidateFolderName(folderName);
            if (string.IsNullOrEmpty(folderName))
            {
                throw new ArgumentException("Folder name is required when deleting a folder.", nameof(folderName));
            }

            string directoryPath = BuildFolderPath(folderName);
            DeleteDirectoryWithRetry(directoryPath);
        }

        /// <summary>
        /// 删除存档数据根目录（<c>persistentDataPath/Data/</c>）及其下所有存档。
        /// </summary>
        public void DeleteAllSaveFiles()
        {
            string rootDirectory = BuildDataRootDirectory();
            DeleteDirectoryWithRetry(rootDirectory);
        }

        #endregion

        #region 存档查询 [QUERY]

        /// <summary>
        /// 是否存在存档文件。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public bool FileExists(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            return File.Exists(paths.SaveFilePath);
        }

        /// <summary>
        /// 枚举指定文件夹内的全部存档槽位（按最后写入时间倒序，最近优先）。
        /// </summary>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <returns>存档元数据数组（文件夹不存在时为空数组）。</returns>
        public SaveFileInfo[] GetSaveFiles(string folderName = DEFAULT_FOLDER_NAME)
        {
            ValidateFolderName(folderName);
            string directoryPath = BuildFolderPath(folderName);
            if (!Directory.Exists(directoryPath))
            {
                return Array.Empty<SaveFileInfo>();
            }

            string extension = SaveServiceSettings.SaveFileExtension;
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

        #endregion

        #region 路径管理 [PATH]

        /// <summary>
        /// 获取文件夹的完整保存路径（以目录分隔符结尾）。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>保存路径。</returns>
        public string DetermineSavePath(string folderName = DEFAULT_FOLDER_NAME)
        {
            ValidateFolderName(folderName);
            return BuildFolderPath(folderName);
        }

        /// <summary>
        /// 单次存档操作的解析路径集合（调用线程解析完成后传入工作线程核心）。
        /// </summary>
        internal readonly struct SavePaths
        {
            /// <summary>目标文件夹完整路径。</summary>
            public readonly string DirectoryPath;

            /// <summary>目标存档文件完整路径。</summary>
            public readonly string SaveFilePath;

            /// <summary>
            /// 创建路径集合。
            /// </summary>
            /// <param name="directoryPath">目标文件夹完整路径。</param>
            /// <param name="saveFilePath">目标存档文件完整路径。</param>
            public SavePaths(string directoryPath, string saveFilePath)
            {
                DirectoryPath = directoryPath;
                SaveFilePath = saveFilePath;
            }
        }

        /// <summary>
        /// 校验并解析文件名/文件夹名为完整路径集合（须在调用线程执行：触达 persistentDataPath 与设置实例）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>路径集合。</returns>
        internal static SavePaths ResolveSavePaths(string fileName, string folderName)
        {
            ValidateFileName(fileName);
            ValidateFolderName(folderName);
            string directoryPath = BuildFolderPath(folderName);
            string saveFilePath = Path.Combine(directoryPath, DetermineSaveFileName(fileName));
            return new SavePaths(directoryPath, saveFilePath);
        }

        /// <summary>
        /// 拼装存档文件夹完整路径（以目录分隔符结尾）。
        /// </summary>
        /// <param name="folderName">已校验的文件夹名称（可为空 = 数据根目录）。</param>
        /// <returns>文件夹路径。</returns>
        private static string BuildFolderPath(string folderName)
        {
            string directoryPath = string.IsNullOrEmpty(folderName)
                ? Path.Combine(BasePath, DataFolderName)
                : Path.Combine(BasePath, DataFolderName, folderName);
            return directoryPath + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// 拼装存档数据根目录完整路径。
        /// </summary>
        /// <returns>根目录路径。</returns>
        private static string BuildDataRootDirectory()
        {
            return Path.Combine(BasePath, DataFolderName);
        }

        /// <summary>
        /// 存档根路径（persistentDataPath，首次访问缓存；仅可在主线程访问；测试可经 <c>s_OverrideBasePath</c> 覆盖）。
        /// </summary>
        private static string BasePath
        {
            get
            {
                if (s_OverrideBasePath != null)
                {
                    return s_OverrideBasePath;
                }

                return s_BasePath ??= Application.persistentDataPath;
            }
        }

        /// <summary>
        /// 判断要保存的文件名称（自动追加配置的扩展名）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <returns>保存文件名。</returns>
        private static string DetermineSaveFileName(string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName) + SaveServiceSettings.SaveFileExtension;
        }

        /// <summary>
        /// 校验文件名：非空、不含路径分隔符/穿越段/非法字符/Windows 保留设备名/临时文件后缀。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        private static void ValidateFileName(string fileName)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Save file name is null or empty.", nameof(fileName));
            }

            if (fileName.IndexOfAny(s_PathSeparators) >= 0
                || fileName.Contains("..")
                || fileName.Contains(TempFileSuffix))
            {
                throw new ArgumentException(StringUtility.Format("Save file name '{0}' contains path separators or reserved segments.", fileName), nameof(fileName));
            }

            // 先对原始输入做非法字符校验：GetFileNameWithoutExtension 会把 ':' 当卷分隔符切掉前段（"slot:1" → "1"），仅查基名会漏网
            if (fileName.IndexOfAny(s_InvalidFileNameChars) >= 0)
            {
                throw new ArgumentException(StringUtility.Format("Save file name '{0}' contains invalid characters.", fileName), nameof(fileName));
            }

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (s_ReservedDeviceNames.Contains(baseName))
            {
                throw new ArgumentException(StringUtility.Format("Save file name '{0}' is a reserved device name.", fileName), nameof(fileName));
            }
        }

        /// <summary>
        /// 校验文件夹名称：可为空（数据根目录）；不含路径分隔符/穿越段/盘符/非法字符。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        private static void ValidateFolderName(string folderName)
        {
            if (folderName == null)
            {
                throw new ArgumentNullException(nameof(folderName));
            }

            if (folderName.Length == 0)
            {
                return;
            }

            if (folderName.IndexOfAny(s_PathSeparators) >= 0
                || folderName.Contains("..")
                || folderName.Contains(":")
                || folderName.Contains(TempFileSuffix)
                || folderName == ".")
            {
                throw new ArgumentException(StringUtility.Format("Save folder name '{0}' contains path separators or reserved segments.", folderName), nameof(folderName));
            }

            if (folderName.IndexOfAny(s_InvalidFileNameChars) >= 0)
            {
                throw new ArgumentException(StringUtility.Format("Save folder name '{0}' contains invalid characters.", folderName), nameof(folderName));
            }
        }

        /// <summary>
        /// 构建 Windows 保留设备名集合（CON/PRN/AUX/NUL/COM1-9/LPT1-9）。
        /// </summary>
        /// <returns>保留设备名集合（忽略大小写）。</returns>
        private static HashSet<string> BuildReservedDeviceNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL"
            };

            for (int i = 1; i <= 9; i++)
            {
                names.Add("COM" + i);
                names.Add("LPT" + i);
            }

            return names;
        }

        #endregion

        #region 文件管线 [FILE PIPELINE]

        /// <summary>
        /// 确保目标目录存在（幂等）。
        /// </summary>
        /// <param name="directoryPath">目标目录。</param>
        private static void EnsureDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        /// <summary>
        /// 将文件头与载荷写入临时文件并强制落盘。
        /// </summary>
        /// <param name="tempFilePath">临时文件路径。</param>
        /// <param name="payload">载荷字节。</param>
        /// <param name="payloadCrc">载荷 CRC-32 校验值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private static void WriteToTempFile(string tempFilePath, byte[] payload, uint payloadCrc, CancellationToken cancellationToken)
        {
            using (FileStream stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
            {
                Span<byte> header = stackalloc byte[SaveFileHeader.Size];
                SaveFileHeader.Write(header, payload.Length, payloadCrc);
                stream.Write(header);
                stream.Write(payload, 0, payload.Length);
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

        /// <summary>
        /// 清扫指定根目录树内的孤儿临时文件（上次写入中断残留）。
        /// </summary>
        /// <param name="rootDirectory">存档数据根目录。</param>
        internal static void CleanupOrphanTempFiles(string rootDirectory)
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

        /// <summary>
        /// 记录读取失败日志（运维可见性：损坏/解密失败等必须留下可追溯痕迹）。
        /// </summary>
        /// <param name="saveFilePath">存档路径。</param>
        /// <param name="error">错误码。</param>
        private static void LogLoadFailure(string saveFilePath, SaveError error)
        {
            LogUtility.Error("[SaveService] Load failed, path: {0}, error: {1}.", saveFilePath, error);
        }

        #endregion
    }
}
