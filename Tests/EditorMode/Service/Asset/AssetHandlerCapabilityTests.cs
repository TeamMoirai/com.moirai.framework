using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Asset;
using Moirai.Atropos.Asset.YooAsset;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace Tests.EditorMode.Service.Asset
{
    /// <summary>
    /// 编辑器侧包名探针接管后的行为用例：清单仍读得到、探针按认领生效、桥不再判具体后端类型。
    /// </summary>
    /// <remarks>
    /// 探针接管前后，包名清单的唯一来源仍是收集器配置：这里断的是行为不降级，而不是某个实现体在不在。<br />
    /// 夹具 <see cref="UnrelatedHandler"/> 是本仓唯一的 <see cref="IAssetService"/> 实现替身，
    /// 抽象面大是刻意的例外——它只为证明认领面存在，一切后端成员都不许被走到。
    /// </remarks>
    internal sealed class AssetHandlerCapabilityTests
    {
        [Test]
        public void Bridge_PackageNames_StillComeFromTheCollector_AfterTheProbeTakesOver()
        {
            AssetPackageBridge.Internal_ResetCacheForTest();

            string[] names = AssetPackageBridge.GetPackageNames();

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
        private sealed class UnrelatedHandler : FrameworkHandler, IAssetService
        {
            /// <summary>本夹具的每个后端成员都是不可达路径：走到即说明用例越过了认领面本身。</summary>
            private static AssertionException NotReached() =>
                new AssertionException("UnrelatedHandler 只用于证明探针不认领，不该被调用任何后端成员");

            public string DefaultPackageName { get; set; }
            public string HostServerURL { get; set; }
            public string FallbackHostServerURL { get; set; }
            public EAssetLoadWayWebGL LoadResWayWebGL { get; set; }
            public string PackageVersion { get; set; }
            public bool AutoUnloadBundleWhenUnused { get; set; }
            public int DownloadingMaxNum { get; set; }
            public int FailedTryAgain { get; set; }
            public long Milliseconds { get; set; }
            public int AssetRecordCapacity { get; set; }
            public int AssetLeaseCapacity { get; set; }
            public int BindingOwnerCapacity { get; set; }
            public int BindingSlotCapacity { get; set; }
            public float IdleAssetExpireTime { get; set; }
            public int IdleAssetCapacity { get; set; }

            public IAssetBindingService BindingService => throw NotReached();
            public string ApplicableGameVersion => throw NotReached();
            public int InternalResourceVersion => throw NotReached();
            public bool UpdatableWhilePlaying => throw NotReached();

            public void Initialize()
            {
                throw NotReached();
            }

            public UniTask<AssetPackageInitResult> InitializePackageAsync(string packageName, bool needInitManifest = false)
            {
                throw NotReached();
            }

            public UniTask<bool> TryInitializePackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "")
            {
                throw NotReached();
            }

            public string GetPackageVersion(string customPackageName = "")
            {
                throw NotReached();
            }

            public AssetPackageVersionResult RequestPackageVersion(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "")
            {
                throw NotReached();
            }

            public void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer)
            {
                throw NotReached();
            }

            public IAssetOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
            {
                throw NotReached();
            }

            public IAssetDownloader CreateAssetDownloader(string customPackageName = "")
            {
                throw NotReached();
            }

            public AssetClearCacheResult StartClearCache(EAssetClearMode clearMode, string customPackageName = "")
            {
                throw NotReached();
            }

            public void ClearAllBundleFiles(string customPackageName = "")
            {
                throw NotReached();
            }

            public void OnLowMemory()
            {
                throw NotReached();
            }

            public void SetForceUnloadUnusedAssetsAction(Action<bool> action)
            {
                throw NotReached();
            }

            public void UnloadUnusedAssets()
            {
                throw NotReached();
            }

            public void UnloadUnusedAssets(bool force)
            {
                throw NotReached();
            }

            public void ForceUnloadAllAssets()
            {
                throw NotReached();
            }

            public void ForceUnloadUnusedAssets(bool performGCCollect)
            {
                throw NotReached();
            }

            public bool IsNeedDownloadFromRemote(string location, string packageName = "")
            {
                throw NotReached();
            }

            public long GetDownloadSize(string location, string packageName = "")
            {
                throw NotReached();
            }

            public AssetInfoEntry[] GetAssetInfos(string tag, string packageName = "")
            {
                throw NotReached();
            }

            public AssetInfoEntry[] GetAssetInfos(string[] tags, string packageName = "")
            {
                throw NotReached();
            }

            public AssetInfoEntry GetAssetInfo(string location, string packageName = "")
            {
                throw NotReached();
            }

            public EAssetHasResult HasAsset(string location, string packageName = "")
            {
                throw NotReached();
            }

            public bool IsLocationValid(string location, string packageName = "")
            {
                throw NotReached();
            }

            public bool TryGetLocationByGuid(string guid, out string location, string packageName = "")
            {
                throw NotReached();
            }

            public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
            {
                throw NotReached();
            }

            public UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "")
            {
                throw NotReached();
            }

            public AssetSceneHandle LoadSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority, string packageName = "")
            {
                throw NotReached();
            }

            public void WarmupAssetRecords(int assetCapacity, int leaseCapacity)
            {
                throw NotReached();
            }

            public AssetLeaseHandle AcquireDirect(AssetKey key)
            {
                throw NotReached();
            }

            public UniTask<AssetLeaseHandle> AcquireDirectAsync(AssetKey key, CancellationToken cancellationToken = default)
            {
                throw NotReached();
            }

            public void Release(AssetLeaseHandle handle)
            {
                throw NotReached();
            }

            public AssetLease<T> LoadLease<T>(AssetKey key) where T : UObject
            {
                throw NotReached();
            }

            public AssetLease<T> LoadLease<T>(string location, string packageName = "") where T : UObject
            {
                throw NotReached();
            }

            public UniTask<AssetLease<T>> LoadLeaseAsync<T>(AssetKey key, CancellationToken cancellationToken = default) where T : UObject
            {
                throw NotReached();
            }

            public UniTask<AssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UObject
            {
                throw NotReached();
            }

            public bool TryGetLeaseAsset(AssetLeaseHandle handle, out UObject asset)
            {
                throw NotReached();
            }

            public AssetLeaseHandle AcquireBinding(AssetKey key)
            {
                throw NotReached();
            }

            public bool TryAcquireBindingCached(AssetKey key, out AssetLeaseHandle handle)
            {
                throw NotReached();
            }

            public UniTask<AssetLeaseHandle> AcquireBindingAsync(AssetKey key, CancellationToken cancellationToken)
            {
                throw NotReached();
            }

            public UniTask<AssetLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName, EAssetLeaseOption options, CancellationToken cancellationToken)
            {
                throw NotReached();
            }

            public bool TryGetSubSpriteAsset(AssetLeaseHandle handle, string spriteName, out Sprite sprite)
            {
                throw NotReached();
            }

            public bool TryGetLeaseAssetId(AssetLeaseHandle handle, out int assetId)
            {
                throw NotReached();
            }

            public void SetLeaseOptions(AssetLeaseHandle handle, EAssetLeaseOption options)
            {
                throw NotReached();
            }

            public AssetLeaseHandle AcquirePrefabSourceLease(string location, string packageName)
            {
                throw NotReached();
            }

            public UniTask<AssetLeaseHandle> AcquirePrefabSourceLeaseAsync(string location, string packageName, CancellationToken cancellationToken)
            {
                throw NotReached();
            }

            public void ProcessAssetMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)
            {
                throw NotReached();
            }

            public int ReleaseAllUnusedAssetRecords()
            {
                throw NotReached();
            }

            public void ForceReleaseAllAssetRecords()
            {
                throw NotReached();
            }

            public int GetAssetInfos(AssetInfo[] results, int startIndex, int maxCount)
            {
                throw NotReached();
            }
        }
    }
}
