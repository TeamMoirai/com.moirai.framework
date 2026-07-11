using System;
using System.IO;
using System.Linq;
using UnityEditorInternal;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    public class EditorScriptableSingleton<T> : ScriptableObject where T : ScriptableObject
    {
        private static T s_Instance;
        public static T Instance
        {
            get
            {
                if (!s_Instance)
                {
                    LoadOrCreate();
                }

                return s_Instance;
            }
        }

        public static T LoadOrCreate()
        {
            string filePath = GetFilePath();
            if (!string.IsNullOrEmpty(filePath))
            {
                var arr = InternalEditorUtility.LoadSerializedFileAndForget(filePath);
                s_Instance = arr.Length > 0 ? arr[0] as T : s_Instance ?? CreateInstance<T>();
            }
            else
            {
                Debug.LogError($"save location of {nameof(EditorScriptableSingleton<T>)} is invalid");
            }

            return s_Instance;
        }

        public static void Save(bool saveAsText = true)
        {
            if (!s_Instance)
            {
                Debug.LogError("Cannot save ScriptableSingleton: no instance!");
                return;
            }

            string filePath = GetFilePath();
            if (!string.IsNullOrEmpty(filePath))
            {
                string directoryName = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directoryName))
                {
                    if (directoryName != null)
                    {
                        Directory.CreateDirectory(directoryName);
                    }
                }

                UnityEngine.Object[] obj = { s_Instance };
                InternalEditorUtility.SaveToSerializedFileAndForget(obj, filePath, saveAsText);
            }
        }

        protected static string GetFilePath()
        {
            return typeof(T).GetCustomAttributes(inherit: true)
                .Where(v => v is FilePathAttribute)
                .Cast<FilePathAttribute>()
                .FirstOrDefault()
                ?.Filepath;
        }
    }

    /// <summary>
    ///   <para>用于指定相对于 Project 文件夹或 Unity 的 preferences 文件夹的文件位置。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class FilePathAttribute : Attribute
    {
        private string m_FilePath;
        private string m_RelativePath;
        private Location m_Location;

        internal string Filepath
        {
            get
            {
                if (m_FilePath == null && m_RelativePath != null)
                {
                    m_FilePath = CombineFilePath(m_RelativePath, m_Location);
                    m_RelativePath = (string)null;
                }

                return m_FilePath;
            }
        }

        public FilePathAttribute(string relativePath, Location location)
        {
            m_RelativePath = !string.IsNullOrEmpty(relativePath)
                ? relativePath
                : throw new ArgumentException("Invalid relative path (it is empty)");
            m_Location = location;
        }

        private static string CombineFilePath(string relativePath, Location location)
        {
            if (relativePath[0] == '/')
                relativePath = relativePath.Substring(1);
            switch (location)
            {
                case Location.PreferencesFolder:
                    return InternalEditorUtility.unityPreferencesFolder + "/" + relativePath;
                case Location.ProjectFolder:
                    return relativePath;
                default:
                    Debug.LogError((object)("Unhandled enum: " + location.ToString()));
                    return relativePath;
            }
        }

        /// <summary>
        ///   <para>指定 Unity 与 <see cref="FilePathAttribute"/> 构造函数中提供的相对路径一起使用的文件夹位置。</para>
        /// </summary>
        public enum Location
        {
            /// <summary>
            ///   <para>使用此位置可相对于 preferences 文件夹保存文件。用于跨所有项目的用户文件。</para>
            /// </summary>
            PreferencesFolder,

            /// <summary>
            ///   <para>使用此位置可相对于 Project Folder 保存文件。用于每个项目的文件（不在项目之间共享）。</para>
            /// </summary>
            ProjectFolder,
        }
    }
}