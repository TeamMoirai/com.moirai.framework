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

            string normalizedPackageName = Kernel.NormalizePackageName(typedKey.PackageName);
            EResourceAssetKind assetKind = ResourceKeyCodec.NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = ResourceKeyCodec.NormalizeAssetType(typedKey.AssetType, assetKind);

            UObject asset = GetOrLoadAsset(typedKey.Location, assetType, assetKind, normalizedPackageName);
            if (asset == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            ulong recordKey = Kernel.GetAssetRecordKey(normalizedPackageName, typedKey.Location, assetType, assetKind,
                EResourceHandleKind.AssetHandle);
            if (!Kernel.TryGetRecordId(recordKey, out int assetId) || !Kernel.IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            return Kernel.AcquireLease(assetId, EResourceLeaseKind.Direct, EResourceLeaseOption.None);
        }

        /// <inheritdoc />
        public override async UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key,
            CancellationToken cancellationToken = default)
        {
            ResourceKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new ResourceKey(key.Location, key.PackageName, typeof(UObject), ResourceKeyCodec.InferAssetKind(typeof(UObject)))
                : key;

            string normalizedPackageName = Kernel.NormalizePackageName(typedKey.PackageName);
            EResourceAssetKind assetKind = ResourceKeyCodec.NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = ResourceKeyCodec.NormalizeAssetType(typedKey.AssetType, assetKind);
            ulong loadingKey = Kernel.GetLoadingOperationKey(typedKey.Location, normalizedPackageName, assetType, assetKind);

            UObject asset = await GetOrLoadAssetAsync(typedKey.Location, assetType, assetKind, normalizedPackageName,
                loadingKey, cancellationToken: cancellationToken);
            if (asset == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            ulong recordKey = Kernel.GetAssetRecordKey(normalizedPackageName, typedKey.Location, assetType, assetKind,
                EResourceHandleKind.AssetHandle);
            if (!Kernel.TryGetRecordId(recordKey, out int assetId) || !Kernel.IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            return Kernel.AcquireLease(assetId, EResourceLeaseKind.Direct, EResourceLeaseOption.None);
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

            if (!Kernel.TryGetLeaseAsset(handle, out UObject asset) || asset is not T typedAsset)
            {
                Kernel.Release(handle);
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
                Kernel.Release(handle);
                return default;
            }

            if (!Kernel.TryGetLeaseAsset(handle, out UObject asset) || asset is not T typedAsset)
            {
                Kernel.Release(handle);
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
        public override void Release(ResourceLeaseHandle handle)
        {
            Kernel.Release(handle);
        }

        /// <inheritdoc />
        public override bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset)
        {
            return Kernel.TryGetLeaseAsset(handle, out asset);
        }

        #endregion

        #region 内部 Lease 方法 [INTERNAL LEASE METHODS]

        /// <inheritdoc />
        internal override bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName,
            out Sprite sprite)
        {
            return Kernel.TryGetSubSpriteAsset(handle, spriteName, out sprite);
        }

        /// <inheritdoc />
        internal override bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
        {
            return Kernel.TryGetLeaseAssetId(handle, out assetId);
        }

        /// <inheritdoc />
        internal override void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options)
        {
            Kernel.SetLeaseOptions(handle, options);
        }

        internal override ResourceLeaseHandle AcquireBinding(ResourceKey key)
        {
            return AcquireDirect(key);
        }

        internal override UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key,
            CancellationToken cancellationToken)
        {
            return AcquireDirectAsync(key, cancellationToken);
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

            string normalizedPackageName = Kernel.NormalizePackageName(packageName);
            ulong loadingKey = Kernel.GetLoadingOperationKey(location, normalizedPackageName, typeof(GameObject),
                EResourceAssetKind.Prefab);
            UObject asset = await GetOrLoadAssetAsync(location, typeof(GameObject), EResourceAssetKind.Prefab,
                normalizedPackageName, loadingKey, cancellationToken: cancellationToken);
            if (asset == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            ulong key = Kernel.GetAssetRecordKey(normalizedPackageName, location, typeof(GameObject),
                EResourceAssetKind.Prefab, EResourceHandleKind.AssetHandle);
            if (!Kernel.TryGetRecordId(key, out int assetId) || !Kernel.IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            return Kernel.AcquireLease(assetId, EResourceLeaseKind.Direct, EResourceLeaseOption.None);
        }

        #endregion
    }
}
