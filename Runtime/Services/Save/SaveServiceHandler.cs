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
    /// 存档处理器抽象基类（存储管线策略抽象）。
    /// <para>承载完整文件管线：路径解析与参数校验、多块容器组装与解析（<see cref="SaveFileContainer"/>）、
    /// 版本化文件头、载荷变换钩子（明文直通 / AES 加密由子类决定）、临时文件 + Flush(true) 落盘 + 原子替换、
    /// 删除退避重试、孤儿临时文件清扫、槽位与块枚举；文件 IO 在工作线程执行，
    /// 避免大存档阻塞主线程（<see cref="CancellationToken"/> 协作式取消贯穿写入与读取）。</para>
    /// <para>数据块序列化职责由 <see cref="ISaveSerializer"/>（<see cref="SaveSerializerRegistry"/> 查询）承担，
    /// 处理器只搬运容器字节——序列化后端与存储管线两轴正交可插拔。</para>
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

        /// <summary>兼容块键：旧单对象 API（Save/Load/TryLoad）映射的保留数据块。</summary>
        public const string MainBlockKey = "__main__";

        /// <summary>保留块键：槽位元数据块（JSON 后端）。</summary>
        internal const string MetaBlockKey = "__meta";

        /// <summary>保留块键前缀（用户块键禁止使用，避免与框架保留块冲突）。</summary>
        internal const string ReservedBlockKeyPrefix = "__";

        /// <summary>块键最大字符数。</summary>
        internal const int MaxBlockKeyLength = 64;

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

        #region 块级读写 [BLOCK SAVE / LOAD]

        /// <summary>
        /// 将数据块异步写入存档文件（读-改-写合并进既有块集合；临时文件 + 落盘刷新 + 原子替换），IO 在工作线程执行。
        /// <para>同文件的写路径经串行门排队，杜绝并发读-改-写丢块。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="data">存档数据对象。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键（禁止保留前缀 <see cref="ReservedBlockKeyPrefix"/>）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="dataVersion">数据块模式版本。</param>
        /// <param name="cancellationToken">取消令牌（协作式：在序列化前后与替换前检查）。</param>
        /// <returns>写入完成的异步任务；失败抛出 <see cref="GameException"/>（含路径上下文）。</returns>
        public UniTask SaveBlockAsync<T>(T data, string fileName, string key, string folderName = DEFAULT_FOLDER_NAME, ESaveBackend backend = ESaveBackend.Json, int dataVersion = 1, CancellationToken cancellationToken = default)
        {
            ValidateBlockData(data);
            ValidateBlockKey(key);
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            SemaphoreSlim gate = SaveFileGate.Get(paths.SaveFilePath);
            return SaveBlockWithGateAsync(paths, key, data, backend, dataVersion, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行块写入（先排队后进线程池；取消发生在排队期时不持门）。
        /// </summary>
        private async UniTask SaveBlockWithGateAsync<T>(SavePaths paths, string key, T data, ESaveBackend backend, int dataVersion, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await UniTask.RunOnThreadPool(() => SaveBlockCore(paths, key, data, backend, dataVersion, cancellationToken), cancellationToken: cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 从存档文件异步加载指定数据块，IO 在工作线程执行。
        /// <para>文件或块不存在、加载失败（损坏/解密失败/反序列化失败，均已记录错误日志）返回 <c>default</c>——需要错误判别时使用 <see cref="TryLoadBlockAsync{T}"/>。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>反序列化后的块数据；失败返回默认值。</returns>
        public UniTask<T> LoadBlockAsync<T>(string fileName, string key, string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            ValidateBlockKey(key);
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            return UniTask.RunOnThreadPool(() => LoadBlockCore<T>(paths, key), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 从存档文件异步加载指定数据块并返回完整错误判别结果，IO 在工作线程执行。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>加载结果（区分无档/无块/损坏/解密失败等错误类别）。</returns>
        public UniTask<SaveResult<T>> TryLoadBlockAsync<T>(string fileName, string key, string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            ValidateBlockKey(key);
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            return UniTask.RunOnThreadPool(() =>
            {
                SaveError error = TryLoadBlockCore<T>(paths, key, out T data);
                return error == SaveError.None ? SaveResult<T>.Success(data) : SaveResult<T>.Failure(error);
            }, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 将数据块写入存档文件（在调用线程执行完整管线，阻塞直至完成）。
        /// <para>仅限主线程调用（触达 persistentDataPath 等 Unity API）；适用于退出前落盘等必须同步完成的场景，大数据量请用 <see cref="SaveBlockAsync{T}"/> 避免阻塞。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="data">存档数据对象。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键（禁止保留前缀）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="dataVersion">数据块模式版本。</param>
        public void SaveBlock<T>(T data, string fileName, string key, string folderName = DEFAULT_FOLDER_NAME, ESaveBackend backend = ESaveBackend.Json, int dataVersion = 1)
        {
            ValidateBlockData(data);
            ValidateBlockKey(key);
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            SemaphoreSlim gate = SaveFileGate.Get(paths.SaveFilePath);
            gate.Wait();
            try
            {
                SaveBlockCore(paths, key, data, backend, dataVersion, CancellationToken.None);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 从存档文件加载指定数据块（在调用线程执行，阻塞直至完成）。
        /// <para>仅限主线程调用；失败兜底语义与 <see cref="LoadBlockAsync{T}"/> 一致（缺档/失败记录日志后返回 <c>default</c>，错误判别用 <see cref="TryLoadBlock{T}"/>）。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <returns>反序列化后的块数据；失败返回默认值。</returns>
        public T LoadBlock<T>(string fileName, string key, string folderName = DEFAULT_FOLDER_NAME)
        {
            ValidateBlockKey(key);
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            return LoadBlockCore<T>(paths, key);
        }

        /// <summary>
        /// 从存档文件加载指定数据块并返回完整错误判别结果（在调用线程执行，阻塞直至完成）。
        /// <para>仅限主线程调用。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <returns>加载结果（区分无档/无块/损坏/解密失败等错误类别）。</returns>
        public SaveResult<T> TryLoadBlock<T>(string fileName, string key, string folderName = DEFAULT_FOLDER_NAME)
        {
            ValidateBlockKey(key);
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            SaveError error = TryLoadBlockCore<T>(paths, key, out T data);
            return error == SaveError.None ? SaveResult<T>.Success(data) : SaveResult<T>.Failure(error);
        }

        /// <summary>
        /// 块写入核心（同步，工作线程调用；须持串行门）：序列化块 → 读既有容器 → 合并 → 原子写回。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="data">存档数据对象。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="dataVersion">数据块模式版本。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        internal void SaveBlockCore<T>(SavePaths paths, string key, T data, ESaveBackend backend, int dataVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] blockBytes;
            try
            {
                blockBytes = SaveSerializerRegistry.GetRequired(backend).Serialize(data);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new GameException(StringUtility.Format("Save block serialization failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name), exception);
            }

            ReadContainerOrEmpty(paths, out List<SaveBlockEntry> existingBlocks);
            List<SaveBlockEntry> mergedBlocks = SaveBlockComposer.Upsert(existingBlocks, new SaveBlockEntry(key, dataVersion, backend, blockBytes));
            WriteContainerFile(paths, mergedBlocks, cancellationToken);
        }

        /// <summary>
        /// 块读取核心（同步，工作线程调用）：错误时返回默认值（错误已记录日志）。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="key">数据块键。</param>
        /// <returns>块数据或默认值。</returns>
        internal T LoadBlockCore<T>(SavePaths paths, string key)
        {
            SaveError error = TryLoadBlockCore<T>(paths, key, out T data);
            return error == SaveError.None ? data : default;
        }

        /// <summary>
        /// 块读取核心（同步，工作线程调用）：读文件 → 校验头/CRC → 还原载荷 → 解析容器 → 定位块 → 反序列化，全错误分类返回。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="data">成功时的块数据。</param>
        /// <returns>错误码。</returns>
        internal SaveError TryLoadBlockCore<T>(SavePaths paths, string key, out T data)
        {
            data = default;
            SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks);
            if (readError != SaveError.None)
            {
                return readError;
            }

            if (!SaveBlockComposer.TryFind(blocks, key, out SaveBlockEntry entry))
            {
                return SaveError.FileNotFound;
            }

            if (!SaveSerializerRegistry.TryGet(entry.Backend, out ISaveSerializer serializer))
            {
                // 块记录的后端未注册（依赖未接入或文件被改写）——明确拒绝而非静默解析
                LogLoadFailure(paths.SaveFilePath, SaveError.InvalidFormat);
                return SaveError.InvalidFormat;
            }

            try
            {
                data = serializer.Deserialize<T>(entry.Bytes);
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Deserialize save block failed, path: {0}, key: {1}, exception: {2}.", paths.SaveFilePath, key, exception.GetType().Name);
                return SaveError.SerializationFailed;
            }

            return ApplyMigration<T>(paths, entry, data);
        }

        /// <summary>
        /// 版本迁移与保护（<see cref="SaveDataBlock"/> 子类生效）：存档版本高于声明值拒绝加载（未来格式保护），
        /// 低于声明值执行 <see cref="SaveDataBlock.OnMigrate"/> 级联升级（内存就地修正，下次写入自然持久化）。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="paths">已解析的路径集合（日志上下文）。</param>
        /// <param name="entry">命中的数据块条目。</param>
        /// <param name="data">反序列化后的数据对象。</param>
        /// <returns>错误码。</returns>
        private static SaveError ApplyMigration<T>(SavePaths paths, SaveBlockEntry entry, T data)
        {
            if (!SaveBlockDescriptor<T>.IsMigrationAware || data is not SaveDataBlock block)
            {
                return SaveError.None;
            }

            if (!SaveBlockDescriptor<T>.HasAttribute)
            {
                // SaveDataBlock 子类必须以 SaveDataAttribute 声明版本——缺失即契约破坏，fail-fast
                LogUtility.Error("[SaveService] SaveDataBlock '{0}' is missing SaveDataAttribute, path: {1}.", typeof(T).FullName, paths.SaveFilePath);
                return SaveError.SerializationFailed;
            }

            int declaredVersion = SaveBlockDescriptor<T>.Version;
            if (entry.DataVersion > declaredVersion)
            {
                LogLoadFailure(paths.SaveFilePath, SaveError.UnsupportedVersion);
                return SaveError.UnsupportedVersion;
            }

            if (entry.DataVersion < declaredVersion)
            {
                block.OnMigrate(entry.DataVersion);
            }

            return SaveError.None;
        }

        #endregion

        #region 块删除与枚举 [BLOCK DELETE / LISTING]

        /// <summary>
        /// 从存档文件中删除指定数据块（读-改-写回；删除最后一个块时整档移除），IO 在工作线程执行。
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        public UniTask DeleteBlockAsync(string fileName, string key, string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            ValidateBlockKey(key);
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            SemaphoreSlim gate = SaveFileGate.Get(paths.SaveFilePath);
            return DeleteBlockWithGateAsync(paths, key, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行块删除。
        /// </summary>
        private async UniTask DeleteBlockWithGateAsync(SavePaths paths, string key, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await UniTask.RunOnThreadPool(() => DeleteBlockCore(paths, key, cancellationToken), cancellationToken: cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 从存档文件中删除指定数据块（在调用线程执行，阻塞直至完成）。
        /// <para>仅限主线程调用；删除最后一个块时整档移除。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        public void DeleteBlock(string fileName, string key, string folderName = DEFAULT_FOLDER_NAME)
        {
            ValidateBlockKey(key);
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            SemaphoreSlim gate = SaveFileGate.Get(paths.SaveFilePath);
            gate.Wait();
            try
            {
                DeleteBlockCore(paths, key, CancellationToken.None);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 块删除核心（同步，工作线程调用；须持串行门）：读容器 → 移除块 → 块集为空时整档删除，否则原子写回。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        internal void DeleteBlockCore(SavePaths paths, string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks);
            if (readError != SaveError.None)
            {
                throw new GameException(StringUtility.Format("Save block delete failed, path: {0}, error: {1}.", paths.SaveFilePath, readError));
            }

            if (!SaveBlockComposer.TryFind(blocks, key, out _))
            {
                return;
            }

            List<SaveBlockEntry> remainingBlocks = SaveBlockComposer.Remove(blocks, key);
            if (remainingBlocks.Count == 0)
            {
                DeleteFileWithRetry(paths.SaveFilePath);
                return;
            }

            WriteContainerFile(paths, remainingBlocks, cancellationToken);
        }

        /// <summary>
        /// 枚举存档文件内的全部数据块（含保留块；读文件与解析在调用线程执行）。
        /// <para>加密处理器下需要解密整档——大存档场景请自行节流调用频率。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <returns>块元信息数组；缺档/损坏时为空数组（损坏已记录错误日志）。</returns>
        public SaveBlockInfo[] GetBlockInfos(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks);
            if (readError != SaveError.None)
            {
                return Array.Empty<SaveBlockInfo>();
            }

            var infos = new SaveBlockInfo[blocks.Count];
            for (int i = 0; i < blocks.Count; i++)
            {
                infos[i] = new SaveBlockInfo(blocks[i].Key, blocks[i].DataVersion, blocks[i].Backend, blocks[i].Bytes.Length);
            }

            return infos;
        }

        #endregion

        #region 存档删除 [DELETE]

        /// <summary>
        /// 从磁盘中删除单个存档（含全部数据块）。
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
        /// <returns>存档元数据数组；文件夹不存在时为空数组。</returns>
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

        #region 组件块管线 [COMPONENT BLOCKS]

        /// <summary>
        /// 将组件捕获条目批量合并写入存档文件（主线程完成捕获，此处仅合并与原子写回），IO 在工作线程执行。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="additions">组件捕获条目（键 = 块键，载荷 = KVT 字节，后端 = <see cref="ESaveBackend.KeyValue"/>）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>写入完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        internal UniTask UpsertRawBlocksAsync(SavePaths paths, List<SaveBlockEntry> additions, CancellationToken cancellationToken)
        {
            if (additions.Count == 0)
            {
                return UniTask.CompletedTask;
            }

            SemaphoreSlim gate = SaveFileGate.Get(paths.SaveFilePath);
            return UpsertRawBlocksWithGateAsync(paths, additions, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行组件块合并写回。
        /// </summary>
        private async UniTask UpsertRawBlocksWithGateAsync(SavePaths paths, List<SaveBlockEntry> additions, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await UniTask.RunOnThreadPool(() =>
                {
                    ReadContainerOrEmpty(paths, out List<SaveBlockEntry> existingBlocks);
                    List<SaveBlockEntry> mergedBlocks = existingBlocks;
                    for (int i = 0; i < additions.Count; i++)
                    {
                        mergedBlocks = SaveBlockComposer.Upsert(mergedBlocks, additions[i]);
                    }

                    WriteContainerFile(paths, mergedBlocks, cancellationToken);
                }, cancellationToken: cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 读取存档文件内全部块载荷（键 → 字节），IO 与容器解析在工作线程执行。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>键 → 块载荷字典；缺档/损坏（已记录日志）返回空字典。</returns>
        internal UniTask<Dictionary<string, byte[]>> ReadRawBlocksAsync(SavePaths paths, CancellationToken cancellationToken)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks);
                if (readError != SaveError.None)
                {
                    return new Dictionary<string, byte[]>();
                }

                var result = new Dictionary<string, byte[]>(blocks.Count);
                for (int i = 0; i < blocks.Count; i++)
                {
                    result[blocks[i].Key] = blocks[i].Bytes;
                }

                return result;
            }, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 删除指定组件块键集合对应的块（读-改-写回；主线程解析路径后调用）。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="keys">块键集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        internal UniTask DeleteRawBlocksAsync(SavePaths paths, List<string> keys, CancellationToken cancellationToken)
        {
            if (keys.Count == 0)
            {
                return UniTask.CompletedTask;
            }

            SemaphoreSlim gate = SaveFileGate.Get(paths.SaveFilePath);
            return DeleteRawBlocksWithGateAsync(paths, keys, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行组件块删除。
        /// </summary>
        private async UniTask DeleteRawBlocksWithGateAsync(SavePaths paths, List<string> keys, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await UniTask.RunOnThreadPool(() =>
                {
                    SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> existingBlocks);
                    if (readError != SaveError.None)
                    {
                        return;
                    }

                    List<SaveBlockEntry> mergedBlocks = existingBlocks;
                    for (int i = 0; i < keys.Count; i++)
                    {
                        mergedBlocks = SaveBlockComposer.Remove(mergedBlocks, keys[i]);
                    }

                    if (mergedBlocks.Count == 0)
                    {
                        DeleteFileWithRetry(paths.SaveFilePath);
                        return;
                    }

                    WriteContainerFile(paths, mergedBlocks, cancellationToken);
                }, cancellationToken: cancellationToken);
            }
            finally
            {
                gate.Release();
            }
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
        /// 校验存档数据对象：非 null fail-fast。
        /// </summary>
        /// <param name="data">存档数据对象。</param>
        private static void ValidateBlockData<T>(T data)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }
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
        /// 校验数据块键：非空白、长度受限、不含控制字符/路径分隔符、禁止保留前缀（<c>__main__</c>/<c>__meta</c> 由框架专用）。
        /// </summary>
        /// <param name="key">数据块键。</param>
        private static void ValidateBlockKey(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Save block key is null or empty.", nameof(key));
            }

            if (key.Length > MaxBlockKeyLength)
            {
                throw new ArgumentException(StringUtility.Format("Save block key '{0}' exceeds the max length {1}.", key, MaxBlockKeyLength), nameof(key));
            }

            if (key.IndexOfAny(s_PathSeparators) >= 0 || key.IndexOfAny(s_InvalidFileNameChars) >= 0)
            {
                throw new ArgumentException(StringUtility.Format("Save block key '{0}' contains invalid characters.", key), nameof(key));
            }

            if (key.StartsWith(ReservedBlockKeyPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(StringUtility.Format("Save block key '{0}' uses the reserved prefix '{1}'.", key, ReservedBlockKeyPrefix), nameof(key));
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
        /// 读取并还原存档容器：缺档返回空块集（<see cref="SaveError.None"/>），其余错误分型返回（调用方决定日志与兜底语义）。
        /// <para>管线：读文件 → 校验头/CRC → 载荷还原（子类：解密/直通）→ 容器解析。</para>
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">成功或缺档时的数据块列表（缺档为空列表）。</param>
        /// <returns>错误码。</returns>
        private SaveError ReadContainerOrEmpty(SavePaths paths, out List<SaveBlockEntry> blocks)
        {
            blocks = new List<SaveBlockEntry>();
            if (!File.Exists(paths.SaveFilePath))
            {
                return SaveError.None;
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

            SaveError restoreError = ReadAndRestoreContainer(fileBytes, out blocks);
            if (restoreError != SaveError.None)
            {
                LogLoadFailure(paths.SaveFilePath, restoreError);
                return restoreError;
            }

            return SaveError.None;
        }

        /// <summary>
        /// 将文件字节（头 + 载荷）还原为容器块列表（纯变换，不做日志；错误分型由各环节判定）。
        /// </summary>
        /// <param name="fileBytes">存档文件完整字节。</param>
        /// <param name="blocks">成功时的数据块列表。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>、<see cref="SaveError.InvalidFormat"/>、<see cref="SaveError.UnsupportedVersion"/>、
        /// <see cref="SaveError.Corrupted"/>、<see cref="SaveError.DecryptionFailed"/> 或 <see cref="SaveError.IntegrityCheckFailed"/>。</returns>
        private SaveError ReadAndRestoreContainer(byte[] fileBytes, out List<SaveBlockEntry> blocks)
        {
            blocks = null;
            SaveError headerError = SaveFileHeader.Read(fileBytes, out SaveFileHeader header);
            if (headerError != SaveError.None)
            {
                return headerError;
            }

            int payloadLength = fileBytes.Length - SaveFileHeader.Size;
            if (payloadLength != header.PayloadLength
                || Crc32.Compute(fileBytes.AsSpan(SaveFileHeader.Size, payloadLength)) != header.PayloadCrc)
            {
                return SaveError.Corrupted;
            }

            byte[] payload = new byte[payloadLength];
            Buffer.BlockCopy(fileBytes, SaveFileHeader.Size, payload, 0, payloadLength);
            fileBytes = null;

            SaveError transformError = OnRestorePayload(payload, out byte[] container);
            if (transformError != SaveError.None)
            {
                return transformError;
            }

            return SaveFileContainer.Read(container, out blocks);
        }

        /// <summary>
        /// 将容器块集写入存档文件：容器组装 → CRC → 临时文件 → 落盘 → 原子替换。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">数据块列表。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private void WriteContainerFile(SavePaths paths, List<SaveBlockEntry> blocks, CancellationToken cancellationToken)
        {
            byte[] container = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(container, blocks);

            SaveError transformError = OnTransformContainer(container, out byte[] payload);
            if (transformError != SaveError.None)
            {
                throw new GameException(StringUtility.Format("Save payload transform failed, path: {0}, error: {1}.", paths.SaveFilePath, transformError));
            }

            uint payloadCrc = Crc32.Compute(payload);
            string tempFilePath = paths.SaveFilePath + TempFileSuffix + Guid.NewGuid().ToString("N");

            try
            {
                EnsureDirectory(paths.DirectoryPath);
                WriteToTempFile(tempFilePath, container, payloadCrc, 0u, cancellationToken);
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
        /// 载荷变换钩子：子类将容器字节变换为存储载荷（加密处理器在此加密，明文处理器直通）。
        /// <para>在工作线程调用；实现必须为纯 .NET 逻辑，禁止触达 Unity 主线程 API。</para>
        /// </summary>
        /// <param name="container">容器字节。</param>
        /// <param name="payload">成功时的存储载荷字节。</param>
        /// <returns>错误码。</returns>
        protected internal abstract SaveError OnTransformContainer(byte[] container, out byte[] payload);

        /// <summary>
        /// 载荷还原钩子：子类将存储载荷还原为容器字节（加密处理器在此解密，明文处理器直通）。
        /// <para>在工作线程调用；实现必须为纯 .NET 逻辑，禁止触达 Unity 主线程 API。</para>
        /// </summary>
        /// <param name="payload">存储载荷字节。</param>
        /// <param name="container">成功时的容器字节。</param>
        /// <returns>错误码。</returns>
        protected internal abstract SaveError OnRestorePayload(byte[] payload, out byte[] container);

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
        /// <param name="flags">文件头特性标志位。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private static void WriteToTempFile(string tempFilePath, byte[] payload, uint payloadCrc, uint flags, CancellationToken cancellationToken)
        {
            using (FileStream stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
            {
                Span<byte> header = stackalloc byte[SaveFileHeader.Size];
                SaveFileHeader.Write(header, payload.Length, payloadCrc, flags);
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
