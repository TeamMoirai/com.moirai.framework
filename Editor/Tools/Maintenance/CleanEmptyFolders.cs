using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 维护类，可通过菜单栏从项目中删除所有空目录 
    /// </summary>
    public static class CleanEmptyFolders
    {
        private static string _consoleLog = "";
        private static List<DirectoryInfo> _listOfEmptyDirectories = new List<DirectoryInfo>();

        /// <summary>
        /// 分析项目中的空目录并删除它们及其关联的元文件 
        /// </summary>
        [MenuItem("Tools/资产相关/清理空文件夹", false, 501)]
        public static void CleanupMissingScripts()
        {
            _listOfEmptyDirectories.Clear();
            var assetsDir = Application.dataPath + System.IO.Path.DirectorySeparatorChar;
            GetEmptyDirectories(new DirectoryInfo(assetsDir), _listOfEmptyDirectories);

            if (0 < _listOfEmptyDirectories.Count)
            {
                _consoleLog = "[CleanEmptyFolders] 本次共清理了 "+ _listOfEmptyDirectories.Count + " 个空文件夹:\n";
                foreach (var d in _listOfEmptyDirectories)
                {
                    _consoleLog += "· "+ d.FullName.Replace(assetsDir, "") + "\n";
                    FileUtil.DeleteFileOrDirectory(d.FullName);
                    FileUtil.DeleteFileOrDirectory(d.FullName+".meta");
                }

                Debug.Log(_consoleLog);
                _consoleLog = "";

                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// 如果文件夹为空，则返回true并更新空文件夹列表
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="listOfEmptyDirectories"></param>
        /// <returns></returns>
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
