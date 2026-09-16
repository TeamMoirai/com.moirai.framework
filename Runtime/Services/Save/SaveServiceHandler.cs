using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
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
        public const string DATA_FOLDER_NAME = "Data";

        /// <summary>默认存档文件夹名。</summary>
        public const string DEFAULT_FOLDER_NAME = "Save";

        /// <summary>便捷单对象 API（Save/Load/TryLoad）映射的保留数据块。</summary>
        public const string MAIN_BLOCK_KEY = "__main__";

        /// <summary>保留块键：槽位元数据块（JSON 后端）。</summary>
        internal const string META_BLOCK_KEY = "__meta";

        /// <summary>保留块键前缀（用户块键禁止使用，避免与框架保留块冲突）。</summary>
        internal const string RESERVED_BLOCK_KEY_PREFIX = "__";

        /// <summary>块键最大字符数。</summary>
        internal const int MAX_BLOCK_KEY_LENGTH = 64;

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

        /// <summary>迁移回写开关（<see cref="OnInit"/> 在主线程从设置解析；测试可直接赋值注入——纯数据，工作线程读取安全；默认值与设置默认一致）。</summary>
        [NonSerialized] internal bool _migrationWriteBack = true;

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
            _migrationWriteBack = SaveServiceSettings.MigrationWriteBack;

            // 后台清扫上次写入中断残留的孤儿临时文件；根目录须在主线程解析（persistentDataPath 为 Unity API）
            string rootDirectory = BuildDataRootDirectory();
            _ = UniTask.RunOnThreadPool(() => backend.CleanupOrphanTempFiles(rootDirectory), configureAwait: false);
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
        /// <param name="key">数据块键（禁止保留前缀 <see cref="RESERVED_BLOCK_KEY_PREFIX"/>）。</param>
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
            GateScope scope = GateScope.EnterFile(paths);
            return SaveBlockWithGateAsync(paths, key, data, backend, dataVersion, scope, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行块写入（先排队后进线程池；取消发生在排队期时不持门）。
        /// </summary>
        private async UniTask SaveBlockWithGateAsync<T>(SavePaths paths, string key, T data, ESaveBackend backend, int dataVersion, GateScope scope, CancellationToken cancellationToken)
        {
            try
            {
                await scope.WaitAsync(cancellationToken);
                // configureAwait: false —— Leave 在线程池续延执行，避免主线程被同步 gate.Wait() 阻死时无法归还信号量
                await UniTask.RunOnThreadPool(() => SaveBlockCore(paths, key, data, backend, dataVersion, cancellationToken), configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
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
            GateScope scope = GateScope.EnterFile(paths);
            return LoadBlockWithGateAsync<T>(paths, key, scope, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行块读取（与写路径互斥：不读到读-改-写进行中的旧档）。
        /// </summary>
        private async UniTask<T> LoadBlockWithGateAsync<T>(SavePaths paths, string key, GateScope scope, CancellationToken cancellationToken)
        {
            try
            {
                await scope.WaitAsync(cancellationToken);
                return await UniTask.RunOnThreadPool(() => LoadBlockCore<T>(paths, key), configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
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
            GateScope scope = GateScope.EnterFile(paths);
            return TryLoadBlockWithGateAsync<T>(paths, key, scope, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行块读取（错误判别版）。
        /// </summary>
        private async UniTask<SaveResult<T>> TryLoadBlockWithGateAsync<T>(SavePaths paths, string key, GateScope scope, CancellationToken cancellationToken)
        {
            try
            {
                await scope.WaitAsync(cancellationToken);
                return await UniTask.RunOnThreadPool(() =>
                {
                    SaveError error = TryLoadBlockCore<T>(paths, key, out T data);
                    return error == SaveError.None ? SaveResult<T>.Success(data) : SaveResult<T>.Failure(error);
                }, configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
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
            GateScope scope = GateScope.EnterFile(paths);
            scope.Wait();
            try
            {
                SaveBlockCore(paths, key, data, backend, dataVersion, CancellationToken.None);
            }
            finally
            {
                scope.Leave();
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
            GateScope scope = GateScope.EnterFile(paths);
            scope.Wait();
            try
            {
                return LoadBlockCore<T>(paths, key);
            }
            finally
            {
                scope.Leave();
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
            GateScope scope = GateScope.EnterFile(paths);
            scope.Wait();
            try
            {
                SaveError error = TryLoadBlockCore<T>(paths, key, out T data);
                return error == SaveError.None ? SaveResult<T>.Success(data) : SaveResult<T>.Failure(error);
            }
            finally
            {
                scope.Leave();
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
                SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, key, ESaveFailureStage.Serialize, SaveError.SerializationFailed);
                throw new GameException(StringUtility.Format("Save block serialization failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name), exception);
            }

            // 既有档不可读时隔离拷贝为 sidecar 后再按空块集覆写（保留可抢救字节）；容器 v2 下坏块在写回时自然剔除，健康块保留
            SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> existingBlocks, out List<SaveBlockError> blockErrors);
            if (readError != SaveError.None)
            {
                QuarantineUnreadableSave(paths, readError);
                existingBlocks = new List<SaveBlockEntry>();
                blockErrors = null;
            }

            // 写入自愈：旧版本档先迁移到当前版本再合并新块（杜绝新形态块落入旧版本档后再次被迁移链误变换）
            MigrateBlocksForWrite(paths, ref existingBlocks, blockErrors, cancellationToken);
            List<SaveBlockEntry> mergedBlocks = SaveBlockComposer.Upsert(existingBlocks, new SaveBlockEntry(key, dataVersion, backend, blockBytes));
            WriteContainerFile(paths, mergedBlocks, cancellationToken);
            SaveService.RaiseBlockSaved(paths.FileName, paths.FolderName, key, backend, blockBytes.Length);
            SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Saved, paths.FileName, paths.FolderName);
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
            SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors);
            if (readError != SaveError.None)
            {
                return readError;
            }

            // 迁移总线前置（文件级版本链；SaveDataBlock.OnMigrate 类型级级联在其后的 ApplyMigration 保留执行）
            SaveError busError = MigrateBlocksIfNeeded(paths, ref blocks, blockErrors, forceWriteBack: false, CancellationToken.None);
            if (busError != SaveError.None)
            {
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.Migrate, busError);
                return busError;
            }

            if (!SaveBlockComposer.TryFind(blocks, key, out SaveBlockEntry entry))
            {
                SaveError missError = ClassifyMissingKey(blockErrors, key);
                if (missError != SaveError.FileNotFound)
                {
                    // 坏块键命中（或结构性坏块致状态不可知）——逐块损坏经失败事件观测（确无块的 FileNotFound 属正常业务流）
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, key, ESaveFailureStage.ContainerParse, missError);
                }

                return missError;
            }

            if (!SaveSerializerRegistry.TryGet(entry.Backend, out ISaveSerializer serializer))
            {
                // 块记录的后端未注册（依赖未接入或文件被改写）——明确拒绝而非静默解析
                LogLoadFailure(paths.SaveFilePath, SaveError.InvalidFormat);
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, key, ESaveFailureStage.ContainerParse, SaveError.InvalidFormat);
                return SaveError.InvalidFormat;
            }

            try
            {
                data = serializer.Deserialize<T>(entry.Bytes);
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Deserialize save block failed, path: {0}, key: {1}, exception: {2}.", paths.SaveFilePath, key, exception.GetType().Name);
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, key, ESaveFailureStage.Deserialize, SaveError.SerializationFailed);
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
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, entry.Key, ESaveFailureStage.Migrate, SaveError.SerializationFailed);
                return SaveError.SerializationFailed;
            }

            int declaredVersion = SaveBlockDescriptor<T>.Version;
            if (entry.DataVersion > declaredVersion)
            {
                LogLoadFailure(paths.SaveFilePath, SaveError.UnsupportedVersion);
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, entry.Key, ESaveFailureStage.Migrate, SaveError.UnsupportedVersion);
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
            GateScope scope = GateScope.EnterFile(paths);
            return DeleteBlockWithGateAsync(paths, key, scope, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行块删除。
        /// </summary>
        private async UniTask DeleteBlockWithGateAsync(SavePaths paths, string key, GateScope scope, CancellationToken cancellationToken)
        {
            try
            {
                await scope.WaitAsync(cancellationToken);
                await UniTask.RunOnThreadPool(() => DeleteBlockCore(paths, key, cancellationToken), configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
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
            GateScope scope = GateScope.EnterFile(paths);
            scope.Wait();
            try
            {
                DeleteBlockCore(paths, key, CancellationToken.None);
            }
            finally
            {
                scope.Leave();
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
            SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors);
            if (readError != SaveError.None)
            {
                throw new GameException(StringUtility.Format("Save block delete failed, path: {0}, error: {1}.", paths.SaveFilePath, readError));
            }

            // 目标块不存在 = 幂等空删早退（不做迁移——迁移不持久化却占会话缓存会导致后续读取跳过迁移拿到旧数据）
            if (!SaveBlockComposer.TryFind(blocks, key, out SaveBlockEntry removedEntry))
            {
                return;
            }

            // 删除触发的整档重写同样先自愈迁移（保持「任何落盘文件均为当前版本」封闭性）
            MigrateBlocksForWrite(paths, ref blocks, blockErrors, cancellationToken);

            // 迁移可能改名了目标块——重定位；被改名走掉视为删除已达成（按迁移后键集合为准，删除事件仍报原始条目）
            if (!SaveBlockComposer.TryFind(blocks, key, out _))
            {
                WriteContainerFile(paths, blocks, cancellationToken);
                SaveService.RaiseBlockDeleted(paths.FileName, paths.FolderName, key, removedEntry.Backend, removedEntry.Bytes.Length);
                return;
            }

            List<SaveBlockEntry> remainingBlocks = SaveBlockComposer.Remove(blocks, key);
            if (remainingBlocks.Count == 0)
            {
                // 删除最后一个块时整档移除——槽位同步消亡
                Storage.DeleteFile(paths.SaveFilePath);
                SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Deleted, paths.FileName, paths.FolderName);
            }
            else
            {
                WriteContainerFile(paths, remainingBlocks, cancellationToken);
            }

            SaveService.RaiseBlockDeleted(paths.FileName, paths.FolderName, key, removedEntry.Backend, removedEntry.Bytes.Length);
        }

        /// <summary>
        /// 枚举存档文件内的全部数据块（含保留块；读文件与解析在调用线程执行）。
        /// <para>加密处理器下需要解密整档——大存档场景请自行节流调用频率。</para>
        /// <para>容器 v2 逐块校验下坏块同样列入清单（<see cref="SaveBlockInfo.Error"/> 非 <see cref="SaveError.None"/>），
        /// 键/版本/后端/尺寸等框架字段仅在 <see cref="SaveBlockInfo.HasMetadata"/> 为 <c>true</c> 时可信。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <returns>块元信息数组（健康块在前、坏块在后）；整档缺档/损坏时为空数组（损坏已记录错误日志）。</returns>
        public SaveBlockInfo[] GetBlockInfos(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            GateScope scope = GateScope.EnterFile(paths);
            scope.Wait();
            try
            {
                SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors);
                if (readError != SaveError.None)
                {
                    return Array.Empty<SaveBlockInfo>();
                }

                int errorCount = blockErrors?.Count ?? 0;
                var infos = new SaveBlockInfo[blocks.Count + errorCount];
                for (int i = 0; i < blocks.Count; i++)
                {
                    infos[i] = new SaveBlockInfo(blocks[i].Key, blocks[i].DataVersion, blocks[i].Backend, blocks[i].Bytes.Length);
                }

                for (int i = 0; i < errorCount; i++)
                {
                    SaveBlockError blockError = blockErrors[i];
                    infos[blocks.Count + i] = blockError.HasMetadata
                        ? new SaveBlockInfo(blockError.Key, blockError.DataVersion, blockError.Backend, blockError.SizeBytes, blockError.Error, true)
                        : new SaveBlockInfo(blockError.Key, 0, default, 0, blockError.Error, false);
                }

                return infos;
            }
            finally
            {
                scope.Leave();
            }
        }

        #endregion

        #region 存档删除 [DELETE]

        /// <summary>
        /// 从磁盘中删除单个存档（含全部数据块与截图 sidecar；幂等——目标不存在视为删除成功，不触发事件）。
        /// <para>持分层门执行（与块级读写互斥）——存在性判定与删除在同一临界区内完成，杜绝并发写入在删除后复活文件。</para>
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        public void DeleteSave(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            _ = TryDeleteSave(fileName, folderName);
        }

        /// <summary>
        /// 从磁盘中删除单个存档并返回目标先前的存在性（含全部数据块与截图 sidecar）。
        /// <para>幂等语义与 <see cref="DeleteSave"/> 一致（目标不存在不触发事件）；返回值消除 NotFound 悬而未决——
        /// <c>false</c> = 目标本不存在，<c>true</c> = 存在并已删除。</para>
        /// <para>持分层门执行（与块级读写互斥）——存在性判定与删除在同一临界区内完成，杜绝并发写入在删除后复活文件。</para>
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>目标先前存在并已删除返回 <c>true</c>；本不存在返回 <c>false</c>。</returns>
        public bool TryDeleteSave(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            GateScope scope = GateScope.EnterFile(paths);
            scope.Wait();
            bool existed;
            try
            {
                SaveStorageBackend storage = Storage;
                existed = storage.Exists(paths.SaveFilePath);
                if (existed)
                {
                    storage.DeleteFile(paths.SaveFilePath);
                    DeleteScreenshot(paths);
                }
            }
            finally
            {
                scope.Leave();
            }

            if (existed)
            {
                SaveMigrationManager.InvalidateSession(paths.SaveFilePath);
                SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Deleted, paths.FileName, paths.FolderName);
            }

            return existed;
        }

        /// <summary>
        /// 删除整个存档文件夹（含其中全部文件与子目录）。
        /// <para>目录级批量删除触发一次 <see cref="SaveService.SlotChanged"/>（<see cref="SaveSlotChangedArgs.FileName"/> 为 <c>null</c>；不保证目录先前存在）。</para>
        /// <para>持分层门执行（根 + 文件夹两级）——与该文件夹内全部槽位的块级读写互斥。</para>
        /// </summary>
        /// <param name="folderName">文件夹名称；不允许为空（清空全部请用 <see cref="DeleteAllSaveFiles"/>）。</param>
        public void DeleteSaveFolder(string folderName = DEFAULT_FOLDER_NAME)
        {
            _ = TryDeleteSaveFolder(folderName);
        }

        /// <summary>
        /// 删除整个存档文件夹并返回目录先前的存在性（含其中全部文件与子目录）。
        /// <para>事件行为与 <see cref="DeleteSaveFolder"/> 完全一致（目录级批量删除恒触发一次 <see cref="SaveService.SlotChanged"/>）；
        /// 返回值消除 NotFound 悬而未决——<c>false</c> = 目录本不存在。</para>
        /// <para>持分层门执行（根 + 文件夹两级）——与该文件夹内全部槽位的块级读写互斥。</para>
        /// </summary>
        /// <param name="folderName">文件夹名称；不允许为空（清空全部请用 <see cref="TryDeleteAllSaveFiles"/>）。</param>
        /// <returns>目录先前存在并已删除返回 <c>true</c>；本不存在返回 <c>false</c>。</returns>
        public bool TryDeleteSaveFolder(string folderName = DEFAULT_FOLDER_NAME)
        {
            ValidateFolderName(folderName);
            if (string.IsNullOrEmpty(folderName))
            {
                throw new ArgumentException("Folder name is required when deleting a folder.", nameof(folderName));
            }

            string directoryPath = BuildFolderPath(folderName);
            GateScope scope = GateScope.EnterFolder(directoryPath);
            scope.Wait();
            bool existed;
            try
            {
                SaveStorageBackend storage = Storage;
                existed = storage.DirectoryExists(directoryPath);
                if (existed)
                {
                    storage.DeleteDirectory(directoryPath);
                }
            }
            finally
            {
                scope.Leave();
            }

            SaveMigrationManager.InvalidateSession(null);
            SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Deleted, null, folderName);
            return existed;
        }

        /// <summary>
        /// 删除存档数据根目录（<c>persistentDataPath/Data/</c>）及其下所有存档。
        /// <para>触发一次 <see cref="SaveService.SlotChanged"/>（<see cref="SaveSlotChangedArgs.FileName"/> 为 <c>null</c>，文件夹为空串 = 数据根目录）。</para>
        /// <para>持分层门执行（根级）——与所有存档的块级读写互斥；用户数据清除场景（如合规删除）结果可靠。</para>
        /// </summary>
        public void DeleteAllSaveFiles()
        {
            _ = TryDeleteAllSaveFiles();
        }

        /// <summary>
        /// 删除存档数据根目录并返回目录先前的存在性（<c>persistentDataPath/Data/</c> 及其下所有存档）。
        /// <para>事件行为与 <see cref="DeleteAllSaveFiles"/> 完全一致（恒触发一次 <see cref="SaveService.SlotChanged"/>）；
        /// 返回值消除 NotFound 悬而未决——<c>false</c> = 数据根目录本不存在。</para>
        /// <para>持分层门执行（根级）——与所有存档的块级读写互斥；用户数据清除场景（如合规删除）结果可靠。</para>
        /// </summary>
        /// <returns>目录先前存在并已删除返回 <c>true</c>；本不存在返回 <c>false</c>。</returns>
        public bool TryDeleteAllSaveFiles()
        {
            string rootDirectory = BuildDataRootDirectory();
            GateScope scope = GateScope.EnterRoot(rootDirectory);
            scope.Wait();
            bool existed;
            try
            {
                SaveStorageBackend storage = Storage;
                existed = storage.DirectoryExists(rootDirectory);
                if (existed)
                {
                    storage.DeleteDirectory(rootDirectory);
                }
            }
            finally
            {
                scope.Leave();
            }

            SaveMigrationManager.InvalidateSession(null);
            SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Deleted, null, string.Empty);
            return existed;
        }

        /// <summary>
        /// 从磁盘中异步删除单个存档（含截图 sidecar；删除退避重试在工作线程执行；幂等——目标不存在不触发事件）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public UniTask DeleteSaveAsync(string fileName, string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            return TryDeleteSaveAsync(fileName, folderName, cancellationToken);
        }

        /// <summary>
        /// 异步删除单个存档并返回目标先前的存在性（含截图 sidecar；删除退避重试在工作线程执行）。
        /// <para>幂等与事件语义同 <see cref="TryDeleteSave"/>；持分层门执行——存在性判定与删除在同一临界区内完成。</para>
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>目标先前存在并已删除返回 <c>true</c>；本不存在返回 <c>false</c>。</returns>
        public async UniTask<bool> TryDeleteSaveAsync(string fileName, string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            GateScope scope = GateScope.EnterFile(paths);
            bool existed;
            try
            {
                await scope.WaitAsync(cancellationToken);
                SaveStorageBackend storage = Storage;
                existed = await UniTask.RunOnThreadPool(() =>
                {
                    bool exists = storage.Exists(paths.SaveFilePath);
                    storage.DeleteFile(paths.SaveFilePath);
                    if (exists)
                    {
                        storage.DeleteFile(ResolveScreenshotPath(paths));
                    }

                    return exists;
                }, configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
            }

            if (existed)
            {
                SaveMigrationManager.InvalidateSession(paths.SaveFilePath);
                SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Deleted, paths.FileName, paths.FolderName);
            }

            return existed;
        }

        /// <summary>
        /// 异步删除整个存档文件夹（含其中全部文件与子目录）。
        /// <para>目录级批量删除触发一次 <see cref="SaveService.SlotChanged"/>（不保证目录先前存在）。</para>
        /// <para>持分层门执行（根 + 文件夹两级）——与该文件夹内全部槽位的块级读写互斥。</para>
        /// </summary>
        /// <param name="folderName">文件夹名称；不允许为空（清空全部请用 <see cref="DeleteAllSaveFilesAsync"/>）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        public UniTask DeleteSaveFolderAsync(string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            return TryDeleteSaveFolderAsync(folderName, cancellationToken);
        }

        /// <summary>
        /// 异步删除整个存档文件夹并返回目录先前的存在性（含其中全部文件与子目录）。
        /// <para>事件行为与 <see cref="DeleteSaveFolderAsync"/> 完全一致（目录级批量删除恒触发一次 <see cref="SaveService.SlotChanged"/>）；
        /// 持分层门执行（根 + 文件夹两级）——存在性判定与删除在同一临界区内完成。</para>
        /// </summary>
        /// <param name="folderName">文件夹名称；不允许为空（清空全部请用 <see cref="TryDeleteAllSaveFilesAsync"/>）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>目录先前存在并已删除返回 <c>true</c>；本不存在返回 <c>false</c>。</returns>
        public async UniTask<bool> TryDeleteSaveFolderAsync(string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            ValidateFolderName(folderName);
            if (string.IsNullOrEmpty(folderName))
            {
                throw new ArgumentException("Folder name is required when deleting a folder.", nameof(folderName));
            }

            string directoryPath = BuildFolderPath(folderName);
            GateScope scope = GateScope.EnterFolder(directoryPath);
            bool existed;
            try
            {
                await scope.WaitAsync(cancellationToken);
                SaveStorageBackend storage = Storage;
                existed = await UniTask.RunOnThreadPool(() =>
                {
                    bool exists = storage.DirectoryExists(directoryPath);
                    if (exists)
                    {
                        storage.DeleteDirectory(directoryPath);
                    }

                    return exists;
                }, configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
            }

            SaveMigrationManager.InvalidateSession(null);
            SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Deleted, null, folderName);
            return existed;
        }

        /// <summary>
        /// 异步删除存档数据根目录（<c>persistentDataPath/Data/</c>）及其下所有存档。
        /// <para>持分层门执行（根级）——与所有存档的块级读写互斥。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public UniTask DeleteAllSaveFilesAsync(CancellationToken cancellationToken = default)
        {
            return TryDeleteAllSaveFilesAsync(cancellationToken);
        }

        /// <summary>
        /// 异步删除存档数据根目录并返回目录先前的存在性（<c>persistentDataPath/Data/</c> 及其下所有存档）。
        /// <para>事件行为与 <see cref="DeleteAllSaveFilesAsync"/> 完全一致（恒触发一次 <see cref="SaveService.SlotChanged"/>）；
        /// 持分层门执行（根级）——存在性判定与删除在同一临界区内完成。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>目录先前存在并已删除返回 <c>true</c>；本不存在返回 <c>false</c>。</returns>
        public async UniTask<bool> TryDeleteAllSaveFilesAsync(CancellationToken cancellationToken = default)
        {
            string rootDirectory = BuildDataRootDirectory();
            GateScope scope = GateScope.EnterRoot(rootDirectory);
            bool existed;
            try
            {
                await scope.WaitAsync(cancellationToken);
                SaveStorageBackend storage = Storage;
                existed = await UniTask.RunOnThreadPool(() =>
                {
                    bool exists = storage.DirectoryExists(rootDirectory);
                    if (exists)
                    {
                        storage.DeleteDirectory(rootDirectory);
                    }

                    return exists;
                }, configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
            }

            SaveMigrationManager.InvalidateSession(null);
            SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Deleted, null, string.Empty);
            return existed;
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
            return UniTask.RunOnThreadPool(() => storage.EnumerateFiles(directoryPath, extension), configureAwait: false, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 创建存档的单槽备份（<c>.bak</c> 后缀，覆盖旧备份）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        public void CreateBackup(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            GateScope scope = GateScope.EnterFile(paths);
            scope.Wait();
            try
            {
                try
                {
                    Storage.CreateBackup(paths.SaveFilePath);
                }
                catch (GameException)
                {
                    SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.StorageWrite, SaveError.IoFailed);
                    throw;
                }
            }
            finally
            {
                scope.Leave();
            }

            SaveService.RaiseSlotChanged(ESaveSlotChangeKind.BackupCreated, paths.FileName, paths.FolderName);
        }

        /// <summary>
        /// 从单槽备份恢复存档（备份经临时文件原子替换回存档路径）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        public void RestoreBackup(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            GateScope scope = GateScope.EnterFile(paths);
            scope.Wait();
            try
            {
                try
                {
                    Storage.RestoreBackup(paths.SaveFilePath);
                }
                catch (GameException)
                {
                    SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.StorageWrite, SaveError.IoFailed);
                    throw;
                }
            }
            finally
            {
                scope.Leave();
            }

            // 备份内容可能处于旧数据版本——失效会话迁移缓存，下次读取重新探测
            SaveMigrationManager.InvalidateSession(paths.SaveFilePath);
            SaveService.RaiseSlotChanged(ESaveSlotChangeKind.BackupRestored, paths.FileName, paths.FolderName);
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

            GateScope scope = GateScope.EnterFile(paths);
            return UpsertRawBlocksWithGateAsync(paths, additions, scope, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行组件块合并写回。
        /// </summary>
        private async UniTask UpsertRawBlocksWithGateAsync(SavePaths paths, List<SaveBlockEntry> additions, GateScope scope, CancellationToken cancellationToken)
        {
            try
            {
                await scope.WaitAsync(cancellationToken);
                await UniTask.RunOnThreadPool(() =>
                {
                    // 坏块在写回时自然剔除（数据已不可读），健康块保留
                    SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> existingBlocks, out List<SaveBlockError> blockErrors);
                    if (readError != SaveError.None)
                    {
                        QuarantineUnreadableSave(paths, readError);
                        existingBlocks = new List<SaveBlockEntry>();
                        blockErrors = null;
                    }

                    // 写入自愈：旧版本档先迁移到当前版本再合并组件块
                    MigrateBlocksForWrite(paths, ref existingBlocks, blockErrors, cancellationToken);
                    List<SaveBlockEntry> mergedBlocks = SaveBlockComposer.UpsertAll(existingBlocks, additions);
                    WriteContainerFile(paths, mergedBlocks, cancellationToken);
                    for (int i = 0; i < additions.Count; i++)
                    {
                        SaveService.RaiseBlockSaved(paths.FileName, paths.FolderName, additions[i].Key, additions[i].Backend, additions[i].Bytes.Length);
                    }

                    SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Saved, paths.FileName, paths.FolderName);
                }, configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
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
            GateScope scope = GateScope.EnterFile(paths);
            return ReadRawBlocksWithGateAsync(paths, scope, cancellationToken);
        }

        /// <summary>
        /// 读取存档文件内全部块载荷（键 → 字节；在调用线程执行，阻塞直至完成；持串行门，与写路径互斥）。
        /// <para>语义与 <see cref="ReadRawBlocksAsync"/> 一致（含迁移总线前置与坏块失败事件）；编辑器工具与同步场景使用。</para>
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <returns>键 → 块载荷字典；缺档/损坏（已记录日志）返回空字典。</returns>
        internal Dictionary<string, byte[]> ReadRawBlocks(SavePaths paths)
        {
            GateScope scope = GateScope.EnterFile(paths);
            scope.Wait();
            try
            {
                // 组件恢复只取健康块——坏块对应组件保持现状（部分恢复），坏块明细记告警日志并逐块触发失败事件
                SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors);
                if (readError != SaveError.None)
                {
                    return new Dictionary<string, byte[]>();
                }

                // 迁移总线前置（组件块同样参与文件级版本链——迁移器可改名组件块键/重写 KVT 记录）
                SaveError migrationError = MigrateBlocksIfNeeded(paths, ref blocks, blockErrors, forceWriteBack: false, CancellationToken.None);
                if (migrationError != SaveError.None)
                {
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.Migrate, migrationError);
                    return new Dictionary<string, byte[]>();
                }

                if (blockErrors != null)
                {
                    for (int i = 0; i < blockErrors.Count; i++)
                    {
                        SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, blockErrors[i].Key, ESaveFailureStage.ContainerParse, blockErrors[i].Error);
                    }
                }

                var result = new Dictionary<string, byte[]>(blocks.Count);
                for (int i = 0; i < blocks.Count; i++)
                {
                    result[blocks[i].Key] = blocks[i].Bytes;
                }

                return result;
            }
            finally
            {
                scope.Leave();
            }
        }

        /// <summary>
        /// 持串行门执行组件块读取。
        /// </summary>
        private async UniTask<Dictionary<string, byte[]>> ReadRawBlocksWithGateAsync(SavePaths paths, GateScope scope, CancellationToken cancellationToken)
        {
            try
            {
                await scope.WaitAsync(cancellationToken);
                return await UniTask.RunOnThreadPool(() =>
                {
                    // 组件恢复只取健康块——坏块对应组件保持现状（部分恢复），坏块明细记告警日志并逐块触发失败事件
                    SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors);
                    if (readError != SaveError.None)
                    {
                        return new Dictionary<string, byte[]>();
                    }

                    // 迁移总线前置（组件块同样参与文件级版本链——迁移器可改名组件块键/重写 KVT 记录）
                    SaveError migrationError = MigrateBlocksIfNeeded(paths, ref blocks, blockErrors, forceWriteBack: false, cancellationToken);
                    if (migrationError != SaveError.None)
                    {
                        SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.Migrate, migrationError);
                        return new Dictionary<string, byte[]>();
                    }

                    if (blockErrors != null)
                    {
                        for (int i = 0; i < blockErrors.Count; i++)
                        {
                            SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, blockErrors[i].Key, ESaveFailureStage.ContainerParse, blockErrors[i].Error);
                        }
                    }

                    var result = new Dictionary<string, byte[]>(blocks.Count);
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        result[blocks[i].Key] = blocks[i].Bytes;
                    }

                    return result;
                }, configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
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

            GateScope scope = GateScope.EnterFile(paths);
            return DeleteRawBlocksWithGateAsync(paths, keys, scope, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行组件块删除。
        /// </summary>
        private async UniTask DeleteRawBlocksWithGateAsync(SavePaths paths, List<string> keys, GateScope scope, CancellationToken cancellationToken)
        {
            try
            {
                await scope.WaitAsync(cancellationToken);
                await UniTask.RunOnThreadPool(() =>
                {
                    SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> existingBlocks, out List<SaveBlockError> blockErrors);
                    if (readError != SaveError.None)
                    {
                        return;
                    }

                    // 删除触发的整档重写同样先自愈迁移
                    MigrateBlocksForWrite(paths, ref existingBlocks, blockErrors, cancellationToken);

                    List<SaveBlockEntry> mergedBlocks = existingBlocks;
                    for (int i = 0; i < keys.Count; i++)
                    {
                        // 仅真实存在并移除的块触发删除事件（幂等空删不触发）
                        if (SaveBlockComposer.TryFind(mergedBlocks, keys[i], out SaveBlockEntry removedEntry))
                        {
                            mergedBlocks = SaveBlockComposer.Remove(mergedBlocks, keys[i]);
                            SaveService.RaiseBlockDeleted(paths.FileName, paths.FolderName, removedEntry.Key, removedEntry.Backend, removedEntry.Bytes.Length);
                        }
                    }

                    if (mergedBlocks.Count == existingBlocks.Count)
                    {
                        return; // 无实际移除——不写回
                    }

                    if (mergedBlocks.Count == 0)
                    {
                        Storage.DeleteFile(paths.SaveFilePath);
                        SaveService.RaiseSlotChanged(ESaveSlotChangeKind.Deleted, paths.FileName, paths.FolderName);
                        return;
                    }

                    WriteContainerFile(paths, mergedBlocks, cancellationToken);
                }, configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
            }
        }

        #endregion

        #region 版本迁移 [MIGRATION]

        /// <summary>
        /// 显式迁移指定存档到当前数据版本（在调用线程执行，阻塞直至完成；仅限主线程）。
        /// <para>显式调用即表达立即修复意图——迁移成功后强制回写（不受 <see cref="SaveServiceSettings.MigrationWriteBack"/> 约束）；
        /// 版本相等/迁移总线未激活为无操作。版本低于当前且迁移链缺失/失败返回 <see cref="SaveError.MigrationFailed"/>，高于当前返回 <see cref="SaveError.UnsupportedVersion"/>。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <returns>错误码（缺档返回 <see cref="SaveError.FileNotFound"/>）。</returns>
        public SaveError MigrateSave(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            GateScope scope = GateScope.EnterFile(paths);
            scope.Wait();
            try
            {
                return MigrateSaveCore(paths, CancellationToken.None);
            }
            finally
            {
                scope.Leave();
            }
        }

        /// <summary>
        /// 显式迁移指定存档到当前数据版本，IO 在工作线程执行。
        /// <para>语义与 <see cref="MigrateSave"/> 一致（迁移成功强制回写）。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>迁移结果（错误码）。</returns>
        public UniTask<SaveError> MigrateSaveAsync(string fileName, string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            GateScope scope = GateScope.EnterFile(paths);
            return MigrateSaveWithGateAsync(paths, scope, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行显式迁移。
        /// </summary>
        private async UniTask<SaveError> MigrateSaveWithGateAsync(SavePaths paths, GateScope scope, CancellationToken cancellationToken)
        {
            try
            {
                await scope.WaitAsync(cancellationToken);
                return await UniTask.RunOnThreadPool(() => MigrateSaveCore(paths, cancellationToken), configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
            }
        }

        /// <summary>
        /// 显式迁移核心（同步，工作线程调用；须持串行门）：读容器 → 迁移 → 成功强制回写。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>错误码。</returns>
        internal SaveError MigrateSaveCore(SavePaths paths, CancellationToken cancellationToken)
        {
            SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors);
            if (readError != SaveError.None)
            {
                return readError;
            }

            if (blocks.Count == 0)
            {
                return SaveError.FileNotFound;
            }

            return MigrateBlocksIfNeeded(paths, ref blocks, blockErrors, forceWriteBack: true, cancellationToken);
        }

        /// <summary>
        /// 读路径迁移前置（加载管线统一入口）：按需执行迁移链并按 <see cref="_migrationWriteBack"/> 惰性回写。
        /// <para>回写失败不阻断本次加载（内存数据已迁移；失败经日志与 <see cref="SaveService.SaveFailed"/> 事件观测，下一会话重试）。</para>
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">健康数据块列表（迁移后替换）。</param>
        /// <param name="blockErrors">坏块清单（元数据块损坏时迁移保守失败）。</param>
        /// <param name="forceWriteBack">显式迁移调用的强制回写（绕过 <see cref="_migrationWriteBack"/> 设置）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>错误码。</returns>
        private SaveError MigrateBlocksIfNeeded(SavePaths paths, ref List<SaveBlockEntry> blocks, List<SaveBlockError> blockErrors, bool forceWriteBack, CancellationToken cancellationToken)
        {
            if (!SaveMigrationManager.IsActive || blocks.Count == 0)
            {
                return SaveError.None;
            }

            SaveError error = SaveMigrationManager.TryMigrateBlocks(paths, blocks, blockErrors, out List<SaveBlockEntry> migratedBlocks, out bool migrated);
            if (error != SaveError.None || !migrated)
            {
                return error;
            }

            blocks = migratedBlocks;
            if (!forceWriteBack && !_migrationWriteBack)
            {
                return SaveError.None;
            }

            try
            {
                WriteContainerFile(paths, blocks, cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                LogUtility.Error("[SaveService] Migration write-back failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name);
                SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.StorageWrite, SaveError.IoFailed);
            }

            return SaveError.None;
        }

        /// <summary>
        /// 写路径迁移前置（写入自愈）：旧版本档先迁移到当前版本再合并写回，失败按写路径契约 fail-fast 上抛 <see cref="GameException"/>。
        /// <para>迁移结果由调用方随后的 <see cref="WriteContainerFile"/> 一并落盘（不单独回写）。</para>
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">健康数据块列表（迁移后替换）。</param>
        /// <param name="blockErrors">坏块清单。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private void MigrateBlocksForWrite(SavePaths paths, ref List<SaveBlockEntry> blocks, List<SaveBlockError> blockErrors, CancellationToken cancellationToken)
        {
            SaveError error = MigrateBlocksIfNeeded(paths, ref blocks, blockErrors, forceWriteBack: false, cancellationToken);
            if (error != SaveError.None)
            {
                SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.Migrate, error);
                throw new GameException(StringUtility.Format("Save migration failed on write path, path: {0}, error: {1}.", paths.SaveFilePath, error));
            }
        }

        /// <summary>
        /// 版本化激活时将当前数据版本盖章进元数据块（保留游戏层元数据字段，仅推进 <see cref="SaveMetadata.SaveVersion"/>）。
        /// <para>元数据块 JSON 损坏时用全新元数据替换并记告警；未激活时零开销直通。</para>
        /// </summary>
        /// <param name="blocks">待写入的数据块列表。</param>
        /// <returns>盖章后的数据块列表。</returns>
        private List<SaveBlockEntry> StampSaveVersionIfActive(List<SaveBlockEntry> blocks)
        {
            if (!SaveMigrationManager.IsActive)
            {
                return blocks;
            }

            int currentVersion = SaveMigrationManager.CurrentVersion;
            SaveMetadata metadata = null;
            if (SaveBlockComposer.TryFind(blocks, META_BLOCK_KEY, out SaveBlockEntry metaEntry))
            {
                try
                {
                    metadata = SaveSerializerRegistry.GetRequired(ESaveBackend.Json).Deserialize<SaveMetadata>(metaEntry.Bytes);
                }
                catch (Exception)
                {
                    LogUtility.Warning("[SaveService] Meta block is undecodable, replacing with a fresh one, path context: stamping save version.");
                }
            }

            if (metadata != null && metadata.SaveVersion == currentVersion)
            {
                return blocks;
            }

            metadata ??= new SaveMetadata();
            metadata.SaveVersion = currentVersion;
            byte[] metaBytes = SaveSerializerRegistry.GetRequired(ESaveBackend.Json).Serialize(metadata);
            return SaveBlockComposer.Upsert(blocks, new SaveBlockEntry(META_BLOCK_KEY, 1, ESaveBackend.Json, metaBytes));
        }

        #endregion

        #region 截图随档文件 [SCREENSHOT SIDECAR]

        /// <summary>
        /// 解析截图 sidecar 完整路径（与存档文件同目录；存档基名 + <c>.screenshot.png</c>）。
        /// </summary>
        /// <param name="paths">存档路径集合。</param>
        /// <returns>截图 sidecar 完整路径。</returns>
        internal static string ResolveScreenshotPath(SavePaths paths)
        {
            string screenshotFileName = SaveScreenshotUtility.DetermineScreenshotFileName(Path.GetFileName(paths.SaveFilePath));
            return Path.Combine(paths.DirectoryPath, screenshotFileName);
        }

        /// <summary>
        /// 写入截图 sidecar（在调用线程执行，阻塞直至完成；经存储层原子写，失败抛 <see cref="GameException"/>）。
        /// </summary>
        /// <param name="paths">存档路径集合。</param>
        /// <param name="pngBytes">PNG 编码字节。</param>
        internal void WriteScreenshot(SavePaths paths, byte[] pngBytes)
        {
            Storage.WriteAtomic(ResolveScreenshotPath(paths), pngBytes, CancellationToken.None);
        }

        /// <summary>
        /// 异步写入截图 sidecar（IO 在工作线程执行；失败抛 <see cref="GameException"/>）。
        /// </summary>
        /// <param name="paths">存档路径集合。</param>
        /// <param name="pngBytes">PNG 编码字节。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>写入完成的异步任务。</returns>
        internal UniTask WriteScreenshotAsync(SavePaths paths, byte[] pngBytes, CancellationToken cancellationToken)
        {
            return Storage.WriteAtomicAsync(ResolveScreenshotPath(paths), pngBytes, cancellationToken);
        }

        /// <summary>
        /// 删除截图 sidecar（幂等——不存在视为成功；存档删除时级联调用，防止同名新档复活陈旧缩略图）。
        /// </summary>
        /// <param name="paths">存档路径集合。</param>
        internal void DeleteScreenshot(SavePaths paths)
        {
            Storage.DeleteFile(ResolveScreenshotPath(paths));
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
        /// 分层门作用域：按「根目录 → 文件夹 → 文件」固定序取门（顺序全局一致，防死锁），
        /// 使整档删除、文件夹删除与根目录清空同块级读写互斥——删除不再与读-改-写交叉（删除后被并发写入复活/丢块），
        /// 用户数据清除类操作（<see cref="DeleteAllSaveFiles"/>）结果可靠。
        /// <para>根门为全局串行点：跨文件夹的存档 IO 互相排队（存档 IO 低频，以队列化换删除可靠性）。
        /// 各层级计数在 <see cref="SaveFileGate"/> 表上登记，<see cref="Leave"/> 幂等归还（取消等门只释放已持层级）。</para>
        /// </summary>
        private sealed class GateScope
        {
            /// <summary>层级键（取门顺序：根 → 文件夹 → 文件）。</summary>
            private readonly string[] _keys;

            /// <summary>层级信号量（与 <see cref="_keys"/> 对齐）。</summary>
            private readonly SemaphoreSlim[] _gates;

            /// <summary>已实际持门的前缀层级数（等门取消时只释放该前缀）。</summary>
            private int _acquiredCount;

            /// <summary>归还标记（幂等守卫）。</summary>
            private bool _left;

            private GateScope(string[] keys, SemaphoreSlim[] gates)
            {
                _keys = keys;
                _gates = gates;
            }

            /// <summary>
            /// 取文件级门作用域（根 + 文件夹 + 文件三级）。
            /// </summary>
            /// <param name="paths">已解析的路径集合。</param>
            /// <returns>门作用域。</returns>
            public static GateScope EnterFile(SavePaths paths)
            {
                return EnterAll(BuildDataRootDirectory(), paths.DirectoryPath, paths.SaveFilePath);
            }

            /// <summary>
            /// 取文件夹级门作用域（根 + 文件夹两级；文件夹批量删除用）。
            /// </summary>
            /// <param name="directoryPath">目标文件夹完整路径（与 <see cref="SavePaths.DirectoryPath"/> 同串）。</param>
            /// <returns>门作用域。</returns>
            public static GateScope EnterFolder(string directoryPath)
            {
                return EnterAll(BuildDataRootDirectory(), directoryPath);
            }

            /// <summary>
            /// 取根级门作用域（仅根目录；清空全部存档用）。
            /// </summary>
            /// <param name="rootDirectory">存档数据根目录完整路径。</param>
            /// <returns>门作用域。</returns>
            public static GateScope EnterRoot(string rootDirectory)
            {
                return EnterAll(rootDirectory);
            }

            private static GateScope EnterAll(params string[] keys)
            {
                var gates = new SemaphoreSlim[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    gates[i] = SaveFileGate.Enter(keys[i]);
                }

                return new GateScope(keys, gates);
            }

            /// <summary>
            /// 同步等待全部层级持门。
            /// </summary>
            public void Wait()
            {
                for (int i = 0; i < _gates.Length; i++)
                {
                    _gates[i].Wait();
                    _acquiredCount = i + 1;
                }
            }

            /// <summary>
            /// 异步等待全部层级持门（等门期取消时抛出，已持前缀层级由 <see cref="Leave"/> 正确归还）。
            /// </summary>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>持门完成的异步任务。</returns>
            public async UniTask WaitAsync(CancellationToken cancellationToken)
            {
                for (int i = 0; i < _gates.Length; i++)
                {
                    await _gates[i].WaitAsync(cancellationToken);
                    _acquiredCount = i + 1;
                }
            }

            /// <summary>
            /// 归还全部层级（逆序释放已持前缀，未持层级仅归还占用计数；幂等——重复调用为空操作）。
            /// </summary>
            public void Leave()
            {
                if (_left)
                {
                    return;
                }

                _left = true;
                for (int i = _gates.Length - 1; i >= 0; i--)
                {
                    SaveFileGate.Leave(_keys[i], _gates[i], i < _acquiredCount);
                }

                _acquiredCount = 0;
            }
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

            /// <summary>API 层原始文件名（事件参数用；手工构造路径集合时为 <c>null</c>）。</summary>
            public readonly string FileName;

            /// <summary>API 层原始文件夹名称（事件参数用；手工构造路径集合时为 <c>null</c>）。</summary>
            public readonly string FolderName;

            /// <summary>
            /// 创建路径集合（无 API 层名称上下文）。
            /// </summary>
            /// <param name="directoryPath">目标文件夹完整路径。</param>
            /// <param name="saveFilePath">目标存档文件完整路径。</param>
            public SavePaths(string directoryPath, string saveFilePath)
                : this(directoryPath, saveFilePath, null, null)
            {
            }

            /// <summary>
            /// 创建路径集合。
            /// </summary>
            /// <param name="directoryPath">目标文件夹完整路径。</param>
            /// <param name="saveFilePath">目标存档文件完整路径。</param>
            /// <param name="fileName">API 层原始文件名。</param>
            /// <param name="folderName">API 层原始文件夹名称。</param>
            public SavePaths(string directoryPath, string saveFilePath, string fileName, string folderName)
            {
                DirectoryPath = directoryPath;
                SaveFilePath = saveFilePath;
                FileName = fileName;
                FolderName = folderName;
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
            return new SavePaths(directoryPath, saveFilePath, fileName, folderName);
        }

        /// <summary>
        /// 拼装存档文件夹完整路径（以目录分隔符结尾）。
        /// </summary>
        /// <param name="folderName">已校验的文件夹名称（可为空 = 数据根目录）。</param>
        /// <returns>文件夹路径。</returns>
        private static string BuildFolderPath(string folderName)
        {
            string directoryPath = string.IsNullOrEmpty(folderName)
                ? Path.Combine(BasePath, DATA_FOLDER_NAME)
                : Path.Combine(BasePath, DATA_FOLDER_NAME, folderName);
            return directoryPath + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// 拼装存档数据根目录完整路径。
        /// </summary>
        /// <returns>根目录路径。</returns>
        private static string BuildDataRootDirectory()
        {
            return Path.Combine(BasePath, DATA_FOLDER_NAME);
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
        /// <para>保留前缀（<c>__</c>）禁止用户新建块——但对既有保留块（<c>__main__</c>/<c>__meta</c>）的读写/删除为合法操作（便捷 API 与元数据管理），
        /// 经 <see cref="MAIN_BLOCK_KEY"/>/<see cref="META_BLOCK_KEY"/> 常量访问时豁免前缀校验。</para>
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

            if (key.Length > MAX_BLOCK_KEY_LENGTH)
            {
                throw new ArgumentException(StringUtility.Format("Save block key '{0}' exceeds the max length {1}.", key, MAX_BLOCK_KEY_LENGTH), nameof(key));
            }

            if (key.IndexOfAny(s_PathSeparators) >= 0 || key.IndexOfAny(s_InvalidFileNameChars) >= 0)
            {
                throw new ArgumentException(StringUtility.Format("Save block key '{0}' contains invalid characters.", key), nameof(key));
            }

            // 保留前缀仅豁免框架专用保留块（__main__/__meta），其余 __ 前缀一律拒绝
            if (key.StartsWith(RESERVED_BLOCK_KEY_PREFIX, StringComparison.Ordinal)
                && !string.Equals(key, MAIN_BLOCK_KEY, StringComparison.Ordinal)
                && !string.Equals(key, META_BLOCK_KEY, StringComparison.Ordinal))
            {
                throw new ArgumentException(StringUtility.Format("Save block key '{0}' uses the reserved prefix '{1}'.", key, RESERVED_BLOCK_KEY_PREFIX), nameof(key));
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

        /// <summary>不可读旧档隔离后缀（写路径覆写前侧车拷贝，保留可抢救字节）。</summary>
        internal const string CorruptFileSuffix = ".corrupt";

        /// <summary>
        /// 将不可读旧档隔离拷贝为 <c>xxx.sav.corrupt</c> sidecar（尽力而为：失败仅记错误日志，不阻断后续覆写）。
        /// <para>写路径读失败（头损坏/解密失败/瞬时 IO）时调用——避免下一次保存静默用「仅含新块」的文件替换旧档。</para>
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="error">读失败错误码（仅用于日志上下文）。</param>
        private void QuarantineUnreadableSave(SavePaths paths, SaveError error)
        {
            SaveError ioError = Storage.TryReadAllBytes(paths.SaveFilePath, out byte[] fileBytes);
            if (ioError != SaveError.None || fileBytes == null)
            {
                LogUtility.Error("[SaveService] Unreadable save cannot be quarantined (raw read failed), path: {0}, readError: {1}, quarantineError: {2}.",
                    paths.SaveFilePath, error, ioError);
                return;
            }

            string quarantinePath = paths.SaveFilePath + CorruptFileSuffix;
            try
            {
                Storage.WriteAtomic(quarantinePath, fileBytes, CancellationToken.None);
                LogUtility.Warning("[SaveService] Unreadable save quarantined before overwrite, path: {0} -> {1}, error: {2}.",
                    paths.SaveFilePath, quarantinePath, error);
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Unreadable save quarantine failed, path: {0}, quarantinePath: {1}, exception: {2}.",
                    paths.SaveFilePath, quarantinePath, exception.GetType().Name);
            }
        }

        /// <summary>
        /// 读取并还原存档容器：缺档返回空块集（<see cref="SaveError.None"/>），其余错误分型返回（调用方决定日志与兜底语义）。
        /// <para>管线：读文件 → 校验头/CRC → 载荷还原（子类：解密/直通）→ 容器解析。
        /// 容器 v2 逐块校验放行的坏块记入 <paramref name="blockErrors"/> 并在此统一记告警日志（部分恢复不阻断业务）。</para>
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">成功或缺档时的健康数据块列表（缺档为空列表；坏块已剔除）。</param>
        /// <param name="blockErrors">坏块清单（无坏块或整档失败为 <c>null</c>）。</param>
        /// <returns>错误码。</returns>
        private SaveError ReadContainerOrEmpty(SavePaths paths, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors)
        {
            blocks = new List<SaveBlockEntry>();
            blockErrors = null;
            SaveError ioError = Storage.TryReadAllBytes(paths.SaveFilePath, out byte[] fileBytes);
            if (ioError == SaveError.FileNotFound)
            {
                // 缺档 = 空块集（正常业务流，不记录日志）
                return SaveError.None;
            }

            if (ioError != SaveError.None)
            {
                // IO 失败的详细日志已由存储层记录；事件统一走 LoadFailed（写路径的合并读失败同样经此观测）
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.StorageRead, ioError);
                return ioError;
            }

            SaveError restoreError = ReadAndRestoreContainer(paths, fileBytes, out blocks, out blockErrors);
            if (restoreError != SaveError.None)
            {
                LogLoadFailure(paths.SaveFilePath, restoreError);
                return restoreError;
            }

            if (blockErrors != null)
            {
                LogUtility.Warning("[SaveService] {0} save block(s) corrupted and skipped, path: {1}.", blockErrors.Count, paths.SaveFilePath);
            }

            return SaveError.None;
        }

        /// <summary>
        /// 将文件字节（头 + 载荷）还原为容器块列表（纯变换，不做日志；错误分型由各环节判定并触发 <see cref="SaveService.LoadFailed"/> 事件）。
        /// <para>载荷不做整档拷贝——以 <see cref="SaveBufferSegment"/> 视图直通载荷还原钩子（明文处理器零拷贝，加密处理器区间直解）。</para>
        /// </summary>
        /// <param name="paths">已解析的路径集合（事件参数上下文）。</param>
        /// <param name="fileBytes">存档文件完整字节。</param>
        /// <param name="blocks">成功时的健康数据块列表。</param>
        /// <param name="blockErrors">坏块清单（无坏块为 <c>null</c>）。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>（含部分恢复）、<see cref="SaveError.InvalidFormat"/>、<see cref="SaveError.UnsupportedVersion"/>、
        /// <see cref="SaveError.Corrupted"/>、<see cref="SaveError.DecryptionFailed"/> 或 <see cref="SaveError.IntegrityCheckFailed"/>。</returns>
        private SaveError ReadAndRestoreContainer(SavePaths paths, byte[] fileBytes, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors)
        {
            blocks = null;
            blockErrors = null;
            SaveError headerError = SaveFileHeader.Read(fileBytes, out SaveFileHeader header);
            if (headerError != SaveError.None)
            {
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.HeaderValidation, headerError);
                return headerError;
            }

            // 整档 CRC 先行把关（头校验优先）：存储损坏在此整档拒绝，不进入逐块部分恢复
            int payloadLength = fileBytes.Length - SaveFileHeader.Size;
            if (payloadLength != header.PayloadLength
                || Crc32.Compute(fileBytes.AsSpan(SaveFileHeader.Size, payloadLength)) != header.PayloadCrc)
            {
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.HeaderValidation, SaveError.Corrupted);
                return SaveError.Corrupted;
            }

            SaveError transformError = OnRestorePayload(new SaveBufferSegment(fileBytes, SaveFileHeader.Size, payloadLength), out SaveBufferSegment restored);
            if (transformError != SaveError.None)
            {
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.Restore, transformError);
                return transformError;
            }

            SaveError containerError = TryDecompressAndReadContainer(header, restored, out blocks, out blockErrors);
            if (containerError != SaveError.None)
            {
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null,
                    (header.Flags & SaveFileHeader.FlagCompressed) != 0 ? ESaveFailureStage.Decompress : ESaveFailureStage.ContainerParse,
                    containerError);
                return containerError;
            }

            return SaveError.None;
        }

        /// <summary>
        /// 按文件头标志还原并解析容器（魔数/flags sniff 幂等：未压缩档原样透传，新旧档共存）。
        /// <para>标志与提供方 ID 不一致（篡改/写中断）判别为 <see cref="SaveError.Corrupted"/>；
        /// 未注册的提供方 ID 判别为 <see cref="SaveError.UnsupportedVersion"/>（未来格式/依赖未接入保护）。</para>
        /// </summary>
        /// <param name="header">已解析的文件头。</param>
        /// <param name="restored">载荷还原视图（解密后）。</param>
        /// <param name="blocks">成功时的健康数据块列表。</param>
        /// <param name="blockErrors">坏块清单（无坏块为 <c>null</c>）。</param>
        /// <returns>错误码。</returns>
        private static SaveError TryDecompressAndReadContainer(SaveFileHeader header, SaveBufferSegment restored, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors)
        {
            blocks = null;
            blockErrors = null;
            if ((header.Flags & SaveFileHeader.FlagCompressed) == 0)
            {
                return header.CompressionProviderId != 0
                    ? SaveError.Corrupted
                    : SaveFileContainer.Read(restored.AsSpan(), out blocks, out blockErrors);
            }

            if (header.CompressionProviderId == 0 || header.CompressionProviderId > byte.MaxValue)
            {
                return SaveError.Corrupted;
            }

            if (!SaveCompressionRegistry.TryGet((byte)header.CompressionProviderId, out ICompressionProvider compression))
            {
                return SaveError.UnsupportedVersion;
            }

            byte[] container;
            try
            {
                // 视图直通解压（明文处理器下别名为文件缓冲——零整档拷贝）
                container = compression.Decompress(restored);
            }
            catch (Exception)
            {
                // 压缩数据非法（流格式破坏）——归一为存储损坏分型
                return SaveError.Corrupted;
            }

            return SaveFileContainer.Read(container, out blocks, out blockErrors);
        }

        /// <summary>
        /// 块缺失细分：键命中坏块清单 = 该块损坏（逐块分型）；存在结构性坏块 = 尾部解析中断、目标块状态不可知按损坏论；否则 = 真无块。
        /// </summary>
        /// <param name="blockErrors">坏块清单（可为 <c>null</c>）。</param>
        /// <param name="key">目标块键。</param>
        /// <returns>错误码：<see cref="SaveError.FileNotFound"/> 或坏块记录的错误码。</returns>
        private static SaveError ClassifyMissingKey(List<SaveBlockError> blockErrors, string key)
        {
            if (blockErrors != null)
            {
                bool hasStructural = false;
                for (int i = 0; i < blockErrors.Count; i++)
                {
                    if (blockErrors[i].Key == null)
                    {
                        hasStructural = true;
                    }
                    else if (string.Equals(blockErrors[i].Key, key, StringComparison.Ordinal))
                    {
                        return blockErrors[i].Error;
                    }
                }

                if (hasStructural)
                {
                    return SaveError.Corrupted;
                }
            }

            return SaveError.FileNotFound;
        }

        /// <summary>
        /// 将容器块集写入存档文件：容器组装 → 压缩（可选，转换链固定为压缩先于加密）→ 载荷变换（子类加密钩子）→ CRC → 组装文件头 → 存储层两段式原子提交。
        /// <para>容器缓冲区统一经 <see cref="ArrayPool{T}"/> 租赁复用（含压缩档——压缩提供方经 <see cref="SaveBufferSegment"/> 视图消费租赁缓冲）；
        /// 文件头 stackalloc + 载荷视图经存储层两段写直落盘，写路径无整档拼接分配（归还缓冲区清零防明文残留）。</para>
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">数据块列表。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private void WriteContainerFile(SavePaths paths, List<SaveBlockEntry> blocks, CancellationToken cancellationToken)
        {
            // 版本化激活时盖章当前数据版本（任何落盘文件均为当前版本——写入自愈封闭性）
            blocks = StampSaveVersionIfActive(blocks);
            int containerSize = SaveFileContainer.GetSize(blocks);
            ICompressionProvider compression = _compression;
            byte[] containerBuffer = ArrayPool<byte>.Shared.Rent(containerSize);
            try
            {
                SaveFileContainer.Write(containerBuffer.AsSpan(0, containerSize), blocks);

                uint flags = 0u;
                uint compressionProviderId = 0u;
                SaveBufferSegment transformInput = new SaveBufferSegment(containerBuffer, 0, containerSize);
                if (compression != null)
                {
                    byte[] compressed;
                    try
                    {
                        compressed = compression.Compress(transformInput);
                    }
                    catch (Exception exception)
                    {
                        SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.Compress, SaveError.CompressionFailed);
                        throw new GameException(StringUtility.Format("Save compression failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name), exception);
                    }

                    transformInput = SaveBufferSegment.FromExact(compressed);
                    flags = SaveFileHeader.FlagCompressed;
                    compressionProviderId = compression.ProviderId;
                }

                SaveError transformError = OnTransformContainer(transformInput, out SaveBufferSegment payload);
                if (transformError != SaveError.None)
                {
                    SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.Transform, SaveError.TransformFailed);
                    throw new GameException(StringUtility.Format("Save payload transform failed, path: {0}, error: {1}.", paths.SaveFilePath, transformError));
                }

                // 写盘的是存储载荷（加密后），不是未加密容器——CRC 也是载荷的；头部 stackalloc + 载荷视图两段写直落盘，零整档拼接
                uint payloadCrc = Crc32.Compute(payload.AsSpan());
                Span<byte> headerBytes = stackalloc byte[SaveFileHeader.Size];
                SaveFileHeader.Write(headerBytes, payload.Length, payloadCrc, flags, compressionProviderId);
                try
                {
                    Storage.WriteAtomic(paths.SaveFilePath, headerBytes, payload.AsSpan(), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, ESaveFailureStage.StorageWrite, SaveError.IoFailed);
                    throw;
                }
            }
            finally
            {
                // 容器为明文（加密在下游钩子）——归还时清零，避免明文残留池化缓冲区被后续租用者复读
                ArrayPool<byte>.Shared.Return(containerBuffer, clearArray: true);
            }
        }

        /// <summary>
        /// 载荷变换钩子：子类将容器字节视图变换为存储载荷视图（加密处理器在此加密，明文处理器视图别名直通）。
        /// <para>在工作线程调用；实现必须为纯 .NET 逻辑，禁止触达 Unity 主线程 API。
        /// 输入缓冲区可能为池化租赁（有效区间以视图为准，勿按 <see cref="SaveBufferSegment.Buffer"/> 全长读取）；
        /// 输出视图的生命周期到调用方消费完毕为止，实现不得缓存输出缓冲区。</para>
        /// </summary>
        /// <param name="container">容器字节视图。</param>
        /// <param name="payload">成功时的存储载荷视图。</param>
        /// <returns>错误码。</returns>
        protected internal abstract SaveError OnTransformContainer(SaveBufferSegment container, out SaveBufferSegment payload);

        /// <summary>
        /// 载荷还原钩子：子类将存储载荷视图还原为容器字节视图（加密处理器在此解密，明文处理器视图别名直通）。
        /// <para>在工作线程调用；实现必须为纯 .NET 逻辑，禁止触达 Unity 主线程 API。
        /// 输入视图的缓冲区即文件完整字节（有效区间自 <see cref="SaveFileHeader.Size"/> 起），直通别名零拷贝；
        /// 输出视图的生命周期到调用方消费完毕为止，实现不得缓存输出缓冲区。</para>
        /// </summary>
        /// <param name="payload">存储载荷视图。</param>
        /// <param name="container">成功时的容器字节视图。</param>
        /// <returns>错误码。</returns>
        protected internal abstract SaveError OnRestorePayload(SaveBufferSegment payload, out SaveBufferSegment container);

        /// <summary>
        /// 记录读取失败日志（运维可见性：损坏/解密失败等必须留下可追溯痕迹）。
        /// </summary>
        /// <param name="saveFilePath">存档路径。</param>
        /// <param name="error">错误码。</param>
        private static void LogLoadFailure(string saveFilePath, SaveError error)
        {
            LogUtility.Error("[SaveService] Load failed({0}), path: {1}.", error, saveFilePath);
        }

        #endregion
    }
}
