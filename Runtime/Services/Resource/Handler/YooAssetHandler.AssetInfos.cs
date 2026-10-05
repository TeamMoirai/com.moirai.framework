using System;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// YooAsset 后端的资源信息查询面：下载体量、按标签/按址取资产信息、存在性与 GUID 定位解析。
    /// </summary>
    /// <remarks>资产信息缓存以"清单是否在更新"为门控：更新期间直查，避免命中过期的负缓存。</remarks>
    partial class YooAssetHandler
    {
        #region 获取资源信息 [GET ASSET INFOS]

        /// <inheritdoc />
        public override bool IsNeedDownloadFromRemote(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetDownloadSize(location) > 0;
        }

        /// <inheritdoc />
        public override long GetDownloadSize(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetDownloadSize(location);
        }

        /// <summary>
        /// 将 YooAsset AssetInfo 数组转换为框架资源信息数组。
        /// </summary>
        private static ResourceAssetInfoEntry[] ConvertAssetInfos(AssetInfo[] infos)
        {
            if (infos == null || infos.Length == 0) return Array.Empty<ResourceAssetInfoEntry>();
            var entries = new ResourceAssetInfoEntry[infos.Length];
            for (int i = 0; i < infos.Length; i++)
            {
                entries[i] = ConvertAssetInfo(infos[i]);
            }
            return entries;
        }

        /// <summary>
        /// 将 YooAsset AssetInfo 转换为框架资源信息。
        /// </summary>
        private static ResourceAssetInfoEntry ConvertAssetInfo(AssetInfo info)
        {
            return new ResourceAssetInfoEntry
            {
                // YooAsset 3.x 清单恒以 AssetPath 注册定位，可寻址地址（Address）可能未分配而为空；
                // Location 优先取地址、空时回退资产路径，两者均可被 ConvertLocationToAssetInfo 解析。
                Location = string.IsNullOrEmpty(info.Address) ? info.AssetPath : info.Address,
                TypeName = info.AssetType?.Name,
            };
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry[] GetAssetInfos(string tag, string packageName = "")
        {
            return ConvertAssetInfos(GetPackageOrThrow(packageName).GetAssetInfos(tag));
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry[] GetAssetInfos(string[] tags, string packageName = "")
        {
            return ConvertAssetInfos(GetPackageOrThrow(packageName).GetAssetInfos(tags));
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry GetAssetInfo(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            AssetInfo yooAssetInfo;
            bool cacheEnabled = !IsManifestUpdateInProgress();
            if (string.IsNullOrEmpty(packageName))
            {
                if (cacheEnabled && _assetInfoMap.TryGetValue(location, out AssetInfo cachedAssetInfo))
                {
                    return ConvertAssetInfo(cachedAssetInfo);
                }

                yooAssetInfo = DefaultPackage.GetAssetInfo(location);
                if (cacheEnabled && CanCacheAssetInfo(yooAssetInfo))
                {
                    _assetInfoMap[location] = yooAssetInfo;
                }

                return ConvertAssetInfo(yooAssetInfo);
            }

            string key = StringUtility.Concat(packageName, "/", location);
            if (cacheEnabled && _assetInfoMap.TryGetValue(key, out AssetInfo pkgCachedAssetInfo))
            {
                return ConvertAssetInfo(pkgCachedAssetInfo);
            }

            var package = GetPackageOrThrow(packageName);
            yooAssetInfo = package.GetAssetInfo(location);
            if (cacheEnabled && CanCacheAssetInfo(yooAssetInfo))
            {
                _assetInfoMap[key] = yooAssetInfo;
            }

            return ConvertAssetInfo(yooAssetInfo);
        }

        private static bool CanCacheAssetInfo(AssetInfo assetInfo)
        {
            // 负缓存门控：无效的 AssetInfo 不缓存，避免清单更新或加载时序变化后命中过期负结果。
            return assetInfo != null && assetInfo.IsValid && string.IsNullOrEmpty(assetInfo.Error);
        }

        /// <inheritdoc />
        public override EResourceHasAssetResult HasAsset(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            var package = GetPackageOrThrow(packageName);
            AssetInfo assetInfo = package.GetAssetInfo(location);
            if (assetInfo == null || !assetInfo.IsValid || !string.IsNullOrEmpty(assetInfo.Error))
            {
                return EResourceHasAssetResult.NotExist;
            }

            if (package.GetDownloadSize(location) > 0)
            {
                return EResourceHasAssetResult.AssetOnline;
            }

            return EResourceHasAssetResult.AssetOnDisk;
        }

        /// <inheritdoc />
        public override bool IsLocationValid(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).IsLocationValid(location);
        }

        /// <inheritdoc />
        /// <remarks>YooAsset 后端要求收集器设置勾选 IncludeAssetGUID，清单按 GUID 建映射后本查询才生效。</remarks>
        public override bool TryGetLocationByGuid(string guid, out string location, string packageName = "")
        {
            location = null;
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }

            string normalizedPackageName = Store.NormalizePackageName(packageName);
            ResourcePackage package = YooAssets.GetPackage(normalizedPackageName);
            if (package == null)
            {
                return false;
            }

            AssetInfo assetInfo = package.GetAssetInfoByGuid(guid);
            if (assetInfo == null || !assetInfo.IsValid || !string.IsNullOrEmpty(assetInfo.Error))
            {
                return false;
            }

            location = assetInfo.AssetPath;
            return true;
        }

        #endregion
    }
}
