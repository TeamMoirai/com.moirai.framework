using System;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源记录内核的记录侧——带代际校验的资产槽、分页 arena、两条索引表与诊断快照。
    /// <para>与后端的全部牵连收在 <see cref="IResourceRecordHost"/>：三个原生句柄算子
    /// 与三个配置读数。本分部不出现任何后端类型名。</para>
    /// </summary>
    internal sealed partial class ResourceRecordStore
    {
        #region 常量 [CONSTANTS]

        private const int IDLE_BUCKET_COUNT = 256;
        private const int KEEP_ALIVE_BUCKET_COUNT = 256;
        #endregion

        #region Slot 结构体 [SLOT STRUCTS]

        private struct AssetSlot
        {
            public ulong Key;
            public int LoadKeyId;
            public UObject Asset;
            // 后端原生句柄只能是引用类型，存进 object 不产生装箱；本文件一律经 IsHandleValid / DisposeHandle / GetSubSprite 取用。
            public object RawHandle;
            public EResourceAssetKind AssetKind;
            public EResourceHandleKind HandleKind;
            public int DirectRefCount;
            public int BindingRefCount;
            public int KeepAliveRefCount;
            public uint Generation;
            public EResourceAssetState State;
            public byte IdleReleaseRequested;
            public int ExpireQueueKind;
            public int ExpireQueuePrev;
            public int ExpireQueueNext;
            public int ExpireQueueBucket;
            public int IdleExpireTick;
            public int KeepAliveExpireTick;
            public int UnusedCandidateIndex;
            public int NextFree;
        }

        private struct LeaseSlot
        {
            public int AssetId;
            public EResourceLeaseKind Kind;
            public EResourceLeaseState State;
            public uint Generation;
            public byte Flags;
            public int NextFree;
        }

        #endregion

        #region 字段 [FIELDS]

        // 资源记录分页数组
        [NonSerialized] private AssetSlot[][] _assetSlotPages;
        [NonSerialized] private int _assetSlotNextIndex;
        [NonSerialized] private int _assetSlotFreeHead = -1;

        // 租约分页数组
        [NonSerialized] private LeaseSlot[][] _leaseSlotPages;
        [NonSerialized] private int _leaseSlotNextIndex;
        [NonSerialized] private int _leaseSlotFreeHead = -1;

        // 索引映射
        [NonSerialized] private readonly ResourceUlongIntMap _assetRecordsByKey = new ResourceUlongIntMap();
        [NonSerialized] private readonly ResourceUlongIntMap _assetRecordByLoadKeyId = new ResourceUlongIntMap();

        // 过期队列
        [NonSerialized] private int[] _idleBuckets;
        [NonSerialized] private int[] _keepAliveBuckets;
        [NonSerialized] private int[] _unusedAssetCandidates;
        [NonSerialized] private int _lastKeepAliveProcessTick = -1;
        [NonSerialized] private int _lastIdleProcessTick = -1;
        [NonSerialized] private int _unusedAssetCandidateCount;
        [NonSerialized] private bool _idleCapacityTrimPending;

        // 加载键自增
        [NonSerialized] private int _loadKeyNextId = 1;

        #endregion

        #region 资源记录管理 [ASSET RECORD MANAGEMENT]

        internal int GetOrCreateAssetRecord(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind, UObject asset, object assetHandle)
        {
            assetKind = ResourceKeyCodec.NormalizeAssetKind(assetType, assetKind);
            assetType = ResourceKeyCodec.NormalizeAssetType(assetType, assetKind);
            string normalizedPackageName = NormalizePackageName(packageName);
            ulong key = GetAssetRecordKey(normalizedPackageName, location, assetType, assetKind, handleKind);
            if (_assetRecordsByKey.TryGetValue(key, out int existingId) && IsValidAssetId(existingId))
            {
                ref AssetSlot existing = ref GetAssetSlotRef(existingId);
                if (existing.Asset == null && asset != null)
                {
                    existing.Asset = asset;
                }

                if (assetHandle != null)
                {
                    if (!Host.IsHandleValid(existing.RawHandle))
                    {
                        existing.RawHandle = assetHandle;
                        existing.HandleKind = handleKind;
                    }
                    else if (!ReferenceEquals(existing.RawHandle, assetHandle))
                    {
                        Host.DisposeHandle(assetHandle);
                    }
                }

                UpdateAssetStateAndIdleQueue(existingId, ref existing);
                return existingId;
            }

            int assetId = AllocateAssetSlot();
            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            slot.Key = key;
            slot.LoadKeyId = AllocateLoadKeyId();
            slot.Asset = asset;
            slot.RawHandle = assetHandle;
            slot.AssetKind = assetKind;
            slot.HandleKind = handleKind;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.NextFree = -1;
            slot.UnusedCandidateIndex = -1;
            slot.State = EResourceAssetState.Idle;
            _assetRecordsByKey.Set(key, assetId);
            RetainResourceKey(key);
            _assetRecordByLoadKeyId.Set((ulong)slot.LoadKeyId, assetId);
            UpdateAssetStateAndIdleQueue(assetId, ref slot);
            return assetId;
        }

        internal int GetOrCreateSubAssetsRecord(string packageName, string location, object subAssetsHandle)
        {
            string normalizedPackageName = NormalizePackageName(packageName);
            ulong key = GetAssetRecordKey(normalizedPackageName, location, typeof(Sprite),
                EResourceAssetKind.SubAssets, EResourceHandleKind.SubAssetsHandle);
            if (_assetRecordsByKey.TryGetValue(key, out int existingId) && IsValidAssetId(existingId))
            {
                ref AssetSlot existing = ref GetAssetSlotRef(existingId);
                if (!Host.IsHandleValid(existing.RawHandle) &&
                    Host.IsHandleValid(subAssetsHandle))
                {
                    existing.RawHandle = subAssetsHandle;
                    existing.HandleKind = EResourceHandleKind.SubAssetsHandle;
                }
                else if (Host.IsHandleValid(subAssetsHandle) &&
                         !ReferenceEquals(existing.RawHandle, subAssetsHandle))
                {
                    Host.DisposeHandle(subAssetsHandle);
                }

                UpdateAssetStateAndIdleQueue(existingId, ref existing);
                return existingId;
            }

            int assetId = AllocateAssetSlot();
            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            slot.Key = key;
            slot.LoadKeyId = AllocateLoadKeyId();
            slot.Asset = null;
            slot.RawHandle = subAssetsHandle;
            slot.AssetKind = EResourceAssetKind.SubAssets;
            slot.HandleKind = EResourceHandleKind.SubAssetsHandle;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.NextFree = -1;
            slot.UnusedCandidateIndex = -1;
            slot.State = EResourceAssetState.Idle;
            _assetRecordsByKey.Set(key, assetId);
            RetainResourceKey(key);
            _assetRecordByLoadKeyId.Set((ulong)slot.LoadKeyId, assetId);
            UpdateAssetStateAndIdleQueue(assetId, ref slot);
            return assetId;
        }

        internal bool TryGetCachedSubAssetsRecord(string normalizedPackageName, string location, out int assetId)
        {
            assetId = -1;
            ulong key = GetAssetRecordKey(normalizedPackageName, location, typeof(Sprite),
                EResourceAssetKind.SubAssets, EResourceHandleKind.SubAssetsHandle);
            if (!_assetRecordsByKey.TryGetValue(key, out int existingId) || !IsValidAssetId(existingId))
            {
                return false;
            }

            // 子资源记录的 Asset 恒为 null（图集在 SubAssetsHandle 里），不能复用 TryGetCachedAssetRecord 的判定。
            ref AssetSlot slot = ref GetAssetSlotRef(existingId);
            if (slot.State == EResourceAssetState.Released || !Host.IsHandleValid(slot.RawHandle))
            {
                return false;
            }

            assetId = existingId;
            return true;
        }

        internal bool TryGetCachedAssetRecord(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind, out int assetId, out UObject asset)
        {
            assetId = -1;
            asset = null;
            if (!TryGetResourceKey(packageName, location, assetType, assetKind, handleKind, out ulong key))
            {
                return false;
            }

            if (!_assetRecordsByKey.TryGetValue(key, out assetId) || !IsValidAssetId(assetId))
            {
                assetId = -1;
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.State == EResourceAssetState.Released || slot.Asset == null ||
                !IsSlotHandleValid(ref slot))
            {
                assetId = -1;
                return false;
            }

            asset = slot.Asset;
            return true;
        }

        #endregion
        #region 诊断 [DIAGNOSTICS]

        internal int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount)
        {
            if (results == null || _assetSlotPages == null)
            {
                return 0;
            }

            int total = _assetSlotNextIndex;
            int written = 0;
            int index = startIndex;
            // 刻度在行循环之外取一次：每行一趟 managed→native 时钟调用，64 行就是 64 趟，
            // 而同一次快照里所有行的"还剩多久过期"本该对着同一个当下算。
            int currentTick = ToKeepAliveTick(Time.unscaledTime);
            while (index < total && written < maxCount)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(index);
                if (slot.Generation == 0 || slot.State == EResourceAssetState.Released)
                {
                    index++;
                    continue;
                }

                ref ResourceAssetInfo info = ref results[written];
                info.LoadKeyId = slot.LoadKeyId;
                info.Package = GetPackageNameById(ResourceKeyCodec.UnpackPackageId(slot.Key));
                info.Location = GetLocationNameById(ResourceKeyCodec.UnpackLocationId(slot.Key));
                Type assetType = GetAssetTypeById(ResourceKeyCodec.UnpackTypeId(slot.Key));
                info.TypeName = assetType != null ? assetType.Name : string.Empty;
                info.Kind = slot.AssetKind;
                info.State = slot.State;
                info.DirectRefCount = slot.DirectRefCount;
                info.BindingRefCount = slot.BindingRefCount;
                info.KeepAliveRefCount = slot.KeepAliveRefCount;
                info.RefCountTotal = slot.DirectRefCount + slot.BindingRefCount + slot.KeepAliveRefCount;
                info.KeepAliveExpireIn = slot.KeepAliveRefCount > 0
                    ? Math.Max(0, slot.KeepAliveExpireTick - currentTick)
                    : 0;
                info.IdleExpireIn = slot.State == EResourceAssetState.Idle && slot.ExpireQueueKind == 2
                    ? Math.Max(0, slot.IdleExpireTick - currentTick)
                    : 0;
                info.IdleReleaseRequested = slot.IdleReleaseRequested != 0;
                info.HandleValid = IsSlotHandleValid(ref slot);
                info.HandleKind = (byte)slot.HandleKind;
                written++;
                index++;
            }

            return written;
        }
        /// <summary>
        /// 把空闲记录数压回 <c>IdleAssetCapacity</c> 以内：每轮淘汰过期刻度最早（即最长空闲）的一条。
        /// </summary>
        #endregion

        #region 辅助方法 [UTILITY METHODS]

        private bool IsSlotHandleValid(ref AssetSlot slot)
        {
            return Host.IsHandleValid(slot.RawHandle);
        }

        private void DisposeAssetSlotHandle(ref AssetSlot slot)
        {
            Host.DisposeHandle(slot.RawHandle);
            slot.RawHandle = null;
            slot.State = EResourceAssetState.Released;
        }

        private int AllocateLoadKeyId()
        {
            int id = _loadKeyNextId++;
            if (_loadKeyNextId <= 0)
            {
                _loadKeyNextId = 1;
            }

            return id;
        }

        private static void ClearAssetSlot(ref AssetSlot slot, bool preserveGeneration)
        {
            uint generation = slot.Generation;
            slot = default;
            slot.Generation = preserveGeneration ? generation : 0;
            slot.NextFree = -1;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.ExpireQueueBucket = -1;
            slot.UnusedCandidateIndex = -1;
            slot.State = EResourceAssetState.Released;
        }

        #endregion

        #region 分页 Slot 分配 [PAGED SLOT ALLOCATION]

        private int AllocateAssetSlot()
        {
            int index;
            if (_assetSlotFreeHead >= 0)
            {
                index = _assetSlotFreeHead;
                ref AssetSlot freeSlot = ref GetAssetSlotRef(index);
                _assetSlotFreeHead = freeSlot.NextFree;
            }
            else
            {
                index = _assetSlotNextIndex++;
                EnsureAssetSlotPage(index);
            }

            ref AssetSlot slot = ref GetAssetSlotRef(index);
            uint generation = slot.Generation + 1;
            if (generation == 0)
            {
                generation = 1;
            }

            slot = default;
            slot.Generation = generation;
            slot.NextFree = -1;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.ExpireQueueBucket = -1;
            slot.UnusedCandidateIndex = -1;
            slot.State = EResourceAssetState.Released;
            return index;
        }

        private void FreeAssetSlot(int index)
        {
            ref AssetSlot slot = ref GetAssetSlotRef(index);
            slot.NextFree = _assetSlotFreeHead;
            _assetSlotFreeHead = index;
        }

        private int AllocateLeaseSlot()
        {
            int index;
            if (_leaseSlotFreeHead >= 0)
            {
                index = _leaseSlotFreeHead;
                ref LeaseSlot freeSlot = ref GetLeaseSlotRef(index);
                _leaseSlotFreeHead = freeSlot.NextFree;
            }
            else
            {
                index = _leaseSlotNextIndex++;
                EnsureLeaseSlotPage(index);
            }

            ref LeaseSlot slot = ref GetLeaseSlotRef(index);
            uint generation = slot.Generation + 1;
            if (generation == 0)
            {
                generation = 1;
            }

            slot = default;
            slot.Generation = generation;
            slot.NextFree = -1;
            slot.State = EResourceLeaseState.Free;
            return index;
        }

        private void FreeLeaseSlot(int index)
        {
            ref LeaseSlot slot = ref GetLeaseSlotRef(index);
            uint generation = slot.Generation;
            slot = default;
            slot.Generation = generation;
            slot.State = EResourceLeaseState.Released;
            slot.NextFree = _leaseSlotFreeHead;
            _leaseSlotFreeHead = index;
        }

        private bool TryGetLeaseSlotIndex(ResourceLeaseHandle handle, out int leaseIndex)
        {
            leaseIndex = handle.Index;
            if (!handle.IsValid || !IsValidLeaseId(handle.Index))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(handle.Index);
            return lease.Generation == handle.Generation && lease.State == EResourceLeaseState.Active;
        }

        internal bool IsValidAssetId(int assetId)
        {
            return assetId >= 0 && assetId < _assetSlotNextIndex && _assetSlotPages != null;
        }

        private bool IsValidActiveAssetId(int assetId)
        {
            if (!IsValidAssetId(assetId))
            {
                return false;
            }

            return GetAssetSlotRef(assetId).Generation != 0;
        }

        private bool IsValidLeaseId(int leaseId)
        {
            return leaseId >= 0 && leaseId < _leaseSlotNextIndex && _leaseSlotPages != null;
        }

        private ref AssetSlot GetAssetSlotRef(int index)
        {
            return ref _assetSlotPages[index >> ResourceRecordStore.RECORD_PAGE_BITS][index & ResourceRecordStore.RECORD_PAGE_MASK];
        }

        private ref LeaseSlot GetLeaseSlotRef(int index)
        {
            return ref _leaseSlotPages[index >> ResourceRecordStore.RECORD_PAGE_BITS][index & ResourceRecordStore.RECORD_PAGE_MASK];
        }

        internal void EnsureAssetSlotPage(int index)
        {
            int pageIndex = index >> ResourceRecordStore.RECORD_PAGE_BITS;
            if (_assetSlotPages == null)
            {
                _assetSlotPages = new AssetSlot[Math.Max(4, pageIndex + 1)][];
            }
            else if (pageIndex >= _assetSlotPages.Length)
            {
                Array.Resize(ref _assetSlotPages, Math.Max(pageIndex + 1, _assetSlotPages.Length << 1));
            }

            if (_assetSlotPages[pageIndex] == null)
            {
                _assetSlotPages[pageIndex] = new AssetSlot[ResourceRecordStore.RECORD_PAGE_SIZE];
            }
        }

        internal void EnsureLeaseSlotPage(int index)
        {
            int pageIndex = index >> ResourceRecordStore.RECORD_PAGE_BITS;
            if (_leaseSlotPages == null)
            {
                _leaseSlotPages = new LeaseSlot[Math.Max(4, pageIndex + 1)][];
            }
            else if (pageIndex >= _leaseSlotPages.Length)
            {
                Array.Resize(ref _leaseSlotPages, Math.Max(pageIndex + 1, _leaseSlotPages.Length << 1));
            }

            if (_leaseSlotPages[pageIndex] == null)
            {
                _leaseSlotPages[pageIndex] = new LeaseSlot[ResourceRecordStore.RECORD_PAGE_SIZE];
            }
        }

        #endregion
    }
}
