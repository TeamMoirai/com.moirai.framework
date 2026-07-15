using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    public class SaveModule : Module, ISaveModule
    {
        private ISaveHandler _saveHandler;

        #region 实现方法 [IMPLEMENTATION METHODS]

        public override void OnInit()
        {
            _saveHandler = SaveSettings.SaveHandler;
            if (_saveHandler is SaveEncryptor saveEncryptor) saveEncryptor.Key = SaveSettings.EncryptionKey;
        }

        public override void Shutdown()
        {
            _saveHandler = null;
        }

        public async Task Save(object saveObject, string fileName, string folderName = ISaveModule.DEFAULT_FOLDER_NAME)
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
            FileStream saveFile = File.Create(tempFilePath);
            await _saveHandler.Save(saveObject, saveFile);
            saveFile.Close();

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

        public async Task<T> Load<T>(string fileName, string folderName = ISaveModule.DEFAULT_FOLDER_NAME)
        {
            string savePath = DetermineSavePath(folderName);
            string saveFileName = savePath + DetermineSaveFileName(fileName);

            // 如果 Saves 目录或保存文件不存在，则无需加载任意内容，直接退出
            if (!Directory.Exists(savePath) || !File.Exists(saveFileName))
            {
                return default;
            }

            FileStream saveFile = File.Open(saveFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            T returnObject = await _saveHandler.Load<T>(saveFile);
            saveFile.Close();

            return returnObject;
        }

        public void DeleteSave(string fileName, string folderName = ISaveModule.DEFAULT_FOLDER_NAME)
        {
            string savePath = DetermineSavePath(folderName);
            string saveFileName = DetermineSaveFileName(fileName);
            if (File.Exists(savePath + saveFileName))
            {
                File.Delete(savePath + saveFileName);
            }
        }

        public void DeleteSaveFolder(string folderName = ISaveModule.DEFAULT_FOLDER_NAME)
        {
            string savePath = DetermineSavePath(folderName);
            if (Directory.Exists(savePath))
            {
                DeleteDirectory(savePath);
            }
        }

        public void DeleteAllSaveFiles()
        {
            string savePath = DetermineSavePath("");

            savePath = savePath.Substring(0, savePath.Length - 1);
            if (savePath.EndsWith("/"))
            {
                savePath = savePath.Substring(0, savePath.Length - 1);
            }

            if (Directory.Exists(savePath))
            {
                DeleteDirectory(savePath);
            }
        }

        public bool FileExists(string fileName, string folderName = ISaveModule.DEFAULT_FOLDER_NAME)
        {
            string savePath = DetermineSavePath(folderName);
            string saveFileName = DetermineSaveFileName(fileName);

            return File.Exists(savePath + saveFileName);
        }

        public string DetermineSavePath(string folderName = ISaveModule.DEFAULT_FOLDER_NAME)
        {
            // 拼装路径
            string savePath = Application.persistentDataPath + ISaveModule.BASE_FOLDER_NAME;

            savePath = savePath + folderName + "/";
            return savePath;
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 判断要保存的文件名称
        /// </summary>
        /// <returns>保存文件名</returns>
        /// <param name="fileName">文件名</param>
        private static string DetermineSaveFileName(string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName) + SaveSettings.SaveFileExtension;
        }

        /// <summary>
        /// 删除指定的目录
        /// </summary>
        /// <param name="targetDir"></param>
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