using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务迁移分部：文件级版本迁移总线（<see cref="SaveMigrationManager"/>）的外观入口。
    /// <para>版本模型：int 递增（0 = 版本化前基线）；游戏层启动期设置 <see cref="CurrentSaveVersion"/> 并注册 <see cref="ISaveMigrator"/>
    /// （实现类由 SaveHost SourceGenerator 扫描自注册，AOT 安全）。加载/写入管线自动前置迁移链；
    /// 本分部提供显式整档迁移入口（用于启动期批量修复旧档等场景）。</para>
    /// <para>降级契约：处理器未就绪时 <see cref="MigrateSave"/>/<see cref="MigrateSaveAsync"/> 返回 <see cref="SaveError.HandlerNotReady"/>；
    /// 注册与版本设置不依赖处理器（静态管理器直挂）。</para>
    /// </summary>
    public partial class SaveService
    {
        #region 版本迁移 [MIGRATION]

        /// <summary>
        /// 当前存档数据版本（int 递增；默认 0 = 迁移总线未激活，读写管线零开销旁路）。
        /// <para>启动期主线程设置；启用版本化且存在旧档时须注册自版本 0 起的迁移链（旧档无元数据块按版本 0 处理，形状未变可用空迁移器桥接）。</para>
        /// </summary>
        public static int CurrentSaveVersion
        {
            get => SaveMigrationManager.CurrentVersion;
            set => SaveMigrationManager.CurrentVersion = value;
        }

        /// <summary>
        /// 注册存档迁移器（同类型重复注册以最新为准；模块初始化器自注册之外的补充手动通道）。
        /// </summary>
        /// <param name="migrator">迁移器实例（版本契约：<c>FromVersion &gt;= 0</c> 且 <c>ToVersion &gt; FromVersion</c>，破坏即抛 <see cref="System.ArgumentException"/>）。</param>
        public static void RegisterMigrator(ISaveMigrator migrator) =>
            SaveMigrationManager.Register(migrator);

        /// <summary>
        /// 显式迁移指定存档到当前数据版本（在调用线程执行，阻塞直至完成；仅限主线程）。
        /// <para>显式调用即表达立即修复意图——迁移成功后强制回写（不受迁移回写设置约束）；
        /// 版本相等/迁移总线未激活为无操作；处理器未就绪返回 <see cref="SaveError.HandlerNotReady"/>。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>错误码（缺档返回 <see cref="SaveError.FileNotFound"/>；迁移链缺失/失败返回 <see cref="SaveError.MigrationFailed"/>；降级返回 <see cref="SaveError.UnsupportedVersion"/>）。</returns>
        public static SaveError MigrateSave(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.MigrateSave(fileName, folderName) ?? SaveError.HandlerNotReady;

        /// <summary>
        /// 显式迁移指定存档到当前数据版本，IO 在工作线程执行。
        /// <para>语义与 <see cref="MigrateSave"/> 一致（迁移成功强制回写）；处理器未就绪返回 <see cref="SaveError.HandlerNotReady"/>。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>迁移结果（错误码）。</returns>
        public static UniTask<SaveError> MigrateSaveAsync(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            s_Handler?.MigrateSaveAsync(fileName, folderName, cancellationToken) ?? UniTask.FromResult(SaveError.HandlerNotReady);

        #endregion
    }
}
