using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Moirai.Atropos.Save;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Editor.Save
{
    /// <summary>
    /// 资产引用收集器：扫描已打开场景中的 <see cref="SaveComponent"/>，把资产引用字段（UnityEngine.Object 派生且非
    /// GameObject/Component 派生）当前引用的资产登记进 <see cref="SaveAssetCatalog"/>（缺失定位串按文件名寻址约定推导）。
    /// <para>消除「漏登记 → 捕获静默写 Null」面：登记后编辑器立即标脏并保存目录资产；
    /// 场景对象实例（非项目资产）与无法推导定位串的资产仅告警不登记。项目使用自定义寻址约定时须人工复核定位串。</para>
    /// </summary>
    public static class SaveAssetCatalogCollector
    {
        /// <summary>
        /// 扫描已打开场景并把资产引用登记入目录（菜单：Tools/Moirai/Save/Collect Asset References into Catalog）。
        /// </summary>
        [MenuItem("Tools/Moirai/Save/Collect Asset References into Catalog", false, 101)]
        public static void CollectFromOpenScenes()
        {
            SaveAssetCatalog catalog = SaveServiceSettings.AssetCatalog;
            if (catalog == null)
            {
                EditorUtility.DisplayDialog("Save Asset Catalog", "未配置资产引用目录——请先在 SaveServiceSettings.m_AssetCatalog 指定目录资产。", "OK");
                return;
            }

            int scannedComponents = 0;
            int scannedFields = 0;
            int added = 0;
            int skippedExisting = 0;
            var warnings = new List<string>();

            Undo.RecordObject(catalog, "Collect Save Asset References");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (SaveComponent component in root.GetComponentsInChildren<SaveComponent>(includeInactive: true))
                    {
                        scannedComponents++;
                        CollectComponent(component, catalog, ref scannedFields, ref added, ref skippedExisting, warnings);
                    }
                }
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            string summary = $"[SaveAssetCatalogCollector] components: {scannedComponents}, asset-ref fields: {scannedFields}, added: {added}, already registered: {skippedExisting}.";
            if (warnings.Count > 0)
            {
                summary += $" warnings: {warnings.Count} (see earlier log).";
            }

            Debug.Log(summary);
        }

        /// <summary>
        /// 收集单个组件的全部资产引用字段值。
        /// </summary>
        private static void CollectComponent(SaveComponent component, SaveAssetCatalog catalog, ref int scannedFields, ref int added, ref int skippedExisting, List<string> warnings)
        {
            for (int i = 0; i < component.Targets.Count; i++)
            {
                SaveTargetBinding binding = component.Targets[i];
                if (binding?.Target == null)
                {
                    continue;
                }

                Type targetType = binding.Target.GetType();
                for (int j = 0; j < binding.EnabledFields.Count; j++)
                {
                    string fieldName = binding.EnabledFields[j];
                    FieldInfo field = FindField(targetType, fieldName);
                    if (field == null || !IsAssetReferenceType(field.FieldType))
                    {
                        continue;
                    }

                    scannedFields++;
                    var asset = field.GetValue(binding.Target) as UObject;
                    if (asset == null)
                    {
                        continue;
                    }

                    RegisterAsset(asset, catalog, component, fieldName, ref added, ref skippedExisting, warnings);
                }
            }
        }

        /// <summary>
        /// 登记单个资产（已登记跳过；场景对象/无法推导定位串仅告警）。
        /// </summary>
        private static void RegisterAsset(UObject asset, SaveAssetCatalog catalog, SaveComponent component, string fieldName, ref int added, ref int skippedExisting, List<string> warnings)
        {
            if (catalog.TryGetLocation(asset, out _))
            {
                skippedExisting++;
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
            {
                warnings.Add($"{component.name}.{fieldName}: '{asset.name}' 不是项目资产（场景对象/运行期实例），跳过。");
                Debug.LogWarning($"[SaveAssetCatalogCollector] '{asset.name}' on '{component.name}.{fieldName}' is not a project asset, skipped.");
                return;
            }

            // 文件名寻址约定（YooAsset AddressByFileName 收集规则）；项目自定义寻址须人工复核
            string location = Path.GetFileNameWithoutExtension(assetPath);
            catalog.m_Entries.Add(new SaveAssetCatalog.Entry { m_Asset = asset, m_Location = location });
            added++;
        }

        /// <summary>
        /// 判定资产引用字段类型（UnityEngine.Object 派生且非 GameObject/Component 派生——后者为场景引用通道）。
        /// </summary>
        private static bool IsAssetReferenceType(Type fieldType)
        {
            return typeof(UObject).IsAssignableFrom(fieldType)
                && fieldType != typeof(GameObject)
                && !typeof(Component).IsAssignableFrom(fieldType);
        }

        /// <summary>
        /// 沿类型链查找字段（含基类私有字段）。
        /// </summary>
        private static FieldInfo FindField(Type type, string fieldName)
        {
            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName, FLAGS | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }
    }
}
