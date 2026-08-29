using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
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
    /// 基于 ResourceService 租约的预制体加载器——同一预制体的多次加载各持一份租约，卸载按 LIFO 配对释放。
    /// </summary>
    internal sealed class ResourcePrefabLoader : IPrefabLoader
    {
        #region 字段 [FIELDS]

        private readonly Dictionary<int, List<ResourceAssetLease<GameObject>>> _leases = new Dictionary<int, List<ResourceAssetLease<GameObject>>>(16);
        private ResourceServiceHandler _resource;

        #endregion

        #region 属性 [PROPERTIES]

        private ResourceServiceHandler Resource
        {
            get
            {
                if (_resource == null)
                {
                    _resource = ResourceService.Handler;
                }

                return _resource;
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
            ResourceAssetLease<GameObject> lease = ResourceService.LoadLease<GameObject>(location);
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
            ResourceAssetLease<GameObject> lease = await ResourceService.LoadLeaseAsync<GameObject>(location, cancellationToken);
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

            int instanceId = prefab.GetInstanceID();
            if (!_leases.TryGetValue(instanceId, out List<ResourceAssetLease<GameObject>> leases) || leases.Count == 0)
            {
                return;
            }

            int last = leases.Count - 1;
            ResourceAssetLease<GameObject> lease = leases[last];
            leases.RemoveAt(last);
            if (leases.Count == 0)
            {
                _leases.Remove(instanceId);
            }

            lease.Dispose();
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private GameObject RetainLease(ResourceAssetLease<GameObject> lease)
        {
            if (lease.Asset == null)
            {
                lease.Dispose();
                return null;
            }

            int instanceId = lease.Asset.GetInstanceID();
            if (!_leases.TryGetValue(instanceId, out List<ResourceAssetLease<GameObject>> leases))
            {
                leases = new List<ResourceAssetLease<GameObject>>(1);
                _leases.Add(instanceId, leases);
            }

            leases.Add(lease);
            return lease.Asset;
        }

        #endregion
    }
}
