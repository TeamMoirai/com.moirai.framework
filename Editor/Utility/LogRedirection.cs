using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditorInternal;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 日志重定向相关的实用函数。
    /// </summary>
    internal static class LogRedirection
    {
        // 包装 LOG 类
        private static readonly string[] LOG_SCRIPTS = new string[]
        {
            "Log.cs",
            "LogUtility.cs",
            "DefaultLogHelper.cs",
            "ResourceLogger.cs",
        };
        
        [OnOpenAsset(0)]
        private static bool OnOpenAsset(int instanceID, int line)
        {
            if (line <= 0)
            {
                return false;
            }
            // 获取资源路径
            string assetPath = AssetDatabase.GetAssetPath(instanceID);
            
            // 判断资源类型
            if (!assetPath.EndsWith(".cs"))
            {
                return false;
            }
            
            bool autoFirstMatch = CheckMethod(assetPath, LOG_SCRIPTS);
            
            var stackTrace = GetStackTrace();
            if (!string.IsNullOrEmpty(stackTrace) && CheckMethod(stackTrace, LOG_SCRIPTS))
            {
                if (!autoFirstMatch)
                {
                    var fullPath = UnityEngine.Application.dataPath.Substring(0, UnityEngine.Application.dataPath.LastIndexOf("Assets", StringComparison.Ordinal));
                    fullPath = $"{fullPath}{assetPath}";
                    // 跳转到目标代码的指定行
                    InternalEditorUtility.OpenFileAtLineExternal(fullPath.Replace('/', '\\'), line);
                    return true;
                }
                
                // 使用正则表达式匹配at的哪个脚本的哪一行
                var matches = Regex.Match(stackTrace, @"\(at (.+)\)",
                    RegexOptions.IgnoreCase);
                while (matches.Success)
                {
                    var pathLine = matches.Groups[1].Value;

                    if (!CheckMethod(pathLine, LOG_SCRIPTS))
                    {
                        var splitIndex = pathLine.LastIndexOf(":", StringComparison.Ordinal);
                        // 脚本路径
                        var path = pathLine.Substring(0, splitIndex);
                        // 行号
                        line = Convert.ToInt32(pathLine.Substring(splitIndex + 1));
                        var fullPath = UnityEngine.Application.dataPath.Substring(0, UnityEngine.Application.dataPath.LastIndexOf("Assets", StringComparison.Ordinal));
                        fullPath = $"{fullPath}{path}";
                        // 跳转到目标代码的指定行
                        InternalEditorUtility.OpenFileAtLineExternal(fullPath.Replace('/', '\\'), line);
                        break;
                    }

                    matches = matches.NextMatch();
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取当前日志窗口选中的日志的堆栈信息。
        /// </summary>
        /// <returns>选中日志的堆栈信息实例。</returns>
        private static string GetStackTrace()
        {
            // 通过反射获取ConsoleWindow类
            var consoleWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow");
            // 获取窗口实例
            var fieldInfo = consoleWindowType.GetField("ms_ConsoleWindow",
                BindingFlags.Static |
                BindingFlags.NonPublic);
            if (fieldInfo != null)
            {
                var consoleInstance = fieldInfo.GetValue(null);
                if (consoleInstance != null)
                    if (EditorWindow.focusedWindow == (EditorWindow)consoleInstance)
                    {
                        // 获取m_ActiveText成员
                        fieldInfo = consoleWindowType.GetField("m_ActiveText", BindingFlags.Instance | BindingFlags.NonPublic);
                        // 获取m_ActiveText的值
                        if (fieldInfo != null)
                        {
                            var activeText = fieldInfo.GetValue(consoleInstance).ToString();
                            return activeText;
                        }
                    }
            }

            return null;
        }
        
        /// <summary>
        /// 判断一个字符串中是否包含另一个字符串数组或列表中的任意一个元素
        /// </summary>
        /// <param name="str"></param>
        /// <param name="excludeWordList"></param>
        /// <returns></returns>
        private static bool CheckMethod(string str, ICollection<string> excludeWordList = null)
        {
            if (str.Trim().Length <= 0 || excludeWordList == null || excludeWordList.Count <= 0)
            {
                return false;
            }
            return excludeWordList.Any(s => str.IndexOf(s, StringComparison.Ordinal) >= 0);
        }
    }
}