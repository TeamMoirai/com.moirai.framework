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
    /// 存档级操作：存档删除与查询、备份恢复。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="SaveServiceHandler"/>——公共 API 零变化。</remarks>
    public abstract partial class SaveServiceHandler
    {

        /// <summary>
        /// 从磁盘中删除单个存档（含全部数据块与截图 sidecar；幂等，目标不存在视为删除成功且不触发事件）。
        /// </summary>
        /// <remarks>
        /// 持分层门执行（与块级读写互斥）：存在性判定与删除在同一临界区内完成，杜绝并发写入在删除后复活文件。
        /// </remarks>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        public void DeleteSave(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            _ = TryDeleteSave(fileName, folderName);
        }

        /// <summary>
        /// 从磁盘中删除单个存档并返回目标先前的存在性（含全部数据块与截图 sidecar）。
        /// </summary>
        /// <remarks>
        /// 幂等语义同 <see cref="DeleteSave"/>（目标不存在不触发事件）；<c>true</c> = 存在并已删除，<c>false</c> = 本不存在。 <br />
        /// 持分层门执行（与块级读写互斥）：存在性判定与删除在同一临界区内完成，杜绝并发写入在删除后复活文件。
        /// </remarks>
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
                SaveStorageBackend storage = StorageBackend;
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
                SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Deleted, paths.FileName, paths.FolderName);
            }

            return existed;
        }

        /// <summary>
        /// 删除整个存档文件夹（含其中全部文件与子目录）。
        /// </summary>
        /// <remarks>
        /// 目录级批量删除恒触发一次 <see cref="SaveSlotChangedEvent"/>（<see cref="SaveSlotChangedEvent.FileName"/> 为 <c>null</c>，不保证目录先前存在）。 <br />
        /// 持分层门执行（根 + 文件夹两级），与该文件夹内全部槽位的块级读写互斥。
        /// </remarks>
        /// <param name="folderName">文件夹名称；不允许为空（清空全部请用 <see cref="DeleteAllSaveFiles"/>）。</param>
        public void DeleteSaveFolder(string folderName = DEFAULT_FOLDER_NAME)
        {
            _ = TryDeleteSaveFolder(folderName);
        }

        /// <summary>
        /// 删除整个存档文件夹并返回目录先前的存在性（含其中全部文件与子目录）。
        /// </summary>
        /// <remarks>
        /// 事件行为同 <see cref="DeleteSaveFolder"/>（恒触发一次 <see cref="SaveSlotChangedEvent"/>）；<c>true</c> = 存在并已删除， <br />
        /// <c>false</c> = 本不存在。
        /// 持分层门执行（根 + 文件夹两级），与该文件夹内全部槽位的块级读写互斥。
        /// </remarks>
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
                SaveStorageBackend storage = StorageBackend;
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
            SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Deleted, null, folderName);
            return existed;
        }

        /// <summary>
        /// 删除存档数据根目录（<c>persistentDataPath/Data/</c>）及其下所有存档。
        /// </summary>
        /// <remarks>
        /// 恒触发一次 <see cref="SaveSlotChangedEvent"/>（<see cref="SaveSlotChangedEvent.FileName"/> 为 <c>null</c>，文件夹为空串）。 <br />
        /// 持分层门执行（根级），与所有存档的块级读写互斥。
        /// </remarks>
        public void DeleteAllSaveFiles()
        {
            _ = TryDeleteAllSaveFiles();
        }

        /// <summary>
        /// 删除存档数据根目录并返回目录先前的存在性（<c>persistentDataPath/Data/</c> 及其下所有存档）。
        /// </summary>
        /// <remarks>
        /// 事件行为同 <see cref="DeleteAllSaveFiles"/>（恒触发一次 <see cref="SaveSlotChangedEvent"/>）；<c>true</c> = 存在并已删除， <br />
        /// <c>false</c> = 本不存在。
        /// 持分层门执行（根级），与所有存档的块级读写互斥。
        /// </remarks>
        /// <returns>目录先前存在并已删除返回 <c>true</c>；本不存在返回 <c>false</c>。</returns>
        public bool TryDeleteAllSaveFiles()
        {
            string rootDirectory = BuildDataRootDirectory();
            GateScope scope = GateScope.EnterRoot(rootDirectory);
            scope.Wait();
            bool existed;
            try
            {
                SaveStorageBackend storage = StorageBackend;
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
            SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Deleted, null, string.Empty);
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
        /// 异步删除单个存档并返回目标先前的存在性（含截图 sidecar）。
        /// </summary>
        /// <remarks>
        /// 删除退避重试在工作线程执行；幂等与事件语义同 <see cref="TryDeleteSave"/>；持分层门执行，存在性判定与删除在同一临界区内完成。
        /// </remarks>
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
                SaveStorageBackend storage = StorageBackend;
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
                SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Deleted, paths.FileName, paths.FolderName);
            }

            return existed;
        }

        /// <summary>
        /// 异步删除整个存档文件夹（含其中全部文件与子目录）。
        /// </summary>
        /// <remarks>
        /// 目录级批量删除恒触发一次 <see cref="SaveSlotChangedEvent"/>（不保证目录先前存在）。 <br />
        /// 持分层门执行（根 + 文件夹两级），与该文件夹内全部槽位的块级读写互斥。
        /// </remarks>
        /// <param name="folderName">文件夹名称；不允许为空（清空全部请用 <see cref="DeleteAllSaveFilesAsync"/>）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        public UniTask DeleteSaveFolderAsync(string folderName = DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            return TryDeleteSaveFolderAsync(folderName, cancellationToken);
        }

        /// <summary>
        /// 异步删除整个存档文件夹并返回目录先前的存在性（含其中全部文件与子目录）。
        /// </summary>
        /// <remarks>
        /// 事件行为同 <see cref="DeleteSaveFolderAsync"/>（恒触发一次 <see cref="SaveSlotChangedEvent"/>）；持分层门执行（根 + 文件夹两级）， <br />
        /// 存在性判定与删除在同一临界区内完成。
        /// </remarks>
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
                SaveStorageBackend storage = StorageBackend;
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
            SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Deleted, null, folderName);
            return existed;
        }

        /// <summary>
        /// 异步删除存档数据根目录（<c>persistentDataPath/Data/</c>）及其下所有存档。
        /// </summary>
        /// <remarks>
        /// 持分层门执行（根级），与所有存档的块级读写互斥。
        /// </remarks>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public UniTask DeleteAllSaveFilesAsync(CancellationToken cancellationToken = default)
        {
            return TryDeleteAllSaveFilesAsync(cancellationToken);
        }

        /// <summary>
        /// 异步删除存档数据根目录并返回目录先前的存在性（<c>persistentDataPath/Data/</c> 及其下所有存档）。
        /// </summary>
        /// <remarks>
        /// 事件行为同 <see cref="DeleteAllSaveFilesAsync"/>（恒触发一次 <see cref="SaveSlotChangedEvent"/>）；持分层门执行（根级），存在性判定与删除在同一临界区内完成。
        /// </remarks>
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
                SaveStorageBackend storage = StorageBackend;
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
            SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.Deleted, null, string.Empty);
            return existed;
        }

    }
}