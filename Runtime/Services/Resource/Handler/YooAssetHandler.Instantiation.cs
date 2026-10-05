using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// YooAsset 后端的预制体实例化面：取预制体源租约、实例化并把实例挂进绑定服务。
    /// </summary>
    /// <remarks>实例化会派发 Awake，其中可重入回收/关停，故两条路径共用同一组代际与 fake null 守卫。</remarks>
    partial class YooAssetHandler
    {
        #region 实例化 [INSTANTIATE]

        /// <inheritdoc />
        public override GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            if (!IsLocationValid(location, packageName))
            {
                LogUtility.Error("Could not found location [{0}].", location);
                return null;
            }

            ResourceLeaseHandle prefabLease = AcquirePrefabSourceLease(location, packageName);
            if (!prefabLease.IsValid)
            {
                return null;
            }

            if (!Store.TryGetLeaseAsset(prefabLease, out UObject prefabObject) ||
                prefabObject is not GameObject prefab)
            {
                Store.Release(prefabLease);
                return null;
            }

            uint unloadGeneration = Store._unloadGeneration;
            GameObject instance = UObject.Instantiate(prefab, parent);

            // 实例化会派发 Awake，其中可以重入强制回收/关停：
            // 此时 prefab 记录可能已被释放，租约不得再挂到清空过的绑定服务上。
            if (instance == null || Store._isDestroying || unloadGeneration != Store._unloadGeneration)
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

        /// <inheritdoc />
        public override async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            if (!IsLocationValid(location, packageName))
            {
                LogUtility.Error("Could not found location [{0}].", location);
                return null;
            }

            ResourceLeaseHandle prefabLease = await AcquirePrefabSourceLeaseAsync(location, packageName,
                cancellationToken);
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

            // 父节点可能在等待期间被销毁：fake null 的 Transform 直接交给 Instantiate 会抛。
            if (!ReferenceEquals(parent, null) && parent == null)
            {
                Store.Release(prefabLease);
                return null;
            }

            uint unloadGeneration = Store._unloadGeneration;
            GameObject instance = UObject.Instantiate(prefab, parent);

            // 实例化会派发 Awake，其中可以重入强制回收/关停：
            // 此时 prefab 记录可能已被释放，租约不得再挂到清空过的绑定服务上。
            if (instance == null || Store._isDestroying || unloadGeneration != Store._unloadGeneration)
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
