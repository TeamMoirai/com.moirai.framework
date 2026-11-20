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
    /// 组件块管线：组件数据到块级的序列化桥接。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="SaveServiceHandler"/>——公共 API 零变化。</remarks>
    public abstract partial class SaveServiceHandler
    {

        /// <summary>
        /// 将组件捕获条目批量合并写入存档文件（主线程完成捕获，此处仅合并与原子写回），IO 在工作线程执行。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="additions">组件捕获条目（键 = 块键，载荷 = KVT 字节，后端 = <see cref="SaveBackendIds.KEY_VALUE"/>）。</param>
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

                    SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Saved, paths.FileName, paths.FolderName);
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
        /// 读取存档文件内全部块载荷（键 → 字节）。
        /// </summary>
        /// <remarks>
        /// 在调用线程执行，阻塞直至完成；持串行门，与写路径互斥；语义同 <see cref="ReadRawBlocksAsync"/>（含迁移总线前置与坏块失败事件）。
        /// </remarks>
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
                    SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.Migrate, migrationError);
                    return new Dictionary<string, byte[]>();
                }

                if (blockErrors != null)
                {
                    for (int i = 0; i < blockErrors.Count; i++)
                    {
                        SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, blockErrors[i].Key, SaveFailedEvent.ESaveFailureStage.ContainerParse, blockErrors[i].Error);
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
                        SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.Migrate, migrationError);
                        return new Dictionary<string, byte[]>();
                    }

                    if (blockErrors != null)
                    {
                        for (int i = 0; i < blockErrors.Count; i++)
                        {
                            SaveService.RaiseLoadFailed(paths.FileName, paths.FolderName, blockErrors[i].Key, SaveFailedEvent.ESaveFailureStage.ContainerParse, blockErrors[i].Error);
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
                        StorageBackend.DeleteFile(paths.SaveFilePath);
                        SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Deleted, paths.FileName, paths.FolderName);
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

        /// <summary>
        /// 将组件捕获条目与删除集单趟合并写入存档文件（读档 → 自愈迁移 → 移除删除集 → upsert 合并 → 原子写回）。
        /// </summary>
        /// <remarks>
        /// IO 在工作线程执行，单趟读单趟写。 <br />
        /// 删除集双通道：<paramref name="removals"/> 为调用方已确定集合（会话内可判定，无需读档）；<paramref name="removalResolver"/> 在持门读档后解析（孤儿判定依赖档内实时块集， <br />
        /// 不得在门外预计算）。
        /// </remarks>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="additions">待 upsert 的块条目（可为空——纯删除合并）。</param>
        /// <param name="removals">已确定的删除块键集合（<c>null</c> = 无）。</param>
        /// <param name="removalResolver">读档后删除集解析委托（入参为档内健康块键→载荷；<c>null</c> = 无）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>写入完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        internal UniTask MergeRawBlocksAsync(SavePaths paths, List<SaveBlockEntry> additions, List<string> removals, Func<Dictionary<string, byte[]>, List<string>> removalResolver, CancellationToken cancellationToken)
        {
            bool hasAdditions = additions != null && additions.Count > 0;
            if (!hasAdditions && removals == null && removalResolver == null)
            {
                return UniTask.CompletedTask;
            }

            GateScope scope = GateScope.EnterFile(paths);
            return MergeRawBlocksWithGateAsync(paths, additions, removals, removalResolver, scope, cancellationToken);
        }

        /// <summary>
        /// 持串行门执行单趟合并写回（读档 → 迁移 → 双通道删除 → upsert → 写回；整档事件序列与分步管线语义对齐）。
        /// </summary>
        private async UniTask MergeRawBlocksWithGateAsync(SavePaths paths, List<SaveBlockEntry> additions, List<string> removals, Func<Dictionary<string, byte[]>, List<string>> removalResolver, GateScope scope, CancellationToken cancellationToken)
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

                    // 写入自愈：旧版本档先迁移到当前版本再合并
                    MigrateBlocksForWrite(paths, ref existingBlocks, blockErrors, cancellationToken);

                    List<SaveBlockEntry> mergedBlocks = existingBlocks;
                    List<string> resolvedRemovals = removalResolver?.Invoke(ToBlockDictionary(existingBlocks));
                    int removalCount = (removals?.Count ?? 0) + (resolvedRemovals?.Count ?? 0);
                    if (removalCount > 0)
                    {
                        for (int i = 0; removals != null && i < removals.Count; i++)
                        {
                            // 仅真实存在并移除的块触发删除事件（幂等空删不触发）
                            if (SaveBlockComposer.TryFind(mergedBlocks, removals[i], out SaveBlockEntry removedEntry))
                            {
                                mergedBlocks = SaveBlockComposer.Remove(mergedBlocks, removals[i]);
                                SaveService.RaiseBlockDeleted(paths.FileName, paths.FolderName, removedEntry.Key, removedEntry.Backend, removedEntry.Bytes.Length);
                            }
                        }

                        for (int i = 0; resolvedRemovals != null && i < resolvedRemovals.Count; i++)
                        {
                            if (SaveBlockComposer.TryFind(mergedBlocks, resolvedRemovals[i], out SaveBlockEntry removedEntry))
                            {
                                mergedBlocks = SaveBlockComposer.Remove(mergedBlocks, resolvedRemovals[i]);
                                SaveService.RaiseBlockDeleted(paths.FileName, paths.FolderName, removedEntry.Key, removedEntry.Backend, removedEntry.Bytes.Length);
                            }
                        }
                    }

                    if (additions != null && additions.Count > 0)
                    {
                        mergedBlocks = SaveBlockComposer.UpsertAll(mergedBlocks, additions);
                    }

                    if (mergedBlocks.Count == 0)
                    {
                        // 全量移除——整档删除（对齐 DeleteRawBlocks 语义）
                        StorageBackend.DeleteFile(paths.SaveFilePath);
                        SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Deleted, paths.FileName, paths.FolderName);
                        return;
                    }

                    WriteContainerFile(paths, mergedBlocks, cancellationToken);
                    if (additions != null)
                    {
                        for (int i = 0; i < additions.Count; i++)
                        {
                            SaveService.RaiseBlockSaved(paths.FileName, paths.FolderName, additions[i].Key, additions[i].Backend, additions[i].Bytes.Length);
                        }
                    }

                    SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Saved, paths.FileName, paths.FolderName);
                }, configureAwait: false, cancellationToken: cancellationToken);
            }
            finally
            {
                scope.Leave();
            }
        }

        /// <summary>
        /// 档内健康块转键→载荷字典（删除集解析委托的入参形态）。
        /// </summary>
        private static Dictionary<string, byte[]> ToBlockDictionary(List<SaveBlockEntry> blocks)
        {
            var result = new Dictionary<string, byte[]>(blocks.Count);
            for (int i = 0; i < blocks.Count; i++)
            {
                result[blocks[i].Key] = blocks[i].Bytes;
            }

            return result;
        }

        /// <summary>
        /// 查询存档文件的最后写入时间（轻量元数据查询——会话级增量守卫依赖）。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="writeTimeUtc">成功时的最后写入时间（UTC）。</param>
        /// <returns>文件存在返回 <c>true</c>。</returns>
        internal bool TryGetSaveWriteTimeUtc(SavePaths paths, out DateTime writeTimeUtc)
        {
            return StorageBackend.TryGetWriteTimeUtc(paths.SaveFilePath, out writeTimeUtc);
        }
    }
}
