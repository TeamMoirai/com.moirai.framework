using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    /// <summary>
    /// Prefab检查工具，提供Prefab状态判断方法
    /// </summary>
    public static class PrefabChecker
    {
        /// <summary>
        /// 判断是否正在编辑Prefab资产
        /// </summary>
        public static bool IsEditingPrefabAsset(GameObject go)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            return prefabStage?.IsPartOfPrefabContents(go) == true;
        }

        /// <summary>
        /// 判断是否为Prefab资产（包括Variant、Model或正在编辑的Prefab）
        /// </summary>
        public static bool IsPrefabAsset(GameObject go)
        {
            if (go == null) return false;

            var assetType = PrefabUtility.GetPrefabAssetType(go);
            var isRegularPrefab = assetType == PrefabAssetType.Regular ||
                                  assetType == PrefabAssetType.Variant ||
                                  assetType == PrefabAssetType.Model;

            return isRegularPrefab || IsEditingPrefabAsset(go);
        }
    }
}