#if HYBRIDCLR_INSTALLED
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using HybridCLR.Editor.Settings;

namespace Moirai.Atropos.Editor.GameSettings
{
    [CustomEditor(typeof(UpdateSettings), true)]
    public class UpdateSettingEditor : UnityEditor.Editor
    {
#if ENABLE_HYBRIDCLR
        public List<string> HotUpdateAssemblies = new() {};
        public List<string> AOTMetaAssemblies = new() {};
        
        private void OnEnable()
        {
            // 获取当前编辑的 ScriptableObject 实例
            UpdateSettings updateSetting = (UpdateSettings)target;
            if (updateSetting != null)
            {
                HotUpdateAssemblies.AddRange(UpdateSettings.HotUpdateAssemblies);
                AOTMetaAssemblies.AddRange(UpdateSettings.AOTMetaAssemblies);
            }
        }

        public override void OnInspectorGUI()
        {
            // 记录对象修改前的状态
            EditorGUI.BeginChangeCheck();

            // 绘制默认的 Inspector 界面
            base.OnInspectorGUI();

            // 检测是否有字段被修改
            if (EditorGUI.EndChangeCheck())
            {
                // 获取当前编辑的 ScriptableObject 实例
                UpdateSettings updateSetting = (UpdateSettings)target;

                // 标记对象为“已修改”，确保修改能被保存
                EditorUtility.SetDirty(updateSetting);
                
                bool isHotChanged = !HotUpdateAssemblies.SequenceEqual(UpdateSettings.HotUpdateAssemblies);
                bool isAOTChanged = !AOTMetaAssemblies.SequenceEqual(UpdateSettings.AOTMetaAssemblies);
                if (isHotChanged)
                {
                    HybridCLRSettings.Instance.hotUpdateAssemblies = UpdateSettings.HotUpdateAssemblies.ToArray();
                    for (int i = 0; i < UpdateSettings.HotUpdateAssemblies.Count; i++)
                    {
                        var assemblyName = UpdateSettings.HotUpdateAssemblies[i];
                        string assemblyNameWithoutExtension = assemblyName.Substring(0, assemblyName.LastIndexOf('.'));
                        HybridCLRSettings.Instance.hotUpdateAssemblies[i] = assemblyNameWithoutExtension;
                    }
                    Debug.Log("HotUpdateAssemblies changed");
                }
                if (isAOTChanged)
                {
                    HybridCLRSettings.Instance.patchAOTAssemblies = UpdateSettings.AOTMetaAssemblies.ToArray();
                    Debug.Log("AOTMetaAssemblies changed");
                }

                if (isAOTChanged || isHotChanged)
                {
                    // 在修改 HybridCLRSettings 后添加
                    EditorUtility.SetDirty(HybridCLRSettings.Instance);
                    HybridCLRSettings.Save();
                    AssetDatabase.SaveAssets();
                }
            }
        }
#endif

        public static void ForceUpdateAssemblies()
        {
            HybridCLRSettings.Instance.hotUpdateAssemblies = UpdateSettings.HotUpdateAssemblies.ToArray();
            for (int i = 0; i < UpdateSettings.HotUpdateAssemblies.Count; i++)
            {
                var assemblyName = UpdateSettings.HotUpdateAssemblies[i];
                string assemblyNameWithoutExtension = assemblyName.Substring(0, assemblyName.LastIndexOf('.'));
                HybridCLRSettings.Instance.hotUpdateAssemblies[i] = assemblyNameWithoutExtension;
            }
            
            HybridCLRSettings.Instance.patchAOTAssemblies = UpdateSettings.AOTMetaAssemblies.ToArray();
            HybridCLRSettings.Save();
            EditorUtility.SetDirty(HybridCLRSettings.Instance);
            AssetDatabase.SaveAssets();

            Debug.Log("HotUpdateAssemblies changed");
        }
    }
}
#endif