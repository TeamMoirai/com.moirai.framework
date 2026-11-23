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
    /// 契约分层三层口径（能力协商架构）： <br />
    /// ① 本接口 = 消费契约——门面公共 API 的抽象面，供服务互调与单测注入；常量出自 <see cref="SaveServiceDefaults"/>，零基座引用。 <br />
    /// ② <see cref="ISaveServiceBackend"/> = internal 能力契约——门面内部能力面（原始块读写/存储后端/写时查询/截图写入）， <br />
    ///    门面经 <c>is</c> 能力协商取用，不经家族基座强转。 <br />
    /// ③ <see cref="SaveServiceHandler"/> = 家族基座——今日唯一内建实现路径（容器/序列化/加密/迁移/云同步等共享实现）； <br />
    ///    实现本契约的后端可派生基座（自动获得能力契约），或经框架友元程序集（IVT 放行面）直接实现 <see cref="ISaveServiceBackend"/> 接入。 <br />
    /// 语义文档逐方法自足（含同步 API 主线程限定），不再回指门面。
    /// </remarks>
    public interface ISaveService
    {
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行完整管线，阻塞直至完成）；同步备份适用于退出前落盘等低频场景。
        /// </summary>
        public void CreateBackup(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行，阻塞直至完成）；删除整档目录，低频运维操作。
        /// </summary>
        public void DeleteAllSaveFiles();
        /// <summary>
        /// <summary>
        /// IO 与删除在工作线程执行；取消令牌协作式生效。
        /// </summary>
        public UniTask DeleteAllSaveFilesAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行，阻塞直至完成）；删除最后一块时整档移除。
        /// </summary>
        public void DeleteBlock(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// IO 在工作线程执行；取消令牌协作式生效。
        /// </summary>
        public UniTask DeleteBlockAsync(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行，阻塞直至完成；含删除退避重试）。
        /// </summary>
        public void DeleteSave(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// IO 与删除退避重试在工作线程执行；取消令牌协作式生效。
        /// </summary>
        public UniTask DeleteSaveAsync(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行，阻塞直至完成）。
        /// </summary>
        public void DeleteSaveFolder(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// IO 在工作线程执行；取消令牌协作式生效。
        /// </summary>
        public UniTask DeleteSaveFolderAsync(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（触达 persistentDataPath 与设置实例）。
        /// </summary>
        public string DetermineSavePath(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（本地镜像探测，零 IO 阻塞；云远端存在性须经异步 API 族裁决）。
        /// </summary>
        public bool FileExists(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程解析容器，阻塞直至完成）。
        /// </summary>
        public SaveBlockInfo[] GetBlockInfos(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程枚举目录并排序，阻塞直至完成）；大数据量槽位枚举用 <see cref="GetSaveFilesAsync"/>。
        /// </summary>
        public SaveFileInfo[] GetSaveFiles(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// 枚举与排序在工作线程执行；缺目录/处理器未就绪返回空数组。
        /// </summary>
        public UniTask<SaveFileInfo[]> GetSaveFilesAsync(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行迁移管线，阻塞直至完成）；迁移链长或档大时用 <see cref="MigrateSaveAsync"/>。
        /// </summary>
        public SaveError MigrateSave(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// 迁移管线在工作线程执行；取消令牌协作式生效。
        /// </summary>
        public UniTask<SaveError> MigrateSaveAsync(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行，阻塞直至完成）。
        /// </summary>
        public void RestoreBackup(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行，阻塞直至完成）。
        /// </summary>
        public bool TryDeleteAllSaveFiles();
        /// <summary>
        /// <summary>
        /// IO 在工作线程执行；取消令牌协作式生效。
        /// </summary>
        public UniTask<bool> TryDeleteAllSaveFilesAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行，阻塞直至完成；含删除退避重试）。
        /// </summary>
        public bool TryDeleteSave(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// IO 与删除退避重试在工作线程执行；取消令牌协作式生效。
        /// </summary>
        public UniTask<bool> TryDeleteSaveAsync(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行，阻塞直至完成）。
        /// </summary>
        public bool TryDeleteSaveFolder(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// IO 在工作线程执行；取消令牌协作式生效。
        /// </summary>
        public UniTask<bool> TryDeleteSaveFolderAsync(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// 序列化与 IO 在工作线程执行；同文件写路径串行排队；取消令牌协作式生效。
        /// </summary>
        public UniTask SaveBlockAsync<T>(T data, string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, ushort backend = SaveBackendIds.JSON, int dataVersion = 1, CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// IO 与反序列化在工作线程执行；取消令牌协作式生效。
        /// </summary>
        public UniTask<T> LoadBlockAsync<T>(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// IO 与反序列化在工作线程执行；取消令牌协作式生效。
        /// </summary>
        public UniTask<SaveResult<T>> TryLoadBlockAsync<T>(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行完整管线，阻塞直至完成）；块大或热路径用 <see cref="LoadBlockAsync"/>。
        /// </summary>
        public T LoadBlock<T>(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
        /// <summary>
        /// <summary>
        /// 仅限主线程调用（在调用线程执行完整管线，阻塞直至完成）；块大或热路径用 <see cref="TryLoadBlockAsync"/>。
        /// </summary>
        public SaveResult<T> TryLoadBlock<T>(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME);
    }
}
