#if ADDRESSABLES_INSTALLED
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.U2D;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// Addressables 后端的取用面：租约 API 的公开接缝、租约取用核心与记录内核的接线。
    /// </summary>
    /// <remarks>
    /// 记账（记录槽、租约、去重、时间轮）全在 <see cref="ResourceRecordStore"/>，与 YooAsset 后端共用同一份； <br />
    /// 异步加载与去重核心见 Loading 分部。Addressables 无同步取资产 API，故同步族按 <see cref="CreateNotSupported"/> 快速失败，不静默返回 Invalid。
    /// </remarks>
    partial class AddressableHandler : IResourceRecordHost
    {
        #region 内核接线 [KERNEL WIRING]

        [NonSerialized] private ResourceRecordStore _store;

        private ResourceRecordStore Store =>
            _store ??= new ResourceRecordStore(this, () => DefaultPackageName);

        internal int LoadingOperationCount => Store.LoadingOperationCount;

        /// <inheritdoc />
        bool IResourceRecordHost.IsHandleValid(object handle)
        {
            return (handle as IAddressableHandleRef)?.IsValid == true;
        }

        /// <inheritdoc />
        void IResourceRecordHost.DisposeHandle(object handle)
        {
            (handle as IAddressableHandleRef)?.Release();
        }

        /// <inheritdoc />
        /// <remarks>只有图集形态的记录（<see cref="SpriteAtlas"/>）有子精灵可取；单资产句柄返回 null
        /// 是正解，不是降级。按名取用走 <c>SpriteAtlas.GetSprite</c>——本机 6000.3 的 CoreModule 里。 <br />
        /// 并没有 <c>TryGetSprite</c>（那串只出现在 TextCore 模块的另一套类型上），别照着记忆写。</remarks>
        Sprite IResourceRecordHost.GetSubSprite(object handle, string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName) || !(handle is AddressableHandleRef<SpriteAtlas> atlasRef))
            {
                return null;
            }

            return atlasRef.GetSprite(spriteName);
        }

        // 三个配置读数不再另写一遍显式实现：容量属性本来就是 public 的 get/set，
        // 隐式即满足接口的 get 要求，多写一层只会让两处读数各说各话。

        #endregion

        #region 句柄包装 [HANDLE WRAPPER]

        /// <summary>
        /// 内核侧只需要"还活着吗"和"放掉"，不需要知道 Addressables 的泛型参数。
        /// </summary>
        private interface IAddressableHandleRef
        {
            bool IsValid { get; }

            void Release();
        }

        /// <summary>
        /// 引用型句柄包装：以类持 struct 字段，避免句柄进内核 <c>object</c> 槽时装箱与解箱。
        /// </summary>
        private sealed class AddressableHandleRef<TObject> : IAddressableHandleRef
        {
            private AsyncOperationHandle<TObject> _handle;
            private bool _released;

            internal AddressableHandleRef(AsyncOperationHandle<TObject> handle)
            {
                _handle = handle;
            }

            /// <summary>释放标记自己扛：不依赖 <c>IsValid()</c> 在 Release 之后是否转 false。</summary>
            public bool IsValid => !_released && _handle.IsValid();

            /// <summary>
            /// 图集形态下按名取子精灵，其余 <c>TObject</c> 恒返回 <c>null</c>。
            /// </summary>
            internal Sprite GetSprite(string spriteName)
            {
                if (_released || !_handle.IsValid())
                {
                    return null;
                }

                return (_handle.Result as SpriteAtlas)?.GetSprite(spriteName);
            }

            public void Release()
            {
                if (_released)
                {
                    return;
                }

                _released = true;
                if (_handle.IsValid())
                {
                    Addressables.Release(_handle);
                }

                _handle = default;
            }
        }

        #endregion

        #region 租约取用 [LEASE ACQUIRE]

        /// <summary>
        /// 取用一条资源并挂上租约，只服务异步成员（Addressables 侧唯一可行的形态是异步）。
        /// </summary>
        private async UniTask<ResourceLeaseHandle> AcquireLeaseAsync(ResourceKey key, EResourceLeaseKind leaseKind,
            EResourceLeaseOption options, CancellationToken cancellationToken)
        {
            Type assetType = key.AssetType ?? typeof(UObject);
            EResourceAssetKind assetKind = ResourceKeyCodec.NormalizeAssetKind(assetType, key.AssetKind);
            UObject resource = await GetOrLoadAssetAsync(key.Location, assetType, assetKind, key.PackageName,
                cancellationToken);
            if (resource == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            string normalizedPackageName = Store.NormalizePackageName(key.PackageName);
            ulong recordKey = Store.GetAssetRecordKey(normalizedPackageName, key.Location, assetType, assetKind,
                EResourceHandleKind.AssetHandle);
            if (!Store.TryGetRecordId(recordKey, out int assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            return Store.AcquireLease(assetId, leaseKind, options);
        }

        #endregion

        #region 公共 Lease API [PUBLIC LEASE API]

        /// <inheritdoc />
        /// <remarks>Addressables 没有同步取资产的公开 API，同步族保持 fail-fast——
        /// 用 <c>Task.Wait()</c> 硬等会把主线程挂在驱动上，比抛错更糟。</remarks>
        public override ResourceLeaseHandle AcquireDirect(ResourceKey key)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken cancellationToken = default)
        {
            return AcquireLeaseAsync(key, EResourceLeaseKind.Direct, EResourceLeaseOption.None, cancellationToken);
        }

        /// <inheritdoc />
        public override void Release(ResourceLeaseHandle handle)
        {
            Store.Release(handle);
        }

        /// <inheritdoc />
        public override ResourceAssetLease<T> LoadLease<T>(ResourceKey key)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override async UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken cancellationToken = default)
        {
            ResourceLeaseHandle handle = await AcquireLeaseAsync(key, EResourceLeaseKind.Direct,
                EResourceLeaseOption.None, cancellationToken);
            if (!handle.IsValid)
            {
                return default;
            }

            if (!Store.TryGetLeaseAsset(handle, out UObject asset) || asset is not T typedAsset)
            {
                Store.Release(handle);
                return default;
            }

            return new ResourceAssetLease<T>(this, handle, typedAsset);
        }

        /// <inheritdoc />
        public override UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "")
        {
            return LoadLeaseAsync<T>(new ResourceKey(location, packageName, typeof(T),
                ResourceKeyCodec.InferAssetKind(typeof(T))), cancellationToken);
        }

        /// <inheritdoc />
        public override bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset)
        {
            return Store.TryGetLeaseAsset(handle, out asset);
        }

        #endregion

        #region 内部 Lease 方法 [INTERNAL LEASE METHODS]

        /// <inheritdoc />
        public override ResourceLeaseHandle AcquireBinding(ResourceKey key)
        {
            throw CreateNotSupported();
        }

        public override bool TryAcquireBindingCached(ResourceKey key, out ResourceLeaseHandle handle)
        {
            // Addressables 无同步加载；cache-only 只读已落地记录，未命中直接 false。
            handle = ResourceLeaseHandle.Invalid;
            EResourceAssetKind assetKind = ResourceKeyCodec.NormalizeAssetKind(key.AssetType, key.AssetKind);
            if (!Store.TryGetCachedAssetRecord(Store.NormalizePackageName(key.PackageName), key.Location,
                    key.AssetType ?? typeof(UObject), assetKind,
                    EResourceHandleKind.AssetHandle, out int assetId, out _))
            {
                return false;
            }

            handle = Store.AcquireLease(assetId, EResourceLeaseKind.Binding, EResourceLeaseOption.None);
            return handle.IsValid;
        }

        /// <inheritdoc />
        public override UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            return AcquireLeaseAsync(key, EResourceLeaseKind.Binding, EResourceLeaseOption.None, cancellationToken);
        }

        /// <inheritdoc />
        public override UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName, EResourceLeaseOption options, CancellationToken cancellationToken)
        {
            return AcquireSubAssetsAsync(location, packageName, options, cancellationToken);
        }

        /// <inheritdoc />
        public override bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite)
        {
            return Store.TryGetSubSpriteAsset(handle, spriteName, out sprite);
        }

        /// <inheritdoc />
        public override bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
        {
            return Store.TryGetLeaseAssetId(handle, out assetId);
        }

        /// <inheritdoc />
        public override void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options)
        {
            Store.SetLeaseOptions(handle, options);
        }

        /// <inheritdoc />
        public override ResourceLeaseHandle AcquirePrefabSourceLease(string location, string packageName)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override UniTask<ResourceLeaseHandle> AcquirePrefabSourceLeaseAsync(string location, string packageName, CancellationToken cancellationToken)
        {
            return AcquireLeaseAsync(new ResourceKey(location, packageName, typeof(GameObject), EResourceAssetKind.Prefab),
                EResourceLeaseKind.Direct, EResourceLeaseOption.None, cancellationToken);
        }

        #endregion
    }
}
#endif
