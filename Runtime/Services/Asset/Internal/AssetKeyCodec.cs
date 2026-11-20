using System;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Asset
{
    /// <summary>
    /// packed resource key 的位域编解码，以及 assetKind / assetType 的互相归一（纯静态、零状态）。
    /// </summary>
    /// <remarks>
    /// 三条名称轴的登记在 <see cref="AssetNameRegistry{TValue}"/>，取 id 与计数在 handler 侧。 <br />
    /// 各轴 id 上限（即该轴位宽上限）由本类界定，越界必抛而非静默截断。
    /// </remarks>
    internal static class AssetKeyCodec
    {
        #region 位域布局 [BIT FIELDS]

        internal const int RESOURCE_KEY_HANDLE_BITS = 4;
        internal const int RESOURCE_KEY_ASSET_KIND_BITS = 4;
        internal const int RESOURCE_KEY_TYPE_BITS = 12;
        internal const int RESOURCE_KEY_LOCATION_BITS = 32;
        internal const int RESOURCE_KEY_PACKAGE_BITS = 12;

        internal const int RESOURCE_KEY_HANDLE_SHIFT = 0;
        internal const int RESOURCE_KEY_ASSET_KIND_SHIFT = RESOURCE_KEY_HANDLE_SHIFT + RESOURCE_KEY_HANDLE_BITS;
        internal const int RESOURCE_KEY_TYPE_SHIFT = RESOURCE_KEY_ASSET_KIND_SHIFT + RESOURCE_KEY_ASSET_KIND_BITS;
        internal const int RESOURCE_KEY_LOCATION_SHIFT = RESOURCE_KEY_TYPE_SHIFT + RESOURCE_KEY_TYPE_BITS;
        internal const int RESOURCE_KEY_PACKAGE_SHIFT = RESOURCE_KEY_LOCATION_SHIFT + RESOURCE_KEY_LOCATION_BITS;

        internal const int RESOURCE_KEY_PACKAGE_MAX = (1 << RESOURCE_KEY_PACKAGE_BITS) - 1;
        internal const int RESOURCE_KEY_LOCATION_MAX = int.MaxValue;
        internal const int RESOURCE_KEY_TYPE_MAX = (1 << RESOURCE_KEY_TYPE_BITS) - 1;
        internal const int RESOURCE_KEY_ASSET_KIND_MAX = (1 << RESOURCE_KEY_ASSET_KIND_BITS) - 1;
        internal const int RESOURCE_KEY_HANDLE_MAX = (1 << RESOURCE_KEY_HANDLE_BITS) - 1;

        #endregion
        
        #region 编解码 [ENCODE/DECODE]

        internal static ulong Pack(int packageId, int locationId, int typeId,
            EAssetKind assetKind, EAssetHandleKind handleKind)
        {
            if (packageId <= 0 || locationId <= 0 || typeId <= 0 ||
                packageId > RESOURCE_KEY_PACKAGE_MAX ||
                locationId > RESOURCE_KEY_LOCATION_MAX ||
                typeId > RESOURCE_KEY_TYPE_MAX ||
                (uint)assetKind > RESOURCE_KEY_ASSET_KIND_MAX ||
                (uint)handleKind > RESOURCE_KEY_HANDLE_MAX)
            {
                throw new GameException("Resource key id range exceeded.");
            }

            return ((ulong)(uint)packageId << RESOURCE_KEY_PACKAGE_SHIFT) |
                   ((ulong)(uint)locationId << RESOURCE_KEY_LOCATION_SHIFT) |
                   ((ulong)(uint)typeId << RESOURCE_KEY_TYPE_SHIFT) |
                   ((ulong)(byte)assetKind << RESOURCE_KEY_ASSET_KIND_SHIFT) |
                   ((ulong)(byte)handleKind << RESOURCE_KEY_HANDLE_SHIFT);
        }

        internal static int UnpackPackageId(ulong key)
        {
            return (int)((key >> RESOURCE_KEY_PACKAGE_SHIFT) & RESOURCE_KEY_PACKAGE_MAX);
        }

        internal static int UnpackLocationId(ulong key)
        {
            return (int)((key >> RESOURCE_KEY_LOCATION_SHIFT) & RESOURCE_KEY_LOCATION_MAX);
        }

        internal static int UnpackTypeId(ulong key)
        {
            return (int)((key >> RESOURCE_KEY_TYPE_SHIFT) & RESOURCE_KEY_TYPE_MAX);
        }

        #endregion
        
        #region 归一化 [NORMALIZE]

        internal static EAssetKind NormalizeAssetKind(Type assetType, EAssetKind assetKind)
        {
            return assetKind == EAssetKind.Unknown ? InferAssetKind(assetType) : assetKind;
        }

        internal static Type NormalizeAssetType(Type assetType, EAssetKind assetKind)
        {
            if (assetKind == EAssetKind.Sprite)
            {
                return typeof(Sprite);
            }

            if (assetKind == EAssetKind.Material)
            {
                return typeof(Material);
            }

            if (assetKind == EAssetKind.Prefab)
            {
                return typeof(GameObject);
            }

            if (assetKind == EAssetKind.SubAssets)
            {
                return typeof(Sprite);
            }

            return assetType ?? typeof(UObject);
        }

        internal static EAssetKind InferAssetKind(Type assetType)
        {
            if (assetType == typeof(Sprite))
            {
                return EAssetKind.Sprite;
            }

            if (assetType == typeof(Material))
            {
                return EAssetKind.Material;
            }

            if (assetType == typeof(GameObject))
            {
                return EAssetKind.Prefab;
            }

            return EAssetKind.Asset;
        }

        #endregion
    }
}
