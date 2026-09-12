using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档存储层契约（字节搬运抽象）：存档编排层（<see cref="SaveServiceHandler"/>）的所有持久化 IO 均经此接口下沉，
    /// 存储后端（本地文件 / 云 KV / 平台存储）与格式管线两轴正交可插拔。
    /// <para>同步原语为契约核心——<see cref="SaveService"/> 同步裸名 API 在调用线程直接消费；
    /// 异步包装默认由 <see cref="SaveStorageBackend"/> 以线程池卸载实现，真异步后端（如云）覆盖并以
    /// <see cref="SaveStorageCapabilities.SupportsTrueAsyncIO"/> 声明。</para>
    /// <para>错误语义契约（与全模块统一）：读取经 <see cref="TryReadAllBytes"/> 分型返回（缺档
    /// <see cref="SaveError.FileNotFound"/> 不记日志，IO 失败 <see cref="SaveError.IoFailed"/> 由实现记录详细日志）；
    /// 写入失败 fail-fast 抛 <see cref="GameException"/>（含路径上下文，实现负责清理临时残留）；
    /// 删除幂等——目标不存在视为删除成功。</para>
    /// <para>实现必须为纯 .NET 逻辑（可在任意线程调用），禁止触达 Unity 主线程 API。</para>
    /// </summary>
    public interface ISaveStorage
    {
        /// <summary>
        /// 后端能力自描述（原子改名/真异步/尺寸上限/易失性）。
        /// </summary>
        SaveStorageCapabilities Capabilities { get; }

        #region 同步原语 [SYNC PRIMITIVES]

        /// <summary>
        /// 是否存在目标文件。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        bool Exists(string filePath);

        /// <summary>
        /// 读取文件全部字节（错误分型返回；IO 失败由实现记录详细日志）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="bytes">成功时的文件字节。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>、<see cref="SaveError.FileNotFound"/> 或 <see cref="SaveError.IoFailed"/>。</returns>
        SaveError TryReadAllBytes(string filePath, out byte[] bytes);

        /// <summary>
        /// 原子写入：完整文件字节经临时文件 + 落盘刷新 + 原子替换提交（失败抛 <see cref="GameException"/>，不留半文件窗口）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径（目录由实现确保存在）。</param>
        /// <param name="bytes">完整文件字节（含文件头）。</param>
        /// <param name="cancellationToken">取消令牌（替换前检查，取消时清理临时文件）。</param>
        void WriteAtomic(string filePath, byte[] bytes, CancellationToken cancellationToken);

        /// <summary>
        /// 删除文件（幂等：不存在视为成功；带退避重试应对云同步/杀毒短时锁）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        void DeleteFile(string filePath);

        /// <summary>
        /// 递归删除目录树（幂等：不存在视为成功；只读文件先清除只读属性；带退避重试）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        void DeleteDirectory(string directoryPath);

        /// <summary>
        /// 枚举目录内指定扩展名的文件（按最后写入时间倒序；目录不存在返回空数组）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <param name="extension">扩展名（含点，精确匹配）。</param>
        /// <returns>文件元信息数组。</returns>
        SaveFileInfo[] EnumerateFiles(string directoryPath, string extension);

        /// <summary>
        /// 创建单档备份（覆盖旧备份；源不存在抛 <see cref="GameException"/>）。
        /// </summary>
        /// <param name="filePath">源文件完整路径。</param>
        void CreateBackup(string filePath);

        /// <summary>
        /// 从单档备份恢复（备份经临时文件原子替换回源路径；备份不存在抛 <see cref="GameException"/>）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        void RestoreBackup(string filePath);

        /// <summary>
        /// 清扫上次写入中断残留的孤儿临时文件（尽力而为，失败仅告警；无临时文件语义的后端空实现）。
        /// </summary>
        /// <param name="rootDirectory">存档数据根目录。</param>
        void CleanupOrphanTempFiles(string rootDirectory);

        #endregion

        #region 异步包装 [ASYNC WRAPPERS]

        /// <summary>
        /// 异步是否存在目标文件。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        UniTask<bool> ExistsAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步读取文件全部字节（缺档/失败返回 <c>null</c>，失败已由实现记录日志；需错误分型用 <see cref="TryReadAllBytes"/>）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件字节；缺档或失败为 <c>null</c>。</returns>
        UniTask<byte[]> ReadAllBytesAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步原子写入（语义同 <see cref="WriteAtomic"/>）。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="bytes">完整文件字节。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>写入完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        UniTask WriteAtomicAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步删除文件（幂等）。
        /// </summary>
        /// <param name="filePath">文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        UniTask DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步递归删除目录树（幂等）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        UniTask DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步枚举目录内指定扩展名的文件（按最后写入时间倒序）。
        /// </summary>
        /// <param name="directoryPath">目录完整路径。</param>
        /// <param name="extension">扩展名（含点，精确匹配）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件元信息数组。</returns>
        UniTask<SaveFileInfo[]> EnumerateFilesAsync(string directoryPath, string extension, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步创建单档备份。
        /// </summary>
        /// <param name="filePath">源文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>备份完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        UniTask CreateBackupAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步从单档备份恢复。
        /// </summary>
        /// <param name="filePath">目标文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>恢复完成的异步任务；失败抛出 <see cref="GameException"/>。</returns>
        UniTask RestoreBackupAsync(string filePath, CancellationToken cancellationToken = default);

        #endregion
    }
}
