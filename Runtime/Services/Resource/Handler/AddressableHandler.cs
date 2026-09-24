#if ADDRESSABLES_INSTALLED
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// <para>基于 Unity Addressables 的资源处理器实现（实验性）。</para>
    /// <para><see cref="ResourceServiceHandler"/> 的 Addressables 后端实现，与 <see cref="YooAssetHandler"/> 共用
    /// <see cref="ResourceRecordStore"/> 记录内核：异步租约 / 绑定 / 预制体实例化 / 图集子精灵 / 场景加载 / 缓存维护与低内存回收都是对等实现。</para>
    /// <para>Addressables 既没有同步加载 API，也没有两步式 Check→Update 的下载器对应面，因此同步取用族与下载族成员统一抛出
    /// <see cref="GameException"/> fail-fast，禁止静默 no-op 掩盖误配置；只有异步版本可答的查询
    /// （<c>IsNeedDownloadFromRemote</c> / <c>GetPackageVersion</c> / <c>GetAssetInfo</c> / 按标签的 <c>GetAssetInfos</c>）退化为恒定值。</para>
    /// </summary>
    [Serializable]
    internal sealed partial class AddressableHandler : ResourceServiceHandler
    {
        #region 基础属性 [BASE PROPERTIES]

        /// <inheritdoc />
        public override string DefaultPackageName { get; set; } = "Default";

        /// <inheritdoc />
        public override IResourceBindingService BindingService => _bindingService;

        private ResourceBindingService _bindingService;

        /// <inheritdoc />
        public override string HostServerURL { get; set; }

        /// <inheritdoc />
        public override string FallbackHostServerURL { get; set; }

        /// <inheritdoc />
        public override EResourceLoadWayWebGL LoadResWayWebGL { get; set; }

        /// <inheritdoc />
        public override string ApplicableGameVersion => Application.version;

        /// <inheritdoc />
        public override int InternalResourceVersion => 0;

        /// <inheritdoc />
        public override string PackageVersion { get; set; }

        /// <inheritdoc />
        public override bool UpdatableWhilePlaying => false;

        /// <inheritdoc />
        public override bool AutoUnloadBundleWhenUnused { get; set; }

        /// <inheritdoc />
        public override int DownloadingMaxNum { get; set; }

        /// <inheritdoc />
        public override int FailedTryAgain { get; set; }

        /// <inheritdoc />
        public override long Milliseconds { get; set; }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public override void Initialize()
        {
            _bindingService = new ResourceBindingService(this);
        }

        /// <inheritdoc />
        protected override void OnShutdown()
        {
            _bindingService?.Shutdown();
            ForceReleaseAllAssetRecords();
        }

        #endregion

        #region 初始化 [INITIALIZATION]

        #endregion

        #region 包管理 [PACKAGE MANAGEMENT]

        /// <inheritdoc />
        public override string GetPackageVersion(string customPackageName = "")
        {
            return string.Empty;
        }

        /// <inheritdoc />
        public override ResourcePackageVersionResult RequestPackageVersion(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer)
        {
            HostServerURL = defaultHostServer;
            FallbackHostServerURL = fallbackHostServer;
        }

        /// <inheritdoc />
        public override IResourceOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override IResourceDownloader CreateResourceDownloader(string customPackageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override ResourceClearCacheResult StartClearCache(EResourceClearMode clearMode, string customPackageName = "")
        {
            Addressables.ClearResourceLocators();
            if (clearMode == EResourceClearMode.ClearAllBundleFiles)
            {
                Caching.ClearCache();
            }

            return new ResourceClearCacheResult
            {
                Operation = null,
                ClearedCount = 0,
            };
        }

        /// <inheritdoc />
        public override void ClearAllBundleFiles(string customPackageName = "")
        {
            Addressables.ClearResourceLocators();
            Caching.ClearCache();
        }

        #endregion

        #region 资源回收 [ASSET RECYCLING]

        /// <inheritdoc />
        public override void OnLowMemory()
        {
            // 这份委托由 ResourceService 初始化时登记进来（RequestForceUnloadUnusedAssets）。
            // 吞掉它等于把 Application.lowMemory 这条链在这一后端上悄悄剪断：调用方照旧返回，
            // 只是再没有人在内存吃紧时请求强制回收。
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }

        private Action<bool> _forceUnloadUnusedAssetsAction;

        /// <inheritdoc />
        public override void SetForceUnloadUnusedAssetsAction(Action<bool> action)
        {
            _forceUnloadUnusedAssetsAction = action;
        }

        /// <inheritdoc />
        public override void UnloadUnusedAssets()
        {
            UnloadUnusedAssets(false);
        }

        /// <inheritdoc />
        public override void UnloadUnusedAssets(bool force)
        {
            // 非强制档在 YooAsset 侧推进的是 bundle 卸载操作，Addressables 没有对应物；
            // 能对上的是"强制档还掉引用计数为零的记录"，所以只接这一半。
            if (force)
            {
                ReleaseAllUnusedAssetRecords();
            }
        }

        /// <inheritdoc />
        public override void ForceUnloadAllAssets()
        {
            ReleaseAllUnusedAssetRecords();
        }

        /// <inheritdoc />
        public override void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            ReleaseAllUnusedAssetRecords();
        }

        #endregion

        #region 获取资源信息 [GET ASSET INFOS]

        /// <inheritdoc />
        public override long GetDownloadSize(string location, string packageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override bool IsNeedDownloadFromRemote(string location, string packageName = "")
        {
            return false;
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry[] GetAssetInfos(string tag, string packageName = "")
        {
            return Array.Empty<ResourceAssetInfoEntry>();
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry[] GetAssetInfos(string[] tags, string packageName = "")
        {
            return Array.Empty<ResourceAssetInfoEntry>();
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry GetAssetInfo(string location, string packageName = "")
        {
            return default;
        }

        #endregion

        #region 资源加载 [ASSET LOADING]

        /// <inheritdoc />
        public override GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            throw CreateNotSupported();
        }

        #endregion

        #region 场景加载 [SCENE LOADING]

        /// <inheritdoc />
        public override ResourceSceneHandle LoadSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority, string packageName = "")
        {
            var handle = Addressables.LoadSceneAsync(location, sceneMode, !suspendLoad, (int)priority);
            return new AddressableSceneHandleAdapter(handle);
        }

        /// <summary>
        /// Addressables 场景句柄适配器。
        /// <para>Addressables 挂起语义与引擎原生不同：activateOnLoad=false 时外层句柄在场景就绪（待激活）即完成，
        /// 激活需显式调用 <see cref="SceneInstance.ActivateAsync"/>。为遵守 <see cref="ResourceSceneHandle"/> 契约
        /// （挂起待激活期间 <c>IsDone</c> 保持 false、<c>SceneObject</c> 为默认值），二者均以 <see cref="Scene.isLoaded"/>
        /// （激活完成标记）为准；未完成时 <c>Progress</c> 封顶于 0.99，避免回报 100%。</para>
        /// </summary>
        private sealed class AddressableSceneHandleAdapter : ResourceSceneHandle
        {
            private AsyncOperationHandle<SceneInstance> _handle;

            public AddressableSceneHandleAdapter(AsyncOperationHandle<SceneInstance> handle)
            {
                _handle = handle;
            }

            /// <inheritdoc />
            public override bool IsDone => !_handle.IsValid() || (_handle.IsDone && (_handle.Status != AsyncOperationStatus.Succeeded || _handle.Result.Scene.isLoaded));

            /// <inheritdoc />
            public override float Progress => !_handle.IsValid()
                ? 1f
                : _handle.Status == AsyncOperationStatus.Succeeded && !_handle.Result.Scene.isLoaded
                    ? 0.99f
                    : _handle.PercentComplete;

            /// <inheritdoc />
            public override string Error => _handle.IsValid() && _handle.Status == AsyncOperationStatus.Failed
                ? _handle.OperationException?.Message
                : string.Empty;

            /// <inheritdoc />
            public override UnityEngine.SceneManagement.Scene SceneObject => _handle.IsValid() && _handle.Status == AsyncOperationStatus.Succeeded && _handle.Result.Scene.isLoaded
                ? _handle.Result.Scene
                : default;

            /// <inheritdoc />
            public override bool UnSuspend()
            {
                if (!_handle.IsValid() || _handle.Status != AsyncOperationStatus.Succeeded)
                {
                    return false;
                }

                _handle.Result.ActivateAsync();
                return true;
            }

            /// <inheritdoc />
            public override bool ActivateScene()
            {
                if (!_handle.IsValid() || _handle.Status != AsyncOperationStatus.Succeeded)
                {
                    return false;
                }

                return SceneManager.SetActiveScene(_handle.Result.Scene);
            }

            /// <inheritdoc />
            public override IResourceOperation UnloadAsync()
            {
                if (!_handle.IsValid())
                {
                    return null;
                }

                return new AddressableOperationAdapter(Addressables.UnloadSceneAsync(_handle));
            }

            /// <inheritdoc />
            public override void Release()
            {
                if (_handle.IsValid())
                {
                    Addressables.Release(_handle);
                }
            }
        }

        #endregion

        #region 容量属性 [CAPACITY PROPERTIES]

        /// <inheritdoc />
        // 夹取后落进字段，生效值即字段值：记录内核按 IResourceRecordHost 活读这三项，
        // 留成裸自动属性会让"写进去的值"与"内核读到的值"分家（这正是审计点名的静默 no-op）。
        [NonSerialized] private int _assetRecordCapacity = 64;
        [NonSerialized] private int _assetLeaseCapacity = 128;

        /// <inheritdoc />
        public override int AssetRecordCapacity
        {
            get => _assetRecordCapacity;
            set
            {
                _assetRecordCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity);
            }
        }

        /// <inheritdoc />
        public override int AssetLeaseCapacity
        {
            get => _assetLeaseCapacity;
            set
            {
                _assetLeaseCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity);
            }
        }

        /// <inheritdoc />
        public override int BindingOwnerCapacity { get; set; }

        /// <inheritdoc />
        public override int BindingSlotCapacity { get; set; }

        /// <inheritdoc />
        [NonSerialized] private float _idleAssetExpireTime = 60f;
        [NonSerialized] private int _idleAssetCapacity = 256;

        /// <inheritdoc />
        public override float IdleAssetExpireTime
        {
            get => _idleAssetExpireTime;
            set => _idleAssetExpireTime = value < 0f ? 0f : value;
        }

        /// <inheritdoc />
        public override int IdleAssetCapacity
        {
            get => _idleAssetCapacity;
            set
            {
                _idleAssetCapacity = value < 0 ? 0 : value;
                // 不当场淘汰：那等于把一次 O(n) 突发挂在一次属性赋值上。
                Store.RequestIdleCapacityTrim();
            }
        }

        #endregion

        #region 预热 [WARMUP]

        /// <inheritdoc />
        public override void WarmupResourceRecords(int assetCapacity, int leaseCapacity)
        {
            Store.EnsureRecordCapacity(assetCapacity);
            Store.EnsureLoadingOperationCapacity(assetCapacity);

            if (assetCapacity > 0)
            {
                Store.EnsureAssetSlotPage(assetCapacity - 1);
            }

            if (leaseCapacity > 0)
            {
                Store.EnsureLeaseSlotPage(leaseCapacity - 1);
            }
        }

        #endregion
        
        #region 公共 Lease API [PUBLIC LEASE API]

        /// <inheritdoc />
        /// <remarks>Addressables 没有同步取资产的公开 API，同步族保持 fail-fast——
        /// 用 <c>Task.Wait()</c> 硬等会把主线程挂在驱动上，比抛错更糟。</remarks>
        /// <inheritdoc />
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

        #region 过期回收 [EXPIRY & RECYCLING]

        /// <inheritdoc />
        public override void ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)
        {
            // 销毁态兜底回收先于预算判定，也先于内核的到期走查——与 YooAsset 侧同一口径，别调换。
            _bindingService?.ProcessDestroyedObjects(destroySweepBudget);
            Store.ProcessResourceMaintenance(unscaledTime, expireBudget);
        }

        /// <inheritdoc />
        public override int ReleaseAllUnusedAssetRecords()
        {
            return Store.ReleaseAllUnusedAssetRecords();
        }

        /// <inheritdoc />
        public override void ForceReleaseAllAssetRecords()
        {
            Store.ForceReleaseAllAssetRecords();
        }

        #endregion

        #region 诊断 [DIAGNOSTICS]

        /// <inheritdoc />
        public override int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount)
        {
            return Store.GetAssetInfos(results, startIndex, maxCount);
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 构建实验性后端能力缺失异常：由调用方以 throw 语句抛出，保证非 void 成员的代码路径终止性。
        /// </summary>
        /// <param name="api">触发失败的调用方成员名。</param>
        /// <returns>预构建的 GameException。</returns>
        private static GameException CreateNotSupported([CallerMemberName] string api = null)
        {
            return new GameException(StringUtility.Format(
                "[AddressableHandler] {0} is not implemented. This experimental backend covers the async lease/binding " +
                "families only; use the async counterpart or YooAssetHandler.",
                api ?? "API"));
        }

        #endregion

        #region 框架抽象适配 [FRAMEWORK ADAPTERS]

        /// <summary>
        /// Addressables 异步操作适配器。
        /// </summary>
        private sealed class AddressableOperationAdapter : IResourceOperation
        {
            private readonly AsyncOperationHandle _handle;

            public AddressableOperationAdapter(AsyncOperationHandle handle)
            {
                _handle = handle;
            }

            public bool IsDone => _handle.IsDone;
            public float Progress => _handle.PercentComplete;
            public bool Succeed => _handle.Status == AsyncOperationStatus.Succeeded;
            public string Error => _handle.Status == AsyncOperationStatus.Failed ? _handle.OperationException?.Message : null;
        }

        #endregion
    }
}
#endif
