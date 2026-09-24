#if ADDRESSABLES_INSTALLED
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// Addressables 后端的初始化、定位与实例化面——把 YooAsset 显式建模的"包 / 清单 / 位置有效性"
    /// 落到 Addressables 的实际形态上：一个隐式目录、按 key 定位、只能异步实例化。
    /// </summary>
    partial class AddressableHandler
    {
        #region 初始化 [INITIALIZATION]

        /// <inheritdoc />
        public override async UniTask<ResourcePackageInitResult> InitPackage(string packageName, bool needInitManifest = false)
        {
            string targetPackage = string.IsNullOrEmpty(packageName) ? DefaultPackageName : packageName;
            // autoReleaseHandle: false —— 目录句柄一释放，ResourceLocators 就空了，
            // 之后所有按 key 的定位与加载都会莫名失败，且没有任何地方说为什么。
            AsyncOperationHandle<IResourceLocator> handle = Addressables.InitializeAsync(false);
            await handle.ToUniTask();
            return new ResourcePackageInitResult
            {
                PackageName = targetPackage,
                Operation = new AddressableOperation<IResourceLocator>(handle),
            };
        }

        /// <inheritdoc />
        public override async UniTask<bool> InitPackageAsync(string packageName = "", string hostServerURL = "",
            string fallbackHostServerURL = "")
        {
            ResourcePackageInitResult result = await InitPackage(packageName);
            return result != null && result.Succeed;
        }

        /// <summary>
        /// <see cref="AsyncOperationHandle{TObject}"/> 到 <see cref="IResourceOperation"/> 的适配。
        /// <para>地址是 struct，故以泛型类持字段：既免装箱，也不必"解箱时精确知道 T"。</para>
        /// </summary>
        private sealed class AddressableOperation<TObject> : IResourceOperation
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
        
        #region 定位 [LOCATE]

        /// <summary>
        /// 按 key 同步定位。地址在初始化后常驻，<c>Locate</c> 是 Addressables 唯一的同步查询面；
        /// type 传 null 表示"任意类型"——传具体类型会把同一地址的其它导入项判成不存在。
        /// </summary>
        private static bool TryLocate(string location, out IList<IResourceLocation> locations)
        {
            locations = null;
            if (string.IsNullOrEmpty(location) || Addressables.ResourceLocators == null)
            {
                return false;
            }

            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                if (locator.Locate(location, null, out IList<IResourceLocation> found) && found != null &&
                    found.Count > 0)
                {
                    locations = found;
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override bool IsLocationValid(string location, string packageName = "")
        {
            return TryLocate(location, out _);
        }

        /// <inheritdoc />
        /// <remarks>只能答"有没有这条地址"：OnDisk / Online 的分别要 <c>GetDownloadSizeAsync</c>，
        /// 那是异步的，同步问不出来。命中一律回 AssetOnDisk，不当"已在本地"的保证用。
        /// 要精确判断请走 <c>IsNeedDownloadFromRemote</c> / <c>GetDownloadSize</c>（本后端保持 fail-fast）。</remarks>
        public override EResourceHasAssetResult HasAsset(string location, string packageName = "")
        {
            return TryLocate(location, out _)
                ? EResourceHasAssetResult.AssetOnDisk
                : EResourceHasAssetResult.NotExist;
        }

        #endregion
        
        #region 实例化 [INSTANTIATE]

        /// <inheritdoc />
        public override async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null,
            CancellationToken cancellationToken = default, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            if (!TryLocate(location, out _))
            {
                LogUtility.Error("Could not found location [{0}].", location);
                return null;
            }

            ResourceLeaseHandle prefabLease = await AcquireLeaseAsync(
                new ResourceKey(location, packageName, typeof(GameObject), EResourceAssetKind.Prefab),
                EResourceLeaseKind.Direct, EResourceLeaseOption.None, cancellationToken);
            if (!prefabLease.IsValid)
            {
                return null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Store.Release(prefabLease);
                return null;
            }

            if (!Store.TryGetLeaseAsset(prefabLease, out UObject prefabObject) ||
                prefabObject is not GameObject prefab)
            {
                Store.Release(prefabLease);
                return null;
            }

            uint unloadGeneration = Store.UnloadGeneration;
            GameObject instance = UObject.Instantiate(prefab, parent);

            // 与 YooAsset 侧同一组守卫：实例化期间可能已发生回收/关停，父节点也可能是 fake null。
            if (instance == null || Store.IsDestroying || unloadGeneration != Store.UnloadGeneration)
            {
                if (instance != null)
                {
                    UObject.Destroy(instance);
                }

                Store.Release(prefabLease);
                return null;
            }

            ResourceOwner owner = EnsureResourceOwner(instance);
            EResourceBindStatus bindStatus = _bindingService.RegisterPrefabSource(owner, prefabLease, prefab);
            if (bindStatus != EResourceBindStatus.Success)
            {
                UObject.Destroy(instance);
                Store.Release(prefabLease);
                return null;
            }

            return instance;
        }

        #endregion
    }
}
#endif
