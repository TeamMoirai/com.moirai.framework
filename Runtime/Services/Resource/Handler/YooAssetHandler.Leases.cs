using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源服务接缝的 Lease 实现——取用、归还、句柄校验与租约到资产的读取，全部压在记录内核之上。
    /// </summary>
    partial class YooAssetHandler
    {
        #region 公共 Lease API [PUBLIC LEASE API]

        /// <inheritdoc />
        public override ResourceLeaseHandle AcquireDirect(ResourceKey key)
        {
            ResourceKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new ResourceKey(key.Location, key.PackageName, typeof(UObject), ResourceKeyCodec.InferAssetKind(typeof(UObject)))
                : key;

            string normalizedPackageName = NormalizePackageName(typedKey.PackageName);
            EResourceAssetKind assetKind = ResourceKeyCodec.NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = ResourceKeyCodec.NormalizeAssetType(typedKey.AssetType, assetKind);

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
                ? new ResourceKey(key.Location, key.PackageName, typeof(UObject), ResourceKeyCodec.InferAssetKind(typeof(UObject)))
                : key;

            string normalizedPackageName = NormalizePackageName(typedKey.PackageName);
            EResourceAssetKind assetKind = ResourceKeyCodec.NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = ResourceKeyCodec.NormalizeAssetType(typedKey.AssetType, assetKind);
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
                ? new ResourceKey(key.Location, key.PackageName, typeof(T), ResourceKeyCodec.InferAssetKind(typeof(T)))
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
            return LoadLease<T>(new ResourceKey(location, packageName, typeof(T), ResourceKeyCodec.InferAssetKind(typeof(T))));
        }

        /// <inheritdoc />
        public override async UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key,
            CancellationToken cancellationToken = default)
        {
            ResourceKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new ResourceKey(key.Location, key.PackageName, typeof(T), ResourceKeyCodec.InferAssetKind(typeof(T)))
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
                new ResourceKey(location, packageName, typeof(T), ResourceKeyCodec.InferAssetKind(typeof(T))), cancellationToken);
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
            if (slot.State == EResourceAssetState.Released || !IsHandleValid(slot.RawHandle))
            {
                return false;
            }

            sprite = GetSubSprite(slot.RawHandle, spriteName);
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
    }
}
