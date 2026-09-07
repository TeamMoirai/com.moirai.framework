using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务外观（Facade）。
    /// <para>统一的静态存档访问入口，通过替换 <see cref="Handler"/> 即可在不同序列化/加密策略之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="SaveServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(SaveServiceHandler))]
    public partial class SaveService : ServiceBase
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="SaveServiceSettings"/> 创建默认存档处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认存档处理器实例。</returns>
        private static SaveServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<SaveService>();
            return SaveServiceSettings.SaveServiceHandler;
        }

        /// <summary>
        /// 初始化存档服务。由容器在构建期调用。
        /// <para>确保 <c>SaveService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            // 确保 Handler 已初始化（加密处理器在此阶段注入密钥与派生参数）
            _ = Handler;
        }

        /// <summary>
        /// 关闭存档服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        #endregion

        #region 存档读写 [SAVE / LOAD]

        /// <summary>
        /// 将存档对象写入磁盘（临时文件 + 落盘刷新 + 原子替换），IO 在工作线程执行。
        /// <para>失败抛出 <see cref="GameException"/>（含路径上下文）；处理器未就绪时静默降级为空任务。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="saveObject">存档对象。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>写入完成的异步任务。</returns>
        public static UniTask Save<T>(T saveObject, string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            s_Handler?.Save(saveObject, fileName, folderName, cancellationToken) ?? UniTask.CompletedTask;

        /// <summary>
        /// 从磁盘加载存档，IO 在工作线程执行。
        /// <para>文件不存在或加载失败（损坏/解密失败/反序列化失败，均已记录错误日志）返回 <c>default</c>——需要错误判别时使用 <see cref="TryLoad{T}"/>。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>反序列化后的存档对象；失败返回默认值。</returns>
        public static UniTask<T> Load<T>(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            s_Handler?.Load<T>(fileName, folderName, cancellationToken) ?? UniTask.FromResult<T>(default);

        /// <summary>
        /// 从磁盘加载存档并返回完整错误判别结果，IO 在工作线程执行。
        /// <para>处理器未就绪时降级为 <see cref="SaveError.HandlerNotReady"/> 失败结果。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>加载结果（区分无档/损坏/解密失败等错误类别）。</returns>
        public static UniTask<SaveResult<T>> TryLoad<T>(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            s_Handler?.TryLoad<T>(fileName, folderName, cancellationToken) ?? UniTask.FromResult(SaveResult<T>.Failure(SaveError.HandlerNotReady));

        #endregion

        #region 存档删除 [DELETE]

        /// <summary>
        /// 从磁盘中删除单个存档。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        public static void DeleteSave(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.DeleteSave(fileName, folderName);

        /// <summary>
        /// 删除整个存档文件夹。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        public static void DeleteSaveFolder(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.DeleteSaveFolder(folderName);

        /// <summary>
        /// 删除存档数据根目录及其下所有存档。
        /// </summary>
        public static void DeleteAllSaveFiles() =>
            s_Handler?.DeleteAllSaveFiles();

        #endregion

        #region 存档查询 [QUERY]

        /// <summary>
        /// 是否存在存档文件。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public static bool FileExists(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.FileExists(fileName, folderName) ?? false;

        /// <summary>
        /// 枚举指定文件夹内的全部存档槽位（按最后写入时间倒序，最近优先）。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>存档元数据数组；处理器未就绪时降级为空数组。</returns>
        public static SaveFileInfo[] GetSaveFiles(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.GetSaveFiles(folderName) ?? Array.Empty<SaveFileInfo>();

        #endregion

        #region 路径管理 [PATH]

        /// <summary>
        /// 获取文件夹的完整保存路径（以目录分隔符结尾）。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>保存路径；处理器未就绪时降级返回 <c>null</c>。</returns>
        public static string DetermineSavePath(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.DetermineSavePath(folderName);

        #endregion
    }
}
