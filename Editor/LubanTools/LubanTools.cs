using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    public static class LubanTools
    {
        [MenuItem("Tools/Config/Luban 转表 &X", false, 25)]
        public static void BuildLubanExcel()
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            string path = Application.dataPath + "/../../Config/gen_code_bin_to_project_lazyload.sh";
#elif UNITY_EDITOR_WIN
            string path = Application.dataPath + "/../../Config/gen_code_bin_to_project_lazyload.bat";
#endif
            Debug.Log($"执行转表：{path}");
            Application.OpenURL(path);
            // ShellHelper.RunByPath(path);
        }
        
        [MenuItem("Tools/Config/打开表格目录", false, 26)]
        public static void OpenConfigFolder()
        {
            OpenFolderHelper.Execute(Application.dataPath + @"/../../Config");
        }
    }
}