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
    /// 版本迁移与截图随档：存档跨版本升级、截图文件管理。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="SaveServiceHandler"/>——公共 API 零变化。</remarks>
    public abstract partial class SaveServiceHandler
    {

        /// <summary>
        /// 显式迁移指定存档到当前数据版本（仅限主线程调用，阻塞直至完成）。
        /// </summary>
        /// <remarks>
        /// 迁移成功后强制回写（不受 <see cref="MigrationWriteBack"/> 约束）；版本相等或迁移总线未激活为无操作。 <br />
        /// 版本低于当前且迁移链缺失/失败返回 <see cref="SaveError.MigrationFailed"/>，高于当前返回 <see cref="SaveError.UnsupportedVersion"/>。
        /// </remarks>
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
        /// 显式迁移指定存档到当前数据版本。
        /// </summary>
        /// <remarks>
        /// IO 在工作线程执行；语义同 <see cref="MigrateSave"/>（迁移成功强制回写）。
        /// </remarks>
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
        /// 读路径迁移前置（加载管线统一入口）：按需执行迁移链并按 <see cref="MigrationWriteBack"/> 惰性回写。
        /// </summary>
        /// <remarks>
        /// 回写失败不阻断本次加载（内存数据已迁移；失败经日志与 <see cref="SaveFailedEvent"/> 事件观测，下一会话重试）。
        /// </remarks>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">健康数据块列表（迁移后替换）。</param>
        /// <param name="blockErrors">坏块清单（元数据块损坏时迁移保守失败）。</param>
        /// <param name="forceWriteBack">显式迁移调用的强制回写（绕过 <see cref="MigrationWriteBack"/> 设置）。</param>
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
            if (!forceWriteBack && !m_MigrationWriteBack)
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
                SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.StorageWrite, SaveError.IoFailed);
            }

            return SaveError.None;
        }

        /// <summary>
        /// 写路径迁移前置（写入自愈）：旧版本档先迁移到当前版本再合并写回，失败 fail-fast 上抛 <see cref="GameException"/>。
        /// </summary>
        /// <remarks>
        /// 迁移结果由调用方随后的 <see cref="WriteContainerFile"/> 一并落盘（不单独回写）。
        /// </remarks>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="blocks">健康数据块列表（迁移后替换）。</param>
        /// <param name="blockErrors">坏块清单。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private void MigrateBlocksForWrite(SavePaths paths, ref List<SaveBlockEntry> blocks, List<SaveBlockError> blockErrors, CancellationToken cancellationToken)
        {
            SaveError error = MigrateBlocksIfNeeded(paths, ref blocks, blockErrors, forceWriteBack: false, cancellationToken);
            if (error != SaveError.None)
            {
                SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.Migrate, error);
                throw new GameException(StringUtility.Format("Save migration failed on write path, path: {0}, error: {1}.", paths.SaveFilePath, error));
            }
        }

        /// <summary>
        /// 版本化激活时将当前数据版本盖章进元数据块（保留游戏层元数据字段，仅推进 <see cref="SaveMetadata.SaveVersion"/>）。
        /// </summary>
        /// <remarks>
        /// 元数据块 JSON 损坏时用全新元数据替换并记告警；未激活时零开销直通。
        /// </remarks>
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
                    metadata = SaveSerializerRegistry.GetRequired(SaveBackendIds.JSON).Deserialize<SaveMetadata>(metaEntry.Bytes);
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
            byte[] metaBytes = SaveSerializerRegistry.GetRequired(SaveBackendIds.JSON).Serialize(metadata);
            return SaveBlockComposer.Upsert(blocks, new SaveBlockEntry(META_BLOCK_KEY, 1, SaveBackendIds.JSON, metaBytes));
        }


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
            StorageBackend.WriteAtomic(ResolveScreenshotPath(paths), pngBytes, CancellationToken.None);
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
            return StorageBackend.WriteAtomicAsync(ResolveScreenshotPath(paths), pngBytes, cancellationToken);
        }

        /// <summary>
        /// 删除截图 sidecar（幂等——不存在视为成功；存档删除时级联调用，防止同名新档复活陈旧缩略图）。
        /// </summary>
        /// <param name="paths">存档路径集合。</param>
        internal void DeleteScreenshot(SavePaths paths)
        {
            StorageBackend.DeleteFile(ResolveScreenshotPath(paths));
        }
    }
}
