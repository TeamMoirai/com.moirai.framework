using System;
using System.Collections.Generic;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 键与名称注册表——packed key 位域编解码，以及三条名称轴的取 id / 计数转交。
    /// <para>本文件只做"编解码 + 归一化"，登记与引用计数本身已并入 <see cref="ResourceNameRegistry{TValue}"/>；
    /// 三轴的 id 上限即 packed key 给该轴留的位宽上限，越界必抛而非静默截断——截断会让两条不同资源
    /// 编出同一个键。</para>
    /// </summary>
    partial class YooAssetHandler
    {
        #region Packed Key 编解码 [PACKED KEY ENCODE/DECODE]

        private static ulong PackResourceKey(int packageId, int locationId, int typeId,
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

        private static int UnpackPackageId(ulong key)
        {
            return (int)((key >> RESOURCE_KEY_PACKAGE_SHIFT) & RESOURCE_KEY_PACKAGE_MAX);
        }

        private static int UnpackLocationId(ulong key)
        {
            return (int)((key >> RESOURCE_KEY_LOCATION_SHIFT) & RESOURCE_KEY_LOCATION_MAX);
        }

        private static int UnpackTypeId(ulong key)
        {
            return (int)((key >> RESOURCE_KEY_TYPE_SHIFT) & RESOURCE_KEY_TYPE_MAX);
        }

        private ulong GetAssetRecordKey(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind)
        {
            int packageId = GetOrAddPackageId(packageName);
            int locationId = GetOrAddLocationId(location);
            int typeId = GetOrAddTypeId(assetType);
            return PackResourceKey(packageId, locationId, typeId, assetKind, handleKind);
        }

        private ulong GetLoadingOperationKey(string location, string packageName, Type assetType,
            EResourceAssetKind assetKind)
        {
            int packageId = GetOrAddPackageId(packageName);
            int locationId = GetOrAddLocationId(location);
            int typeId = GetOrAddTypeId(assetType);
            return PackResourceKey(packageId, locationId, typeId, assetKind, EResourceHandleKind.AssetHandle);
        }

        /// <summary>
        /// 非驻留取键：三条轴都已登记才拼得出键，任一轴缺席即返回 false 且不登记任何 id。
        /// </summary>
        private bool TryGetResourceKey(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind, out ulong key)
        {
            key = 0;
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            if (!_packageNames.TryGetId(NormalizePackageName(packageName), out int packageId) ||
                !_locationNames.TryGetId(location ?? string.Empty, out int locationId) ||
                !_typeNames.TryGetId(assetType, out int typeId))
            {
                return false;
            }

            key = PackResourceKey(packageId, locationId, typeId, assetKind, handleKind);
            return true;
        }

        #endregion
        #region 三条名称轴 [NAME AXES]

        private int GetOrAddPackageId(string packageName) =>
            _packageNames.GetOrAdd(NormalizePackageName(packageName));

        private int GetOrAddLocationId(string location) =>
            _locationNames.GetOrAdd(location ?? string.Empty);

        private int GetOrAddTypeId(Type assetType) =>
            _typeNames.GetOrAdd(assetType ?? typeof(UObject));

        private string GetPackageNameById(int id) => _packageNames.GetValue(id);

        private string GetLocationNameById(int id) => _locationNames.GetValue(id);

        private Type GetAssetTypeById(int id) => _typeNames.GetValue(id);

        /// <summary>记录一条资源建立时，把它用到的三个名字各计一次。</summary>
        private void RetainResourceKey(ulong key)
        {
            _packageNames.Retain(UnpackPackageId(key));
            _locationNames.Retain(UnpackLocationId(key));
            _typeNames.Retain(UnpackTypeId(key));
        }

        /// <summary>记录释放时反向减数；减到零的那条轴把名字摘掉并把 id 还回空闲栈。</summary>
        private void ReleaseResourceKey(ulong key)
        {
            _packageNames.Release(UnpackPackageId(key));
            _locationNames.Release(UnpackLocationId(key));
            _typeNames.Release(UnpackTypeId(key));
        }

        /// <summary>整表清空前逐键减数（如后端整体重置），不减则名字与 id 永久滞留。</summary>
        private void ReleaseAllResourceKeysFromMap(ResourceUlongIntMap map)
        {
            // 方法组缓存为实例委托字段，避免冷路径批量清理时逐次 new Action 分配。
            _releaseResourceKeysNoTrimCache ??= ReleaseResourceKeyNoTrim;
            map.ForEachKey(_releaseResourceKeysNoTrimCache);
        }

        [NonSerialized] private Action<ulong> _releaseResourceKeysNoTrimCache;

        private void ReleaseResourceKeyNoTrim(ulong key)
        {
            // 刻意不是 ReleaseResourceKey 的别名：整表清空时只该减数，
            // 摘字典与回收 id 在那些随后一并作废的表上是白做，而且会在遍历另一张表的键时
            // 反向改动本表的字典。原实现就是两条分开的路，合并注册表时不能顺手并掉。
            _packageNames.DecrementOnly(UnpackPackageId(key));
            _locationNames.DecrementOnly(UnpackLocationId(key));
            _typeNames.DecrementOnly(UnpackTypeId(key));
        }

        #endregion
        #region 归一化 [NORMALIZE]

        private string NormalizePackageName(string packageName)
        {
            return string.IsNullOrEmpty(packageName) ? DefaultPackageName : packageName;
        }

        private static EResourceAssetKind NormalizeAssetKind(Type assetType, EResourceAssetKind assetKind)
        {
            return assetKind == EResourceAssetKind.Unknown ? InferAssetKind(assetType) : assetKind;
        }

        private static Type NormalizeAssetType(Type assetType, EResourceAssetKind assetKind)
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

        private static EResourceAssetKind InferAssetKind(Type assetType)
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
