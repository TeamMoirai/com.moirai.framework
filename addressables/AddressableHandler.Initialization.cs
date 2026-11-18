#if ADDRESSABLES_INSTALLED
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Moirai.Atropos.Asset
{
    /// <summary>
    /// Addressables 后端的初始化与包生命周期面：一个隐式目录、单包初始化、包版本与缓存清理。
    /// </summary>
    /// <remarks>Addressables 没有多包与两步式 Check→Update 下载器，包管理族里不可答的成员统一 fail-fast。</remarks>
    partial class AddressableHandler
    {
        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public override void Initialize()
        {
            CreateBindingService();
            WarmupBindingRecords();
        }

        /// <inheritdoc />
        protected override void OnShutdown()
        {
            base.OnShutdown();

            ShutdownBindingService();
            ForceReleaseAllAssetRecords();
        }

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <inheritdoc />
        public override async UniTask<AssetPackageInitResult> InitializePackageAsync(string packageName, bool needInitManifest = false)
        {
            string targetPackage = string.IsNullOrEmpty(packageName) ? DefaultPackageName : packageName;
            // autoReleaseHandle: false —— 目录句柄一释放，ResourceLocators 就空了，
            // 之后所有按 key 的定位与加载都会莫名失败，且没有任何地方说为什么。
            AsyncOperationHandle<IResourceLocator> handle = Addressables.InitializeAsync(false);
            await handle.ToUniTask();
            return new AssetPackageInitResult
            {
                PackageName = targetPackage,
                Operation = new AddressableOperation<IResourceLocator>(handle),
            };
        }

        /// <inheritdoc />
        public override async UniTask<bool> TryInitializePackageAsync(string packageName = "", string hostServerURL = "",
            string fallbackHostServerURL = "")
        {
            AssetPackageInitResult result = await InitializePackageAsync(packageName);
            return result != null && result.Succeed;
        }

        /// <summary>
        /// <see cref="AsyncOperationHandle{TObject}"/> 到 <see cref="IAssetOperation"/> 的适配。
        /// </summary>
        /// <remarks>句柄是 struct，故以泛型类持字段：既免装箱，也不必在解箱时精确知道 <c>TObject</c>。</remarks>
        private sealed class AddressableOperation<TObject> : IAssetOperation
        {
            private readonly AsyncOperationHandle<TObject> _handle;

            internal AddressableOperation(AsyncOperationHandle<TObject> handle)
            {
                _handle = handle;
            }

            public bool IsDone => _handle.IsDone;

            public float Progress => _handle.IsValid() ? _handle.PercentComplete : 1f;

            public bool Succeed => _handle.IsValid() && _handle.Status == AsyncOperationStatus.Succeeded;

            public string Error => _handle.IsValid() && _handle.OperationException != null
                ? _handle.OperationException.Message
                : string.Empty;
        }

        #endregion

        #region 包管理 [PACKAGE MANAGEMENT]

        /// <inheritdoc />
        public override string GetPackageVersion(string customPackageName = "")
        {
            return string.Empty;
        }

        /// <inheritdoc />
        public override AssetPackageVersionResult RequestPackageVersion(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "")
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
        public override IAssetOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override IAssetDownloader CreateAssetDownloader(string customPackageName = "")
        {
            throw CreateNotSupported();
        }

        /// <inheritdoc />
        public override AssetClearCacheResult StartClearCache(EAssetClearMode clearMode, string customPackageName = "")
        {
            Addressables.ClearResourceLocators();
            if (clearMode == EAssetClearMode.ClearAllBundleFiles)
            {
                Caching.ClearCache();
            }

            return new AssetClearCacheResult
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
    }
}
#endif
