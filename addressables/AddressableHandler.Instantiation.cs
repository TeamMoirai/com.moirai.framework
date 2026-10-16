#if ADDRESSABLES_INSTALLED
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// Addressables 后端的实例化面：预制体经租约取用后实例化，实例挂进绑定服务。
    /// </summary>
    /// <remarks>Addressables 无同步取资产 API，同步实例化 fail-fast；异步路径与内建后端侧共用同一组守卫。</remarks>
    partial class AddressableHandler
    {
        #region 实例化 [INSTANTIATE]

        /// <inheritdoc />
        public override GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            throw CreateNotSupported();
        }

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
                ELeaseKind.Direct, EResourceLeaseOption.None, cancellationToken);
            if (!prefabLease.IsValid)
            {
                return null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Release(prefabLease);
                return null;
            }

            if (!TryGetLeaseAsset(prefabLease, out UObject prefabObject) ||
                prefabObject is not GameObject prefab)
            {
                Release(prefabLease);
                return null;
            }

            uint unloadGeneration = UnloadGeneration;
            GameObject instance = UObject.Instantiate(prefab, parent);

            // 与内建后端侧同一组守卫：实例化期间可能已发生回收/关停，父节点也可能是 fake null。
            if (instance == null || IsRecordStoreDestroying || unloadGeneration != UnloadGeneration)
            {
                if (instance != null)
                {
                    UObject.Destroy(instance);
                }

                Release(prefabLease);
                return null;
            }

            ResourceOwner owner = EnsureResourceOwner(instance);
            EResourceBindStatus bindStatus = RegisterPrefabSource(owner, prefabLease, prefab);
            if (bindStatus != EResourceBindStatus.Success)
            {
                UObject.Destroy(instance);
                Release(prefabLease);
                return null;
            }

            return instance;
        }

        #endregion
    }
}
#endif
