using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源服务核心记录管理——分页 slot 系统、generation 验证、Timer-wheel 过期、加载去重。
    /// </summary>
    public sealed partial class YooAssetHandler
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
            public Object Asset;
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

        private struct AssetInfoSlot
        {
            public ulong Key;
            public AssetInfo AssetInfo;
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

        // AssetInfo 缓存分页数组
        [NonSerialized] private AssetInfoSlot[][] _assetInfoSlotPages;
        [NonSerialized] private int _assetInfoSlotNextIndex;
        [NonSerialized] private int _assetInfoSlotFreeHead = -1;

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
                ? new ResourceKey(key.Location, key.PackageName, typeof(Object), InferAssetKind(typeof(Object)))
                : key;

            string normalizedPackageName = NormalizePackageName(typedKey.PackageName);
            EResourceAssetKind assetKind = NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = NormalizeAssetType(typedKey.AssetType, assetKind);
            ulong loadingKey = GetLoadingOperationKey(typedKey.Location, normalizedPackageName, assetType, assetKind);

            Object asset = GetOrLoadAsset(typedKey.Location, assetType, assetKind, normalizedPackageName);
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
                ? new ResourceKey(key.Location, key.PackageName, typeof(Object), InferAssetKind(typeof(Object)))
                : key;

            string normalizedPackageName = NormalizePackageName(typedKey.PackageName);
            EResourceAssetKind assetKind = NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = NormalizeAssetType(typedKey.AssetType, assetKind);
            ulong loadingKey = GetLoadingOperationKey(typedKey.Location, normalizedPackageName, assetType, assetKind);

            Object asset = await GetOrLoadAssetAsync(typedKey.Location, assetType, assetKind, normalizedPackageName,
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
        public override bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle)
        {
            handle = AcquireDirect(key);
            return handle.IsValid;
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

            if (!TryGetLeaseAsset(handle, out Object asset) || asset is not T typedAsset)
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

            if (!TryGetLeaseAsset(handle, out Object asset) || asset is not T typedAsset)
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
        public override bool TryGetLeaseAsset(ResourceLeaseHandle handle, out Object asset)
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

            SubAssetsHandle subHandle = GetSubAssetsHandleAsync(location, normalizedPackageName);
            if (subHandle == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            AttachLoadingSubAssetsHandle(loadingKey, subHandle);
            if (!subHandle.IsDone)
            {
                await subHandle.ToUniTask(cancellationToken: cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested || !subHandle.IsValid ||
                subHandle.Status == EOperationStatus.Failed)
            {
                DisposeSubAssetsHandle(subHandle);
                return ResourceLeaseHandle.Invalid;
            }

            int assetId = GetOrCreateSubAssetsRecord(normalizedPackageName, location, subHandle);
            CompleteLoading(loadingKey);
            return AcquireLease(assetId, EResourceLeaseKind.Binding, options);
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
            Object asset = await GetOrLoadAssetAsync(location, typeof(GameObject), EResourceAssetKind.Prefab,
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

        #region 资源加载核心 [CORE ASSET LOADING]

        private Object GetOrLoadAsset(string location, Type assetType, EResourceAssetKind assetKind,
            string packageName)
        {
            string normalizedPackageName = NormalizePackageName(packageName);
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            ulong loadingKey = GetLoadingOperationKey(location, normalizedPackageName, assetType, assetKind);

            while (true)
            {
                if (_isDestroying)
                {
                    return null;
                }

                if (TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out Object cachedAsset))
                {
                    return cachedAsset;
                }

                if (!TryBeginLoading(loadingKey))
                {
                    AssetHandle joinHandle = GetHandleSync(location, assetType, packageName);
                    if (joinHandle == null || joinHandle.AssetObject == null ||
                        joinHandle.Status == EOperationStatus.Failed)
                    {
                        DisposeHandle(joinHandle);
                        return null;
                    }

                    GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, joinHandle.AssetObject, joinHandle);
                    return TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out cachedAsset)
                        ? cachedAsset
                        : null;
                }

                int loadGeneration = unchecked((int)_assetUnloadGeneration);
                if (!IsLoadingStateCurrent(loadGeneration))
                {
                    FailLoading(loadingKey, null);
                    return null;
                }

                AssetHandle handle = GetHandleSync(location, assetType, packageName);
                if (handle == null || handle.AssetObject == null || handle.Status == EOperationStatus.Failed)
                {
                    DisposeHandle(handle);
                    FailLoading(loadingKey, null);
                    return null;
                }

                GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                    EResourceHandleKind.AssetHandle, handle.AssetObject, handle);
                CompleteLoading(loadingKey);
                return handle.AssetObject;
            }
        }

        private async UniTask<Object> GetOrLoadAssetAsync(string location, Type assetType,
            EResourceAssetKind assetKind, string packageName, ulong loadingKey,
            uint priority = 0, CancellationToken cancellationToken = default,
            LoadAssetUpdateCallback loadAssetUpdateCallback = null, object userData = null)
        {
            string normalizedPackageName = NormalizePackageName(packageName);
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                if (_isDestroying)
                {
                    return null;
                }

                if (TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out Object cachedAsset))
                {
                    return cachedAsset;
                }

                if (!TryBeginLoading(loadingKey))
                {
                    if (!await WaitForLoadingAsync(loadingKey, cancellationToken))
                    {
                        return null;
                    }

                    continue;
                }

                int loadGeneration = unchecked((int)_assetUnloadGeneration);
                if (!IsLoadingStateCurrent(loadGeneration))
                {
                    FailLoading(loadingKey, null);
                    return null;
                }

                AssetHandle handle = GetHandleAsync(location, assetType, packageName: packageName, priority: priority);
                if (handle == null)
                {
                    FailLoading(loadingKey, null);
                    return null;
                }

                AttachLoadingAssetHandle(loadingKey, handle);
                StartProgressTask(location, handle, loadAssetUpdateCallback, userData, cancellationToken);
                bool callerCancellationRequested = false;
                if (!handle.IsDone)
                {
                    await handle.ToUniTask(cancellationToken: cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    callerCancellationRequested = true;
                }

                if (!IsLoadingStateCurrent(loadGeneration))
                {
                    DisposeHandle(handle);
                    FailLoading(loadingKey, null);
                    return null;
                }

                if (ShouldAbortLoadingAfterCallerCancellation(loadingKey, cancellationToken, ref callerCancellationRequested))
                {
                    DisposeHandle(handle);
                    FailLoading(loadingKey, null);
                    return null;
                }

                if (!handle.IsValid || handle.AssetObject == null || handle.Status == EOperationStatus.Failed)
                {
                    DisposeHandle(handle);
                    FailLoading(loadingKey, null);
                    return null;
                }

                if (_isDestroying)
                {
                    DisposeHandle(handle);
                    FailLoading(loadingKey, null);
                    return null;
                }

                GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                    EResourceHandleKind.AssetHandle, handle.AssetObject, handle);
                CompleteLoading(loadingKey);
                if (callerCancellationRequested)
                {
                    return null;
                }

                return TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out cachedAsset)
                    ? cachedAsset
                    : null;
            }
        }

        #endregion

        #region 加载去重 [LOADING DEDUP]

        private bool TryBeginLoading(ulong assetObjectKey)
        {
            bool keyAlreadyRetained = false;
            if (_assetLoadingOperationByKey.TryGetValue(assetObjectKey, out int existingSlotIndex))
            {
                if (IsValidLoadingOperationSlotId(existingSlotIndex))
                {
                    ref LoadingOperationSlot existingSlot = ref GetLoadingOperationSlotRef(existingSlotIndex);
                    if (existingSlot.State == 1 && existingSlot.Key == assetObjectKey &&
                        existingSlot.Operation != null)
                    {
                        return false;
                    }
                }

                _assetLoadingOperationByKey.Remove(assetObjectKey);
                keyAlreadyRetained = true;
            }

            int slotIndex = AllocateLoadingOperationSlot();
            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(slotIndex);
            slot.Key = assetObjectKey;
            slot.Operation = MemoryPool.Acquire<LoadingOperationState>();
            slot.State = 1;
            _assetLoadingOperationByKey.Set(assetObjectKey, slotIndex);
            if (!keyAlreadyRetained)
            {
                RetainResourceKey(assetObjectKey);
            }

            return true;
        }

        private void AttachLoadingAssetHandle(ulong assetObjectKey, AssetHandle handle)
        {
            if (TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                loadingOperation.AssetHandle = handle;
            }
        }

        private void AttachLoadingSubAssetsHandle(ulong assetObjectKey, SubAssetsHandle handle)
        {
            if (TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                loadingOperation.SubAssetsHandle = handle;
            }
        }

        private async UniTask<bool> WaitForLoadingAsync(ulong assetObjectKey,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                return true;
            }

            loadingOperation.AddWaiter();
            while (!loadingOperation.IsDone)
            {
                if (cancellationToken.IsCancellationRequested || _isDestroying)
                {
                    loadingOperation.RemoveWaiter();
                    ReleaseLoadingOperationIfReady(loadingOperation);
                    return false;
                }

                await UniTask.Yield();
            }

            bool succeeded = loadingOperation.Succeeded;
            loadingOperation.RemoveWaiter();
            ReleaseLoadingOperationIfReady(loadingOperation);
            return succeeded;
        }

        private void CompleteLoading(ulong assetObjectKey)
        {
            if (!TryRemoveLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                return;
            }

            loadingOperation.Complete(true);
            loadingOperation.RequestRelease();
            ReleaseLoadingOperationIfReady(loadingOperation);
        }

        private void FailLoading(ulong assetObjectKey, Exception exception)
        {
            if (!TryRemoveLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                return;
            }

            loadingOperation.Complete(false);
            loadingOperation.RequestRelease();
            ReleaseLoadingOperationIfReady(loadingOperation);
        }

        private bool TryGetLoadingOperation(ulong assetObjectKey, out LoadingOperationState loadingOperation)
        {
            loadingOperation = null;
            if (!_assetLoadingOperationByKey.TryGetValue(assetObjectKey, out int slotIndex) ||
                !IsValidLoadingOperationSlotId(slotIndex))
            {
                return false;
            }

            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(slotIndex);
            if (slot.State != 1 || slot.Key != assetObjectKey || slot.Operation == null)
            {
                return false;
            }

            loadingOperation = slot.Operation;
            return true;
        }

        private bool TryRemoveLoadingOperation(ulong assetObjectKey, out LoadingOperationState loadingOperation)
        {
            loadingOperation = null;
            if (!_assetLoadingOperationByKey.TryGetValue(assetObjectKey, out int slotIndex) ||
                !IsValidLoadingOperationSlotId(slotIndex))
            {
                return false;
            }

            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(slotIndex);
            if (slot.State != 1 || slot.Key != assetObjectKey || slot.Operation == null)
            {
                _assetLoadingOperationByKey.Remove(assetObjectKey);
                ReleaseResourceKey(assetObjectKey);
                return false;
            }

            loadingOperation = slot.Operation;
            _assetLoadingOperationByKey.Remove(assetObjectKey);
            ReleaseResourceKey(assetObjectKey);
            FreeLoadingOperationSlot(slotIndex);
            return true;
        }

        private bool HasLoadingWaiters(ulong assetObjectKey)
        {
            return TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation) &&
                   loadingOperation.WaiterCount > 0;
        }

        private bool IsLoadingStateCurrent(int loadGeneration)
        {
            return !_isDestroying && loadGeneration == unchecked((int)_assetUnloadGeneration);
        }

        private bool ShouldAbortLoadingAfterCallerCancellation(ulong assetObjectKey,
            CancellationToken cancellationToken, ref bool callerCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                callerCancellationRequested = true;
            }

            return callerCancellationRequested && !HasLoadingWaiters(assetObjectKey);
        }

        private static void ReleaseLoadingOperationIfReady(LoadingOperationState loadingOperation)
        {
            if (loadingOperation is { ReleaseRequested: true, WaiterCount: 0 })
            {
                MemoryPool.Release(loadingOperation);
            }
        }

        private void ShutdownLoadingOperations()
        {
            int total = _loadingOperationSlotNextIndex;
            for (int i = 0; i < total; i++)
            {
                ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(i);
                if (slot.State != 1 || slot.Operation == null)
                {
                    continue;
                }

                LoadingOperationState loadingOperation = slot.Operation;
                loadingOperation.Complete(false);
                loadingOperation.RequestRelease();
                ReleaseLoadingOperationIfReady(loadingOperation);
                ClearLoadingOperationSlot(ref slot);
            }

            ReleaseAllResourceKeysFromMap(_assetLoadingOperationByKey);
            _assetLoadingOperationByKey.Clear();
            _loadingOperationSlotPages = null;
            _loadingOperationSlotNextIndex = 0;
            _loadingOperationSlotFreeHead = -1;
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
            EResourceAssetKind assetKind, EResourceHandleKind handleKind, Object asset, AssetHandle assetHandle)
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

        private bool TryGetCachedAssetRecord(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind, out int assetId, out Object asset)
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

        private bool TryAddLegacyDirectRefByKey(string packageName, string location, Type assetType, Object asset)
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

        private bool TryAddLegacyDirectRefByAsset(Object asset)
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
            if (asset is not Object unityObject)
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

        #region 过期回收 [EXPIRY & RECYCLING]

        internal override void ProcessKeepAlive(float unscaledTime, int maxCount)
        {
            if ((_keepAliveBuckets == null && _idleBuckets == null) || maxCount <= 0)
            {
                return;
            }

            int currentTick = ToKeepAliveTick(unscaledTime);
            int processed = ProcessDueKeepAliveBuckets(currentTick, maxCount);
            if (processed < maxCount)
            {
                ProcessDueIdleBuckets(currentTick, maxCount - processed);
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

        internal override int ReleaseAllUnusedAssetRecords()
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

        internal override void ForceReleaseAllAssetRecords()
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
                UnlinkAssetByUnityObject(i, ref slot);
                ClearAssetSlot(ref slot, preserveGeneration: true);
                FreeAssetSlot(i);
            }

            ReleaseAllResourceKeysFromMap(_assetRecordsByKey);
            _assetRecordsByKey.Clear();
            _assetRecordByLoadKeyId.Clear();
            _assetRecordHeadByUnityObjectId.Clear();
            _unusedAssetCandidateCount = 0;

            _leaseSlotNextIndex = 0;
            _leaseSlotFreeHead = -1;
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
            UnlinkAssetByUnityObject(assetId, ref slot);
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
                   slot.LegacyDirectRefCount == 0 &&
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

            if (slot.DirectRefCount + slot.LegacyDirectRefCount + slot.BindingRefCount > 0)
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

        #endregion

        #region Timer-Wheel [TIMER WHEEL]

        private void EnterIdle(int assetId, ref AssetSlot slot)
        {
            if (!IsSlotHandleValid(ref slot))
            {
                return;
            }

            int expireTick = ToKeepAliveTick(Time.unscaledTime) + Mathf.Max(0, Mathf.CeilToInt(_idleAssetExpireTime));
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

        #endregion

        #region Unused Candidate 管理 [UNUSED CANDIDATE MANAGEMENT]

        private void AddUnusedAssetCandidate(int assetId, ref AssetSlot slot)
        {
            if (slot.UnusedCandidateIndex >= 0)
            {
                return;
            }

            if (_unusedAssetCandidates == null)
            {
                _unusedAssetCandidates = new int[Math.Max(16, _assetRecordCapacity)];
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
                int currentTick = ToKeepAliveTick(Time.unscaledTime);
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

        #endregion

        #region 资源名称注册表 [RESOURCE NAME REGISTRY]

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
            assetType ??= typeof(Object);
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

        #endregion

        #region 辅助方法 [UTILITY METHODS]

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

            return assetType ?? typeof(Object);
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

        private static int ToKeepAliveTick(float unscaledTime)
        {
            return Mathf.Max(0, Mathf.FloorToInt(unscaledTime));
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

        private void StartProgressTask(string location, AssetHandle handle,
            LoadAssetUpdateCallback loadAssetUpdateCallback, object userData, CancellationToken cancellationToken)
        {
            if (loadAssetUpdateCallback != null && handle is { IsValid: true, IsDone: false })
            {
                InvokeProgress(location, handle, loadAssetUpdateCallback, userData, cancellationToken).Forget();
            }
        }

        private async UniTaskVoid InvokeProgress(string location, AssetHandle assetHandle,
            LoadAssetUpdateCallback loadAssetUpdateCallback, object userData, CancellationToken cancellationToken)
        {
            if (loadAssetUpdateCallback != null)
            {
                float lastReportedProgress = -1f;
                while (assetHandle is { IsValid: true, IsDone: false })
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    await UniTask.Yield();
                    float progress = assetHandle.Progress;
                    if (lastReportedProgress < 0f || progress - lastReportedProgress >= PROGRESS_CALLBACK_THRESHOLD)
                    {
                        lastReportedProgress = progress;
                        loadAssetUpdateCallback.Invoke(location, progress, userData);
                    }
                }

                if (!cancellationToken.IsCancellationRequested && assetHandle is { IsValid: true } &&
                    lastReportedProgress < 1f)
                {
                    loadAssetUpdateCallback.Invoke(location, 1f, userData);
                }
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

        private SubAssetsHandle GetSubAssetsHandleAsync(string location, string packageName)
        {
            return GetPackageOrThrow(packageName).LoadSubAssetsAsync<Sprite>(location);
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
