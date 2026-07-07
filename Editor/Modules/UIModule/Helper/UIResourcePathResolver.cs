using System;
using System.IO;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    /// <summary>
    /// 资源路径解析器接口，定义UI资源路径的解析规则
    /// </summary>
    public interface IUIResourcePathResolver
    {
        /// <summary>
        /// 获取资源路径
        /// </summary>
        string GetResourcePath(GameObject targetObject, UIScriptGenerateData scriptGenerateData);

        /// <summary>
        /// 判断是否可以生成
        /// </summary>
        bool CanGenerate(GameObject targetObject, UIScriptGenerateData scriptGenerateData);
    }

    /// <summary>
    /// 默认资源路径解析器实现
    /// </summary>
    public sealed class DefaultUIResourcePathResolver : IUIResourcePathResolver
    {
        /// <inheritdoc/>
        public string GetResourcePath(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null)
            {
                return $"\"{nameof(targetObject)}\"";
            }

            var defaultPath = targetObject.name;
            var assetPath = UIGenerateQuick.GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return defaultPath;
            }

            assetPath = assetPath.Replace('\\', '/');

            return scriptGenerateData.FromResources ?
                GetResourcesPath(assetPath, scriptGenerateData, defaultPath) :
                GetAssetBundlePath(assetPath, scriptGenerateData, defaultPath);
        }

        /// <inheritdoc/>
        public bool CanGenerate(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null || scriptGenerateData == null)
            {
                return false;
            }

            var assetPath = UIGenerateQuick.GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            assetPath = assetPath.Replace('\\', '/');
            var isValidPath = assetPath.StartsWith(scriptGenerateData.UIPrefabRootPath, StringComparison.OrdinalIgnoreCase);
            if (!isValidPath)
            {
                Debug.LogWarning($"UI asset path does not match UIGenerateConfiguration.UIPrefabRootPath.\n[AssetPath]{assetPath}\n[ConfigPath]{scriptGenerateData.UIPrefabRootPath}");
            }

            return isValidPath;
        }

        private static string GetResourcesPath(string assetPath, UIScriptGenerateData scriptGenerateData, string defaultPath)
        {
            var resourcesRoot = scriptGenerateData.UIPrefabRootPath;
            var relPath = GetResourcesRelativePath(assetPath, resourcesRoot);
            if (relPath == null)
            {
                Debug.LogWarning($"[UI Generate] Resource {assetPath} is not under configured Resources root: {resourcesRoot}");
                return defaultPath;
            }

            return relPath;
        }

        private static string GetAssetBundlePath(string assetPath, UIScriptGenerateData scriptGenerateData, string defaultPath)
        {
            try
            {
                var defaultPackage = YooAsset.Editor.AssetBundleCollectorSettingData.Setting.GetPackage("DefaultPackage");
                if (defaultPackage?.EnableAddressable == true)
                {
                    return defaultPath;
                }
            }
            catch
            {
                // ignored
            }

            var bundleRoot = scriptGenerateData.UIPrefabRootPath;
            if (!assetPath.StartsWith(bundleRoot, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[UI Generate] Resource {assetPath} is not under configured AssetBundle root: {bundleRoot}");
                return defaultPath;
            }

            return Path.ChangeExtension(assetPath, null);
        }

        private static string GetResourcesRelativePath(string assetPath, string resourcesRoot)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(resourcesRoot))
            {
                return null;
            }

            assetPath = assetPath.Replace('\\', '/');
            resourcesRoot = resourcesRoot.Replace('\\', '/');

            if (!assetPath.StartsWith(resourcesRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relPath = assetPath.Substring(resourcesRoot.Length).TrimStart('/');
            return Path.ChangeExtension(relPath, null);
        }
    }
}