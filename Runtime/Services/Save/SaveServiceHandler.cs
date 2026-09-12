using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档处理器抽象基类（存储管线策略抽象）。
    /// <para>承载格式与编排管线：路径解析与参数校验、多块容器组装与解析（<see cref="SaveFileContainer"/>）、
    /// 版本化文件头读写与 CRC、载荷变换钩子（明文直通 / AES 加密由子类决定）、按路径串行门（<see cref="SaveFileGate"/>）、
    /// 槽位与块枚举；全部持久化 IO 委托存储层 <see cref="ISaveStorage"/>（<see cref="SaveStorageBackend"/> 插拔件，
    /// 本地文件 / 云 KV 后端正交可换），文件 IO 在工作线程执行，
    /// 避免大存档阻塞主线程（<see cref="CancellationToken"/> 协作式取消贯穿写入与读取）。</para>
    /// <para>数据块序列化职责由 <see cref="ISaveSerializer"/>（<see cref="SaveSerializerRegistry"/> 查询）承担，
    /// 处理器只搬运容器字节——序列化后端、存储后端与存储管线三轴正交可插拔。</para>
    /// <para>由 <see cref="SaveServiceSettings"/> 序列化持有实例，经 <see cref="SaveService"/> 静态外观访问。</para>
    /// <para>错误语义契约（全模块统一）：写入路径失败 fail-fast 抛 <see cref="GameException"/>（写失败绝不容忍半档状态）；
    /// 读取路径失败返回 default + 记错误日志（错误判别用 Try* 族 <see cref="SaveResult{T}"/>）；
    /// 删除路径幂等——目标不存在视为删除成功（静默返回），调用方无需先查存在性。</para>
    /// </summary>
    public abstract class SaveServiceHandler : FrameworkHandler
    {
        /// <summary>存档数据根目录名（persistentDataPath 下）。</summary>
        public const string DataFolderName = "Data";

        /// <summary>默认存档文件夹名。</summary>
        public const string DEFAULT_FOLDER_NAME = "Save";

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

        /// <summary>存储后端（<see cref="OnInit"/> 在主线程从设置解析；标记 NonSerialized 避免序列化快照污染）。</summary>
        [NonSerialized] private SaveStorageBackend _storage;

        /// <summary>
        /// 存储后端（未经容器初始化的直接实例回退共享文件后端——纯 .NET 无副作用，任意线程安全；
        /// 严禁在核心管线惰性触达 <see cref="SaveServiceSettings"/>（Resources.Load 为 Unity 主线程 API，工作线程触达即崩）。
        /// </summary>
        private SaveStorageBackend Storage => _storage ?? FileSaveStorageBackend.Default;

        /// <summary>压缩提供方（<c>null</c> = 不压缩；<see cref="OnInit"/> 在主线程从设置解析，测试可直接赋值注入——无状态纯 .NET，工作线程调用安全）。</summary>
        [NonSerialized] internal ICompressionProvider _compression;

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化存档处理器。由容器在构建期调用（主线程：解析存储后端与压缩提供方，并后台清扫孤儿临时文件）。
        /// </summary>
        protected override void OnInit()
        {
            SaveStorageBackend backend = SaveServiceSettings.StorageBackend;
            if (backend == null)
            {
                LogUtility.Warning("[SaveService] Storage backend is not configured, falling back to FileSaveStorageBackend.");
                backend = FileSaveStorageBackend.Default;
            }

            _storage = backend;
            _compression = SaveServiceSettings.CompressionProvider;

            // 后台清扫上次写入中断残留的孤儿临时文件；根目录须在主线程解析（persistentDataPath 为 Unity API）
            string rootDirectory = BuildDataRootDirectory();
            _ = UniTask.RunOnThreadPool(() => backend.CleanupOrphanTempFiles(rootDirectory));
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
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            return SaveBlockWithGateAsync(paths, key, data, backend, dataVersion, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行块写入（先排队后进线程池；取消发生在排队期时不持门）。
        /// </summary>
        private async UniTask SaveBlockWithGateAsync<T>(SavePaths paths, string key, T data, ESaveBackend backend, int dataVersion, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            bool acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken);
                acquired = true;
                await UniTask.RunOnThreadPool(() => SaveBlockCore(paths, key, data, backend, dataVersion, cancellationToken), cancellationToken: cancellationToken);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired);
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
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            return LoadBlockWithGateAsync<T>(paths, key, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行块读取（与写路径互斥：不读到读-改-写进行中的旧档）。
        /// </summary>
        private async UniTask<T> LoadBlockWithGateAsync<T>(SavePaths paths, string key, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            bool acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken);
                acquired = true;
                return await UniTask.RunOnThreadPool(() => LoadBlockCore<T>(paths, key), cancellationToken: cancellationToken);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired);
            }
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
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            return TryLoadBlockWithGateAsync<T>(paths, key, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行块读取（错误判别版）。
        /// </summary>
        private async UniTask<SaveResult<T>> TryLoadBlockWithGateAsync<T>(SavePaths paths, string key, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            bool acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken);
                acquired = true;
                return await UniTask.RunOnThreadPool(() =>
                {
                    SaveError error = TryLoadBlockCore<T>(paths, key, out T data);
                    return error == SaveError.None ? SaveResult<T>.Success(data) : SaveResult<T>.Failure(error);
                }, cancellationToken: cancellationToken);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired);
            }
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
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            gate.Wait();
            try
            {
                SaveBlockCore(paths, key, data, backend, dataVersion, CancellationToken.None);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired: true);
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
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            gate.Wait();
            try
            {
                return LoadBlockCore<T>(paths, key);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired: true);
            }
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
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            gate.Wait();
            try
            {
                SaveError error = TryLoadBlockCore<T>(paths, key, out T data);
                return error == SaveError.None ? SaveResult<T>.Success(data) : SaveResult<T>.Failure(error);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired: true);
            }
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

            SaveError migrationError = ApplyMigration<T>(paths, entry, data);
            if (migrationError != SaveError.None)
            {
                // 失败路径必须回收 data（TryLoad 契约：错误时输出默认值，不得泄漏半初始化对象）
                data = default;
                return migrationError;
            }

            return SaveError.None;
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
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            return DeleteBlockWithGateAsync(paths, key, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行块删除。
        /// </summary>
        private async UniTask DeleteBlockWithGateAsync(SavePaths paths, string key, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            bool acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken);
                acquired = true;
                await UniTask.RunOnThreadPool(() => DeleteBlockCore(paths, key, cancellationToken), cancellationToken: cancellationToken);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired);
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
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            gate.Wait();
            try
            {
                DeleteBlockCore(paths, key, CancellationToken.None);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired: true);
            }
        }

        /// <summary>
        /// 块删除核心（同步，工作线程调用；须持串行门）：读容器 → 移除块 → 块集为空时整档删除，否则原子写回。
        /// <para>幂等契约：块或文件不存在时静默返回（删除即达成目标状态）。</para>
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
                Storage.DeleteFile(paths.SaveFilePath);
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
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            gate.Wait();
            try
            {
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
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired: true);
            }
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
            Storage.DeleteFile(paths.SaveFilePath);
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
            Storage.DeleteDirectory(directoryPath);
        }

        /// <summary>
        /// 删除存档数据根目录（<c>persistentDataPath/Data/</c>）及其下所有存档。
        /// </summary>
        public void DeleteAllSaveFiles()
        {
            string rootDirectory = BuildDataRootDirectory();
            Storage.DeleteDirectory(rootDirectory);
        }

        /// <summary>
        /// 从磁盘中异步删除单个存档（删除退避重试在工作线程执行）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public UniTask DeleteSaveAsync(string fileName, string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            SaveStorageBackend storage = Storage;
            return UniTask.RunOnThreadPool(() => storage.DeleteFile(paths.SaveFilePath), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 异步删除整个存档文件夹（含其中全部文件与子目录）。
        /// </summary>
        /// <param name="folderName">文件夹名称；不允许为空（清空全部请用 <see cref="DeleteAllSaveFilesAsync"/>）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        public UniTask DeleteSaveFolderAsync(string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            ValidateFolderName(folderName);
            if (string.IsNullOrEmpty(folderName))
            {
                throw new ArgumentException("Folder name is required when deleting a folder.", nameof(folderName));
            }

            string directoryPath = BuildFolderPath(folderName);
            SaveStorageBackend storage = Storage;
            return UniTask.RunOnThreadPool(() => storage.DeleteDirectory(directoryPath), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 异步删除存档数据根目录（<c>persistentDataPath/Data/</c>）及其下所有存档。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public UniTask DeleteAllSaveFilesAsync(CancellationToken cancellationToken = default)
        {
            string rootDirectory = BuildDataRootDirectory();
            SaveStorageBackend storage = Storage;
            return UniTask.RunOnThreadPool(() => storage.DeleteDirectory(rootDirectory), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 异步枚举指定文件夹内的全部存档槽位（枚举与排序在工作线程执行）。
        /// </summary>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>存档元数据数组（按最后写入时间倒序）；文件夹不存在时为空数组。</returns>
        public UniTask<SaveFileInfo[]> GetSaveFilesAsync(string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            ValidateFolderName(folderName);
            string directoryPath = BuildFolderPath(folderName);
            string extension = SaveServiceSettings.SaveFileExtension;
            SaveStorageBackend storage = Storage;
            return UniTask.RunOnThreadPool(() => storage.EnumerateFiles(directoryPath, extension), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 创建存档的单槽备份（<c>.bak</c> 后缀，覆盖旧备份）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        public void CreateBackup(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            gate.Wait();
            try
            {
                Storage.CreateBackup(paths.SaveFilePath);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired: true);
            }
        }

        /// <summary>
        /// 从单槽备份恢复存档（备份经临时文件原子替换回存档路径）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        public void RestoreBackup(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            gate.Wait();
            try
            {
                Storage.RestoreBackup(paths.SaveFilePath);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired: true);
            }
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
            return Storage.Exists(paths.SaveFilePath);
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
            string extension = SaveServiceSettings.SaveFileExtension;
            return Storage.EnumerateFiles(directoryPath, extension);
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

            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            return UpsertRawBlocksWithGateAsync(paths, additions, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行组件块合并写回。
        /// </summary>
        private async UniTask UpsertRawBlocksWithGateAsync(SavePaths paths, List<SaveBlockEntry> additions, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            bool acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken);
                acquired = true;
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
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired);
            }
        }

        /// <summary>
        /// 读取存档文件内全部块载荷（键 → 字节），IO 与容器解析在工作线程执行（持串行门，与写路径互斥）。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>键 → 块载荷字典；缺档/损坏（已记录日志）返回空字典。</returns>
        internal UniTask<Dictionary<string, byte[]>> ReadRawBlocksAsync(SavePaths paths, CancellationToken cancellationToken)
        {
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            return ReadRawBlocksWithGateAsync(paths, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行组件块读取。
        /// </summary>
        private async UniTask<Dictionary<string, byte[]>> ReadRawBlocksWithGateAsync(SavePaths paths, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            bool acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken);
                acquired = true;
                return await UniTask.RunOnThreadPool(() =>
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
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired);
            }
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

            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            return DeleteRawBlocksWithGateAsync(paths, keys, gate, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行组件块删除。
        /// </summary>
        private async UniTask DeleteRawBlocksWithGateAsync(SavePaths paths, List<string> keys, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            bool acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken);
                acquired = true;
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
                        Storage.DeleteFile(paths.SaveFilePath);
                        return;
                    }

                    WriteContainerFile(paths, mergedBlocks, cancellationToken);
                }, cancellationToken: cancellationToken);
            }
            finally
            {
                SaveFileGate.Leave(paths.SaveFilePath, gate, acquired);
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
                || fileName.Contains(FileSaveStorageBackend.TempFileSuffix))
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
                || folderName.Contains(FileSaveStorageBackend.TempFileSuffix)
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
        /// 校验数据块键：非空白、长度受限、不含控制字符/路径分隔符、禁止保留前缀。
        /// <para>保留前缀（<c>__</c>）禁止用户新建块——但对既有保留块（<c>__main__</c>/<c>__meta</c>）的读写/删除为合法操作（兼容旧 API 与元数据管理），
        /// 经 <see cref="MainBlockKey"/>/<see cref="MetaBlockKey"/> 常量访问时豁免前缀校验。</para>
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

            // 保留前缀仅豁免框架专用保留块（__main__/__meta），其余 __ 前缀一律拒绝
            if (key.StartsWith(ReservedBlockKeyPrefix, StringComparison.Ordinal)
                && !string.Equals(key, MainBlockKey, StringComparison.Ordinal)
                && !string.Equals(key, MetaBlockKey, StringComparison.Ordinal))
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
            SaveError ioError = Storage.TryReadAllBytes(paths.SaveFilePath, out byte[] fileBytes);
            if (ioError == SaveError.FileNotFound)
            {
                // 缺档 = 空块集（正常业务流，不记录日志）
                return SaveError.None;
            }

            if (ioError != SaveError.None)
            {
                // IO 失败的详细日志已由存储层记录
                return ioError;
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
            uint actualCrc = payloadLength >= 0
                ? Crc32.Compute(fileBytes.AsSpan(SaveFileHeader.Size, payloadLength))
                : 0u;
            if (payloadLength != header.PayloadLength || actualCrc != header.PayloadCrc)
            {
                return SaveError.Corrupted;
            }

            byte[] payload = new byte[payloadLength];
            Buffer.BlockCopy(fileBytes, SaveFileHeader.Size, payload, 0, payloadLength);
            fileBytes = null;

            SaveError transformError = OnRestorePayload(payload, out byte[] restored);
            if (transformError != SaveError.None)
            {
                return transformError;
            }

            SaveError decompressError = TryDecompressContainer(header, restored, out byte[] container);
            if (decompressError != SaveError.None)
            {
                return decompressError;
            }

            return SaveFileContainer.Read(container, out blocks);
        }

        /// <summary>
        /// 按文件头标志还原容器字节（魔数/flags sniff 幂等：未压缩档原样透传，新旧档共存）。
        /// <para>标志与提供方 ID 不一致（篡改/写中断）判别为 <see cref="SaveError.Corrupted"/>；
        /// 未注册的提供方 ID 判别为 <see cref="SaveError.UnsupportedVersion"/>（未来格式/依赖未接入保护）。</para>
        /// </summary>
        /// <param name="header">已解析的文件头。</param>
        /// <param name="restored">载荷还原字节（解密后）。</param>
        /// <param name="container">成功时的容器字节。</param>
        /// <returns>错误码。</returns>
        private static SaveError TryDecompressContainer(SaveFileHeader header, byte[] restored, out byte[] container)
        {
            container = restored;
            if ((header.Flags & SaveFileHeader.FlagCompressed) == 0)
            {
                return header.CompressionProviderId != 0 ? SaveError.Corrupted : SaveError.None;
            }

            if (header.CompressionProviderId == 0 || header.CompressionProviderId > byte.MaxValue)
            {
                return SaveError.Corrupted;
            }

            if (!SaveCompressionRegistry.TryGet((byte)header.CompressionProviderId, out ICompressionProvider compression))
            {
                return SaveError.UnsupportedVersion;
            }

            try
            {
                container = compression.Decompress(restored);
                return SaveError.None;
            }
            catch (Exception)
            {
                // 压缩数据非法（流格式破坏）——归一为存储损坏分型
                return SaveError.Corrupted;
            }
        }

        /// <summary>
        /// 将容器块集写入存档文件：容器组装 → 压缩（可选，转换链固定为压缩先于加密）→ 载荷变换（子类加密钩子）→ CRC → 组装文件头 → 存储层原子提交。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">数据块列表。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private void WriteContainerFile(SavePaths paths, List<SaveBlockEntry> blocks, CancellationToken cancellationToken)
        {
            byte[] container = new byte[SaveFileContainer.GetSize(blocks)];
            SaveFileContainer.Write(container, blocks);

            uint flags = 0u;
            uint compressionProviderId = 0u;
            byte[] transformInput = container;
            ICompressionProvider compression = _compression;
            if (compression != null)
            {
                try
                {
                    transformInput = compression.Compress(container);
                }
                catch (Exception exception)
                {
                    throw new GameException(StringUtility.Format("Save compression failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name), exception);
                }

                flags = SaveFileHeader.FlagCompressed;
                compressionProviderId = compression.ProviderId;
            }

            SaveError transformError = OnTransformContainer(transformInput, out byte[] payload);
            if (transformError != SaveError.None)
            {
                throw new GameException(StringUtility.Format("Save payload transform failed, path: {0}, error: {1}.", paths.SaveFilePath, transformError));
            }

            // 写盘的是存储载荷（加密后），不是未加密容器——CRC 也是载荷的
            uint payloadCrc = Crc32.Compute(payload);
            byte[] fileBytes = new byte[SaveFileHeader.Size + payload.Length];
            SaveFileHeader.Write(fileBytes.AsSpan(0, SaveFileHeader.Size), payload.Length, payloadCrc, flags, compressionProviderId);
            Buffer.BlockCopy(payload, 0, fileBytes, SaveFileHeader.Size, payload.Length);
            Storage.WriteAtomic(paths.SaveFilePath, fileBytes, cancellationToken);
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
