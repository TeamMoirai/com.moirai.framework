using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 块级存档：块读写（Save/Load Block，含主线程/异步双路）。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="SaveServiceHandler"/>——公共 API 零变化。</remarks>
    public abstract partial class SaveServiceHandler
    {

        /// <summary>
        /// 将数据块异步写入存档文件（读-改-写合并进既有块集合；临时文件 + 落盘刷新 + 原子替换）。
        /// </summary>
        /// <remarks>
        /// IO 在工作线程执行；同文件的写路径经串行门排队，杜绝并发读-改-写丢块。
        /// </remarks>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="data">存档数据对象。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键（禁止保留前缀 <see cref="RESERVED_BLOCK_KEY_PREFIX"/>）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="dataVersion">数据块模式版本。</param>
        /// <param name="cancellationToken">取消令牌（协作式：在序列化前后与替换前检查）。</param>
        /// <returns>写入完成的异步任务；失败抛出 <see cref="MoiraiException"/>（含路径上下文）。</returns>
        public UniTask SaveBlockAsync<T>(T data, string fileName, string key, string folderName = DEFAULT_FOLDER_NAME, ushort backend = SaveBackendIds.JSON, int dataVersion = 1, CancellationToken cancellationToken = default)
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
        private async UniTask SaveBlockWithGateAsync<T>(SavePaths paths, string key, T data, ushort backend, int dataVersion, GateScope scope, CancellationToken cancellationToken)
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
        /// 从存档文件异步加载指定数据块。
        /// </summary>
        /// <remarks>
        /// IO 在工作线程执行；文件或块不存在、加载失败（损坏/解密失败/反序列化失败，均已记录错误日志）返回 <c>default</c>，需错误判别用 <see cref="TryLoadBlockAsync{T}"/>。
        /// </remarks>
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
        /// </summary>
        /// <remarks>
        /// 仅限主线程调用（触达 <c>persistentDataPath</c> 等 Unity API）；适用于须同步完成的场景，大数据量请用 <see cref="SaveBlockAsync{T}"/>。
        /// </remarks>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="data">存档数据对象。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键（禁止保留前缀）。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="backend">序列化后端标识。</param>
        /// <param name="dataVersion">数据块模式版本。</param>
        public void SaveBlock<T>(T data, string fileName, string key, string folderName = DEFAULT_FOLDER_NAME, ushort backend = SaveBackendIds.JSON, int dataVersion = 1)
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
        /// </summary>
        /// <remarks>
        /// 仅限主线程调用；兜底语义同 <see cref="LoadBlockAsync{T}"/>（缺档/失败记日志后返回 <c>default</c>，错误判别用 <see cref="TryLoadBlock{T}"/>）。
        /// </remarks>
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
        /// 从存档文件加载指定数据块并返回完整错误判别结果（仅限主线程调用，阻塞直至完成）。
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
        internal void SaveBlockCore<T>(SavePaths paths, string key, T data, ushort backend, int dataVersion, CancellationToken cancellationToken)
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
                SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, key, SaveFailedEvent.ESaveFailureStage.Serialize, SaveError.SerializationFailed);
                throw new MoiraiException(StringUtility.Format("Save block serialization failed, path: {0}, exception: {1}.", paths.SaveFilePath, exception.GetType().Name), exception);
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
            SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Saved, paths.FileName, paths.FolderName);
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
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.Migrate, busError);
                return busError;
            }

            if (!SaveBlockComposer.TryFind(blocks, key, out SaveBlockEntry entry))
            {
                SaveError missError = ClassifyMissingKey(blockErrors, key);
                if (missError != SaveError.FileNotFound)
                {
                    // 坏块键命中（或结构性坏块致状态不可知）——逐块损坏经失败事件观测（确无块的 FileNotFound 属正常业务流）
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, key, SaveFailedEvent.ESaveFailureStage.ContainerParse, missError);
                }

                return missError;
            }

            if (!SaveSerializerRegistry.TryGet(entry.Backend, out ISaveSerializer serializer))
            {
                // 块记录的后端未注册（依赖未接入或文件被改写）——明确拒绝而非静默解析
                LogLoadFailure(paths.SaveFilePath, SaveError.InvalidFormat);
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, key, SaveFailedEvent.ESaveFailureStage.ContainerParse, SaveError.InvalidFormat);
                return SaveError.InvalidFormat;
            }

            try
            {
                data = serializer.Deserialize<T>(entry.Bytes);
            }
            catch (Exception exception)
            {
                LogUtility.Error("[SaveService] Deserialize save block failed, path: {0}, key: {1}, exception: {2}.", paths.SaveFilePath, key, exception.GetType().Name);
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, key, SaveFailedEvent.ESaveFailureStage.Deserialize, SaveError.SerializationFailed);
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
        /// 版本迁移与保护（<see cref="SaveDataBlock"/> 子类生效）：存档版本高于声明值拒绝加载，低于声明值经 <see cref="SaveDataBlock.OnMigrate"/> 级联升级。
        /// </summary>
        /// <remarks>
        /// 迁移为内存就地修正，下次写入自然持久化。
        /// </remarks>
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
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, entry.Key, SaveFailedEvent.ESaveFailureStage.Migrate, SaveError.SerializationFailed);
                return SaveError.SerializationFailed;
            }

            int declaredVersion = SaveBlockDescriptor<T>.Version;
            if (entry.DataVersion > declaredVersion)
            {
                LogLoadFailure(paths.SaveFilePath, SaveError.UnsupportedVersion);
                SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, entry.Key, SaveFailedEvent.ESaveFailureStage.Migrate, SaveError.UnsupportedVersion);
                return SaveError.UnsupportedVersion;
            }

            if (entry.DataVersion < declaredVersion)
            {
                block.OnMigrate(entry.DataVersion);
            }

            return SaveError.None;
        }
    }
}
