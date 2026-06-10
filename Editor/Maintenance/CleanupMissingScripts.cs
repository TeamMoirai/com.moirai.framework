using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 清理所选 GameObjects 上所有缺失的脚本
    /// </summary>
    public static class CleanupMissingScripts
    {
        /// <summary>
        /// 清理所选 GameObjects 上所有缺失的脚本
        /// </summary>
        [MenuItem("Tools/资产相关/清理所选 GameObjects 上所有缺失的脚本", false, 503)]
        public static void ProcessCleaning()
        {
            Object[] collectedDeepHierarchy = EditorUtility.CollectDeepHierarchy(Selection.gameObjects);
            int removedComponentsCounter = 0;
            int gameobjectsAffectedCounter = 0;
            foreach (Object targetObject in collectedDeepHierarchy)
            {
                if (targetObject is GameObject gameObject)
                {
                    int amountOfMissingScripts = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                    if (amountOfMissingScripts > 0)
                    {
                        Undo.RegisterCompleteObjectUndo(gameObject, "Removing missing scripts");
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
                        removedComponentsCounter += amountOfMissingScripts;
                        gameobjectsAffectedCounter++;
                    }
                }
            }
            Log.Info("[CleanupMissingScripts] 从 " + gameobjectsAffectedCounter + " 移除了 " + removedComponentsCounter + " 个缺失的脚本。");
        }
    }
}
