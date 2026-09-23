using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源服务核心记录管理——分页 slot 系统、generation 验证、Timer-wheel 过期、加载去重。
    /// </summary>
    partial class YooAssetHandler
    {
        #region 常量 [CONSTANTS]

        private const int RECORD_PAGE_BITS = 8;
        private const int RECORD_PAGE_SIZE = 1 << RECORD_PAGE_BITS;
        private const int RECORD_PAGE_MASK = RECORD_PAGE_SIZE - 1;
        private const int IDLE_BUCKET_COUNT = 256;
        private const int KEEP_ALIVE_BUCKET_COUNT = 256;
        private const float PROGRESS_CALLBACK_THRESHOLD = 0.01f;

        #region packed key 位域常量 [PACKED KEY BIT FIELDS]

        private const int RESOURCE_KEY_HANDLE_BITS = 4;
        private const int RESOURCE_KEY_ASSET_KIND_BITS = 4;
        private const int RESOURCE_KEY_TYPE_BITS = 12;
        private const int RESOURCE_KEY_LOCATION_BITS = 32;
        private const int RESOURCE_KEY_PACKAGE_BITS = 12;

        private const int RESOURCE_KEY_HANDLE_SHIFT = 0;
        private const int RESOURCE_KEY_ASSET_KIND_SHIFT = RESOURCE_KEY_HANDLE_SHIFT + RESOURCE_KEY_HANDLE_BITS;
        private const int RESOURCE_KEY_TYPE_SHIFT = RESOURCE_KEY_ASSET_KIND_SHIFT + RESOURCE_KEY_ASSET_KIND_BITS;
        private const int RESOURCE_KEY_LOCATION_SHIFT = RESOURCE_KEY_TYPE_SHIFT + RESOURCE_KEY_TYPE_BITS;
        private const int RESOURCE_KEY_PACKAGE_SHIFT = RESOURCE_KEY_LOCATION_SHIFT + RESOURCE_KEY_LOCATION_BITS;

        private const int RESOURCE_KEY_PACKAGE_MAX = (1 << RESOURCE_KEY_PACKAGE_BITS) - 1;
        private const int RESOURCE_KEY_LOCATION_MAX = int.MaxValue;
        private const int RESOURCE_KEY_TYPE_MAX = (1 << RESOURCE_KEY_TYPE_BITS) - 1;
        private const int RESOURCE_KEY_ASSET_KIND_MAX = (1 << RESOURCE_KEY_ASSET_KIND_BITS) - 1;
        private const int RESOURCE_KEY_HANDLE_MAX = (1 << RESOURCE_KEY_HANDLE_BITS) - 1;

        #endregion

        #endregion

        #region Slot 结构体 [SLOT STRUCTS]

        private struct AssetSlot
        {
            public ulong Key;
            public int LoadKeyId;
            public UObject Asset;
            public ulong AssetInstanceId;
            public AssetHandle AssetHandle;
            public SubAssetsHandle SubAssetsHandle;
            public EResourceAssetKind AssetKind;
            public EResourceHandleKind HandleKind;
            public int DirectRefCount;
            public int LegacyDirectRefCount;
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
            public int NextByUnityObject;
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

        private struct LoadingOperationSlot
        {
            public ulong Key;
            public LoadingOperationState Operation;
            public byte State;
            public int NextFree;
        }

        #endregion

        #region 字段 [FIELDS]

        [NonSerialized] private bool _isDestroying;

        // 资源记录分页数组
        [NonSerialized] private AssetSlot[][] _assetSlotPages;
        [NonSerialized] private int _assetSlotNextIndex;
        [NonSerialized] private int _assetSlotFreeHead = -1;

        // 租约分页数组
        [NonSerialized] private LeaseSlot[][] _leaseSlotPages;
        [NonSerialized] private int _leaseSlotNextIndex;
        [NonSerialized] private int _leaseSlotFreeHead = -1;

        // 加载操作分页数组
        [NonSerialized] private LoadingOperationSlot[][] _loadingOperationSlotPages;
        [NonSerialized] private int _loadingOperationSlotNextIndex;
        [NonSerialized] private int _loadingOperationSlotFreeHead = -1;

        // 索引映射
        [NonSerialized] private readonly ResourceUlongIntMap _assetRecordsByKey = new ResourceUlongIntMap();
        [NonSerialized] private readonly ResourceUlongIntMap _assetRecordByLoadKeyId = new ResourceUlongIntMap();
        [NonSerialized] private readonly ResourceUlongIntMap _assetRecordHeadByUnityObjectId = new ResourceUlongIntMap();
        [NonSerialized] private readonly ResourceUlongIntMap _assetLoadingOperationByKey = new ResourceUlongIntMap();

        // 过期队列
        [NonSerialized] private int[] _idleBuckets;
        [NonSerialized] private int[] _keepAliveBuckets;
        [NonSerialized] private int[] _unusedAssetCandidates;
        [NonSerialized] private int _lastKeepAliveProcessTick = -1;
        [NonSerialized] private int _lastIdleProcessTick = -1;
        [NonSerialized] private int _unusedAssetCandidateCount;
        [NonSerialized] private bool _idleCapacityTrimPending;

        // 资源名称注册表（package/location/type → ID）
        [NonSerialized] private string[] _resourcePackagesById;
        [NonSerialized] private string[] _resourceLocationsById;
        [NonSerialized] private Type[] _resourceTypesById;
        [NonSerialized] private int[] _resourcePackageRefCounts;
        [NonSerialized] private int[] _resourceLocationRefCounts;
        [NonSerialized] private int[] _resourceTypeRefCounts;
        [NonSerialized] private readonly Dictionary<string, int> _resourcePackageIds = new Dictionary<string, int>();
        [NonSerialized] private readonly Dictionary<string, int> _resourceLocationIds = new Dictionary<string, int>();
        [NonSerialized] private readonly Dictionary<Type, int> _resourceTypeIds = new Dictionary<Type, int>();
        [NonSerialized] private int _nextPackageId = 1;
        [NonSerialized] private int _nextLocationId = 1;
        [NonSerialized] private int _nextTypeId = 1;
        [NonSerialized] private readonly Stack<int> _freePackageIds = new Stack<int>();
        [NonSerialized] private readonly Stack<int> _freeLocationIds = new Stack<int>();
        [NonSerialized] private readonly Stack<int> _freeTypeIds = new Stack<int>();

        // 加载键自增
        [NonSerialized] private int _loadKeyNextId = 1;
        [NonSerialized] private uint _assetUnloadGeneration = 1;

        #endregion

        #region 公共 Lease API [PUBLIC LEASE API]

        /// <inheritdoc />
        public override ResourceLeaseHandle AcquireDirect(ResourceKey key)
        {
            ResourceKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new ResourceKey(key.Location, key.PackageName, typeof(UObject), InferAssetKind(typeof(UObject)))
                : key;

            string normalizedPackageName = NormalizePackageName(typedKey.PackageName);
            EResourceAssetKind assetKind = NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = NormalizeAssetType(typedKey.AssetType, assetKind);

            UObject asset = GetOrLoadAsset(typedKey.Location, assetType, assetKind, normalizedPackageName);
            if (asset == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            ulong recordKey = GetAssetRecordKey(normalizedPackageName, typedKey.Location, assetType, assetKind,
                EResourceHandleKind.AssetHandle);
            if (!_assetRecordsByKey.TryGetValue(recordKey, out int assetId) || !IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            return AcquireLease(assetId, EResourceLeaseKind.Direct, EResourceLeaseOption.None);
        }

        /// <inheritdoc />
        public override async UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key,
            CancellationToken cancellationToken = default)
        {
            ResourceKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new ResourceKey(key.Location, key.PackageName, typeof(UObject), InferAssetKind(typeof(UObject)))
                : key;

            string normalizedPackageName = NormalizePackageName(typedKey.PackageName);
            EResourceAssetKind assetKind = NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = NormalizeAssetType(typedKey.AssetType, assetKind);
            ulong loadingKey = GetLoadingOperationKey(typedKey.Location, normalizedPackageName, assetType, assetKind);

            UObject asset = await GetOrLoadAssetAsync(typedKey.Location, assetType, assetKind, normalizedPackageName,
                loadingKey, cancellationToken: cancellationToken);
            if (asset == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            ulong recordKey = GetAssetRecordKey(normalizedPackageName, typedKey.Location, assetType, assetKind,
                EResourceHandleKind.AssetHandle);
            if (!_assetRecordsByKey.TryGetValue(recordKey, out int assetId) || !IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            return AcquireLease(assetId, EResourceLeaseKind.Direct, EResourceLeaseOption.None);
        }

        /// <inheritdoc />
        public override void Release(ResourceLeaseHandle handle)
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
                    Mathf.Max(0, Mathf.CeilToInt(_idleAssetExpireTime));
                AddToKeepAliveBucket(assetId, ref kaAsset);
            }

            UpdateAssetStateAndIdleQueue(assetId, ref GetAssetSlotRef(assetId));
            FreeLeaseSlot(leaseIndex);
        }

        /// <inheritdoc />
        public override ResourceAssetLease<T> LoadLease<T>(ResourceKey key)
        {
            ResourceKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new ResourceKey(key.Location, key.PackageName, typeof(T), InferAssetKind(typeof(T)))
                : key;
            ResourceLeaseHandle handle = AcquireDirect(typedKey);
            if (!handle.IsValid)
            {
                return default;
            }

            if (!TryGetLeaseAsset(handle, out UObject asset) || asset is not T typedAsset)
            {
                Release(handle);
                return default;
            }

            return new ResourceAssetLease<T>(this, handle, typedAsset);
        }

        /// <inheritdoc />
        public override ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "")
        {
            return LoadLease<T>(new ResourceKey(location, packageName, typeof(T), InferAssetKind(typeof(T))));
        }

        /// <inheritdoc />
        public override async UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key,
            CancellationToken cancellationToken = default)
        {
            ResourceKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new ResourceKey(key.Location, key.PackageName, typeof(T), InferAssetKind(typeof(T)))
                : key;
            ResourceLeaseHandle handle = await AcquireDirectAsync(typedKey, cancellationToken);
            if (!handle.IsValid)
            {
                return default;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Release(handle);
                return default;
            }

            if (!TryGetLeaseAsset(handle, out UObject asset) || asset is not T typedAsset)
            {
                Release(handle);
                return default;
            }

            return new ResourceAssetLease<T>(this, handle, typedAsset);
        }

        /// <inheritdoc />
        public override UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location,
            CancellationToken cancellationToken = default, string packageName = "")
        {
            return LoadLeaseAsync<T>(
                new ResourceKey(location, packageName, typeof(T), InferAssetKind(typeof(T))), cancellationToken);
        }

        /// <inheritdoc />
        public override bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset)
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

        #endregion

        #region 内部 Lease 方法 [INTERNAL LEASE METHODS]

        internal override ResourceLeaseHandle AcquireBinding(ResourceKey key)
        {
            return AcquireDirect(key);
        }

        internal override UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key,
            CancellationToken cancellationToken)
        {
            return AcquireDirectAsync(key, cancellationToken);
        }

        internal override async UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location,
            string packageName, EResourceLeaseOption options, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(location))
            {
                return ResourceLeaseHandle.Invalid;
            }

            string normalizedPackageName = NormalizePackageName(packageName);
            ulong loadingKey = GetLoadingOperationKey(location, normalizedPackageName, typeof(Sprite),
                EResourceAssetKind.SubAssets);

            while (true)
            {
                if (cancellationToken.IsCancellationRequested || _isDestroying)
                {
                    return ResourceLeaseHandle.Invalid;
                }

                if (TryGetCachedSubAssetsRecord(normalizedPackageName, location, out int cachedAssetId))
                {
                    return AcquireLease(cachedAssetId, EResourceLeaseKind.Binding, options);
                }

                if (!TryBeginLoading(loadingKey))
                {
                    // 同一图集并发绑定：并入赢家的加载，不再各自发起一次 SubAssets 请求
                    if (!await WaitForLoadingAsync(loadingKey, cancellationToken))
                    {
                        return ResourceLeaseHandle.Invalid;
                    }

                    continue;
                }

                int loadGeneration = unchecked((int)_assetUnloadGeneration);
                SubAssetsHandle subHandle = null;
                try
                {
                    if (!IsLoadingStateCurrent(loadGeneration))
                    {
                        FailLoading(loadingKey, null);
                        return ResourceLeaseHandle.Invalid;
                    }

                    subHandle = GetSubAssetsHandleAsync(location, normalizedPackageName);
                    if (subHandle == null)
                    {
                        FailLoading(loadingKey, NewLoadingFailure("SubAssets", location, normalizedPackageName),
                            ELogLevel.Warning);
                        return ResourceLeaseHandle.Invalid;
                    }

                    AttachLoadingSubAssetsHandle(loadingKey, subHandle);
                    bool callerCancellationRequested = false;
                    if (!subHandle.IsDone)
                    {
                        await subHandle.ToUniTask(cancellationToken: cancellationToken);
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        callerCancellationRequested = true;
                    }

                    if (!IsLoadingStateCurrent(loadGeneration))
                    {
                        // 强卸载/关停已发生：句柄原样交回，绝不写进已 Dispose 的 Package
                        DisposeSubAssetsHandle(subHandle);
                        subHandle = null;
                        FailLoading(loadingKey, null);
                        return ResourceLeaseHandle.Invalid;
                    }

                    bool abortedByCallerCancellation = ShouldAbortLoadingAfterCallerCancellation(loadingKey,
                        cancellationToken, ref callerCancellationRequested);
                    bool loadFailed = !subHandle.IsValid || subHandle.Status == EOperationStatus.Failed;
                    if (abortedByCallerCancellation || loadFailed)
                    {
                        Exception failure = !abortedByCallerCancellation && loadFailed
                            ? NewLoadingFailure("SubAssets", location, normalizedPackageName, subHandle.Status,
                                subHandle.Error)
                            : null;
                        DisposeSubAssetsHandle(subHandle);
                        subHandle = null;
                        FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return ResourceLeaseHandle.Invalid;
                    }

                    int assetId = GetOrCreateSubAssetsRecord(normalizedPackageName, location, subHandle);
                    subHandle = null; // 所有权已移交记录，异常兜底不得再 dispose
                    CompleteLoading(loadingKey);
                    return callerCancellationRequested
                        ? ResourceLeaseHandle.Invalid
                        : AcquireLease(assetId, EResourceLeaseKind.Binding, options);
                }
                catch (OperationCanceledException)
                {
                    // 取消是本 API 的正常出口（契约返回 Invalid，不抛出），但已预留的去重槽必须闭环失败，
                    // 否则同图集后续并发绑定会在 WaitForLoadingAsync 里空转到各自超时/关停。
                    DisposeSubAssetsHandle(subHandle);
                    FailLoading(loadingKey, null);
                    return ResourceLeaseHandle.Invalid;
                }
                catch (Exception ex)
                {
                    DisposeSubAssetsHandle(subHandle);
                    FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource SubAssets load threw. Location:{0} Package:{1}", location, normalizedPackageName), ex));
                    return ResourceLeaseHandle.Invalid;
                }
            }
        }

        internal override bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite)
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
            if (slot.State == EResourceAssetState.Released || !IsSubAssetsHandleValid(slot.SubAssetsHandle))
            {
                return false;
            }

            sprite = slot.SubAssetsHandle.GetSubAssetObject<Sprite>(spriteName);
            return sprite != null;
        }

        internal override bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
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

        internal override void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options)
        {
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            lease.Flags = (byte)options;
        }

        internal override ResourceLeaseHandle AcquirePrefabSourceLease(string location, string packageName)
        {
            ResourceKey key = new ResourceKey(location, packageName, typeof(GameObject), EResourceAssetKind.Prefab);
            return AcquireDirect(key);
        }

        internal override async UniTask<ResourceLeaseHandle> AcquirePrefabSourceLeaseAsync(string location,
            string packageName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(location))
            {
                return ResourceLeaseHandle.Invalid;
            }

            string normalizedPackageName = NormalizePackageName(packageName);
            ulong loadingKey = GetLoadingOperationKey(location, normalizedPackageName, typeof(GameObject),
                EResourceAssetKind.Prefab);
            UObject asset = await GetOrLoadAssetAsync(location, typeof(GameObject), EResourceAssetKind.Prefab,
                normalizedPackageName, loadingKey, cancellationToken: cancellationToken);
            if (asset == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            ulong key = GetAssetRecordKey(normalizedPackageName, location, typeof(GameObject),
                EResourceAssetKind.Prefab, EResourceHandleKind.AssetHandle);
            if (!_assetRecordsByKey.TryGetValue(key, out int assetId) || !IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            return AcquireLease(assetId, EResourceLeaseKind.Direct, EResourceLeaseOption.None);
        }

        #endregion
        #region 资源记录管理 [ASSET RECORD MANAGEMENT]

        private ResourceLeaseHandle AcquireLease(int assetId, EResourceLeaseKind leaseKind,
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

        private int GetOrCreateAssetRecord(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind, UObject asset, AssetHandle assetHandle)
        {
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            string normalizedPackageName = NormalizePackageName(packageName);
            ulong key = GetAssetRecordKey(normalizedPackageName, location, assetType, assetKind, handleKind);
            if (_assetRecordsByKey.TryGetValue(key, out int existingId) && IsValidAssetId(existingId))
            {
                ref AssetSlot existing = ref GetAssetSlotRef(existingId);
                if (existing.Asset == null && asset != null)
                {
                    existing.Asset = asset;
                    existing.AssetInstanceId = UnityObjectId.Get(asset);
                    LinkAssetByUnityObject(existingId, ref existing);
                }

                if (assetHandle != null)
                {
                    if (existing.AssetHandle == null || !existing.AssetHandle.IsValid)
                    {
                        existing.AssetHandle = assetHandle;
                        existing.HandleKind = handleKind;
                    }
                    else if (!ReferenceEquals(existing.AssetHandle, assetHandle))
                    {
                        DisposeHandle(assetHandle);
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
            slot.AssetInstanceId = UnityObjectId.Get(asset);
            slot.AssetHandle = assetHandle;
            slot.AssetKind = assetKind;
            slot.HandleKind = handleKind;
            slot.NextByUnityObject = -1;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.NextFree = -1;
            slot.UnusedCandidateIndex = -1;
            slot.State = EResourceAssetState.Idle;
            _assetRecordsByKey.Set(key, assetId);
            RetainResourceKey(key);
            _assetRecordByLoadKeyId.Set((ulong)slot.LoadKeyId, assetId);
            LinkAssetByUnityObject(assetId, ref slot);
            UpdateAssetStateAndIdleQueue(assetId, ref slot);
            return assetId;
        }

        private int GetOrCreateSubAssetsRecord(string packageName, string location, SubAssetsHandle subAssetsHandle)
        {
            string normalizedPackageName = NormalizePackageName(packageName);
            ulong key = GetAssetRecordKey(normalizedPackageName, location, typeof(Sprite),
                EResourceAssetKind.SubAssets, EResourceHandleKind.SubAssetsHandle);
            if (_assetRecordsByKey.TryGetValue(key, out int existingId) && IsValidAssetId(existingId))
            {
                ref AssetSlot existing = ref GetAssetSlotRef(existingId);
                if (!IsSubAssetsHandleValid(existing.SubAssetsHandle) &&
                    IsSubAssetsHandleValid(subAssetsHandle))
                {
                    existing.SubAssetsHandle = subAssetsHandle;
                    existing.HandleKind = EResourceHandleKind.SubAssetsHandle;
                }
                else if (IsSubAssetsHandleValid(subAssetsHandle) &&
                         !ReferenceEquals(existing.SubAssetsHandle, subAssetsHandle))
                {
                    DisposeSubAssetsHandle(subAssetsHandle);
                }

                UpdateAssetStateAndIdleQueue(existingId, ref existing);
                return existingId;
            }

            int assetId = AllocateAssetSlot();
            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            slot.Key = key;
            slot.LoadKeyId = AllocateLoadKeyId();
            slot.Asset = null;
            slot.AssetInstanceId = 0;
            slot.SubAssetsHandle = subAssetsHandle;
            slot.AssetKind = EResourceAssetKind.SubAssets;
            slot.HandleKind = EResourceHandleKind.SubAssetsHandle;
            slot.NextByUnityObject = -1;
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

        private bool TryGetCachedSubAssetsRecord(string normalizedPackageName, string location, out int assetId)
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
            if (slot.State == EResourceAssetState.Released || !IsSubAssetsHandleValid(slot.SubAssetsHandle))
            {
                return false;
            }

            assetId = existingId;
            return true;
        }

        private bool TryGetCachedAssetRecord(string packageName, string location, Type assetType,
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
        #region Legacy 桥接 [LEGACY BRIDGING]

        private bool TryAddLegacyDirectRef(int assetId, uint generation)
        {
            if (!IsValidAssetId(assetId))
            {
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.Generation != generation || slot.State == EResourceAssetState.Released)
            {
                return false;
            }

            slot.LegacyDirectRefCount++;
            UpdateAssetStateAndIdleQueue(assetId, ref slot);
            return true;
        }

        private bool TryAddLegacyDirectRefByKey(string packageName, string location, Type assetType, UObject asset)
        {
            EResourceAssetKind assetKind = InferAssetKind(assetType);
            assetType = NormalizeAssetType(assetType, assetKind);
            ulong key = GetAssetRecordKey(packageName, location, assetType, assetKind,
                EResourceHandleKind.AssetHandle);
            if (_assetRecordsByKey.TryGetValue(key, out int assetId) && IsValidAssetId(assetId))
            {
                ref AssetSlot slot = ref GetAssetSlotRef(assetId);
                return TryAddLegacyDirectRef(assetId, slot.Generation);
            }

            return TryAddLegacyDirectRefByAsset(asset);
        }

        private bool TryAddLegacyDirectRefByAsset(UObject asset)
        {
            if (asset == null)
            {
                return false;
            }

            ulong instanceId = UnityObjectId.Get(asset);
            if (!_assetRecordHeadByUnityObjectId.TryGetValue(instanceId, out int current))
            {
                return false;
            }

            int matchedAssetId = -1;
            while (current >= 0)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.NextByUnityObject;
                if (slot.AssetInstanceId == instanceId && slot.State != EResourceAssetState.Released)
                {
                    matchedAssetId = current;
                    break;
                }

                current = next;
            }

            if (matchedAssetId < 0)
            {
                return false;
            }

            ref AssetSlot matched = ref GetAssetSlotRef(matchedAssetId);
            return TryAddLegacyDirectRef(matchedAssetId, matched.Generation);
        }

        private bool TryReleaseLegacyDirectByAsset(object asset)
        {
            if (asset is not UObject unityObject)
            {
                return false;
            }

            ulong instanceId = UnityObjectId.Get(unityObject);
            if (!_assetRecordHeadByUnityObjectId.TryGetValue(instanceId, out int current))
            {
                return false;
            }

            int matchedAssetId = -1;
            while (current >= 0)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.NextByUnityObject;
                if (slot.AssetInstanceId == instanceId && slot.LegacyDirectRefCount > 0 &&
                    slot.State != EResourceAssetState.Released)
                {
                    matchedAssetId = current;
                    break;
                }

                current = next;
            }

            if (matchedAssetId < 0)
            {
                return false;
            }

            ref AssetSlot matched = ref GetAssetSlotRef(matchedAssetId);
            matched.LegacyDirectRefCount--;
            UpdateAssetStateAndIdleQueue(matchedAssetId, ref matched);
            return true;
        }

        #endregion
        #region 诊断 [DIAGNOSTICS]

        public override int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount)
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
                info.Package = GetPackageNameById(UnpackPackageId(slot.Key));
                info.Location = GetLocationNameById(UnpackLocationId(slot.Key));
                Type assetType = GetAssetTypeById(UnpackTypeId(slot.Key));
                info.TypeName = assetType != null ? assetType.Name : string.Empty;
                info.Kind = slot.AssetKind;
                info.State = slot.State;
                info.DirectRefCount = slot.DirectRefCount;
                info.LegacyDirectRefCount = slot.LegacyDirectRefCount;
                info.BindingRefCount = slot.BindingRefCount;
                info.KeepAliveRefCount = slot.KeepAliveRefCount;
                info.RefCountTotal = slot.DirectRefCount + slot.LegacyDirectRefCount +
                    slot.BindingRefCount + slot.KeepAliveRefCount;
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

        #endregion
        #region 辅助方法 [UTILITY METHODS]

        private static bool IsSlotHandleValid(ref AssetSlot slot)
        {
            return slot.HandleKind == EResourceHandleKind.SubAssetsHandle
                ? IsSubAssetsHandleValid(slot.SubAssetsHandle)
                : slot.AssetHandle is { IsValid: true };
        }

        private static bool IsSubAssetsHandleValid(SubAssetsHandle handle)
        {
            return handle != null && handle.IsValid;
        }

        private static void DisposeSubAssetsHandle(SubAssetsHandle handle)
        {
            if (IsSubAssetsHandleValid(handle))
            {
                handle.Dispose();
            }
        }

        private static void DisposeHandle(AssetHandle handle)
        {
            if (handle is { IsValid: true })
            {
                handle.Dispose();
            }
        }

        private void DisposeAssetSlotHandle(ref AssetSlot slot)
        {
            AssetHandle handle = slot.AssetHandle;
            if (handle is { IsValid: true })
            {
                handle.Dispose();
            }

            DisposeSubAssetsHandle(slot.SubAssetsHandle);
            slot.AssetHandle = null;
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

        private void LinkAssetByUnityObject(int assetId, ref AssetSlot slot)
        {
            if (slot.AssetInstanceId == 0)
            {
                return;
            }

            if (_assetRecordHeadByUnityObjectId.TryGetValue(slot.AssetInstanceId, out int head))
            {
                slot.NextByUnityObject = head;
            }
            else
            {
                slot.NextByUnityObject = -1;
            }

            _assetRecordHeadByUnityObjectId.Set(slot.AssetInstanceId, assetId);
        }

        private void UnlinkAssetByUnityObject(int assetId, ref AssetSlot slot)
        {
            ulong instanceId = slot.AssetInstanceId;
            if (instanceId == 0 || !_assetRecordHeadByUnityObjectId.TryGetValue(instanceId, out int current))
            {
                return;
            }

            int previous = -1;
            while (current >= 0)
            {
                ref AssetSlot currentSlot = ref GetAssetSlotRef(current);
                int next = currentSlot.NextByUnityObject;
                if (current == assetId)
                {
                    if (previous >= 0)
                    {
                        ref AssetSlot previousSlot = ref GetAssetSlotRef(previous);
                        previousSlot.NextByUnityObject = next;
                    }
                    else if (next >= 0)
                    {
                        _assetRecordHeadByUnityObjectId.Set(instanceId, next);
                    }
                    else
                    {
                        _assetRecordHeadByUnityObjectId.Remove(instanceId);
                    }

                    currentSlot.NextByUnityObject = -1;
                    return;
                }

                previous = current;
                current = next;
            }
        }

        private ResourceOwner EnsureResourceOwner(GameObject root)
        {
            ResourceOwner owner = root.GetComponent<ResourceOwner>();
            if (owner == null)
            {
                owner = root.AddComponent<ResourceOwner>();
            }

            _bindingService.RegisterOwner(owner);
            return owner;
        }

        private static void ClearAssetSlot(ref AssetSlot slot, bool preserveGeneration)
        {
            uint generation = slot.Generation;
            slot = default;
            slot.Generation = preserveGeneration ? generation : 0;
            slot.NextByUnityObject = -1;
            slot.NextFree = -1;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.ExpireQueueBucket = -1;
            slot.UnusedCandidateIndex = -1;
            slot.State = EResourceAssetState.Released;
        }

        private static void ClearLoadingOperationSlot(ref LoadingOperationSlot slot)
        {
            slot.Key = 0;
            slot.Operation = null;
            slot.State = 0;
            slot.NextFree = -1;
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
            slot.NextByUnityObject = -1;
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

        private bool IsValidAssetId(int assetId)
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

        private bool IsValidLoadingOperationSlotId(int index)
        {
            return index >= 0 && index < _loadingOperationSlotNextIndex && _loadingOperationSlotPages != null;
        }

        private ref AssetSlot GetAssetSlotRef(int index)
        {
            return ref _assetSlotPages[index >> RECORD_PAGE_BITS][index & RECORD_PAGE_MASK];
        }

        private ref LeaseSlot GetLeaseSlotRef(int index)
        {
            return ref _leaseSlotPages[index >> RECORD_PAGE_BITS][index & RECORD_PAGE_MASK];
        }

        private ref LoadingOperationSlot GetLoadingOperationSlotRef(int index)
        {
            return ref _loadingOperationSlotPages[index >> RECORD_PAGE_BITS][index & RECORD_PAGE_MASK];
        }

        private void EnsureAssetSlotPage(int index)
        {
            int pageIndex = index >> RECORD_PAGE_BITS;
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
                _assetSlotPages[pageIndex] = new AssetSlot[RECORD_PAGE_SIZE];
            }
        }

        private void EnsureLeaseSlotPage(int index)
        {
            int pageIndex = index >> RECORD_PAGE_BITS;
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
                _leaseSlotPages[pageIndex] = new LeaseSlot[RECORD_PAGE_SIZE];
            }
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

        #endregion
    }
}
