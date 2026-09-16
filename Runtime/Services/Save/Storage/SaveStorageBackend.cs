using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档存储后端抽象基类（框架插拔件惯例：<see cref="SaveServiceSettings"/> 以 [SerializeReference] + ProviderDropdown 持有实例）。
    /// <para>实现 <see cref="ISaveStorage"/>：同步原语为抽象契约（后端必须实现）；异步包装默认线程池卸载同步原语，
    /// 真异步后端（如云存储）覆盖异步方法并以 <see cref="SaveStorageCapabilities.SupportsTrueAsyncIO"/> 声明——
    /// 不支持同步 IO 的后端可在同步方法抛 <see cref="NotSupportedException"/>（同步裸名 API 随之不可用，调用方按能力降级）。</para>
    /// <para>实现必须为纯 .NET 逻辑（可在任意线程调用），禁止触达 Unity 主线程 API。</para>
    /// </summary>
    [Serializable]
    public abstract class SaveStorageBackend : ISaveStorage
    {
        /// <summary>
        /// 后端能力自描述（原子改名/真异步/尺寸上限/易失性）。
        /// </summary>
        public abstract SaveStorageCapabilities Capabilities { get; }

        #region 同步原语 [SYNC PRIMITIVES]

        /// <summary>
        /// 是否存在目标文件。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public abstract bool Exists(string filePath);

        /// <summary>
        /// 是否存在目标目录（与 <see cref="Exists"/> 对称——目录级删除的存在性判定依赖）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public abstract bool DirectoryExists(string directoryPath);

        /// <summary>
        /// 查询目标文件的最后写入时间（轻量元数据查询——会话级增量守卫依赖）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="writeTimeUtc">成功时的最后写入时间（UTC）。</param>
        /// <returns>文件存在返回 <c>true</c>；缺档/查询失败返回 <c>false</c>。</returns>
        public abstract bool TryGetWriteTimeUtc(string filePath, out DateTime writeTimeUtc);

        /// <summary>
        /// 读取文件全部字节（错误分型返回；IO 失败由实现记录详细日志）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="bytes">成功时的文件字节。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>、<see cref="SaveError.FileNotFound"/> 或 <see cref="SaveError.IoFailed"/>。</returns>
        public abstract SaveError TryReadAllBytes(string filePath, out byte[] bytes);

        /// <summary>
        /// 原子写入：完整文件字节经临时文件 + 落盘刷新 + 原子替换提交（失败抛 <see cref="GameException"/>，不留半文件窗口）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径（目录由实现确保存在）。</param>
        /// <param name="bytes">完整文件字节（含文件头）。</param>
        /// <param name="cancellationToken">取消令牌（替换前检查，取消时清理临时文件）。</param>
        public abstract void WriteAtomic(string filePath, byte[] bytes, CancellationToken cancellationToken);

        /// <summary>
        /// 原子写入（两段式）：头部与载荷不经拼接拷贝直接分段落盘（语义与 <see cref="WriteAtomic(string, byte[], CancellationToken)"/> 一致）。
        /// <para>默认实现拼接后走整段写入（保持正确性）；具备流式写能力的后端应覆写为真分段落盘以消灭拼接分配。</para>
        /// </summary>
        /// <param name="filePath">目标文件完整路径（目录由实现确保存在）。</param>
        /// <param name="head">文件头部字节（先写入）。</param>
        /// <param name="payload">载荷字节（头部之后写入）。</param>
        /// <param name="cancellationToken">取消令牌（替换前检查，取消时清理临时文件）。</param>
        public virtual void WriteAtomic(string filePath, ReadOnlySpan<byte> head, ReadOnlySpan<byte> payload, CancellationToken cancellationToken)
        {
            byte[] combined = new byte[head.Length + payload.Length];
            head.CopyTo(combined);
            payload.CopyTo(combined.AsSpan(head.Length));
            WriteAtomic(filePath, combined, cancellationToken);
        }

        /// <summary>
        /// 删除文件（幂等：不存在视为成功；带退避重试应对云同步/杀毒短时锁）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        public abstract void DeleteFile(string filePath);

        /// <summary>
        /// 递归删除目录树（幂等：不存在视为成功；只读文件先清除只读属性；带退避重试）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        public abstract void DeleteDirectory(string directoryPath);

        /// <summary>
        /// 枚举目录内指定扩展名的文件（按最后写入时间倒序；目录不存在返回空数组）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <param name="extension">扩展名（含点，精确匹配）。</param>
        /// <returns>文件元信息数组。</returns>
        public abstract SaveFileInfo[] EnumerateFiles(string directoryPath, string extension);

        /// <summary>
        /// 创建单档备份（覆盖旧备份；源不存在抛 <see cref="GameException"/>）。
        /// </summary>
        /// <param name="filePath">源文件完整路径。</param>
        public abstract void CreateBackup(string filePath);

        /// <summary>
        /// 从单档备份恢复（备份经临时文件原子替换回源路径；备份不存在抛 <see cref="GameException"/>）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        public abstract void RestoreBackup(string filePath);

        /// <summary>
        /// 清扫上次写入中断残留的孤儿临时文件（尽力而为，失败仅告警；无临时文件语义的后端空实现）。
        /// </summary>
        /// <param name="rootDirectory">存档数据根目录。</param>
        public virtual void CleanupOrphanTempFiles(string rootDirectory)
        {
        }

        #endregion

        #region 异步包装 [ASYNC WRAPPERS]

        /// <summary>
        /// 异步是否存在目标文件（默认线程池卸载同步原语；真异步后端覆盖）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public virtual UniTask<bool> ExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return UniTask.RunOnThreadPool(() => Exists(filePath), configureAwait: false, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 异步读取文件全部字节（默认线程池卸载同步原语；缺档/失败返回 <c>null</c>，失败已由实现记录日志）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件字节；缺档或失败为 <c>null</c>。</returns>
        public virtual UniTask<byte[]> ReadAllBytesAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                SaveError error = TryReadAllBytes(filePath, out byte[] bytes);
                return error == SaveError.None ? bytes : null;
            }, configureAwait: false, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 异步原子写入（默认线程池卸载同步原语；语义同 <see cref="WriteAtomic"/>）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="bytes">完整文件字节。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>写入完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        public virtual UniTask WriteAtomicAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
        {
            return UniTask.RunOnThreadPool(() => WriteAtomic(filePath, bytes, cancellationToken), configureAwait: false, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 异步删除文件（默认线程池卸载同步原语；幂等）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public virtual UniTask DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return UniTask.RunOnThreadPool(() => DeleteFile(filePath), configureAwait: false, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 异步递归删除目录树（默认线程池卸载同步原语；幂等）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public virtual UniTask DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            return UniTask.RunOnThreadPool(() => DeleteDirectory(directoryPath), configureAwait: false, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 异步枚举目录内指定扩展名的文件（默认线程池卸载同步原语；按最后写入时间倒序）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <param name="extension">扩展名（含点，精确匹配）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件元信息数组。</returns>
        public virtual UniTask<SaveFileInfo[]> EnumerateFilesAsync(string directoryPath, string extension, CancellationToken cancellationToken = default)
        {
            return UniTask.RunOnThreadPool(() => EnumerateFiles(directoryPath, extension), configureAwait: false, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 异步创建单档备份（默认线程池卸载同步原语）。
        /// </summary>
        /// <param name="filePath">源文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>备份完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        public virtual UniTask CreateBackupAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return UniTask.RunOnThreadPool(() => CreateBackup(filePath), configureAwait: false, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 异步从单档备份恢复（默认线程池卸载同步原语）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>恢复完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        public virtual UniTask RestoreBackupAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return UniTask.RunOnThreadPool(() => RestoreBackup(filePath), configureAwait: false, cancellationToken: cancellationToken);
        }

        #endregion
    }
}
