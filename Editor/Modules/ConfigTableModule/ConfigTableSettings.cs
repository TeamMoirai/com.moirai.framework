using System;
using Sirenix.OdinInspector;
using System.IO;
using Moirai.Atropos.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable.Editor
{
    [FrameworkSetting("配置表设置", "配置表导出与代码生成路径", -501,
        "Assets/Settings/Framework/Editor/")]
    public sealed class ConfigTableSettings : FrameworkSettings<ConfigTableSettings>
    {
        private bool IsConfigRootValid => Directory.Exists(Application.dataPath + m_ConfigRootRelativePath);

        [InfoBox("@" + nameof(ConfigRootPathInfo))]
        [ReadOnly]
        [SerializeField] private string m_ConfigRootRelativePath = "/../../Config";
        private string ConfigRootPathInfo => $"配置工具的相对根目录\n相对于 '{Application.dataPath}' 文件夹";

        /// <summary>配置表目录的完整路径</summary>
        public static string ConfigRootFullPath => Application.dataPath + Instance.m_ConfigRootRelativePath;

        [Header("配置导出路径")]
        [InfoBox("修改完配置记得手动[更新配置路径]")]

        [FolderPath]
        [SerializeField] private string m_ClientDataOutPutPath = "Assets/AssetRaw/Default/Config/Table";
        private string ClientDataOutPutPath => GetRelativePath(ConfigRootFullPath, m_ClientDataOutPutPath);

        [FolderPath]
        [SerializeField] private string m_ClientCodeOutPutPath = "Assets/Scripts/GameProto";
        private string ClientCodeOutPutPath => GetRelativePath(ConfigRootFullPath, m_ClientCodeOutPutPath);

        /// <summary>资源验证根目录</summary>
        /// <example>../Client/</example>
        private string PathValidatorRoot => GetRelativePath(ConfigRootFullPath, Application.dataPath + "/..");

        /// <summary>
        /// 计算从 <see cref="relativeTo"/> 到 <see cref="path"/> 的相对路径
        /// </summary>
        /// <remarks>将绝对路径转换为相对于指定目录的 Unity 风格相对路径</remarks>
        private static string GetRelativePath(string relativeTo, string path) =>
            PathUtility.FormatToSysFilePath(Path.GetRelativePath(relativeTo, path) + "/");

        #region 初始化配置根目录

        private const string TEMPLATES_RELATIVE_PATH = "Templates~/Config";
        private static string ResolveTemplatesConfigPath()
        {
            string pkgRoot = MoiraiPackageHelper.GetPackageRootPath();
            if (string.IsNullOrEmpty(pkgRoot)) return null;

            return Path.Combine(pkgRoot, TEMPLATES_RELATIVE_PATH).Replace("\\", "/");
        }

        /// <summary>
        /// 将框架内置的 Config 模板复制到用户指定的目录。<br/>
        /// 若选中的目录名不包含 "Config"，则自动在其下创建 Config 子目录。<br/>
        /// 目标路径在 Assets 内时会自动添加 "~" 后缀以避免 Unity 导入。
        /// </summary>
        [Button("生成 Config 到指定目录", ButtonSizes.Large), PropertyOrder(-999f)]
        [HideIf(nameof(IsConfigRootValid))]
        private void CopyTemplatesConfigToTarget()
        {
            string sourcePath = ResolveTemplatesConfigPath();
            if (!Directory.Exists(sourcePath))
            {
                EditorUtility.DisplayDialog("错误", $"模板目录不存在:\n{sourcePath}", "确定");
                return;
            }

            string targetPath = SelectConfigRootRelativePath();
            SetConfigRootRelativePath(targetPath);

            if (string.IsNullOrEmpty(targetPath)) return;

            if (Directory.Exists(targetPath) && Directory.GetFileSystemEntries(targetPath).Length > 0)
            {
                if (!EditorUtility.DisplayDialog("目录非空", $"目标目录已存在文件:\n{targetPath}\n\n是否继续？", "继续", "取消"))
                    return;
            }

            CopyDirectory(sourcePath, targetPath);

            EditorUtility.DisplayDialog($"生成 Config 到指定目录",
                $"生成 Config 到:{targetPath}\n\n" +
                "现在打开配置目录（Tools/Config/打开表格目录）手动执行 build-luban 编译最新版Luban，" +
                "或者将编译好的文件导入配置目录的[Luban]文件夹。",
                "确定");
        }

        [Button("重定向 Config 目录", ButtonSizes.Large), PropertyOrder(-998f)]
        [HideIf(nameof(IsConfigRootValid))]
        private void RedirectConfigDirectory()
        {
            SetConfigRootRelativePath(SelectConfigRootRelativePath());
        }

        private const string DEFAULT_CONFIG_FOLDER_NAME = "Config";
        /// <summary>
        /// 弹出目录选择对话框，返回 Config 目录的完整路径。
        /// 若选中目录名不含 "Config"，则自动在其下拼接 Config 子目录。
        /// </summary>
        /// <returns>Config 目录完整路径，若路径在 Assets 内则带 "~" 后缀；用户取消则返回 null。</returns>
        private string SelectConfigRootRelativePath()
        {
            string selectedPath = EditorUtility.SaveFolderPanel("选择 Config 文件夹所在的目录", "", "");
            if (string.IsNullOrEmpty(selectedPath)) return null;

            string targetPath = selectedPath;
            string dirName = Path.GetFileName(selectedPath);

            if (!dirName.Contains(DEFAULT_CONFIG_FOLDER_NAME)) targetPath = Path.Combine(selectedPath, DEFAULT_CONFIG_FOLDER_NAME);

            // 避免被 Unity 读取
            bool insideAssets = targetPath.Contains(Application.dataPath);
            if (insideAssets) targetPath += "~";

            return targetPath;
        }

        /// <summary>
        /// 将完整路径转换为相对于 Application.dataPath 的相对路径，并保存到配置中。
        /// </summary>
        /// <param name="targetPath">Config 目录完整路径（可带 "~" 后缀）。</param>
        private void SetConfigRootRelativePath(string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath)) return;

            bool insideAssets = targetPath.EndsWith("~");

            string targetPathWithoutSuffix = targetPath.TrimEnd('~');
            string relativePath = "/" + PathUtility.FormatToUnityPath(Path.GetRelativePath(Application.dataPath + "/", targetPathWithoutSuffix));
            if (insideAssets) relativePath += "~";
            m_ConfigRootRelativePath = relativePath;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();

            Debug.LogWarning($"已自动设置 ConfigRootRelativePath: {relativePath}");
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source))
            {
                if (Path.GetFileName(file) == ".meta") continue;
                string destFile = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string dir in Directory.GetDirectories(source))
            {
                if (Path.GetFileName(dir).StartsWith(".")) continue;
                string destDir = Path.Combine(destination, Path.GetFileName(dir));
                CopyDirectory(dir, destDir);
            }
        }

        [Button("更新配置路径", ButtonSizes.Large)]
        [ShowIf(nameof(IsConfigRootValid))]
        private void UpdateGenerateConfigSettings()
        {
            if (!IsConfigRootValid)
            {
                Debug.LogError("配置表路径无效，请生成或重定向指定目录。");
                return;
            }

            string configRoot = ConfigRootFullPath;

            UpdatePathExportConf(configRoot);
            UpdateConfigTableModuleInit(configRoot);

            Debug.Log("已更新 path_export.conf 和 ConfigTableModule_Init.cs");
        }

        private void UpdatePathExportConf(string configRoot)
        {
            string confPath = Path.Combine(configRoot, "path_export.conf");
            if (!File.Exists(confPath))
            {
                Debug.LogWarning($"path_export.conf 不存在: {confPath}");
                return;
            }

            string clientDataOutPutPath = ClientDataOutPutPath;
            string clientCodeOutPutPath = ClientCodeOutPutPath;

            Debug.LogWarning($"[ConfigTable] Update OutPut Path\n" +
                             $"ClientDataOutPutPath:{clientDataOutPutPath}\n" +
                             $"ClientCodeOutPutPath:{clientCodeOutPutPath}");

            string content = File.ReadAllText(confPath);
            content = ReplaceConfValue(content, "DATA_OUTPUT_PATH_CLIENT", clientDataOutPutPath);
            content = ReplaceConfValue(content, "CODE_OUTPUT_PATH_CLIENT", clientCodeOutPutPath + "Gen\\");
            content = ReplaceConfValue(content, "CONFIG_SCRIPT_TARGET", clientCodeOutPutPath + "ConfigTableModule.cs");
            // ReSharper disable once StringLiteralTypo
            content = ReplaceConfValue(content, "CONFIGINIT_SCRIPT_TARGET", clientCodeOutPutPath + "ConfigTableModule_Init.cs");
            // ReSharper disable once StringLiteralTypo
            content = ReplaceConfValue(content, "EXTERNALTYPEUTIL_SCRIPT_TARGET", clientCodeOutPutPath + "ExternalTypeUtil.cs");
            content = ReplaceConfValue(content, "PATH_VALIDATOR_ROOT", PathValidatorRoot);

            File.WriteAllText(confPath, content);
        }

        private void UpdateConfigTableModuleInit(string configRoot)
        {
            string initPath = Path.Combine(configRoot, "CustomTemplate", "ConfigTableModule_Init.cs");
            if (!File.Exists(initPath))
            {
                Debug.LogWarning($"ConfigTableModule_Init.cs 不存在: {initPath}");
                return;
            }

            string configPath = ClientDataOutPutPath;
            if (string.IsNullOrEmpty(configPath))
            {
                Debug.LogWarning($"无法从 ClientDataOutPutPath 提取 Unity 资源路径: {configPath}");
                return;
            }

            string content = File.ReadAllText(initPath);
            string target = $"private const string CONFIG_PATH = \"{configPath}\";";
            if (content.Contains(target)) return;

            content = ReplaceConstString(content, "CONFIG_PATH", configPath);
            File.WriteAllText(initPath, content);
        }

        private static string ReplaceConfValue(string content, string key, string newValue)
        {
            string[] lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith(key + "="))
                {
                    lines[i] = key + "=" + newValue;
                    break;
                }
            }
            return string.Join("\n", lines);
        }

        private static string ReplaceConstString(string content, string constName, string newValue)
        {
            string search = $"private const string {constName} = \"";
            int idx = content.IndexOf(search);
            if (idx < 0) return content;

            int valueStart = idx + search.Length;
            int valueEnd = content.IndexOf('"', valueStart);
            if (valueEnd < 0) return content;

            return content.Substring(0, valueStart) + newValue + content.Substring(valueEnd);
        }

        #endregion

    }
}