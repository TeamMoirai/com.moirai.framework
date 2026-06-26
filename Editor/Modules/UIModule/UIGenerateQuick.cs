using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    public static class UIGenerateQuick
    {
        [MenuItem("GameObject/ScriptGenerator/生成绑定代码", priority = 50)]
        public static void UIGenerateBind()
        {
            GameObject selectedObject = Selection.gameObjects.FirstOrDefault();
            if (selectedObject == null)
            {
                Debug.LogError("没有选中物体!");
                return;
            }

            var uiScriptConfigs = UIGeneratorSettings.UIScriptGenerateConfigs;
            if (uiScriptConfigs == null || uiScriptConfigs.Count == 0)
            {
                Debug.Log("没有UI生成配置 请前往UISettingWindow添加");
                return;
            }

            foreach (var config in uiScriptConfigs)
            {
                if (CheckCanGenerate(selectedObject, config))
                {
                    UIScriptGeneratorHelper.GenerateUIBindScript(selectedObject, config);
                    return;
                }
            }

            Debug.Log("没有找到符合规则路径的生成配置 请检查!");
        }

        [MenuItem("GameObject/ScriptGenerator/仅复制属性", priority = 51)]
        public static void UICopyBindVariableContent()
        {
            GameObject selectedObject = Selection.gameObjects.FirstOrDefault();
            if (selectedObject == null)
            {
                Debug.LogError("没有选中物体!");
                return;
            }

            UIScriptGeneratorHelper.CopyVariableContentToClipboard(selectedObject);
        }

        public static bool CheckCanGenerate(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null || scriptGenerateData == null) return false;

            string assetPath = GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return false; // 不在 Assets 下

            assetPath = assetPath.Replace('\\', '/');
            bool result = assetPath.StartsWith(scriptGenerateData.UIPrefabRootPath, StringComparison.OrdinalIgnoreCase);
            return result;
        }

        public static string GetPrefabAssetPath(GameObject go)
        {
            var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (prefabAsset != null)
                return AssetDatabase.GetAssetPath(prefabAsset);

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.IsPartOfPrefabContents(go))
                return prefabStage.assetPath;

            return null;
        }
    }
}