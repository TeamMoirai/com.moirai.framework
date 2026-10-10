#if ADDRESSABLES_INSTALLED
using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// Addressables 后端的查询面：定位解析、资产信息与存在性判断——一切从唯一的隐式目录来。
    /// </summary>
    /// <remarks>OnDisk / Online 之分要异步的 <c>GetDownloadSize</c> 才答得出，同步族退化为恒定值或 fail-fast。</remarks>
    partial class AddressableHandler
    {
        #region 定位 [LOCATE]

        /// <summary>
        /// 按 key 同步定位，是 Addressables 唯一的同步查询面。
        /// </summary>
        /// <remarks>地址在初始化后常驻；类型维度传 <c>null</c> 表示任意类型，传具体类型会把同一地址的其它导入项判成不存在。</remarks>
        private static bool TryLocate(string location, out IList<IResourceLocation> locations)
        {
            locations = null;
            if (string.IsNullOrEmpty(location) || Addressables.ResourceLocators == null)
            {
                return false;
            }

            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                if (locator.Locate(location, null, out IList<IResourceLocation> found) && found != null &&
                    found.Count > 0)
                {
                    locations = found;
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override bool IsLocationValid(string location, string packageName = "")
        {
            return TryLocate(location, out _);
        }

        /// <inheritdoc />
        /// <remarks>Addressables 原生以 GUID 为 key（自家 AssetReference 的 RuntimeKey 就是 GUID），解析成功时定位地址即 GUID 本身。</remarks>
        public override bool TryGetLocationByGuid(string guid, out string location, string packageName = "")
        {
            location = null;
            if (string.IsNullOrEmpty(guid) || !TryLocate(guid, out _))
            {
                return false;
            }

            location = guid;
            return true;
        }

        /// <inheritdoc />
        /// <remarks>只能答"有没有这条地址"：OnDisk / Online 的分别要 <c>GetDownloadSizeAsync</c>，
        /// 那是异步的，同步问不出来。命中一律回 AssetOnDisk，不当"已在本地"的保证用。 <br />
        /// 要精确判断请走 <c>IsNeedDownloadFromRemote</c> / <c>GetDownloadSize</c>（本后端保持 fail-fast）。</remarks>
        public override EResourceHasAssetResult HasAsset(string location, string packageName = "")
        {
            return TryLocate(location, out _)
                ? EResourceHasAssetResult.AssetOnDisk
                : EResourceHasAssetResult.NotExist;
        }

        #endregion

        #region 获取资源信息 [GET ASSET INFOS]

        /// <inheritdoc />
        public override long GetDownloadSize(string location, string packageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override bool IsNeedDownloadFromRemote(string location, string packageName = "")
        {
            return false;
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry[] GetAssetInfos(string tag, string packageName = "")
        {
            return Array.Empty<ResourceAssetInfoEntry>();
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry[] GetAssetInfos(string[] tags, string packageName = "")
        {
            return Array.Empty<ResourceAssetInfoEntry>();
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry GetAssetInfo(string location, string packageName = "")
        {
            return default;
        }

        #endregion
    }
}
#endif
