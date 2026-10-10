using Moirai.Atropos.Editor;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable.Editor
{
    public static class LubanTools
    {
        [MenuItem("Tools/Config/Luban 转表 &X", false, 25)]
        public static void BuildLubanExcel()
        {
            if (!CheckConfigRoot()) return;

            // 转表唯一入口是 gen.sh；gen.bat 只是 Windows 启动器，按平台补后缀即可。
            // 不带参数即客户端；服务端与两端要显式 gen.sh server / gen.sh all
            string path = LubanSettings.ConfigRootFullPath + "/gen";
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            path += ".sh";
#elif UNITY_EDITOR_WIN
            path += ".bat";
#endif
            Debug.Log("执行转表：" + path);
            Application.OpenURL(path);
            // ShellHelper.RunByPath(path);
        }

        [MenuItem("Tools/Config/打开表格目录", false, 26)]
        public static void OpenConfigFolder()
        {
            if (!CheckConfigRoot()) return;

            OpenFolderHelper.Execute(LubanSettings.ConfigRootFullPath);
        }

        private static bool CheckConfigRoot()
        {
            if (Directory.Exists(LubanSettings.ConfigRootFullPath)) return true;

            if (EditorUtility.DisplayDialog("配置表目录不存在",
                    $"ConfigRootPath 无效:\n{LubanSettings.ConfigRootFullPath}\n\n是否打开设置界面进行配置？",
                    "打开设置", "取消"))
            {
                Selection.activeObject = LubanSettings.Instance;
            }
            return false;
        }
    }
}