using System;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// packed resource key 的位域编解码，以及 assetKind / assetType 的互相归一。
    /// <para>纯静态、零状态：三条名称轴的登记在 <see cref="ResourceNameRegistry{TValue}"/>，
    /// 取 id 与计数在 handler 侧。这里只负责"三个 id 加两个枚举位怎么塞进一个 ulong、越界怎么拦"，
    /// 因此各轴 id 上限（即该轴位宽上限）也由它界定——越界必抛而非静默截断，截断会让两条不同资源
    /// 编出同一个键。</para>
    /// </summary>
    internal static class ResourceKeyCodec
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
            EResourceAssetKind assetKind, EResourceHandleKind handleKind)
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

        internal static EResourceAssetKind NormalizeAssetKind(Type assetType, EResourceAssetKind assetKind)
        {
            return assetKind == EResourceAssetKind.Unknown ? InferAssetKind(assetType) : assetKind;
        }

        internal static Type NormalizeAssetType(Type assetType, EResourceAssetKind assetKind)
        {
            if (assetKind == EResourceAssetKind.Sprite)
            {
                return typeof(Sprite);
            }

            if (assetKind == EResourceAssetKind.Material)
            {
                return typeof(Material);
            }

            if (assetKind == EResourceAssetKind.Prefab)
            {
                return typeof(GameObject);
            }

            if (assetKind == EResourceAssetKind.SubAssets)
            {
                return typeof(Sprite);
            }

            return assetType ?? typeof(UObject);
        }

        internal static EResourceAssetKind InferAssetKind(Type assetType)
        {
            if (assetType == typeof(Sprite))
            {
                return EResourceAssetKind.Sprite;
            }

            if (assetType == typeof(Material))
            {
                return EResourceAssetKind.Material;
            }

            if (assetType == typeof(GameObject))
            {
                return EResourceAssetKind.Prefab;
            }

            return EResourceAssetKind.Asset;
        }

        #endregion
    }
}
