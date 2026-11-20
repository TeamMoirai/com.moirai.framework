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
    /// 存档查询与备份：文件枚举、存在性检查与备份/恢复。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="SaveServiceHandler"/>——公共 API 零变化。</remarks>
    public abstract partial class SaveServiceHandler
    {
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
            SaveStorageBackend storage = StorageBackend;
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
                    StorageBackend.CreateBackup(paths.SaveFilePath);
                }
                catch (GameException)
                {
                    SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.StorageWrite, SaveError.IoFailed);
                    throw;
                }
            }
            finally
            {
                scope.Leave();
            }

            SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.BackupCreated, paths.FileName, paths.FolderName);
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
                    StorageBackend.RestoreBackup(paths.SaveFilePath);
                }
                catch (GameException)
                {
                    SaveService.RaiseSaveFailed(paths.FileName, paths.FolderName, null, SaveFailedEvent.ESaveFailureStage.StorageWrite, SaveError.IoFailed);
                    throw;
                }
            }
            finally
            {
                scope.Leave();
            }

            // 备份内容可能处于旧数据版本——失效会话迁移缓存，下次读取重新探测
            SaveMigrationManager.InvalidateSession(paths.SaveFilePath);
            SaveService.RaiseSlotChanged(SaveSlotChangedEvent.ESaveSlotChangeKind.BackupRestored, paths.FileName, paths.FolderName);
        }


        /// <summary>
        /// 是否存在存档文件。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public bool FileExists(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            SavePaths paths = ResolveSavePaths(fileName, folderName);
            return StorageBackend.Exists(paths.SaveFilePath);
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
            return StorageBackend.EnumerateFiles(directoryPath, extension);
        }
    }
}
