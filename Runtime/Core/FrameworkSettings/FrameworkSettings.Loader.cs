#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos
{
    public partial class FrameworkSettings<T>
    {
        // 重入标记：本方法自己的 CreateAsset 会同步派发 projectChanged，订阅方可能立刻回头读同一份设置。
        private static bool s_IsLoading;
        // 跳过创建时只报一次：导入期的设置访问是反复的，别把原来的递归换成刷屏
        private static string s_SkippedCreatePath;

        /// <summary>
        /// 加载或创建设置文件，新创建时通过 onNewAsset 回调初始化。
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public static TSetting LoadSettingSO<TSetting>(string settingPath, Action<TSetting> onNewAsset = null) where TSetting : ScriptableObject
        {
            // 重入期间资产还没进库，再查一遍只会再造一次、再派发一次 → 同步自递归（实测能把编辑器钉死在导入阶段）。
            // 重入一律按「暂不可用」返回 null，让调用方降级，绝不叠加。
            if (s_IsLoading) return null;

            s_IsLoading = true;
            try
            {
                return LoadSettingSOCore(settingPath, onNewAsset);
            }
            finally
            {
                s_IsLoading = false;
            }
        }

        private static TSetting LoadSettingSOCore<TSetting>(string settingPath, Action<TSetting> onNewAsset) where TSetting : ScriptableObject
        {
            #region 检出重复配置（只报告，不代删）

            string[] guids = AssetDatabase.FindAssets($"t:{typeof(TSetting).Name}");

            bool hasSetting = false;
            string duplicatePaths = null;
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath != settingPath)
                {
                    duplicatePaths = duplicatePaths == null ? assetPath : duplicatePaths + "、" + assetPath;
                }
                else
                {
                    hasSetting = true;
                }
            }

            // 本方法是"读一次配置"的公共入口，
            // 在其中静默删用户资产不可接受：同名类型存两份合法用法不少（分平台、A/B、包内默认 + 项目覆盖），删掉任何一份都是丢工作。
            // 真实风险如实报出来即可——打包后 Resources.Load 在多份之间取哪一份不由路径决定。
            if (duplicatePaths != null)
            {
                Debug.LogError(
                    $"{typeof(TSetting).Name} 存在多份资产。期望路径：{settingPath}；另有：{duplicatePaths}。" +
                    "打包时 Resources.Load 取到哪一份不确定，请自行确认并保留唯一副本" +
                    "（本方法不会代你删除任何资产）。");
            }

            if (hasSetting)
            {
                s_SkippedCreatePath = null;
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

            // 资产库查不到 ≠ 资产不存在：项目导入 / 域重载途中 FindAssets 会漏掉已落盘的文件。
            // 此时 CreateAsset 会覆盖用户配置、重生成 meta GUID，并再次派发 projectChanged 回到本方法。
            if (File.Exists(Path.GetFullPath(normalizedPath)))
            {
                var onDisk = AssetDatabase.LoadAssetAtPath<TSetting>(normalizedPath);
                if (onDisk == null && s_SkippedCreatePath != normalizedPath)
                {
                    s_SkippedCreatePath = normalizedPath;
                    // 仍按失败返回（本类禁用 LogUtility，见类注释）：宁可不给值，也不拿空实例顶掉用户盘上的那份
                    Debug.LogWarning($"{typeof(TSetting).Name} 资产文件已在磁盘上但资产库尚未导入，本次跳过创建：{normalizedPath}");
                }

                return onDisk;
            }

            TSetting setting = CreateInstance<TSetting>();
            onNewAsset?.Invoke(setting);
            AssetDatabase.CreateAsset(setting, settingPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"创建{typeof(TSetting).Name}，路径:{settingPath}");
            return setting;

            #endregion
        }

        /// <summary>
        /// 获取资源数据库中给定类型的所有实例。
        /// </summary>
        private static TSetting[] GetAllSettings<TSetting>() where TSetting : ScriptableObject
        {
            // 参考自 https://answers.unity.com/questions/1425758/how-can-i-find-all-instances-of-a-scriptable-objec.html
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            TSetting[] results = new TSetting[guids.Length];
            for (int i = 0; i < guids.Length; i++) // 可能会得到优化
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                results[i] = AssetDatabase.LoadAssetAtPath<TSetting>(path);
            }

            return results;
        }
    }
}
#endif