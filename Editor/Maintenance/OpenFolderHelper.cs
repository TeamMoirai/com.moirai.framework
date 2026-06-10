using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 打开文件夹相关的实用函数。
    /// </summary>
    public static class OpenFolderHelper
    {
        private const string MENU_ITEM_PATH = "Tools/Open Folder/";
        private const int MENU_ITEM_PRIORITY = -990;

        /// <summary>
        /// 打开 Data Path 文件夹。
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Data Path", false, MENU_ITEM_PRIORITY)]
        public static void OpenFolderDataPath()
        {
            Execute(Application.dataPath);
        }

        /// <summary>
        /// 打开 Persistent Data Path 文件夹。
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Persistent Data Path", false, MENU_ITEM_PRIORITY + 1)]
        public static void OpenFolderPersistentDataPath()
        {
            Execute(Application.persistentDataPath);
        }

        /// <summary>
        /// 打开 Streaming Assets Path 文件夹。
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Streaming Assets Path", false, MENU_ITEM_PRIORITY + 2)]
        public static void OpenFolderStreamingAssetsPath()
        {
            Execute(Application.streamingAssetsPath);
        }

        /// <summary>
        /// 打开 Temporary Cache Path 文件夹。
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Temporary Cache Path", false, MENU_ITEM_PRIORITY + 3)]
        public static void OpenFolderTemporaryCachePath()
        {
            Execute(Application.temporaryCachePath);
        }

#if UNITY_2018_3_OR_NEWER

        /// <summary>
        /// 打开 Console Log Path 文件夹。
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Console Log Path", false, MENU_ITEM_PRIORITY + 4)]
        public static void OpenFolderConsoleLogPath()
        {
            Execute(System.IO.Path.GetDirectoryName(Application.consoleLogPath));
        }

#endif

        /// <summary>
        /// 打开指定路径的文件夹。
        /// </summary>
        /// <param name="folder">要打开的文件夹的路径。</param>
        public static void Execute(string folder)
        {
            folder = $"\"{folder}\"";
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    Process.Start("Explorer.exe", folder.Replace('/', '\\'));
                    break;

                case RuntimePlatform.OSXEditor:
                    Process.Start("open", folder);
                    break;

                default:
                    throw new System.Exception($"Not support open folder on '{Application.platform}' platform.");
            }
        }
    }
}