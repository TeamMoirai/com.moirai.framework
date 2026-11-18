using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Asset
{
    /// <summary>
    /// 资源服务接缝的 Lease 实现——取用、归还、句柄校验与租约到资产的读取，全部压在记录内核之上。
    /// </summary>
    partial class YooAssetHandler
    {
        #region 公共 Lease API [PUBLIC LEASE API]

        /// <inheritdoc />
        public override AssetLeaseHandle AcquireDirect(AssetKey key)
        {
            AssetKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new AssetKey(key.Location, key.PackageName, typeof(UObject), AssetKeyCodec.InferAssetKind(typeof(UObject)))
                : key;

            string normalizedPackageName = Store.NormalizePackageName(typedKey.PackageName);
            EAssetKind assetKind = AssetKeyCodec.NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = AssetKeyCodec.NormalizeAssetType(typedKey.AssetType, assetKind);

            // AssetHandle 口径下 loading key 与 record key 同一打包结果：解析一次，缓存命中 / 加载 / 取租约共用。
            ulong recordKey = Store.GetAssetRecordKey(normalizedPackageName, typedKey.Location, assetType, assetKind,
                EAssetHandleKind.AssetHandle);

            UObject asset = GetOrLoadAsset(typedKey.Location, assetType, assetKind, normalizedPackageName, recordKey);
            if (asset == null)
            {
                return AssetLeaseHandle.Invalid;
            }

            if (!Store.TryGetRecordId(recordKey, out int assetId) || !Store.IsValidAssetId(assetId))
            {
                return AssetLeaseHandle.Invalid;
            }

            return Store.AcquireLease(assetId, EAssetLeaseKind.Direct, EAssetLeaseOption.None);
        }

        /// <inheritdoc />
        public override async UniTask<AssetLeaseHandle> AcquireDirectAsync(AssetKey key,
            CancellationToken cancellationToken = default)
        {
            AssetKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new AssetKey(key.Location, key.PackageName, typeof(UObject), AssetKeyCodec.InferAssetKind(typeof(UObject)))
                : key;

            string normalizedPackageName = Store.NormalizePackageName(typedKey.PackageName);
            EAssetKind assetKind = AssetKeyCodec.NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = AssetKeyCodec.NormalizeAssetType(typedKey.AssetType, assetKind);
            ulong loadingKey = Store.GetLoadingOperationKey(typedKey.Location, normalizedPackageName, assetType, assetKind);

            UObject asset = await GetOrLoadAssetAsync(typedKey.Location, assetType, assetKind, normalizedPackageName,
                loadingKey, cancellationToken: cancellationToken);
            if (asset == null)
            {
                return AssetLeaseHandle.Invalid;
            }

            if (!Store.TryGetRecordId(loadingKey, out int assetId) || !Store.IsValidAssetId(assetId))
            {
                return AssetLeaseHandle.Invalid;
            }

            return Store.AcquireLease(assetId, EAssetLeaseKind.Direct, EAssetLeaseOption.None);
        }

        /// <inheritdoc />
        public override AssetLease<T> LoadLease<T>(AssetKey key)
        {
            AssetKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new AssetKey(key.Location, key.PackageName, typeof(T), AssetKeyCodec.InferAssetKind(typeof(T)))
                : key;
            AssetLeaseHandle handle = AcquireDirect(typedKey);
            if (!handle.IsValid)
            {
                return default;
            }

            if (!Store.TryGetLeaseAsset(handle, out UObject asset) || asset is not T typedAsset)
            {
                Store.Release(handle);
                return default;
            }

            return new AssetLease<T>(this, handle, typedAsset);
        }

        /// <inheritdoc />
        public override AssetLease<T> LoadLease<T>(string location, string packageName = "")
        {
            return LoadLease<T>(new AssetKey(location, packageName, typeof(T), AssetKeyCodec.InferAssetKind(typeof(T))));
        }

        /// <inheritdoc />
        public override async UniTask<AssetLease<T>> LoadLeaseAsync<T>(AssetKey key,
            CancellationToken cancellationToken = default)
        {
            AssetKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new AssetKey(key.Location, key.PackageName, typeof(T), AssetKeyCodec.InferAssetKind(typeof(T)))
                : key;
            AssetLeaseHandle handle = await AcquireDirectAsync(typedKey, cancellationToken);
            if (!handle.IsValid)
            {
                return default;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Store.Release(handle);
                return default;
            }

            if (!Store.TryGetLeaseAsset(handle, out UObject asset) || asset is not T typedAsset)
            {
                Store.Release(handle);
                return default;
            }

            return new AssetLease<T>(this, handle, typedAsset);
        }

        /// <inheritdoc />
        public override UniTask<AssetLease<T>> LoadLeaseAsync<T>(string location,
            CancellationToken cancellationToken = default, string packageName = "")
        {
            return LoadLeaseAsync<T>(
                new AssetKey(location, packageName, typeof(T), AssetKeyCodec.InferAssetKind(typeof(T))), cancellationToken);
        }

        #endregion

        #region 内部 Lease 方法 [INTERNAL LEASE METHODS]

        public override AssetLeaseHandle AcquireBinding(AssetKey key)
        {
            return AcquireDirect(key);
        }

        public override bool TryAcquireBindingCached(AssetKey key, out AssetLeaseHandle handle)
        {
            handle = AssetLeaseHandle.Invalid;
            AssetKey typedKey = key.AssetType == null && !key.HasResolvedIds
                ? new AssetKey(key.Location, key.PackageName, typeof(UObject), AssetKeyCodec.InferAssetKind(typeof(UObject)))
                : key;

            string normalizedPackageName = Store.NormalizePackageName(typedKey.PackageName);
            EAssetKind assetKind = AssetKeyCodec.NormalizeAssetKind(typedKey.AssetType, typedKey.AssetKind);
            Type assetType = AssetKeyCodec.NormalizeAssetType(typedKey.AssetType, assetKind);
            ulong recordKey = Store.GetAssetRecordKey(normalizedPackageName, typedKey.Location, assetType, assetKind,
                EAssetHandleKind.AssetHandle);
            if (!Store.TryGetCachedAssetRecordByKey(recordKey, out int assetId, out _))
            {
                return false;
            }

            handle = Store.AcquireLease(assetId, EAssetLeaseKind.Binding, EAssetLeaseOption.None);
            return handle.IsValid;
        }

        public override UniTask<AssetLeaseHandle> AcquireBindingAsync(AssetKey key,
            CancellationToken cancellationToken)
        {
            return AcquireDirectAsync(key, cancellationToken);
        }

        public override AssetLeaseHandle AcquirePrefabSourceLease(string location, string packageName)
        {
            AssetKey key = new AssetKey(location, packageName, typeof(GameObject), EAssetKind.Prefab);
            return AcquireDirect(key);
        }

        public override async UniTask<AssetLeaseHandle> AcquirePrefabSourceLeaseAsync(string location,
            string packageName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(location))
            {
                return AssetLeaseHandle.Invalid;
            }

            string normalizedPackageName = Store.NormalizePackageName(packageName);
            ulong loadingKey = Store.GetLoadingOperationKey(location, normalizedPackageName, typeof(GameObject),
                EAssetKind.Prefab);
            UObject asset = await GetOrLoadAssetAsync(location, typeof(GameObject), EAssetKind.Prefab,
                normalizedPackageName, loadingKey, cancellationToken: cancellationToken);
            if (asset == null)
            {
                return AssetLeaseHandle.Invalid;
            }

            if (!Store.TryGetRecordId(loadingKey, out int assetId) || !Store.IsValidAssetId(assetId))
            {
                return AssetLeaseHandle.Invalid;
            }

            return Store.AcquireLease(assetId, EAssetLeaseKind.Direct, EAssetLeaseOption.None);
        }

        #endregion
    }
}
