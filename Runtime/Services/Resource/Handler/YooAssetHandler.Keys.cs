using System;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 键与名称注册表——packed key 位域编解码、package / location / type 计数字典与归一化。
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

        private bool TryGetResourceKey(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind, out ulong key)
        {
            key = 0;
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            if (!_resourcePackageIds.TryGetValue(NormalizePackageName(packageName), out int packageId) ||
                !_resourceLocationIds.TryGetValue(location ?? string.Empty, out int locationId) ||
                !_resourceTypeIds.TryGetValue(assetType, out int typeId))
            {
                return false;
            }

            key = PackResourceKey(packageId, locationId, typeId, assetKind, handleKind);
            return true;
        }

        private int GetOrAddPackageId(string packageName)
        {
            packageName = NormalizePackageName(packageName);
            if (_resourcePackageIds.TryGetValue(packageName, out int id))
            {
                return id;
            }

            id = AllocateResourceId(ref _nextPackageId, RESOURCE_KEY_PACKAGE_MAX, _freePackageIds);
            _resourcePackageIds.Add(packageName, id);
            EnsureResourceNameSlot(ref _resourcePackagesById, ref _resourcePackageRefCounts, id);
            _resourcePackagesById[id] = packageName;
            return id;
        }

        private int GetOrAddLocationId(string location)
        {
            location ??= string.Empty;
            if (_resourceLocationIds.TryGetValue(location, out int id))
            {
                return id;
            }

            id = AllocateResourceId(ref _nextLocationId, RESOURCE_KEY_LOCATION_MAX, _freeLocationIds);
            _resourceLocationIds.Add(location, id);
            EnsureResourceNameSlot(ref _resourceLocationsById, ref _resourceLocationRefCounts, id);
            _resourceLocationsById[id] = location;
            return id;
        }

        private int GetOrAddTypeId(Type assetType)
        {
            assetType ??= typeof(UObject);
            if (_resourceTypeIds.TryGetValue(assetType, out int id))
            {
                return id;
            }

            id = AllocateResourceId(ref _nextTypeId, RESOURCE_KEY_TYPE_MAX, _freeTypeIds);
            _resourceTypeIds.Add(assetType, id);
            EnsureResourceTypeSlot(id);
            _resourceTypesById[id] = assetType;
            return id;
        }

        private static int AllocateResourceId(ref int nextId, int maxId, System.Collections.Generic.Stack<int> freeIds)
        {
            while (freeIds != null && freeIds.Count > 0)
            {
                int freeId = freeIds.Pop();
                if (freeId > 0 && freeId <= maxId)
                {
                    return freeId;
                }
            }

            if (nextId <= 0 || nextId > maxId)
            {
                throw new GameException("Resource key id range exceeded.");
            }

            return nextId++;
        }

        private static void EnsureResourceNameSlot(ref string[] values, ref int[] refCounts, int id)
        {
            EnsureResourceArray(ref values, id);
            EnsureResourceArray(ref refCounts, id);
        }

        private void EnsureResourceTypeSlot(int id)
        {
            EnsureResourceArray(ref _resourceTypesById, id);
            EnsureResourceArray(ref _resourceTypeRefCounts, id);
        }

        private static void EnsureResourceArray<T>(ref T[] array, int index)
        {
            if (array == null)
            {
                array = new T[Math.Max(16, index + 1)];
                return;
            }

            if (index < array.Length)
            {
                return;
            }

            Array.Resize(ref array, Math.Max(index + 1, array.Length << 1));
        }

        private string GetPackageNameById(int id)
        {
            return _resourcePackagesById != null && id > 0 && id < _resourcePackagesById.Length
                ? _resourcePackagesById[id]
                : string.Empty;
        }

        private string GetLocationNameById(int id)
        {
            return _resourceLocationsById != null && id > 0 && id < _resourceLocationsById.Length
                ? _resourceLocationsById[id]
                : string.Empty;
        }

        private Type GetAssetTypeById(int id)
        {
            return _resourceTypesById != null && id > 0 && id < _resourceTypesById.Length
                ? _resourceTypesById[id]
                : null;
        }

        private void RetainResourceKey(ulong key)
        {
            IncrementResourceRef(_resourcePackageRefCounts, UnpackPackageId(key));
            IncrementResourceRef(_resourceLocationRefCounts, UnpackLocationId(key));
            IncrementResourceRef(_resourceTypeRefCounts, UnpackTypeId(key));
        }

        private void ReleaseResourceKey(ulong key)
        {
            ReleasePackageId(UnpackPackageId(key));
            ReleaseLocationId(UnpackLocationId(key));
            ReleaseTypeId(UnpackTypeId(key));
        }

        private void ReleaseAllResourceKeysFromMap(ResourceUlongIntMap map)
        {
            // 方法组缓存为实例委托字段，避免冷路径批量清理时逐次 new Action 分配。
            _releaseResourceKeysNoTrimCache ??= ReleaseResourceKeyNoTrim;
            map.ForEachKey(_releaseResourceKeysNoTrimCache);
        }

        private Action<ulong> _releaseResourceKeysNoTrimCache;

        private void ReleaseResourceKeyNoTrim(ulong key)
        {
            DecrementResourceRef(_resourcePackageRefCounts, UnpackPackageId(key));
            DecrementResourceRef(_resourceLocationRefCounts, UnpackLocationId(key));
            DecrementResourceRef(_resourceTypeRefCounts, UnpackTypeId(key));
        }

        private void ReleasePackageId(int id)
        {
            if (!DecrementResourceRef(_resourcePackageRefCounts, id))
            {
                return;
            }

            string value = id < _resourcePackagesById.Length ? _resourcePackagesById[id] : null;
            if (value != null)
            {
                _resourcePackageIds.Remove(value);
                _resourcePackagesById[id] = null;
                _freePackageIds.Push(id);
            }
        }

        private void ReleaseLocationId(int id)
        {
            if (!DecrementResourceRef(_resourceLocationRefCounts, id))
            {
                return;
            }

            string value = id < _resourceLocationsById.Length ? _resourceLocationsById[id] : null;
            if (value != null)
            {
                _resourceLocationIds.Remove(value);
                _resourceLocationsById[id] = null;
                _freeLocationIds.Push(id);
            }
        }

        private void ReleaseTypeId(int id)
        {
            if (!DecrementResourceRef(_resourceTypeRefCounts, id))
            {
                return;
            }

            Type value = id < _resourceTypesById.Length ? _resourceTypesById[id] : null;
            if (value != null)
            {
                _resourceTypeIds.Remove(value);
                _resourceTypesById[id] = null;
                _freeTypeIds.Push(id);
            }
        }

        private static void IncrementResourceRef(int[] refCounts, int id)
        {
            if (refCounts == null || id <= 0 || id >= refCounts.Length)
            {
                return;
            }

            refCounts[id]++;
        }

        private static bool DecrementResourceRef(int[] refCounts, int id)
        {
            if (refCounts == null || id <= 0 || id >= refCounts.Length || refCounts[id] <= 0)
            {
                return false;
            }

            refCounts[id]--;
            return refCounts[id] == 0;
        }

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
