using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档处理器抽象基类（策略模式抽象策略）。
    /// <para>承载文件管理流程（路径拼装、目录创建、原子写入、删除清理），
    /// 子类只需实现 <see cref="SerializeAsync"/> / <see cref="DeserializeAsync{T}"/> 序列化策略。</para>
    /// <para>由 <see cref="SaveServiceSettings"/> 序列化持有实例，经 <see cref="SaveService"/> 静态外观访问。</para>
    /// </summary>
    public abstract class SaveServiceHandler : FrameworkHandler
    {
        /// <summary>存档根目录（persistentDataPath 下）。</summary>
        public const string BASE_FOLDER_NAME = "/Data/";
        /// <summary>默认存档文件夹名。</summary>
        public const string DEFAULT_FOLDER_NAME = "Save";

        #region 存档读写 [SAVE / LOAD]

        /// <summary>
        /// 将指定的 saveObject、fileName 和 folderName 保存到磁盘上的文件中
        /// </summary>
        /// <param name="saveObject">保存对象</param>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public async UniTask Save(object saveObject, string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            string savePath = DetermineSavePath(folderName);
            string saveFileName = DetermineSaveFileName(fileName);

            // 如果该目录尚不存在，则创建
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }

            string saveFilePath = savePath + saveFileName;
            string tempFilePath = saveFilePath + ".tmp";

            // 将对象序列化并写入磁盘上的文件中
            using (FileStream saveFile = File.Create(tempFilePath))
            {
                await SerializeAsync(saveObject, saveFile);
            }

            // 释放临时文件——用try-final确保清理
            try
            {
                if (File.Exists(saveFilePath))
                {
                    File.Delete(saveFilePath);
                }
                File.Move(tempFilePath, saveFilePath);
            }
            catch
            {
                // 故障时清理临时文件
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); }
                    catch
                    {
                        // ignored
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// 根据文件名将指定的文件加载到指定的文件夹中
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public async UniTask<T> Load<T>(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            string savePath = DetermineSavePath(folderName);
            string saveFileName = savePath + DetermineSaveFileName(fileName);

            // 如果 Saves 目录或保存文件不存在，则无需加载任意内容，直接退出
            if (!Directory.Exists(savePath) || !File.Exists(saveFileName))
            {
                return default;
            }

            using (FileStream saveFile = File.Open(saveFileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return await DeserializeAsync<T>(saveFile);
            }
        }

        #endregion

        #region 序列化策略 [SERIALIZATION STRATEGY]

        /// <summary>
        /// 将对象序列化写入文件流。由子类实现具体格式（JSON / 二进制 / 加密等）。
        /// </summary>
        protected internal abstract UniTask SerializeAsync(object saveObject, FileStream saveFile);

        /// <summary>
        /// 从文件流反序列化对象。由子类实现具体格式。
        /// </summary>
        protected internal abstract UniTask<T> DeserializeAsync<T>(FileStream saveFile);

        #endregion

        #region 存档删除 [DELETE]

        /// <summary>
        /// 从磁盘中删除保存
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public void DeleteSave(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            string savePath = DetermineSavePath(folderName);
            string saveFileName = DetermineSaveFileName(fileName);
            if (File.Exists(savePath + saveFileName))
            {
                File.Delete(savePath + saveFileName);
            }
        }

        /// <summary>
        /// 删除整个保存文件夹
        /// </summary>
        /// <param name="folderName">文件夹名称</param>
        public void DeleteSaveFolder(string folderName = DEFAULT_FOLDER_NAME)
        {
            string savePath = DetermineSavePath(folderName);
            if (Directory.Exists(savePath))
            {
                DeleteDirectory(savePath);
            }
        }

        /// <summary>
        /// 删除所有的保存文件
        /// </summary>
        public void DeleteAllSaveFiles()
        {
            string savePath = DetermineSavePath("");
            savePath = Path.GetDirectoryName(savePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (savePath != null && Directory.Exists(savePath))
            {
                DeleteDirectory(savePath);
            }
        }

        #endregion

        #region 路径管理 [PATH]

        /// <summary>
        /// 是否存在存档文件
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="folderName">文件夹名称</param>
        public bool FileExists(string fileName, string folderName = DEFAULT_FOLDER_NAME)
        {
            string savePath = DetermineSavePath(folderName);
            string saveFileName = DetermineSaveFileName(fileName);

            return File.Exists(savePath + saveFileName);
        }

        /// <summary>
        /// 获取文件夹的完整保存路径
        /// </summary>
        /// <param name="folderName">文件夹名称</param>
        /// <returns>保存路径</returns>
        public string DetermineSavePath(string folderName = DEFAULT_FOLDER_NAME)
        {
            // 拼装路径
            string savePath = Application.persistentDataPath + BASE_FOLDER_NAME;

            savePath = savePath + folderName + "/";
            return savePath;
        }

        /// <summary>
        /// 判断要保存的文件名称（自动追加配置的扩展名）。
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <returns>保存文件名</returns>
        protected static string DetermineSaveFileName(string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName) + SaveServiceSettings.SaveFileExtension;
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 删除指定的目录
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        private static void DeleteDirectory(string targetDir)
        {
            string[] files = Directory.GetFiles(targetDir);
            string[] dirs = Directory.GetDirectories(targetDir);

            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs)
            {
                DeleteDirectory(dir);
            }

            Directory.Delete(targetDir, false);

            if (File.Exists(targetDir + ".meta"))
            {
                File.Delete(targetDir + ".meta");
            }
        }

        #endregion
    }
}
