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
        #region 处理器 [HANDLER]

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

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化存档服务。由容器在构建期调用。
        /// <para>确保 <c>SaveService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            // 确保 Handler 已初始化（加密处理器在此阶段注入密钥）
            _ = Handler;
        }

        /// <summary>
        /// 关闭存档服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            s_Handler?.Internal_Shutdown();
            s_Handler = null;
        }

        #endregion

        #region 存档读写 [SAVE / LOAD]

        /// <summary>
        /// 将指定的 saveObject、fileName 和 folderName 保存到磁盘上的文件中
        /// </summary>
        /// <param name="saveObject">保存对象</param>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public static UniTask Save(object saveObject, string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            Handler.Save(saveObject, fileName, folderName);

        /// <summary>
        /// 根据文件名将指定的文件加载到指定的文件夹中
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public static UniTask<T> Load<T>(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            Handler.Load<T>(fileName, folderName);

        #endregion

        #region 存档删除 [DELETE]

        /// <summary>
        /// 从磁盘中删除保存
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public static void DeleteSave(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            Handler.DeleteSave(fileName, folderName);

        /// <summary>
        /// 删除整个保存文件夹
        /// </summary>
        /// <param name="folderName">文件夹名称</param>
        public static void DeleteSaveFolder(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            Handler.DeleteSaveFolder(folderName);

        /// <summary>
        /// 删除所有的保存文件
        /// </summary>
        public static void DeleteAllSaveFiles() =>
            Handler.DeleteAllSaveFiles();

        #endregion

        #region 路径管理 [PATH]

        /// <summary>
        /// 是否存在存档文件
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public static bool FileExists(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            Handler.FileExists(fileName, folderName);

        /// <summary>
        /// 获取文件夹的完整保存路径
        /// </summary>
        /// <param name="folderName">文件夹名称</param>
        public static string DetermineSavePath(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            Handler.DetermineSavePath(folderName);

        #endregion
    }
}
