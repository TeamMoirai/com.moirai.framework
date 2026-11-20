using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Asset;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// GameObject 池预制体加载器接口。
    /// </summary>
    public interface IPrefabLoader
    {
        /// <summary>
        /// 同步加载预制体。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>预制体。</returns>
        GameObject LoadPrefab(string location);

        /// <summary>
        /// 异步加载预制体。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>预制体。</returns>
        UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default);

        /// <summary>
        /// 卸载预制体（与一次加载配对调用）。
        /// </summary>
        /// <param name="prefab">预制体。</param>
        void UnloadPrefab(GameObject prefab);
    }

    /// <summary>
    /// 基于 AssetService 租约的预制体加载器——同一预制体的多次加载各持一份租约，卸载按 LIFO 配对释放。
    /// </summary>
    internal sealed class ResourcePrefabLoader : IPrefabLoader
    {
        #region 字段 [FIELDS]

        private readonly Dictionary<int, List<AssetLease<GameObject>>> _leases = new Dictionary<int, List<AssetLease<GameObject>>>(16);
        private IAssetService _asset;

        #endregion

        #region 属性 [PROPERTIES]

        private IAssetService Resource
        {
            get
            {
                if (_asset == null)
                {
                    _asset = AssetService.Handler;
                }

                return _asset;
            }
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 同步加载预制体。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>预制体。</returns>
        public GameObject LoadPrefab(string location)
        {
            AssetLease<GameObject> lease = AssetService.LoadLease<GameObject>(location);
            return RetainLease(lease);
        }

        /// <summary>
        /// 异步加载预制体。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>预制体。</returns>
        public async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
        {
            AssetLease<GameObject> lease = await AssetService.LoadLeaseAsync<GameObject>(location, cancellationToken);
            return RetainLease(lease);
        }

        /// <summary>
        /// 卸载预制体（释放最近一次加载的租约）。
        /// </summary>
        /// <param name="prefab">预制体。</param>
        public void UnloadPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            int instanceId = UnityUtility.GetObjectEntityId(prefab);
            if (!_leases.TryGetValue(instanceId, out List<AssetLease<GameObject>> leases) || leases.Count == 0)
            {
                return;
            }

            int last = leases.Count - 1;
            AssetLease<GameObject> lease = leases[last];
            leases.RemoveAt(last);
            if (leases.Count == 0)
            {
                _leases.Remove(instanceId);
            }

            lease.Dispose();
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private GameObject RetainLease(AssetLease<GameObject> lease)
        {
            if (lease.Asset == null)
            {
                lease.Dispose();
                return null;
            }

            int instanceId = UnityUtility.GetObjectEntityId(lease.Asset);
            if (!_leases.TryGetValue(instanceId, out List<AssetLease<GameObject>> leases))
            {
                leases = new List<AssetLease<GameObject>>(1);
                _leases.Add(instanceId, leases);
            }

            leases.Add(lease);
            return lease.Asset;
        }

        #endregion
    }
}
