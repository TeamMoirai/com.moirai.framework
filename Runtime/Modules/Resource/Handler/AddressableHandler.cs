#if ADDRESSABLES_INSTALLED
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 基于 Unity Addressables 的资源处理器实现（实验性）。
    /// <para><see cref="ResourceServiceHandler"/> 的 Addressables 后端实现。</para>
    /// <para>仅信息查询与真实缓存维护为可用行为；所有分发资源句柄或伪造成功语义的成员统一抛出 <see cref="GameException"/> fail-fast，禁止静默 no-op 掩盖误配置。</para>
    /// </summary>
    [Serializable]
    public sealed class AddressableHandler : ResourceServiceHandler
    {
        #region 基础属性 [BASE PROPERTIES]

        /// <inheritdoc />
        public override string DefaultPackageName { get; set; } = "Default";

        /// <inheritdoc />
        public override EResourcePlayMode PlayMode
        {
            get => EResourcePlayMode.Offline;
            set { }
        }

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

        /// <inheritdoc />
        public override UniTask<ResourcePackageInitResult> InitPackage(string packageName, bool needInitManifest = false)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override UniTask<bool> InitPackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "")
        {
            throw CreateNotSupported();
        }

        #endregion

        #region 包管理 [PACKAGE MANAGEMENT]

        /// <inheritdoc />
        public override string GetPackageVersion(string customPackageName = "")
        {
            return string.Empty;
        }

        /// <inheritdoc />
        public override ResourcePackageVersionResult RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "")
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
        public override ResourceClearCacheResult ClearCacheAsync(EResourceClearMode clearMode, string customPackageName = "")
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
            LogUtility.Warning("Low memory reported...");
        }

        /// <inheritdoc />
        public override void SetForceUnloadUnusedAssetsAction(Action<bool> action)
        {
        }

        /// <inheritdoc />
        public override void UnloadUnusedAssets()
        {
        }

        /// <inheritdoc />
        public override void UnloadUnusedAssets(bool force)
        {
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

        /// <inheritdoc />
        public override EResourceHasAssetResult HasAsset(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                return EResourceHasAssetResult.NotExist;
            }

            return Addressables.ResourceLocators != null && Addressables.ResourceLocators.Any()
                ? EResourceHasAssetResult.AssetOnDisk
                : EResourceHasAssetResult.NotExist;
        }

        /// <inheritdoc />
        public override bool IsLocationValid(string location, string packageName = "")
        {
            return !string.IsNullOrEmpty(location);
        }

        #endregion

        #region 资源加载 [ASSET LOADING]

        /// <inheritdoc />
        public override GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "")
        {
            throw CreateNotSupported();
        }

        #endregion

        #region 遗留 API [LEGACY API]

        /// <inheritdoc />
        [Obsolete("Use LoadLease<T> for explicit ownership.")]
        public override T LoadAsset<T>(string location, string packageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public override UniTask LoadAsset<T>(string location, Action<T> callback, string packageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public override UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public override UniTask LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public override UniTask LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            throw CreateNotSupported();
        }

        #endregion

        #region 容量属性 [CAPACITY PROPERTIES]

        /// <inheritdoc />
        public override int AssetRecordCapacity { get; set; }

        /// <inheritdoc />
        public override int AssetLeaseCapacity { get; set; }

        /// <inheritdoc />
        public override int BindingOwnerCapacity { get; set; }

        /// <inheritdoc />
        public override int BindingSlotCapacity { get; set; }

        /// <inheritdoc />
        public override int RegisteredTargetCapacity { get; set; }

        /// <inheritdoc />
        public override float IdleAssetExpireTime { get; set; }

        #endregion

        #region 预热 [WARMUP]

        /// <inheritdoc />
        public override void WarmupResourceRecords(int assetCapacity, int leaseCapacity, int unityObjectIndexCapacity)
        {
        }

        #endregion

        #region 资源卸载 [ASSET UNLOAD]

        /// <inheritdoc />
        [Obsolete("Use ResourceAssetLease<T> or Binding instead of LoadAsset/UnloadAsset.")]
        public override void UnloadAsset(object asset)
        {
            throw CreateNotSupported();
        }

        #endregion

        #region 公共 Lease API [PUBLIC LEASE API]

        /// <inheritdoc />
        public override ResourceLeaseHandle AcquireDirect(ResourceKey key)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken cancellationToken = default)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle)
        {
            handle = ResourceLeaseHandle.Invalid;
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override void Release(ResourceLeaseHandle handle)
        {
            throw CreateNotSupported();
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
        public override UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken cancellationToken = default)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override bool TryGetLeaseAsset(ResourceLeaseHandle handle, out Object asset)
        {
            asset = null;
            throw CreateNotSupported();
        }

        #endregion

        #region 内部 Lease 方法 [INTERNAL LEASE METHODS]

        /// <inheritdoc />
        internal override ResourceLeaseHandle AcquireBinding(ResourceKey key)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        internal override UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        internal override UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName, EResourceLeaseOption options, CancellationToken cancellationToken)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        internal override bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite)
        {
            sprite = null;
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        internal override bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
        {
            assetId = 0;
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        internal override void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        internal override ResourceLeaseHandle AcquirePrefabSourceLease(string location, string packageName)
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        internal override UniTask<ResourceLeaseHandle> AcquirePrefabSourceLeaseAsync(string location, string packageName, CancellationToken cancellationToken)
        {
            throw CreateNotSupported();
        }

        #endregion

        #region 过期回收 [EXPIRY & RECYCLING]

        /// <inheritdoc />
        internal override void ProcessKeepAlive(float unscaledTime, int maxCount)
        {
        }

        /// <inheritdoc />
        internal override int ReleaseAllUnusedAssetRecords()
        {
            return 0;
        }

        /// <inheritdoc />
        internal override void ForceReleaseAllAssetRecords()
        {
        }

        #endregion

        #region 诊断 [DIAGNOSTICS]

        /// <inheritdoc />
        public override int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount)
        {
            return 0;
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
                "[AddressableHandler] {0} is not implemented. This backend is experimental: lease/binding ownership is provided by YooAssetHandler.",
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
