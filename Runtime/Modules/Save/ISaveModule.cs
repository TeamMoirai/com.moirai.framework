using System.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    public interface ISaveModule
    {
        /// <summary>
        /// 系统将用于保存文件的默认顶级文件夹
        /// </summary>
        protected const string BASE_FOLDER_NAME = "/Data/";
        /// <summary>
        /// 保存文件夹的名称（如果未提供）
        /// </summary>
        protected const string DEFAULT_FOLDER_NAME = "Save";

        /// <summary>
        /// 将指定的 saveObject、fileName 和 folderName 保存到磁盘上的文件中
        /// </summary>
        /// <param name="saveObject">保存对象</param>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public Task Save(object saveObject, string fileName, string folderName = DEFAULT_FOLDER_NAME);

        /// <summary>
        /// 根据文件名将指定的文件加载到指定的文件夹中
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public Task<T> Load<T>(string fileName, string folderName = DEFAULT_FOLDER_NAME);

        /// <summary>
        /// 从磁盘中删除保存
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public void DeleteSave(string fileName, string folderName = DEFAULT_FOLDER_NAME);

        /// <summary>
        /// 删除整个保存文件夹
        /// </summary>
        /// <param name="folderName"></param>
        public void DeleteSaveFolder(string folderName = DEFAULT_FOLDER_NAME);

        /// <summary>
        /// 删除所有的保存文件
        /// </summary>
        public void DeleteAllSaveFiles();

        /// <summary>
        /// 是否存在存档文件
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="folderName"></param>
        /// <returns></returns>
        public bool FileExists(string fileName, string folderName = DEFAULT_FOLDER_NAME);

        /// <summary>
        /// 获取文件夹的完整保存路径
        /// </summary>
        /// <returns>保存路径</returns>
        /// <param name="folderName">文件夹名称</param>
        public string DetermineSavePath(string folderName = ISaveModule.DEFAULT_FOLDER_NAME);
    }
}