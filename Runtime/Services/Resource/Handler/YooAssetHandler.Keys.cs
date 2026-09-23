using System;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 键与名称注册表——三条名称轴的取 id / 计数转交，以及包名归一。
    /// <para>位域怎么排、越界怎么拦在 <see cref="ResourceKeyCodec"/>；登记与引用计数本身在
    /// <see cref="ResourceNameRegistry{TValue}"/>。本文件只剩把两者接起来的那几行转发。</para>
    /// </summary>
    partial class YooAssetHandler
    {
        #region 取键 [KEY BUILDING]

        private ulong GetAssetRecordKey(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind)
        {
            int packageId = GetOrAddPackageId(packageName);
            int locationId = GetOrAddLocationId(location);
            int typeId = GetOrAddTypeId(assetType);
            return ResourceKeyCodec.Pack(packageId, locationId, typeId, assetKind, handleKind);
        }

        private ulong GetLoadingOperationKey(string location, string packageName, Type assetType,
            EResourceAssetKind assetKind)
        {
            int packageId = GetOrAddPackageId(packageName);
            int locationId = GetOrAddLocationId(location);
            int typeId = GetOrAddTypeId(assetType);
            return ResourceKeyCodec.Pack(packageId, locationId, typeId, assetKind, EResourceHandleKind.AssetHandle);
        }

        /// <summary>
        /// 非驻留取键：三条轴都已登记才拼得出键，任一轴缺席即返回 false 且不登记任何 id。
        /// </summary>
        private bool TryGetResourceKey(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind, out ulong key)
        {
            key = 0;
            assetKind = ResourceKeyCodec.NormalizeAssetKind(assetType, assetKind);
            assetType = ResourceKeyCodec.NormalizeAssetType(assetType, assetKind);
            if (!_packageNames.TryGetId(NormalizePackageName(packageName), out int packageId) ||
                !_locationNames.TryGetId(location ?? string.Empty, out int locationId) ||
                !_typeNames.TryGetId(assetType, out int typeId))
            {
                return false;
            }

            key = ResourceKeyCodec.Pack(packageId, locationId, typeId, assetKind, handleKind);
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
            _packageNames.Retain(ResourceKeyCodec.UnpackPackageId(key));
            _locationNames.Retain(ResourceKeyCodec.UnpackLocationId(key));
            _typeNames.Retain(ResourceKeyCodec.UnpackTypeId(key));
        }

        /// <summary>记录释放时反向减数；减到零的那条轴把名字摘掉并把 id 还回空闲栈。</summary>
        private void ReleaseResourceKey(ulong key)
        {
            _packageNames.Release(ResourceKeyCodec.UnpackPackageId(key));
            _locationNames.Release(ResourceKeyCodec.UnpackLocationId(key));
            _typeNames.Release(ResourceKeyCodec.UnpackTypeId(key));
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
            _packageNames.DecrementOnly(ResourceKeyCodec.UnpackPackageId(key));
            _locationNames.DecrementOnly(ResourceKeyCodec.UnpackLocationId(key));
            _typeNames.DecrementOnly(ResourceKeyCodec.UnpackTypeId(key));
        }

        #endregion
        #region 归一化 [NORMALIZE]

        private string NormalizePackageName(string packageName)
        {
            return string.IsNullOrEmpty(packageName) ? DefaultPackageName : packageName;
        }

        #endregion
    }
}
