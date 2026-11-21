using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务契约（策略模式契约接口）：存档后端的唯一消费契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 默认实现为 <see cref="PlainSaveHandler"/>（明文 JSON），另有 <see cref="AESEncryptedSaveHandler"/>（AES 加密）内建后端， <br />
    /// 可在 <see cref="SaveServiceSettings"/> 中替换。 <br />
    /// 本契约与引擎基座 <see cref="SaveServiceHandler"/> 分层：基座承载多块容器/序列化/加密/迁移/云同步等 2600+ 行共享实现（豁免契约化）， <br />
    /// 后端经派生基座落地；本接口只收编门面公共 API（消费面），供服务互调与单测注入。 <br />
    /// internal 协作面（存储后端选择/原始块读写/截图写入等）不进公共契约，框架内部按引擎基座引用访问。 <br />
    /// 实现约束：实现类须继承 <see cref="SaveServiceHandler"/>（引擎基座 + <see cref="Moirai.Atropos.FrameworkHandler"/> 生命周期）。 <br />
    /// 语义文档与 <see cref="SaveService"/> 门面对应 API 同源。
    /// </remarks>
    public interface ISaveService
    {
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public void CreateBackup(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public void DeleteAllSaveFiles();
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask DeleteAllSaveFilesAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public void DeleteBlock(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask DeleteBlockAsync(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public void DeleteSave(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask DeleteSaveAsync(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public void DeleteSaveFolder(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask DeleteSaveFolderAsync(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public string DetermineSavePath(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public bool FileExists(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public SaveBlockInfo[] GetBlockInfos(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public SaveFileInfo[] GetSaveFiles(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask<SaveFileInfo[]> GetSaveFilesAsync(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public SaveError MigrateSave(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask<SaveError> MigrateSaveAsync(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public void RestoreBackup(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public bool TryDeleteAllSaveFiles();
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask<bool> TryDeleteAllSaveFilesAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public bool TryDeleteSave(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask<bool> TryDeleteSaveAsync(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public bool TryDeleteSaveFolder(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask<bool> TryDeleteSaveFolderAsync(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask SaveBlockAsync<T>(T data, string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, ushort backend = SaveBackendIds.JSON, int dataVersion = 1, CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask<T> LoadBlockAsync<T>(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public UniTask<SaveResult<T>> TryLoadBlockAsync<T>(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public T LoadBlock<T>(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// 见 <see cref="SaveService"/> 门面对应 API 的语义文档。
        /// </summary>
        public SaveResult<T> TryLoadBlock<T>(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME);
    }
}
