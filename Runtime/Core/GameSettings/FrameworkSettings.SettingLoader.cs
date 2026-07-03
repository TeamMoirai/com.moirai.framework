#if UNITY_EDITOR
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos
{
    public partial class FrameworkSettings
    {
        /// <summary>
        /// 创建设置文件
        /// </summary>
        /// <param name="settingPath"></param>
        /// <typeparam name="TSetting"></typeparam>
        // ReSharper disable once InconsistentNaming
        public static TSetting LoadSettingSO<TSetting>(string settingPath) where TSetting : ScriptableObject
        {
            #region 保证配置文件唯一

            string[] guids = AssetDatabase.FindAssets($"t:{typeof(TSetting).Name}");

            bool hasSetting = false;
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath != settingPath)
                {
                    Debug.LogWarning($"删除不正确的配置路径：{assetPath}");
                    AssetDatabase.DeleteAsset(assetPath);
                    AssetDatabase.DeleteAsset(assetPath + ".meta");
                }
                else if (!hasSetting)
                {
                    hasSetting = true;
                }
            }

            if (hasSetting)
            {
                return AssetDatabase.LoadAssetAtPath<TSetting>(settingPath);
            }

            #endregion

            #region 确保目录存在

            // 更健壮的路径处理
            string normalizedPath = settingPath.Replace('\\', '/');
            string parentDir = Path.GetDirectoryName(normalizedPath);

            if (parentDir == null)
            {
                Debug.LogError("无效的设置路径：" + settingPath);
                return null;
            }

            // 多层目录创建
            if (!Directory.Exists(parentDir))
            {
                // 递归创建所有不存在的父目录
                Directory.CreateDirectory(parentDir);
                // AssetDatabase.Refresh();
                Thread.Sleep(100); // 防止文件系统延迟
            }

            #endregion

            #region 创建配置文件
            TSetting setting = ScriptableObject.CreateInstance<TSetting>();
            AssetDatabase.CreateAsset(setting, settingPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"创建{typeof(TSetting).Name}，路径:{settingPath}");
            return setting;
            #endregion
        }

        /// <summary>
        /// 获取资源数据库中给定类型的所有实例
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        private static T[] GetAllInstances<T>() where T : ScriptableObject
        {
            // 参考自 https://answers.unity.com/questions/1425758/how-can-i-find-all-instances-of-a-scriptable-objec.html
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name); // FindAssets 使用标签查看文档以获取更多信息
            T[] a = new T[guids.Length];
            for (int i = 0; i < guids.Length; i++) // 可能会得到优化
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                a[i] = AssetDatabase.LoadAssetAtPath<T>(path);
            }

            return a;
        }
    }
}
#endif