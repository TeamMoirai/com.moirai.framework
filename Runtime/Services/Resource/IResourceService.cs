using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源服务契约（策略模式契约接口）：资源后端的唯一实现契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 通用资源加载、缓存、租约与绑定契约，框架不依赖具体资源系统；内建后端 <see cref="YooAssetHandler"/>（经家族基座 <see cref="ResourceRecordHostHandler"/> 承载记录簿共享实现）， <br />
    /// 装配外的后端（如 addressables 插件）实现本契约即可。实现约束：实现类须继承 <see cref="Moirai.Atropos.FrameworkHandler"/>（生命周期与序列化基座）。 <br />
    /// 各成员的语义文档与 <see cref="ResourceService"/> 门面对应 API 同源；internal 诊断面（<c>GetLeaseSourceInfos</c>）在附属接口 <see cref="IResourceLeaseDiagnostics"/>，租约窄缝在 <see cref="IResourceLeaseSource"/>。
    /// </remarks>
    public interface IResourceService
    {
        /// <summary>本后端是否消费资源包名轴：为假时编辑器侧不接管包名下拉行（只有一张隐式目录、没有包名轴的后端覆写为假）。默认真。</summary>
        bool ConsumesPackageNames => true;

        /// <summary>DefaultPackageName。</summary>
        string DefaultPackageName { get; set; }

        /// <summary>HostServerURL。</summary>
        string HostServerURL { get; set; }

        /// <summary>FallbackHostServerURL。</summary>
        string FallbackHostServerURL { get; set; }

        /// <summary>LoadResWayWebGL。</summary>
        EResourceLoadWayWebGL LoadResWayWebGL { get; set; }

        /// <summary>UpdatableWhilePlaying。</summary>
        bool UpdatableWhilePlaying { get; }

        /// <summary>AutoUnloadBundleWhenUnused。</summary>
        bool AutoUnloadBundleWhenUnused { get; set; }

        /// <summary>DownloadingMaxNum。</summary>
        int DownloadingMaxNum { get; set; }

        /// <summary>FailedTryAgain。</summary>
        int FailedTryAgain { get; set; }

        /// <summary>Milliseconds。</summary>
        long Milliseconds { get; set; }

        /// <summary>AssetRecordCapacity。</summary>
        int AssetRecordCapacity { get; set; }

        /// <summary>AssetLeaseCapacity。</summary>
        int AssetLeaseCapacity { get; set; }

        /// <summary>BindingOwnerCapacity。</summary>
        int BindingOwnerCapacity { get; set; }

        /// <summary>BindingSlotCapacity。</summary>
        int BindingSlotCapacity { get; set; }

        /// <summary>IdleAssetExpireTime。</summary>
        float IdleAssetExpireTime { get; set; }

        /// <summary>IdleAssetCapacity。</summary>
        int IdleAssetCapacity { get; set; }

        /// <summary>BindingService。</summary>
        IResourceBindingService BindingService { get; }

        /// <summary>ApplicableGameVersion。</summary>
        string ApplicableGameVersion { get; }

        /// <summary>InternalResourceVersion。</summary>
        int InternalResourceVersion { get; }

        /// <summary>PackageVersion。</summary>
        string PackageVersion { get; set; }

        /// <summary>是否已经初始化（生命周期标志，源自 FrameworkHandler.IsInitialized）。</summary>
        bool IsInitialized { get; }

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void Initialize();

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        UniTask<ResourcePackageInitResult> InitializePackageAsync(string packageName, bool needInitManifest = false);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        UniTask<bool> TryInitializePackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        string GetPackageVersion(string customPackageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourcePackageVersionResult RequestPackageVersion(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        IResourceOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        IResourceDownloader CreateResourceDownloader(string customPackageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourceClearCacheResult StartClearCache(EResourceClearMode clearMode, string customPackageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void ClearAllBundleFiles(string customPackageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void OnLowMemory();

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void SetForceUnloadUnusedAssetsAction(Action<bool> action);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void UnloadUnusedAssets();

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void UnloadUnusedAssets(bool force);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void ForceUnloadAllAssets();

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void ForceUnloadUnusedAssets(bool performGCCollect);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        bool IsNeedDownloadFromRemote(string location, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        long GetDownloadSize(string location, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourceAssetInfoEntry[] GetAssetInfos(string tag, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourceAssetInfoEntry[] GetAssetInfos(string[] tags, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourceAssetInfoEntry GetAssetInfo(string location, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        EResourceHasAssetResult HasAsset(string location, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        bool IsLocationValid(string location, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        bool TryGetLocationByGuid(string guid, out string location, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        GameObject LoadGameObject(string location, Transform parent = null, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourceSceneHandle LoadSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority, string packageName = "");

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void WarmupResourceRecords(int assetCapacity, int leaseCapacity);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourceLeaseHandle AcquireDirect(ResourceKey key);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken cancellationToken = default);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void Release(ResourceLeaseHandle handle);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourceAssetLease<T> LoadLease<T>(ResourceKey key) where T : UObject;

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "") where T : UObject;

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken cancellationToken = default) where T : UObject;

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UObject;

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourceLeaseHandle AcquireBinding(ResourceKey key);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        bool TryAcquireBindingCached(ResourceKey key, out ResourceLeaseHandle handle);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key, CancellationToken cancellationToken);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName, EResourceLeaseOption options, CancellationToken cancellationToken);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        ResourceLeaseHandle AcquirePrefabSourceLease(string location, string packageName);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        UniTask<ResourceLeaseHandle> AcquirePrefabSourceLeaseAsync(string location, string packageName, CancellationToken cancellationToken);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget);

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        int ReleaseAllUnusedAssetRecords();

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        void ForceReleaseAllAssetRecords();

        /// <summary>见 <see cref="ResourceService"/> 门面对应 API 的语义文档。</summary>
        int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount);
    }

    /// <summary>
    /// 资源后端的 internal 诊断回调面：租约来源诊断的参数类型（<see cref="ResourceLeaseSourceInfo"/>）是 internal，不进公共契约。
    /// </summary>
    internal interface IResourceLeaseDiagnostics
    {
        /// <summary>填充租约来源诊断信息。</summary>
        int GetLeaseSourceInfos(ResourceLeaseSourceInfo[] results, int startIndex, int maxCount, out int outstandingCount, out int sourcedCount);
    }
}
