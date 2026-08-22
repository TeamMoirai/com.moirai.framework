using System;
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
    internal sealed partial class ResourceService
    {
        #region 常量 [CONSTANTS]

        private const int RecordPageBits = 8;
        private const int RecordPageSize = 1 << RecordPageBits;
        private const int RecordPageMask = RecordPageSize - 1;
        private const int IdleBucketCount = 256;
        private const int KeepAliveBucketCount = 256;
        private const float ProgressCallbackThreshold = 0.01f;

        #region packed key 位域常量 [PACKED KEY BIT FIELDS]

        private const int ResourceKeyPackageShift = 48;
        private const int ResourceKeyLocationShift = 20;
        private const int ResourceKeyTypeShift = 4;
        private const int ResourceKeyAssetKindShift = 2;
        private const int ResourceKeyHandleShift = 0;

        private const int ResourceKeyPackageMax = 0xFFFF;
        private const int ResourceKeyLocationMax = 0xFFFFFFF;
        private const int ResourceKeyTypeMax = 0xFFFF;
        private const int ResourceKeyAssetKindMax = 0x3;
        private const int ResourceKeyHandleMax = 0x3;

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

        private bool _isDestroying;

        // 资源记录分页数组
        private AssetSlot[][] _assetSlotPages;
        private int _assetSlotNextIndex;
        private int _assetSlotFreeHead = -1;

        // 租约分页数组
        private LeaseSlot[][] _leaseSlotPages;
        private int _leaseSlotNextIndex;
        private int _leaseSlotFreeHead = -1;

        // 加载操作分页数组
        private LoadingOperationSlot[][] _loadingOperationSlotPages;
        private int _loadingOperationSlotNextIndex;
        private int _loadingOperationSlotFreeHead = -1;

        // AssetInfo 缓存分页数组
        private AssetInfoSlot[][] _assetInfoSlotPages;
        private int _assetInfoSlotNextIndex;
        private int _assetInfoSlotFreeHead = -1;

        // 索引映射
        private readonly ResourceUlongIntMap _assetRecordsByKey = new();
        private readonly ResourceUlongIntMap _assetRecordByLoadKeyId = new();
        private readonly ResourceUlongIntMap _assetRecordHeadByUnityObjectId = new();
        private readonly ResourceUlongIntMap _assetLoadingOperationByKey = new();
        private readonly ResourceUlongIntMap _assetInfoByKey = new();

        // 过期队列
        private int[] _idleBuckets;
        private int[] _keepAliveBuckets;
        private int[] _unusedAssetCandidates;
        private int _unusedAssetCandidateCount;

        // 资源名称注册表（package/location/type → ID）
        private string[] _resourcePackagesById;
        private string[] _resourceLocationsById;
        private Type[] _resourceTypesById;
        private int[] _resourcePackageRefCounts;
        private int[] _resourceLocationRefCounts;
        private int[] _resourceTypeRefCounts;
        private int _nextPackageId = 1;
        private int _nextLocationId = 1;
        private int _nextTypeId = 1;
        private readonly System.Collections.Generic.Stack<int> _freePackageIds = new();
        private readonly System.Collections.Generic.Stack<int> _freeLocationIds = new();
        private readonly System.Collections.Generic.Stack<int> _freeTypeIds = new();

        // 加载键自增
        private int _loadKeyNextId = 1;
        private uint _assetUnloadGeneration = 1;

        #endregion

        #region 公共 Lease API [PUBLIC LEASE API]

        /// <inheritdoc />
        public ResourceLeaseHandle AcquireDirect(ResourceKey key)
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
        public async UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key,
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
        public bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle)
        {
            handle = AcquireDirect(key);
            return handle.IsValid;
        }

        /// <inheritdoc />
        public void Release(ResourceLeaseHandle handle)
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
        public ResourceAssetLease<T> LoadLease<T>(ResourceKey key) where T : Object
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
        public ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "") where T : Object
        {
            return LoadLease<T>(new ResourceKey(location, packageName, typeof(T), InferAssetKind(typeof(T))));
        }

        /// <inheritdoc />
        public async UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key,
            CancellationToken cancellationToken = default) where T : Object
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
        public UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location,
            CancellationToken cancellationToken = default, string packageName = "") where T : Object
        {
            return LoadLeaseAsync<T>(
                new ResourceKey(location, packageName, typeof(T), InferAssetKind(typeof(T))), cancellationToken);
        }

        /// <inheritdoc />
        public bool TryGetLeaseAsset(ResourceLeaseHandle handle, out Object asset)
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

        internal ResourceLeaseHandle AcquireBinding(ResourceKey key)
        {
            return AcquireDirect(key);
        }

        internal UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key,
            CancellationToken cancellationToken)
        {
            return AcquireDirectAsync(key, cancellationToken);
        }

        internal async UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location,
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
            if (slot.State == EResourceAssetState.Released || !IsSubAssetsHandleValid(slot.SubAssetsHandle))
            {
                return false;
            }

            sprite = slot.SubAssetsHandle.GetSubAssetObject<Sprite>(spriteName);
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

        internal ResourceLeaseHandle AcquirePrefabSourceLease(string location, string packageName)
        {
            ResourceKey key = new ResourceKey(location, packageName, typeof(GameObject), EResourceAssetKind.Prefab);
            return AcquireDirect(key);
        }

        internal async UniTask<ResourceLeaseHandle> AcquirePrefabSourceLeaseAsync(string location,
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

                AssetHandle handle = GetHandleAsync(location, assetType, packageName: packageName);
                if (handle == null)
                {
                    FailLoading(loadingKey, null);
                    return null;
                }

                AttachLoadingAssetHandle(loadingKey, handle);
                StartProgressTask(location, handle, loadAssetUpdateCallback, userData, cancellationToken);

                if (!handle.IsDone)
                {
                    await handle.ToUniTask(cancellationToken: cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested ||
                    !handle.IsValid || handle.AssetObject == null || handle.Status == EOperationStatus.Failed ||
                    _isDestroying)
                {
                    DisposeHandle(handle);
                    FailLoading(loadingKey, null);
                    return null;
                }

                GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                    EResourceHandleKind.AssetHandle, handle.AssetObject, handle);
                CompleteLoading(loadingKey);
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
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            ulong key = GetAssetRecordKey(packageName, location, assetType, assetKind, handleKind);
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

        internal void ProcessKeepAlive(float unscaledTime, int maxProcessCount)
        {
            if (_idleBuckets == null)
            {
                return;
            }

            int currentTick = ToKeepAliveTick(unscaledTime);
            int processed = 0;

            // 处理 idle 过期
            for (int i = 0; i < IdleBucketCount && processed < maxProcessCount; i++)
            {
                int bucketIndex = (currentTick - i) & (IdleBucketCount - 1);
                int current = _idleBuckets[bucketIndex];
                while (current >= 0 && processed < maxProcessCount)
                {
                    ref AssetSlot slot = ref GetAssetSlotRef(current);
                    int next = slot.ExpireQueueNext;
                    if (slot.ExpireQueueKind == 2 && slot.IdleExpireTick <= currentTick)
                    {
                        if (HasNoResourceRefs(ref slot))
                        {
                            slot.IdleReleaseRequested = 1;
                            ReleaseAssetStorage(current, slot.Generation);
                            processed++;
                        }
                    }

                    current = next;
                }
            }

            // 处理 keep-alive 过期
            if (_keepAliveBuckets != null)
            {
                for (int i = 0; i < KeepAliveBucketCount && processed < maxProcessCount; i++)
                {
                    int bucketIndex = (currentTick - i) & (KeepAliveBucketCount - 1);
                    int current = _keepAliveBuckets[bucketIndex];
                    while (current >= 0 && processed < maxProcessCount)
                    {
                        ref AssetSlot slot = ref GetAssetSlotRef(current);
                        int next = slot.ExpireQueueNext;
                        if (slot.ExpireQueueKind == 1 && slot.KeepAliveExpireTick <= currentTick)
                        {
                            slot.KeepAliveRefCount = Math.Max(0, slot.KeepAliveRefCount - 1);
                            if (slot.KeepAliveRefCount == 0)
                            {
                                RemoveFromKeepAliveBucket(current, ref slot);
                                UpdateAssetStateAndIdleQueue(current, ref slot);
                                processed++;
                            }
                        }

                        current = next;
                    }
                }
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
            if (_idleBuckets == null)
            {
                _idleBuckets = new int[IdleBucketCount];
                for (int i = 0; i < IdleBucketCount; i++)
                {
                    _idleBuckets[i] = -1;
                }
            }

            int bucket = expireTick & (IdleBucketCount - 1);
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
            if (_keepAliveBuckets == null)
            {
                _keepAliveBuckets = new int[KeepAliveBucketCount];
                for (int i = 0; i < KeepAliveBucketCount; i++)
                {
                    _keepAliveBuckets[i] = -1;
                }
            }

            RemoveFromKeepAliveBucket(assetId, ref slot);
            int bucket = slot.KeepAliveExpireTick & (KeepAliveBucketCount - 1);
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

            int bucket = slot.KeepAliveExpireTick & (KeepAliveBucketCount - 1);
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
        }

        private void RemoveFromIdleBucket(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind != 2 || _idleBuckets == null)
            {
                return;
            }

            int bucket = slot.IdleExpireTick & (IdleBucketCount - 1);
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

        public int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount)
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
                packageId > ResourceKeyPackageMax || locationId > ResourceKeyLocationMax ||
                typeId > ResourceKeyTypeMax ||
                (int)assetKind > ResourceKeyAssetKindMax || (int)handleKind > ResourceKeyHandleMax)
            {
                throw new GameException("Resource key id range exceeded.");
            }

            return ((ulong)(uint)packageId << ResourceKeyPackageShift) |
                   ((ulong)(uint)locationId << ResourceKeyLocationShift) |
                   ((ulong)(uint)typeId << ResourceKeyTypeShift) |
                   ((ulong)(byte)assetKind << ResourceKeyAssetKindShift) |
                   ((ulong)(byte)handleKind << ResourceKeyHandleShift);
        }

        private static int UnpackPackageId(ulong key)
        {
            return (int)((key >> ResourceKeyPackageShift) & ResourceKeyPackageMax);
        }

        private static int UnpackLocationId(ulong key)
        {
            return (int)((key >> ResourceKeyLocationShift) & ResourceKeyLocationMax);
        }

        private static int UnpackTypeId(ulong key)
        {
            return (int)((key >> ResourceKeyTypeShift) & ResourceKeyTypeMax);
        }

        private ulong GetAssetRecordKey(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, EResourceHandleKind handleKind)
        {
            int packageId = GetOrAllocatePackageId(packageName);
            int locationId = GetOrAllocateLocationId(location);
            int typeId = GetOrAllocateTypeId(assetType);
            return PackResourceKey(packageId, locationId, typeId, assetKind, handleKind);
        }

        private ulong GetLoadingOperationKey(string location, string packageName, Type assetType,
            EResourceAssetKind assetKind)
        {
            int packageId = GetOrAllocatePackageId(packageName);
            int locationId = GetOrAllocateLocationId(location);
            int typeId = GetOrAllocateTypeId(assetType);
            return PackResourceKey(packageId, locationId, typeId, assetKind, EResourceHandleKind.AssetHandle);
        }

        #endregion

        #region 资源名称注册表 [RESOURCE NAME REGISTRY]

        private int GetOrAllocatePackageId(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                packageName = DefaultPackageName;
            }

            for (int i = 1; i < _nextPackageId; i++)
            {
                if (_resourcePackagesById != null && i < _resourcePackagesById.Length &&
                    _resourcePackagesById[i] == packageName)
                {
                    _resourcePackageRefCounts[i]++;
                    return i;
                }
            }

            int id = AllocateResourceId(ref _nextPackageId, ResourceKeyPackageMax, _freePackageIds);
            EnsureResourceNameSlot(ref _resourcePackagesById, ref _resourcePackageRefCounts, id);
            _resourcePackagesById[id] = packageName;
            _resourcePackageRefCounts[id] = 1;
            return id;
        }

        private int GetOrAllocateLocationId(string location)
        {
            for (int i = 1; i < _nextLocationId; i++)
            {
                if (_resourceLocationsById != null && i < _resourceLocationsById.Length &&
                    _resourceLocationsById[i] == location)
                {
                    _resourceLocationRefCounts[i]++;
                    return i;
                }
            }

            int id = AllocateResourceId(ref _nextLocationId, ResourceKeyLocationMax, _freeLocationIds);
            EnsureResourceNameSlot(ref _resourceLocationsById, ref _resourceLocationRefCounts, id);
            _resourceLocationsById[id] = location;
            _resourceLocationRefCounts[id] = 1;
            return id;
        }

        private int GetOrAllocateTypeId(Type assetType)
        {
            for (int i = 1; i < _nextTypeId; i++)
            {
                if (_resourceTypesById != null && i < _resourceTypesById.Length &&
                    _resourceTypesById[i] == assetType)
                {
                    _resourceTypeRefCounts[i]++;
                    return i;
                }
            }

            int id = AllocateResourceId(ref _nextTypeId, ResourceKeyTypeMax, _freeTypeIds);
            EnsureResourceTypeSlot(id);
            _resourceTypesById[id] = assetType;
            _resourceTypeRefCounts[id] = 1;
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
            int packageId = UnpackPackageId(key);
            int locationId = UnpackLocationId(key);
            int typeId = UnpackTypeId(key);
            if (packageId > 0 && packageId < _resourcePackageRefCounts?.Length)
            {
                _resourcePackageRefCounts[packageId]++;
            }

            if (locationId > 0 && locationId < _resourceLocationRefCounts?.Length)
            {
                _resourceLocationRefCounts[locationId]++;
            }

            if (typeId > 0 && typeId < _resourceTypeRefCounts?.Length)
            {
                _resourceTypeRefCounts[typeId]++;
            }
        }

        private void ReleaseResourceKey(ulong key)
        {
            int packageId = UnpackPackageId(key);
            int locationId = UnpackLocationId(key);
            int typeId = UnpackTypeId(key);

            if (packageId > 0 && packageId < _resourcePackageRefCounts?.Length)
            {
                _resourcePackageRefCounts[packageId]--;
                if (_resourcePackageRefCounts[packageId] == 0)
                {
                    _freePackageIds.Push(packageId);
                }
            }

            if (locationId > 0 && locationId < _resourceLocationRefCounts?.Length)
            {
                _resourceLocationRefCounts[locationId]--;
                if (_resourceLocationRefCounts[locationId] == 0)
                {
                    _freeLocationIds.Push(locationId);
                }
            }

            if (typeId > 0 && typeId < _resourceTypeRefCounts?.Length)
            {
                _resourceTypeRefCounts[typeId]--;
                if (_resourceTypeRefCounts[typeId] == 0)
                {
                    _freeTypeIds.Push(typeId);
                }
            }
        }

        private void ReleaseAllResourceKeysFromMap(ResourceUlongIntMap map)
        {
            map.ForEachKey(ReleaseResourceKey);
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
                    if (lastReportedProgress < 0f || progress - lastReportedProgress >= ProgressCallbackThreshold)
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

            _bindingService?.RegisterOwner(owner);
            return owner;
        }

        private SubAssetsHandle GetSubAssetsHandleAsync(string location, string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadSubAssetsAsync<Sprite>(location);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadSubAssetsAsync<Sprite>(location);
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
            return ref _assetSlotPages[index >> RecordPageBits][index & RecordPageMask];
        }

        private ref LeaseSlot GetLeaseSlotRef(int index)
        {
            return ref _leaseSlotPages[index >> RecordPageBits][index & RecordPageMask];
        }

        private ref LoadingOperationSlot GetLoadingOperationSlotRef(int index)
        {
            return ref _loadingOperationSlotPages[index >> RecordPageBits][index & RecordPageMask];
        }

        private void EnsureAssetSlotPage(int index)
        {
            int pageIndex = index >> RecordPageBits;
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
                _assetSlotPages[pageIndex] = new AssetSlot[RecordPageSize];
            }
        }

        private void EnsureLeaseSlotPage(int index)
        {
            int pageIndex = index >> RecordPageBits;
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
                _leaseSlotPages[pageIndex] = new LeaseSlot[RecordPageSize];
            }
        }

        private void EnsureLoadingOperationSlotPage(int index)
        {
            int pageIndex = index >> RecordPageBits;
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
                _loadingOperationSlotPages[pageIndex] = new LoadingOperationSlot[RecordPageSize];
            }
        }

        #endregion
    }
}
