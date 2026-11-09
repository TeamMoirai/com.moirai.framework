using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

namespace Moirai.Atropos
{
    /// <summary>
    /// 维护类，可通过菜单栏从项目中删除所有空目录。
    /// </summary>
    public static class CleanEmptyFolders
    {
        private static string s_ConsoleLog = "";
        private static readonly List<DirectoryInfo> s_ListOfEmptyDirectories = new List<DirectoryInfo>();

        /// <summary>
        /// 分析项目中的空目录并删除它们及其关联的元文件。
        /// </summary>
        [MenuItem("Tools/资产相关/清理空文件夹", false, 501)]
        public static void CleanupMissingScripts()
        {
            s_ListOfEmptyDirectories.Clear();
            var assetsDir = Application.dataPath + System.IO.Path.DirectorySeparatorChar;
            GetEmptyDirectories(new DirectoryInfo(assetsDir), s_ListOfEmptyDirectories);

            if (0 < s_ListOfEmptyDirectories.Count)
            {
                s_ConsoleLog = "[CleanEmptyFolders] 本次共清理了 "+ s_ListOfEmptyDirectories.Count + " 个空文件夹:\n";
                foreach (var d in s_ListOfEmptyDirectories)
                {
                    s_ConsoleLog += "· "+ d.FullName.Replace(assetsDir, "") + "\n";
                    FileUtil.DeleteFileOrDirectory(d.FullName);
                    FileUtil.DeleteFileOrDirectory(d.FullName+".meta");
                }

                Debug.Log(s_ConsoleLog);
                s_ConsoleLog = "";

                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// 如果文件夹为空，则返回true并更新空文件夹列表。
        /// </summary>
        static bool GetEmptyDirectories(DirectoryInfo directory, List<DirectoryInfo> listOfEmptyDirectories)
        {
            bool directoryIsEmpty = true;
            directoryIsEmpty = (directory.GetDirectories().Count(x => !GetEmptyDirectories(x, listOfEmptyDirectories)) == 0) && (directory.GetFiles("*.*").All(x => x.Extension == ".meta"));

            if (directoryIsEmpty)
            {
                listOfEmptyDirectories.Add(directory);
            }
            
            return directoryIsEmpty;
        }
    }
}
