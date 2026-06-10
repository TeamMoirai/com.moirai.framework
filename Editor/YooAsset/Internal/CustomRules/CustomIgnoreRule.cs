using UnityEditor;

namespace YooAsset.Editor
{
    public class CustomNormalIgnoreRule : IIgnoreRule
    {
        /// <summary>
        /// 查询是否为忽略文件
        /// </summary>
        /// <remarks><see cref="NormalIgnoreRule"/></remarks>
        public bool IsIgnore(AssetInfo assetInfo)
        {
            if (assetInfo.AssetPath.StartsWith("Assets/") == false && assetInfo.AssetPath.StartsWith("Packages/") == false)
            {
                UnityEngine.Debug.LogError($"Invalid asset path : {assetInfo.AssetPath}");
                return true;
            }

            // 忽略文件夹
            if (AssetDatabase.IsValidFolder(assetInfo.AssetPath))
                return true;

            // 忽略编辑器图标资源
            if (assetInfo.AssetPath.Contains("/Gizmos/"))
                return true;

            // 忽略编辑器专属资源
            if (assetInfo.AssetPath.Contains("/Editor/") || assetInfo.AssetPath.Contains("/Editor Resources/"))
                return true;
            
            // 忽略编辑器下的类型资源
            if (assetInfo.AssetType == typeof(LightingDataAsset))
                return true;
            if (assetInfo.AssetType == typeof(LightmapParameters))
                return true;

            // 忽略Unity引擎无法识别的文件
            if (assetInfo.AssetType == typeof(UnityEditor.DefaultAsset))
            {
                UnityEngine.Debug.LogWarning($"Cannot pack default asset : {assetInfo.AssetPath}");
                return true;
            }
            
            // 忽略 _Preset 资源
            if (assetInfo.AssetPath.Contains("/_Preset/"))
                return true;
            
            return DefaultIgnoreRule.IgnoreFileExtensions.Contains(assetInfo.FileExtension);
        }
    }
}