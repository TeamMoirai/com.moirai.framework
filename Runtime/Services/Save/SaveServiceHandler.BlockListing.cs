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
    /// 块删除与枚举：块清理、目录列表查询。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="SaveServiceHandler"/>——公共 API 零变化。</remarks>
    public abstract partial class SaveServiceHandler
    {

        /// <summary>
        /// 从存档文件中删除指定数据块（读-改-写回；删除最后一个块时整档移除），IO 在工作线程执行。
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称；空串表示存档数据根目录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务；失败抛出 <see cref="MoiraiException"/>。</returns>
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
        /// 从存档文件中删除指定数据块（仅限主线程调用，阻塞直至完成）。
        /// </summary>
        /// <remarks>
        /// 删除最后一个块时整档移除。
        /// </remarks>
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
        /// 块删除核心（同步）：读容器 → 移除块 → 块集为空时整档删除，否则原子写回。
        /// </summary>
        /// <remarks>
        /// 工作线程调用，须持串行门；幂等——块或文件不存在时静默返回。
        /// </remarks>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        internal void DeleteBlockCore(SavePaths paths, string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveError readError = ReadContainerOrEmpty(paths, out List<SaveBlockEntry> blocks, out List<SaveBlockError> blockErrors);
            if (readError != SaveError.None)
            {
                throw new MoiraiException(StringUtility.Format("Save block delete failed, path: {0}, error: {1}.", paths.SaveFilePath, readError));
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
                StorageBackend.DeleteFile(paths.SaveFilePath);
                SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Deleted, paths.FileName, paths.FolderName);
            }
            else
            {
                WriteContainerFile(paths, remainingBlocks, cancellationToken);
            }

            SaveService.RaiseBlockDeleted(paths.FileName, paths.FolderName, key, removedEntry.Backend, removedEntry.Bytes.Length);
        }

        /// <summary>
        /// 枚举存档文件内的全部数据块（含保留块；读文件与解析在调用线程执行）。
        /// </summary>
        /// <remarks>
        /// 加密处理器下需解密整档，大存档场景请节流调用频率。 <br />
        /// 坏块同样列入清单（<see cref="SaveBlockInfo.Error"/> 非 <see cref="SaveError.None"/>）； <br />
        /// 框架字段（键/版本/后端/尺寸）仅在 <see cref="SaveBlockInfo.HasMetadata"/> 为 <c>true</c> 时可信。
        /// </remarks>
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
    }
}
