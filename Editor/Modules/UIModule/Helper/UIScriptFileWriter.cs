using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    public interface IUIScriptFileWriter
    {
        void Write(GameObject targetObject, string className, string scriptContent, UIScriptGenerateData scriptGenerateData, string windowScriptContent);
    }

    /// <summary>
    /// 默认脚本文件写入器实现
    /// </summary>
    public sealed class DefaultUIScriptFileWriter : IUIScriptFileWriter
    {
        /// <inheritdoc/>
        public void Write(GameObject targetObject, string className, string scriptContent, UIScriptGenerateData scriptGenerateData, string windowScriptContent)
        {
            if (string.IsNullOrEmpty(className)) throw new ArgumentNullException(nameof(className));
            if (scriptContent == null) throw new ArgumentNullException(nameof(scriptContent));
            if (scriptGenerateData == null) throw new ArgumentNullException(nameof(scriptGenerateData));

            var scriptFolderPath = scriptGenerateData.GenerateHolderCodePath;
            var scriptFilePath = Path.Combine(scriptFolderPath, $"{className}.g.cs");

            Directory.CreateDirectory(scriptFolderPath);

            #region 自动生成脚本

            if (File.Exists(scriptFilePath) && IsContentUnchanged(scriptFilePath, scriptContent))
            {
                UIScriptGeneratorHelper.BindUIScript();
                return;
            }

            File.WriteAllText(scriptFilePath, scriptContent, Encoding.UTF8);

            #endregion

            #region 窗口实现类模板

            var windowFilePath = Path.Combine(scriptFolderPath, $"{className}.cs");
            if (!File.Exists(windowFilePath))
            {
                File.WriteAllText(windowFilePath, windowScriptContent, Encoding.UTF8);
            }

            #endregion

            AssetDatabase.Refresh();
        }

        private static bool IsContentUnchanged(string filePath, string newContent)
        {
            var oldText = File.ReadAllText(filePath, Encoding.UTF8);
            return oldText.Equals(newContent, StringComparison.Ordinal);
        }
    }
}