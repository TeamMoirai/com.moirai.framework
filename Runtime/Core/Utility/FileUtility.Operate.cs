using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Moirai.Atropos
{
    public static partial class FileUtility
    {
        /// <summary>
        /// 移除空文件夹。
        /// </summary>
        /// <param name="directoryName">要处理的文件夹名称。</param>
        /// <returns>是否移除空文件夹成功。</returns>
        public static bool RemoveEmptyDirectory(string directoryName)
        {
            if (string.IsNullOrEmpty(directoryName))
            {
                throw new GameException("Directory name is invalid.");
            }

            try
            {
                if (!Directory.Exists(directoryName))
                {
                    return false;
                }

                // 不使用 SearchOption.AllDirectories，以便于在可能产生异常的环境下删除尽可能多的目录
                string[] subDirectoryNames = Directory.GetDirectories(directoryName, "*");
                int subDirectoryCount = subDirectoryNames.Length;
                foreach (string subDirectoryName in subDirectoryNames)
                {
                    if (RemoveEmptyDirectory(subDirectoryName))
                    {
                        subDirectoryCount--;
                    }
                }

                if (subDirectoryCount > 0)
                {
                    return false;
                }

                if (Directory.GetFiles(directoryName, "*").Length > 0)
                {
                    return false;
                }

                Directory.Delete(directoryName);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        public static string GetFileExtension(string path)
        {
            return System.IO.Path.GetExtension(path).ToLower();
        }

        public static string[] GetSpecifyFilesInFolder(string path, string[] extensions = null, bool exclude = false)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (extensions == null)
            {
                return Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            }
            else if (exclude)
            {
                return Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => !extensions.Contains(GetFileExtension(f))).ToArray();
            }
            else
            {
                return Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(GetFileExtension(f))).ToArray();
            }
        }

        public static string[] GetSpecifyFilesInFolder(string path, string pattern)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return Directory.GetFiles(path, pattern, SearchOption.AllDirectories);
        }

        public static string[] GetAllFilesInFolder(string path)
        {
            return GetSpecifyFilesInFolder(path);
        }

        public static string[] GetAllDirsInFolder(string path)
        {
            return Directory.GetDirectories(path, "*", SearchOption.AllDirectories);
        }

        public static List<string> GetAllFilesName(string path, List<string> FileList)
        {
            DirectoryInfo dir = new DirectoryInfo(path);
            FileInfo[] fil = dir.GetFiles();
            DirectoryInfo[] dii = dir.GetDirectories();
            foreach (FileInfo f in fil)
            {
                if (!f.FullName.EndsWith(".meta"))
                {
                    Log.Info(f.Name);
                    FileList.Add(f.Name);
                }
            }

            // 获取子文件夹内的文件列表，递归遍历
            foreach (DirectoryInfo d in dii)
            {
                GetAllFilesName(d.FullName, FileList);
            }

            return FileList;
        }

        public static void CheckFileAndCreateDirWhenNeeded(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            FileInfo file_info = new FileInfo(filePath);
            DirectoryInfo dir_info = file_info.Directory;
            if (!dir_info.Exists)
            {
                Directory.CreateDirectory(dir_info.FullName);
            }
        }

        public static void CheckDirAndCreateWhenNeeded(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
        }

        public static bool SafeWriteAllBytes(string outFile, byte[] outBytes)
        {
            try
            {
                if (string.IsNullOrEmpty(outFile))
                {
                    return false;
                }

                CheckFileAndCreateDirWhenNeeded(outFile);
                if (System.IO.File.Exists(outFile))
                {
                    System.IO.File.SetAttributes(outFile, FileAttributes.Normal);
                }

                System.IO.File.WriteAllBytes(outFile, outBytes);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(
                    string.Format("SafeWriteAllBytes failed! path = {0} with err = {1}", outFile, ex.Message));
                return false;
            }
        }

        public static bool SafeWriteAllLines(string outFile, string[] outLines)
        {
            try
            {
                if (string.IsNullOrEmpty(outFile))
                {
                    return false;
                }

                CheckFileAndCreateDirWhenNeeded(outFile);
                if (System.IO.File.Exists(outFile))
                {
                    System.IO.File.SetAttributes(outFile, FileAttributes.Normal);
                }

                System.IO.File.WriteAllLines(outFile, outLines);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(
                    string.Format("SafeWriteAllLines failed! path = {0} with err = {1}", outFile, ex.Message));
                return false;
            }
        }

        public static bool SafeWriteAllText(string outFile, string text)
        {
            try
            {
                if (string.IsNullOrEmpty(outFile))
                {
                    return false;
                }

                CheckFileAndCreateDirWhenNeeded(outFile);
                if (System.IO.File.Exists(outFile))
                {
                    System.IO.File.SetAttributes(outFile, FileAttributes.Normal);
                }

                System.IO.File.WriteAllText(outFile, text);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(string.Format("SafeWriteAllText failed! path = {0} with err = {1}", outFile,
                    ex.Message));
                return false;
            }
        }

        public static byte[] SafeReadAllBytes(string inFile)
        {
            try
            {
                if (string.IsNullOrEmpty(inFile))
                {
                    return null;
                }

                if (!System.IO.File.Exists(inFile))
                {
                    return null;
                }

                System.IO.File.SetAttributes(inFile, FileAttributes.Normal);
                return System.IO.File.ReadAllBytes(inFile);
            }
            catch (System.Exception ex)
            {
                Log.Error(string.Format("SafeReadAllBytes failed! path = {0} with err = {1}", inFile, ex.Message));
                return null;
            }
        }

        public static string[] SafeReadAllLines(string inFile)
        {
            try
            {
                if (string.IsNullOrEmpty(inFile))
                {
                    return null;
                }

                if (!System.IO.File.Exists(inFile))
                {
                    return null;
                }

                System.IO.File.SetAttributes(inFile, FileAttributes.Normal);
                return System.IO.File.ReadAllLines(inFile);
            }
            catch (System.Exception ex)
            {
                Log.Error(string.Format("SafeReadAllLines failed! path = {0} with err = {1}", inFile, ex.Message));
                return null;
            }
        }

        public static string SafeReadAllText(string inFile)
        {
            try
            {
                if (string.IsNullOrEmpty(inFile))
                {
                    return null;
                }

                if (!System.IO.File.Exists(inFile))
                {
                    return null;
                }

                System.IO.File.SetAttributes(inFile, FileAttributes.Normal);
                return System.IO.File.ReadAllText(inFile);
            }
            catch (System.Exception ex)
            {
                Log.Error(string.Format("SafeReadAllText failed! path = {0} with err = {1}", inFile, ex.Message));
                return null;
            }
        }

        private static void DeleteDirectory(string dirPath, string[] excludeName = null)
        {
            if (!Directory.Exists(dirPath))
            {
                return;
            }

            string[] files = Directory.GetFiles(dirPath);
            string[] dirs = Directory.GetDirectories(dirPath);

            foreach (string file in files)
            {
                bool delete = true;
                if (excludeName != null)
                {
                    foreach (string s in excludeName)
                    {
                        if (file.EndsWith(s))
                        {
                            delete = false;
                        }
                    }
                }

                if (delete)
                {
                    System.IO.File.SetAttributes(file, FileAttributes.Normal);
                    System.IO.File.Delete(file);
                }
            }

            foreach (string dir in dirs)
            {
                DeleteDirectory(dir, excludeName);
            }

            string[] filesAfter = Directory.GetFiles(dirPath);
            string[] dirsAfter = Directory.GetDirectories(dirPath);
            if (filesAfter.Length == 0 && dirsAfter.Length == 0)
            {
                Directory.Delete(dirPath, false);
            }
        }

        public static bool SafeClearDir(string folderPath, string[] excludeName = null)
        {
            try
            {
                if (string.IsNullOrEmpty(folderPath))
                {
                    return true;
                }

                if (Directory.Exists(folderPath))
                {
                    DeleteDirectory(folderPath, excludeName);
                }

                Directory.CreateDirectory(folderPath);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(string.Format("SafeClearDir failed! path = {0} with err = {1}", folderPath, ex.Message));
                return false;
            }
        }

        public static bool SafeDeleteDir(string folderPath, string[] excludeName = null)
        {
            try
            {
                if (string.IsNullOrEmpty(folderPath))
                {
                    return true;
                }

                if (Directory.Exists(folderPath))
                {
                    DeleteDirectory(folderPath, excludeName);
                }

                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(string.Format("SafeDeleteDir failed! path = {0} with err: {1}", folderPath, ex.Message));
                return false;
            }
        }

        public static bool SafeDeleteFile(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return true;
                }

                if (!System.IO.File.Exists(filePath))
                {
                    return true;
                }

                System.IO.File.SetAttributes(filePath, FileAttributes.Normal);
                System.IO.File.Delete(filePath);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(string.Format("SafeDeleteFile failed! path = {0} with err: {1}", filePath, ex.Message));
                return false;
            }
        }

        public static bool SafeRenameFile(string sourceFileName, string destFileName)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceFileName))
                {
                    return false;
                }

                if (!System.IO.File.Exists(sourceFileName))
                {
                    return true;
                }

                SafeDeleteFile(destFileName);
                System.IO.File.SetAttributes(sourceFileName, FileAttributes.Normal);
                System.IO.File.Move(sourceFileName, destFileName);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(string.Format("SafeRenameFile failed! path = {0} with err: {1}", sourceFileName,
                    ex.Message));
                return false;
            }
        }

        public static bool SafeCopyFile(string fromFile, string toFile)
        {
            try
            {
                if (string.IsNullOrEmpty(fromFile))
                {
                    return false;
                }

                if (!System.IO.File.Exists(fromFile))
                {
                    return false;
                }

                CheckFileAndCreateDirWhenNeeded(toFile);
                SafeDeleteFile(toFile);
                System.IO.File.Copy(fromFile, toFile, true);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(string.Format("SafeCopyFile failed! formFile = {0}, toFile = {1}, with err = {2}",
                    fromFile, toFile, ex.Message));
                return false;
            }
        }

        public static bool SafeCopyDirectory(string sourceDirName, string destDirName, bool copySubDirs, string[] excludeName = null)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(sourceDirName);
                if (dir.Exists == false)
                {
                    throw new DirectoryNotFoundException(
                        "Source directory does not exist or could not be found: "
                        + sourceDirName);
                }

                DirectoryInfo[] dirs = dir.GetDirectories();
                if (Directory.Exists(destDirName) == false)
                {
                    Directory.CreateDirectory(destDirName);
                }

                FileInfo[] files = dir.GetFiles();
                foreach (FileInfo file in files)
                {
                    bool copy = true;
                    if (excludeName != null)
                    {
                        foreach (string s in excludeName)
                        {
                            if (file.Name.EndsWith(s))
                            {
                                copy = false;
                            }
                        }
                    }
                    if (copy)
                    {
                        string temppath = System.IO.Path.Combine(destDirName, file.Name);
                        file.CopyTo(temppath, true);
                    }
                }

                if (copySubDirs == true)
                {
                    foreach (DirectoryInfo subdir in dirs)
                    {
                        string temppath = System.IO.Path.Combine(destDirName, subdir.Name);
                        SafeCopyDirectory(subdir.FullName, temppath, copySubDirs);
                    }
                }
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(string.Format("SafeCopyDirectory failed! sourceDirName = {0}, destDirName = {1}, with err = {2}",
                    sourceDirName, destDirName, ex.Message));
                return false;
            }
        }
        
        public static void MoveFile(string source, string dest, bool overwrite = true)
        {
            var directoryInfo = new FileInfo(dest).Directory;
            if (directoryInfo != null)
            {
                var targetPath = directoryInfo.FullName;

                if (Directory.Exists(targetPath) == false)
                {
                    Directory.CreateDirectory(targetPath);
                }
            }

            if (System.IO.File.Exists(source) == true)
            {
                if (overwrite == true)
                {
                    if (System.IO.File.Exists(dest) == true)
                    {
                        System.IO.File.SetAttributes(dest, FileAttributes.Normal);
                        System.IO.File.Delete(dest);
                    }
                }
                System.IO.File.Move(source, dest);
            }
        }
    }
}