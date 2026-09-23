using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源记录内核——带代际校验的资产槽与租约槽、两条索引表、两座过期时间轮，以及在其上的租约
    /// 取用与归还。在途加载去重与 packed key 位宽常量在 <c>ResourceRecordKernel.Loading.cs</c>，
    /// 名称轴登记在同类的另一分部。
    /// <para>本类不出现任何后端类型名：与后端的全部牵连收在 <see cref="IResourceRecordKernelHost"/>，
    /// 即三个原生句柄算子与三个配置读数。时间轮摘链一律按槽里存下的桶号走、走查途中不得同步摘除，
    /// 这两条各记过一次真实事故，改前先读方法上的注释。</para>
    /// </summary>
    internal sealed partial class ResourceRecordKernel
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

        internal ResourceLeaseHandle AcquireLease(int assetId, EResourceLeaseKind leaseKind,
            EResourceLeaseOption options)
        {
            if (!IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            ref AssetSlot asset = ref GetAssetSlotRef(assetId);
            if (leaseKind == EResourceLeaseKind.Binding)
            {
                asset.BindingRefCount++;
            }
            else
            {
                asset.DirectRefCount++;
                leaseKind = EResourceLeaseKind.Direct;
            }

            asset.IdleReleaseRequested = 0;
            RemoveUnusedAssetCandidate(assetId, ref asset);
            RemoveFromExpiryQueue(assetId, ref asset);
            if (asset.KeepAliveRefCount > 0)
            {
                asset.KeepAliveRefCount = 0;
            }

            UpdateAssetState(ref asset);

            int leaseIndex = AllocateLeaseSlot();
            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            lease.AssetId = assetId;
            lease.Kind = leaseKind;
            lease.State = EResourceLeaseState.Active;
            lease.Flags = (byte)options;
            return new ResourceLeaseHandle(leaseIndex, lease.Generation);
        }

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
            return ref _assetSlotPages[index >> ResourceRecordKernel.RECORD_PAGE_BITS][index & ResourceRecordKernel.RECORD_PAGE_MASK];
        }

        private ref LeaseSlot GetLeaseSlotRef(int index)
        {
            return ref _leaseSlotPages[index >> ResourceRecordKernel.RECORD_PAGE_BITS][index & ResourceRecordKernel.RECORD_PAGE_MASK];
        }

        internal void EnsureAssetSlotPage(int index)
        {
            int pageIndex = index >> ResourceRecordKernel.RECORD_PAGE_BITS;
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
                _assetSlotPages[pageIndex] = new AssetSlot[ResourceRecordKernel.RECORD_PAGE_SIZE];
            }
        }

        internal void EnsureLeaseSlotPage(int index)
        {
            int pageIndex = index >> ResourceRecordKernel.RECORD_PAGE_BITS;
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
                _leaseSlotPages[pageIndex] = new LeaseSlot[ResourceRecordKernel.RECORD_PAGE_SIZE];
            }
        }

        #endregion
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
        internal void TrimIdleAssetCapacity()
        {
            _idleCapacityTrimPending = false;

            if (_unusedAssetCandidates == null)
            {
                return;
            }

            while (_unusedAssetCandidateCount > Host.IdleAssetCapacity)
            {
                int candidateCount = _unusedAssetCandidateCount;
                int victimIndex = -1;
                int victimExpireTick = int.MaxValue;

                for (int i = 0; i < candidateCount; i++)
                {
                    int assetId = _unusedAssetCandidates[i];
                    if (!IsValidAssetId(assetId))
                    {
                        continue;
                    }

                    ref AssetSlot slot = ref GetAssetSlotRef(assetId);
                    if (slot.ExpireQueueKind != 2 || slot.IdleExpireTick >= victimExpireTick)
                    {
                        continue;
                    }

                    victimExpireTick = slot.IdleExpireTick;
                    victimIndex = i;
                }

                if (victimIndex < 0)
                {
                    return;
                }

                int victimId = _unusedAssetCandidates[victimIndex];
                ref AssetSlot victim = ref GetAssetSlotRef(victimId);
                uint victimGeneration = victim.Generation;
                victim.IdleReleaseRequested = 1;
                ReleaseAssetStorage(victimId, victimGeneration);

                if (_unusedAssetCandidateCount >= candidateCount)
                {
                    // 仍被引用而未能释放：留给到期轮盘，不在此原地打转。
                    return;
                }
            }
        }

        private int ProcessDueKeepAliveBuckets(int currentTick, int maxCount)
        {
            if (maxCount <= 0)
            {
                return 0;
            }

            if (_lastKeepAliveProcessTick < 0 || currentTick - _lastKeepAliveProcessTick > KEEP_ALIVE_BUCKET_COUNT)
            {
                _lastKeepAliveProcessTick = currentTick - KEEP_ALIVE_BUCKET_COUNT;
            }

            int processed = 0;
            while (_lastKeepAliveProcessTick < currentTick && processed < maxCount)
            {
                int bucketTick = _lastKeepAliveProcessTick + 1;
                int bucketProcessed = ProcessKeepAliveBucket(bucketTick, currentTick, maxCount - processed, out bool completed);
                processed += bucketProcessed;
                if (!completed)
                {
                    break;
                }

                _lastKeepAliveProcessTick = bucketTick;
            }

            return processed;
        }

        private int ProcessDueIdleBuckets(int currentTick, int maxCount)
        {
            if (maxCount <= 0)
            {
                return 0;
            }

            if (_lastIdleProcessTick < 0 || currentTick - _lastIdleProcessTick > IDLE_BUCKET_COUNT)
            {
                _lastIdleProcessTick = currentTick - IDLE_BUCKET_COUNT;
            }

            int processed = 0;
            while (_lastIdleProcessTick < currentTick && processed < maxCount)
            {
                int bucketTick = _lastIdleProcessTick + 1;
                int bucketProcessed = ProcessIdleBucket(bucketTick, currentTick, maxCount - processed, out bool completed);
                processed += bucketProcessed;
                if (!completed)
                {
                    break;
                }

                _lastIdleProcessTick = bucketTick;
            }

            return processed;
        }

        private int ProcessKeepAliveBucket(int bucketTick, int currentTick, int maxCount, out bool completed)
        {
            completed = true;
            if (_keepAliveBuckets == null || maxCount <= 0)
            {
                return 0;
            }

            int bucket = bucketTick & (KEEP_ALIVE_BUCKET_COUNT - 1);
            if (bucket >= _keepAliveBuckets.Length)
            {
                LogUtility.Error("[Resource][Wheel] KA bucket OOB: bucketTick={0} bucket={1} len={2} lastTick={3} currentTick={4} nextIndex={5} pages={6}",
                    bucketTick, bucket, _keepAliveBuckets.Length, _lastKeepAliveProcessTick, currentTick,
                    _assetSlotNextIndex, _assetSlotPages != null ? _assetSlotPages.Length : 0);
                return 0;
            }

            int processed = 0;
            int current = _keepAliveBuckets[bucket];
            while (current >= 0)
            {
                if (current >= _assetSlotNextIndex)
                {
                    LogUtility.Error("[Resource][Wheel] KA zombie id: id={0} bucket={1} nextIndex={2} pages={3} head={4}",
                        current, bucket, _assetSlotNextIndex, _assetSlotPages != null ? _assetSlotPages.Length : 0, _keepAliveBuckets[bucket]);
                    _keepAliveBuckets[bucket] = -1;
                    break;
                }

                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.ExpireQueueNext;
                if (slot.ExpireQueueKind == 1 && slot.KeepAliveExpireTick <= currentTick)
                {
                    if (processed >= maxCount)
                    {
                        completed = false;
                        break;
                    }

                    RemoveFromKeepAliveBucket(current, ref slot);
                    if (slot.KeepAliveRefCount > 0)
                    {
                        slot.KeepAliveRefCount = 0;
                    }

                    UpdateAssetStateAndIdleQueue(current, ref slot);
                    processed++;
                }

                current = next;
            }

            return processed;
        }

        private int ProcessIdleBucket(int bucketTick, int currentTick, int maxCount, out bool completed)
        {
            completed = true;
            if (_idleBuckets == null || maxCount <= 0)
            {
                return 0;
            }

            int bucket = bucketTick & (IDLE_BUCKET_COUNT - 1);
            if (bucket >= _idleBuckets.Length)
            {
                LogUtility.Error("[Resource][Wheel] Idle bucket OOB: bucketTick={0} bucket={1} len={2} lastTick={3} currentTick={4} nextIndex={5} pages={6}",
                    bucketTick, bucket, _idleBuckets.Length, _lastIdleProcessTick, currentTick,
                    _assetSlotNextIndex, _assetSlotPages != null ? _assetSlotPages.Length : 0);
                return 0;
            }

            int processed = 0;
            int current = _idleBuckets[bucket];
            while (current >= 0)
            {
                if (current >= _assetSlotNextIndex)
                {
                    LogUtility.Error("[Resource][Wheel] Idle zombie id: id={0} bucket={1} nextIndex={2} pages={3} head={4}",
                        current, bucket, _assetSlotNextIndex, _assetSlotPages != null ? _assetSlotPages.Length : 0, _idleBuckets[bucket]);
                    _idleBuckets[bucket] = -1;
                    break;
                }

                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.ExpireQueueNext;
                if (slot.ExpireQueueKind == 2 && slot.IdleExpireTick <= currentTick)
                {
                    if (processed >= maxCount)
                    {
                        completed = false;
                        break;
                    }

                    RemoveFromIdleBucket(current, ref slot);
                    if (HasNoResourceRefs(ref slot))
                    {
                        slot.IdleReleaseRequested = 1;
                        ReleaseAssetStorage(current, slot.Generation);
                    }
                    else
                    {
                        UpdateAssetStateAndIdleQueue(current, ref slot);
                    }

                    processed++;
                }

                current = next;
            }

            return processed;
        }

        private void ReleaseAssetStorage(int assetId, uint generation)
        {
            if (!IsValidAssetId(assetId))
            {
                return;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.Generation != generation || slot.State == EResourceAssetState.Released)
            {
                return;
            }

            if (!HasNoResourceRefs(ref slot) || slot.IdleReleaseRequested == 0)
            {
                UpdateAssetStateAndIdleQueue(assetId, ref slot);
                return;
            }

            RemoveFromExpiryQueue(assetId, ref slot);
            RemoveUnusedAssetCandidate(assetId, ref slot);
            DisposeAssetSlotHandle(ref slot);
            ulong key = slot.Key;
            _assetRecordsByKey.Remove(key);
            ReleaseResourceKey(key);
            if (slot.LoadKeyId > 0)
            {
                _assetRecordByLoadKeyId.Remove((ulong)slot.LoadKeyId);
            }

            ClearAssetSlot(ref slot, preserveGeneration: true);
            FreeAssetSlot(assetId);
        }

        private static bool HasNoResourceRefs(ref AssetSlot slot)
        {
            return slot.DirectRefCount == 0 &&
                   slot.BindingRefCount == 0 &&
                   slot.KeepAliveRefCount == 0;
        }

        private void UpdateAssetState(ref AssetSlot slot)
        {
            if (!IsSlotHandleValid(ref slot))
            {
                slot.State = EResourceAssetState.Released;
                return;
            }

            if (slot.DirectRefCount + slot.BindingRefCount > 0)
            {
                slot.State = EResourceAssetState.Active;
                return;
            }

            slot.State = slot.KeepAliveRefCount > 0
                ? EResourceAssetState.KeepAlive
                : EResourceAssetState.Idle;
        }

        private void UpdateAssetStateAndIdleQueue(int assetId, ref AssetSlot slot)
        {
            UpdateAssetState(ref slot);
            if (slot.State == EResourceAssetState.Idle)
            {
                slot.IdleReleaseRequested = 0;
                AddUnusedAssetCandidate(assetId, ref slot);
                EnterIdle(assetId, ref slot);

                if (_unusedAssetCandidateCount > Host.IdleAssetCapacity)
                {
                    _idleCapacityTrimPending = true;
                }
            }
            else if (slot.ExpireQueueKind == 2)
            {
                RemoveFromIdleBucket(assetId, ref slot);
                slot.IdleReleaseRequested = 0;
                RemoveUnusedAssetCandidate(assetId, ref slot);
            }
            else
            {
                RemoveUnusedAssetCandidate(assetId, ref slot);
            }
        }

        private void EnterIdle(int assetId, ref AssetSlot slot)
        {
            if (!IsSlotHandleValid(ref slot))
            {
                return;
            }

            int expireTick = ToKeepAliveTick(Time.unscaledTime) + Mathf.Max(0, Mathf.CeilToInt(Host.IdleAssetExpireTime));
            if (slot.ExpireQueueKind == 2 && slot.IdleExpireTick == expireTick)
            {
                return;
            }

            RemoveFromExpiryQueue(assetId, ref slot);
            slot.IdleExpireTick = expireTick;
            if (_idleBuckets == null || _idleBuckets.Length != IDLE_BUCKET_COUNT)
            {
                _idleBuckets = new int[IDLE_BUCKET_COUNT];
                for (int i = 0; i < IDLE_BUCKET_COUNT; i++)
                {
                    _idleBuckets[i] = -1;
                }
            }

            int bucket = expireTick & (IDLE_BUCKET_COUNT - 1);
            slot.ExpireQueueBucket = bucket;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = _idleBuckets[bucket];
            if (slot.ExpireQueueNext >= 0)
            {
                ref AssetSlot next = ref GetAssetSlotRef(slot.ExpireQueueNext);
                next.ExpireQueuePrev = assetId;
            }

            _idleBuckets[bucket] = assetId;
            slot.ExpireQueueKind = 2;
        }

        private void AddToKeepAliveBucket(int assetId, ref AssetSlot slot)
        {
            if (_keepAliveBuckets == null || _keepAliveBuckets.Length != KEEP_ALIVE_BUCKET_COUNT)
            {
                _keepAliveBuckets = new int[KEEP_ALIVE_BUCKET_COUNT];
                for (int i = 0; i < KEEP_ALIVE_BUCKET_COUNT; i++)
                {
                    _keepAliveBuckets[i] = -1;
                }
            }

            RemoveFromKeepAliveBucket(assetId, ref slot);
            int bucket = slot.KeepAliveExpireTick & (KEEP_ALIVE_BUCKET_COUNT - 1);
            slot.ExpireQueueBucket = bucket;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = _keepAliveBuckets[bucket];
            if (slot.ExpireQueueNext >= 0)
            {
                ref AssetSlot next = ref GetAssetSlotRef(slot.ExpireQueueNext);
                next.ExpireQueuePrev = assetId;
            }

            _keepAliveBuckets[bucket] = assetId;
            slot.ExpireQueueKind = 1;
        }

        private void RemoveFromExpiryQueue(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind == 1)
            {
                RemoveFromKeepAliveBucket(assetId, ref slot);
            }
            else if (slot.ExpireQueueKind == 2)
            {
                RemoveFromIdleBucket(assetId, ref slot);
            }
        }

        private void RemoveFromKeepAliveBucket(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind != 1 || _keepAliveBuckets == null)
            {
                return;
            }

            // 链接时已存桶号：tick 可能在链接后被更新，反推桶号会定位到错误桶导致 unlink 静默失败、桶头悬挂僵尸 id。
            int bucket = slot.ExpireQueueBucket;
            if (bucket < 0 || bucket >= KEEP_ALIVE_BUCKET_COUNT)
            {
                slot.ExpireQueuePrev = -1;
                slot.ExpireQueueNext = -1;
                slot.ExpireQueueKind = 0;
                slot.ExpireQueueBucket = -1;
                return;
            }

            int prev = slot.ExpireQueuePrev;
            int next = slot.ExpireQueueNext;
            if (prev >= 0)
            {
                ref AssetSlot prevSlot = ref GetAssetSlotRef(prev);
                prevSlot.ExpireQueueNext = next;
            }
            else if (_keepAliveBuckets[bucket] == assetId)
            {
                _keepAliveBuckets[bucket] = next;
            }

            if (next >= 0)
            {
                ref AssetSlot nextSlot = ref GetAssetSlotRef(next);
                nextSlot.ExpireQueuePrev = prev;
            }

            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.ExpireQueueKind = 0;
            slot.ExpireQueueBucket = -1;
        }

        private void RemoveFromIdleBucket(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind != 2 || _idleBuckets == null)
            {
                return;
            }

            // 同 KeepAlive：读链接时存储的桶号，禁止由当前 tick 反推。
            int bucket = slot.ExpireQueueBucket;
            if (bucket < 0 || bucket >= IDLE_BUCKET_COUNT)
            {
                slot.ExpireQueuePrev = -1;
                slot.ExpireQueueNext = -1;
                slot.ExpireQueueKind = 0;
                slot.ExpireQueueBucket = -1;
                return;
            }

            int prev = slot.ExpireQueuePrev;
            int next = slot.ExpireQueueNext;
            if (prev >= 0)
            {
                ref AssetSlot prevSlot = ref GetAssetSlotRef(prev);
                prevSlot.ExpireQueueNext = next;
            }
            else if (_idleBuckets[bucket] == assetId)
            {
                _idleBuckets[bucket] = next;
            }

            if (next >= 0)
            {
                ref AssetSlot nextSlot = ref GetAssetSlotRef(next);
                nextSlot.ExpireQueuePrev = prev;
            }

            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.ExpireQueueKind = 0;
            slot.ExpireQueueBucket = -1;
        }

        private void AddUnusedAssetCandidate(int assetId, ref AssetSlot slot)
        {
            if (slot.UnusedCandidateIndex >= 0)
            {
                return;
            }

            if (_unusedAssetCandidates == null)
            {
                _unusedAssetCandidates = new int[Math.Max(16, Host.AssetRecordCapacity)];
            }
            else if (_unusedAssetCandidateCount >= _unusedAssetCandidates.Length)
            {
                Array.Resize(ref _unusedAssetCandidates, _unusedAssetCandidates.Length << 1);
            }

            slot.UnusedCandidateIndex = _unusedAssetCandidateCount;
            _unusedAssetCandidates[_unusedAssetCandidateCount++] = assetId;
        }

        private void RemoveUnusedAssetCandidate(int assetId, ref AssetSlot slot)
        {
            int index = slot.UnusedCandidateIndex;
            if (index < 0 || index >= _unusedAssetCandidateCount)
            {
                slot.UnusedCandidateIndex = -1;
                return;
            }

            if (_unusedAssetCandidates[index] != assetId)
            {
                for (int i = 0; i < _unusedAssetCandidateCount; i++)
                {
                    if (_unusedAssetCandidates[i] == assetId)
                    {
                        RemoveUnusedAssetCandidateAt(i);
                        return;
                    }
                }

                slot.UnusedCandidateIndex = -1;
                return;
            }

            RemoveUnusedAssetCandidateAt(index);
        }

        private void RemoveUnusedAssetCandidateAt(int index)
        {
            if (index < 0 || index >= _unusedAssetCandidateCount)
            {
                return;
            }

            int removedAssetId = _unusedAssetCandidates[index];
            int lastIndex = --_unusedAssetCandidateCount;
            int movedAssetId = _unusedAssetCandidates[lastIndex];
            _unusedAssetCandidates[lastIndex] = 0;
            if (index != lastIndex)
            {
                _unusedAssetCandidates[index] = movedAssetId;
                if (IsValidAssetId(movedAssetId))
                {
                    ref AssetSlot movedSlot = ref GetAssetSlotRef(movedAssetId);
                    movedSlot.UnusedCandidateIndex = index;
                }
            }

            if (IsValidAssetId(removedAssetId))
            {
                ref AssetSlot removedSlot = ref GetAssetSlotRef(removedAssetId);
                removedSlot.UnusedCandidateIndex = -1;
            }
        }

        private static int ToKeepAliveTick(float unscaledTime)
        {
            return Mathf.Max(0, Mathf.FloorToInt(unscaledTime));
        }
        internal void ProcessResourceMaintenance(float unscaledTime, int expireBudget)
        {
            if ((_keepAliveBuckets != null || _idleBuckets != null) && expireBudget > 0)
            {
                int currentTick = ToKeepAliveTick(unscaledTime);
                int processed = ProcessDueKeepAliveBuckets(currentTick, expireBudget);
                if (processed < expireBudget)
                {
                    ProcessDueIdleBuckets(currentTick, expireBudget - processed);
                }
            }

            // 容量淘汰排在轮盘走查之后：走查途中同步摘除会让已捕获的 next 指针失效、整桶被跳过。
            if (_idleCapacityTrimPending)
            {
                TrimIdleAssetCapacity();
            }
        }

        internal int ReleaseAllUnusedAssetRecords()
        {
            int releasedCount = 0;
            int index = 0;
            while (index < _unusedAssetCandidateCount)
            {
                int assetId = _unusedAssetCandidates[index];
                if (!IsValidAssetId(assetId))
                {
                    RemoveUnusedAssetCandidateAt(index);
                    continue;
                }

                ref AssetSlot slot = ref GetAssetSlotRef(assetId);
                if (slot.Generation == 0 || slot.State == EResourceAssetState.Released ||
                    !IsSlotHandleValid(ref slot))
                {
                    RemoveUnusedAssetCandidateAt(index);
                    continue;
                }

                if (!HasNoResourceRefs(ref slot))
                {
                    RemoveUnusedAssetCandidate(assetId, ref slot);
                    continue;
                }

                slot.IdleReleaseRequested = 1;
                uint generation = slot.Generation;
                int previousCandidateCount = _unusedAssetCandidateCount;
                ReleaseAssetStorage(assetId, generation);
                if (_unusedAssetCandidateCount == previousCandidateCount && index < _unusedAssetCandidateCount &&
                    _unusedAssetCandidates[index] == assetId)
                {
                    RemoveUnusedAssetCandidateAt(index);
                }

                releasedCount++;
            }

            return releasedCount;
        }

        internal void ForceReleaseAllAssetRecords()
        {
            int total = _assetSlotNextIndex;
            for (int i = 0; i < total; i++)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(i);
                if (slot.Generation == 0 || slot.State == EResourceAssetState.Released)
                {
                    continue;
                }

                RemoveFromExpiryQueue(i, ref slot);
                RemoveUnusedAssetCandidate(i, ref slot);
                DisposeAssetSlotHandle(ref slot);
                ClearAssetSlot(ref slot, preserveGeneration: true);
                FreeAssetSlot(i);
            }

            ReleaseAllResourceKeysFromMap(_assetRecordsByKey);
            _assetRecordsByKey.Clear();
            _assetRecordByLoadKeyId.Clear();
            _unusedAssetCandidateCount = 0;

            _leaseSlotNextIndex = 0;
            _leaseSlotFreeHead = -1;
        }

        /// <inheritdoc />
        internal void Release(ResourceLeaseHandle handle)
        {
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            int assetId = lease.AssetId;
            EResourceLeaseOption options = (EResourceLeaseOption)lease.Flags;
            lease.State = EResourceLeaseState.Released;

            if (lease.Kind == EResourceLeaseKind.Binding)
            {
                ref AssetSlot asset = ref GetAssetSlotRef(assetId);
                if (asset.BindingRefCount > 0)
                {
                    asset.BindingRefCount--;
                }
            }
            else
            {
                ref AssetSlot asset = ref GetAssetSlotRef(assetId);
                if (asset.DirectRefCount > 0)
                {
                    asset.DirectRefCount--;
                }
            }

            if ((options & EResourceLeaseOption.KeepAliveOnRelease) != 0)
            {
                ref AssetSlot kaAsset = ref GetAssetSlotRef(assetId);
                kaAsset.KeepAliveRefCount++;
                kaAsset.KeepAliveExpireTick = ToKeepAliveTick(Time.unscaledTime) +
                    Mathf.Max(0, Mathf.CeilToInt(Host.IdleAssetExpireTime));
                AddToKeepAliveBucket(assetId, ref kaAsset);
            }

            UpdateAssetStateAndIdleQueue(assetId, ref GetAssetSlotRef(assetId));
            FreeLeaseSlot(leaseIndex);
        }

        /// <inheritdoc />
        internal bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset)
        {
            asset = null;
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            if (!IsValidAssetId(lease.AssetId))
            {
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(lease.AssetId);
            if (slot.State == EResourceAssetState.Released || slot.Asset == null)
            {
                return false;
            }

            asset = slot.Asset;
            return true;
        }

        internal bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            if (!IsValidAssetId(lease.AssetId))
            {
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(lease.AssetId);
            if (slot.State == EResourceAssetState.Released || !Host.IsHandleValid(slot.RawHandle))
            {
                return false;
            }

            sprite = Host.GetSubSprite(slot.RawHandle, spriteName);
            return sprite != null;
        }

        internal bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
        {
            assetId = -1;
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            assetId = lease.AssetId;
            return IsValidAssetId(assetId);
        }

        internal void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options)
        {
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            lease.Flags = (byte)options;
        }

    }
}
