using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Resource.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace Tests.EditorMode.Service.Resource
{
    /// <summary>
    /// 编辑器侧包名探针接管后的行为用例：清单仍读得到、探针按认领生效、桥不再判具体后端类型。
    /// </summary>
    /// <remarks>
    /// 探针接管前后，包名清单的唯一来源仍是收集器配置：这里断的是行为不降级，而不是某个实现体在不在。<br />
    /// 夹具 <see cref="UnrelatedHandler"/> 是本仓唯一的 <see cref="ResourceServiceHandler"/> 派生替身，
    /// 抽象面大是刻意的例外——它只为证明认领面存在，一切后端成员都不许被走到。
    /// </remarks>
    internal sealed class ResourceHandlerCapabilityTests
    {
        [Test]
        public void Bridge_PackageNames_StillComeFromTheCollector_AfterTheProbeTakesOver()
        {
            ResourcePackageBridge.Internal_ResetCacheForTest();

            string[] names = ResourcePackageBridge.GetPackageNames();

            Assert.IsNotNull(names, "内建后端在位时不该读成 null（null 专指后端自报不消费包名轴）");
            Assert.IsNotEmpty(names, "探针没接上时这里会是空数组：收集器读取没被搬进 YooAssetEditorPackageProbe");
        }

        [Test]
        public void Probe_DoesNotClaim_AHandlerItIsNot()
        {
            var probe = new YooAssetEditorPackageProbe();

            Assert.IsFalse(probe.AppliesTo(null), "null 后端不认领");
            Assert.IsFalse(probe.AppliesTo(new UnrelatedHandler()),
                "探针只认自己的后端；不认领时桥必须退回空数组而不是拿别人的清单冒名");
        }

        /// <summary>只用来占位的无关后端：不读盘、不装配，仅为证明认领面存在。</summary>
        private sealed class UnrelatedHandler : ResourceServiceHandler
        {
            /// <summary>本夹具的每个后端成员都是不可达路径：走到即说明用例越过了认领面本身。</summary>
            private static AssertionException NotReached() =>
                new AssertionException("UnrelatedHandler 只用于证明探针不认领，不该被调用任何后端成员");

            public override string DefaultPackageName { get; set; }
            public override string HostServerURL { get; set; }
            public override string FallbackHostServerURL { get; set; }
            public override EResourceLoadWayWebGL LoadResWayWebGL { get; set; }
            public override string PackageVersion { get; set; }
            public override bool AutoUnloadBundleWhenUnused { get; set; }
            public override int DownloadingMaxNum { get; set; }
            public override int FailedTryAgain { get; set; }
            public override long Milliseconds { get; set; }
            public override int AssetRecordCapacity { get; set; }
            public override int AssetLeaseCapacity { get; set; }
            public override int BindingOwnerCapacity { get; set; }
            public override int BindingSlotCapacity { get; set; }
            public override float IdleAssetExpireTime { get; set; }
            public override int IdleAssetCapacity { get; set; }

            public override IResourceBindingService BindingService => throw NotReached();
            public override string ApplicableGameVersion => throw NotReached();
            public override int InternalResourceVersion => throw NotReached();
            public override bool UpdatableWhilePlaying => throw NotReached();

            public override void Initialize()
            {
                throw NotReached();
            }

            public override UniTask<ResourcePackageInitResult> InitializePackageAsync(string packageName, bool needInitManifest = false)
            {
                throw NotReached();
            }

            public override UniTask<bool> TryInitializePackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "")
            {
                throw NotReached();
            }

            public override string GetPackageVersion(string customPackageName = "")
            {
                throw NotReached();
            }

            public override ResourcePackageVersionResult RequestPackageVersion(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "")
            {
                throw NotReached();
            }

            public override void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer)
            {
                throw NotReached();
            }

            public override IResourceOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
            {
                throw NotReached();
            }

            public override IResourceDownloader CreateResourceDownloader(string customPackageName = "")
            {
                throw NotReached();
            }

            public override ResourceClearCacheResult StartClearCache(EResourceClearMode clearMode, string customPackageName = "")
            {
                throw NotReached();
            }

            public override void ClearAllBundleFiles(string customPackageName = "")
            {
                throw NotReached();
            }

            public override void OnLowMemory()
            {
                throw NotReached();
            }

            public override void SetForceUnloadUnusedAssetsAction(Action<bool> action)
            {
                throw NotReached();
            }

            public override void UnloadUnusedAssets()
            {
                throw NotReached();
            }

            public override void UnloadUnusedAssets(bool force)
            {
                throw NotReached();
            }

            public override void ForceUnloadAllAssets()
            {
                throw NotReached();
            }

            public override void ForceUnloadUnusedAssets(bool performGCCollect)
            {
                throw NotReached();
            }

            public override bool IsNeedDownloadFromRemote(string location, string packageName = "")
            {
                throw NotReached();
            }

            public override long GetDownloadSize(string location, string packageName = "")
            {
                throw NotReached();
            }

            public override ResourceAssetInfoEntry[] GetAssetInfos(string tag, string packageName = "")
            {
                throw NotReached();
            }

            public override ResourceAssetInfoEntry[] GetAssetInfos(string[] tags, string packageName = "")
            {
                throw NotReached();
            }

            public override ResourceAssetInfoEntry GetAssetInfo(string location, string packageName = "")
            {
                throw NotReached();
            }

            public override EResourceHasAssetResult HasAsset(string location, string packageName = "")
            {
                throw NotReached();
            }

            public override bool IsLocationValid(string location, string packageName = "")
            {
                throw NotReached();
            }

            public override bool TryGetLocationByGuid(string guid, out string location, string packageName = "")
            {
                throw NotReached();
            }

            public override GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
            {
                throw NotReached();
            }

            public override UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "")
            {
                throw NotReached();
            }

            public override ResourceSceneHandle LoadSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority, string packageName = "")
            {
                throw NotReached();
            }

            public override void WarmupResourceRecords(int assetCapacity, int leaseCapacity)
            {
                throw NotReached();
            }

            public override ResourceLeaseHandle AcquireDirect(ResourceKey key)
            {
                throw NotReached();
            }

            public override UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken cancellationToken = default)
            {
                throw NotReached();
            }

            public override void Release(ResourceLeaseHandle handle)
            {
                throw NotReached();
            }

            public override ResourceAssetLease<T> LoadLease<T>(ResourceKey key)
            {
                throw NotReached();
            }

            public override ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "")
            {
                throw NotReached();
            }

            public override UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken cancellationToken = default)
            {
                throw NotReached();
            }

            public override UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "")
            {
                throw NotReached();
            }

            public override bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset)
            {
                throw NotReached();
            }

            public override ResourceLeaseHandle AcquireBinding(ResourceKey key)
            {
                throw NotReached();
            }

            public override bool TryAcquireBindingCached(ResourceKey key, out ResourceLeaseHandle handle)
            {
                throw NotReached();
            }

            public override UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key, CancellationToken cancellationToken)
            {
                throw NotReached();
            }

            public override UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName, EResourceLeaseOption options, CancellationToken cancellationToken)
            {
                throw NotReached();
            }

            public override bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite)
            {
                throw NotReached();
            }

            public override bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
            {
                throw NotReached();
            }

            public override void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options)
            {
                throw NotReached();
            }

            public override ResourceLeaseHandle AcquirePrefabSourceLease(string location, string packageName)
            {
                throw NotReached();
            }

            public override UniTask<ResourceLeaseHandle> AcquirePrefabSourceLeaseAsync(string location, string packageName, CancellationToken cancellationToken)
            {
                throw NotReached();
            }

            public override void ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)
            {
                throw NotReached();
            }

            public override int ReleaseAllUnusedAssetRecords()
            {
                throw NotReached();
            }

            public override void ForceReleaseAllAssetRecords()
            {
                throw NotReached();
            }

            public override int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount)
            {
                throw NotReached();
            }
        }
    }
}
