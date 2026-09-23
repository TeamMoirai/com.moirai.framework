using System;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源记录内核的键空间——三条名称轴（package / location / type）的登记、packed key 的组装
    /// 与拆解，以及"这条键还有没有人认得"的引用计数。
    /// <para>位域布局与 assetKind / assetType 归一在 <see cref="ResourceKeyCodec"/>（纯静态）；
    /// 单条轴的登记与回收在 <see cref="ResourceNameRegistry{TValue}"/>（每轴一份）；这里只剩把两者
    /// 接起来的那几行。</para>
    /// <para>与后端之间只有 <paramref name="defaultPackageName"/> 这一根活的连线：包名是可写接缝
    /// （<c>ResourceService.DefaultPackageName</c> 的 setter 运行期可达），构造时捕获值会永远读到旧包名，
    /// 所以按 <see cref="Func{TResult}"/> 注入、每次活读。后端算子（校验与释放原生句柄）之后要搬进来时
    /// 再立接口，为一根线立一个类型不值。</para>
    /// </summary>
    internal sealed partial class ResourceRecordKernel
    {
        // 三套分页 slot 数组共用的页布局：一页 256 槽、按位取页号与页内下标。
        // 另外两座 arena（资产记录 / 租约）仍在 handler 里，它们这里按类型名取用本组常量。
        internal const int RECORD_PAGE_BITS = 8;
        internal const int RECORD_PAGE_SIZE = 1 << RECORD_PAGE_BITS;
        internal const int RECORD_PAGE_MASK = RECORD_PAGE_SIZE - 1;

        private readonly IResourceRecordKernelHost _host;
        private readonly Func<string> _defaultPackageName;

        private IResourceRecordKernelHost Host => _host;

        // 三条轴各一份注册表。id 上限即该轴在 packed key 里分到的位宽上限，越界必抛而非截断。
        // 本类不标 [Serializable]，注册表内部那些数组与计数表天然整棵子树不参与序列化。
        private readonly ResourceNameRegistry<string> _packageNames =
            new ResourceNameRegistry<string>(ResourceKeyCodec.RESOURCE_KEY_PACKAGE_MAX, string.Empty);
        private readonly ResourceNameRegistry<string> _locationNames =
            new ResourceNameRegistry<string>(ResourceKeyCodec.RESOURCE_KEY_LOCATION_MAX, string.Empty);
        private readonly ResourceNameRegistry<Type> _typeNames =
            new ResourceNameRegistry<Type>(ResourceKeyCodec.RESOURCE_KEY_TYPE_MAX, null);

        // 卸载世代与销毁标记：判"这条记录、这次在途加载还作不作数"的依据。语义上归内核，
        // 写它的仍是 handler 的生命周期（初始化、强卸载、关停）。
        internal bool IsDestroying;
        internal uint UnloadGeneration = 1;

        // 在途加载去重：一座 arena + 一张 packed key -> 槽号的开地址表。
        // [NonSerialized] 逐字段保留（与 handler 侧运行时数组同一口径）：托管引用序列化对私有
        // 字段的处理不在我实测过的范围内，这里省一对标注不值得拿序列化边界去赌。
        [NonSerialized] private LoadingOperationSlot[][] _loadingOperationSlotPages;
        [NonSerialized] private int _loadingOperationSlotNextIndex;
        [NonSerialized] private int _loadingOperationSlotFreeHead = -1;
        private readonly ResourceUlongIntMap _assetLoadingOperationByKey = new ResourceUlongIntMap();

        /// <summary>按 packed key 查记录槽号。不新增登记——查不到就是没有。</summary>
        internal bool TryGetRecordId(ulong key, out int assetId)
        {
            return _assetRecordsByKey.TryGetValue(key, out assetId) && IsValidAssetId(assetId);
        }

        internal void EnsureRecordCapacity(int capacity)
        {
            _assetRecordsByKey.EnsureCapacity(capacity);
            _assetRecordByLoadKeyId.EnsureCapacity(capacity);
        }

        internal void EnsureLoadingOperationCapacity(int capacity)
        {
            _assetLoadingOperationByKey.EnsureCapacity(capacity);
        }

        /// <summary>在途去重槽数量——测试观测点，读数经它而不是反射进字段。</summary>
        internal int LoadingOperationCount => _assetLoadingOperationByKey.Count;

        private struct LoadingOperationSlot
        {
            public ulong Key;
            public LoadingOperationState Operation;
            public byte State;
            public int NextFree;
        }
        private int AllocateLoadingOperationSlot()
        {
            int index;
            if (_loadingOperationSlotFreeHead >= 0)
            {
                index = _loadingOperationSlotFreeHead;
                ref LoadingOperationSlot freeSlot = ref GetLoadingOperationSlotRef(index);
                _loadingOperationSlotFreeHead = freeSlot.NextFree;
            }
            else
            {
                index = _loadingOperationSlotNextIndex++;
                EnsureLoadingOperationSlotPage(index);
            }

            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(index);
            slot = default;
            slot.NextFree = -1;
            return index;
        }
        private void FreeLoadingOperationSlot(int index)
        {
            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(index);
            ClearLoadingOperationSlot(ref slot);
            slot.NextFree = _loadingOperationSlotFreeHead;
            _loadingOperationSlotFreeHead = index;
        }
        private static void ClearLoadingOperationSlot(ref LoadingOperationSlot slot)
        {
            slot.Key = 0;
            slot.Operation = null;
            slot.State = 0;
            slot.NextFree = -1;
        }
        private bool IsValidLoadingOperationSlotId(int index)
        {
            return index >= 0 && index < _loadingOperationSlotNextIndex && _loadingOperationSlotPages != null;
        }
        private ref LoadingOperationSlot GetLoadingOperationSlotRef(int index)
        {
            return ref _loadingOperationSlotPages[index >> RECORD_PAGE_BITS][index & RECORD_PAGE_MASK];
        }
        private void EnsureLoadingOperationSlotPage(int index)
        {
            int pageIndex = index >> RECORD_PAGE_BITS;
            if (_loadingOperationSlotPages == null)
            {
                _loadingOperationSlotPages = new LoadingOperationSlot[Math.Max(4, pageIndex + 1)][];
            }
            else if (pageIndex >= _loadingOperationSlotPages.Length)
            {
                Array.Resize(ref _loadingOperationSlotPages,
                    Math.Max(pageIndex + 1, _loadingOperationSlotPages.Length << 1));
            }

            if (_loadingOperationSlotPages[pageIndex] == null)
            {
                _loadingOperationSlotPages[pageIndex] = new LoadingOperationSlot[RECORD_PAGE_SIZE];
            }
        }

        internal ResourceRecordKernel(IResourceRecordKernelHost host, Func<string> defaultPackageName)
        {
            _host = host;
            _defaultPackageName = defaultPackageName;
        }

        #region 取键 [KEY BUILDING]

        internal ulong GetAssetRecordKey(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind)
        {
            int packageId = GetOrAddPackageId(packageName);
            int locationId = GetOrAddLocationId(location);
            int typeId = GetOrAddTypeId(assetType);
            return ResourceKeyCodec.Pack(packageId, locationId, typeId, assetKind, handleKind);
        }

        internal ulong GetLoadingOperationKey(string location, string packageName, Type assetType,
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
        internal bool TryGetResourceKey(string packageName, string location, Type assetType,
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

        internal string GetPackageNameById(int id) => _packageNames.GetValue(id);

        internal string GetLocationNameById(int id) => _locationNames.GetValue(id);

        internal Type GetAssetTypeById(int id) => _typeNames.GetValue(id);

        /// <summary>记录一条资源建立时，把它用到的三个名字各计一次。</summary>
        internal void RetainResourceKey(ulong key)
        {
            _packageNames.Retain(ResourceKeyCodec.UnpackPackageId(key));
            _locationNames.Retain(ResourceKeyCodec.UnpackLocationId(key));
            _typeNames.Retain(ResourceKeyCodec.UnpackTypeId(key));
        }

        /// <summary>记录释放时反向减数；减到零的那条轴把名字摘掉并把 id 还回空闲栈。</summary>
        internal void ReleaseResourceKey(ulong key)
        {
            _packageNames.Release(ResourceKeyCodec.UnpackPackageId(key));
            _locationNames.Release(ResourceKeyCodec.UnpackLocationId(key));
            _typeNames.Release(ResourceKeyCodec.UnpackTypeId(key));
        }

        /// <summary>整表清空前逐键减数（如后端整体重置），不减则名字与 id 永久滞留。</summary>
        internal void ReleaseAllResourceKeysFromMap(ResourceUlongIntMap map)
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

        internal string NormalizePackageName(string packageName)
        {
            return string.IsNullOrEmpty(packageName) ? _defaultPackageName() : packageName;
        }

        #endregion
    }
}
