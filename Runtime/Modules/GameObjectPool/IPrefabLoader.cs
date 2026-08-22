using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos.GameObjectPool
{
    /// <summary>
    /// 预制体加载器接口。
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
        UniTask<GameObject> LoadPrefabAsync(string location, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// 卸载预制体。
        /// </summary>
        /// <param name="prefab">预制体。</param>
        void UnloadPrefab(GameObject prefab);
    }

    /// <summary>
    /// 基于 ResourceService 的预制体加载器，使用引用计数管理预制体生命周期。
    /// </summary>
    internal sealed class ResourcePrefabLoader : IPrefabLoader
    {
        #region 字段 [FIELDS]

        private readonly Dictionary<int, int> _refCounts = new Dictionary<int, int>(16);
        private IResourceService _resourceService;

        #endregion

        #region 属性 [PROPERTIES]

        private IResourceService ResourceService
        {
            get
            {
                if (_resourceService == null)
                {
                    _resourceService = GameApp.Resource;
                }

                return _resourceService;
            }
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 同步加载预制体。
        /// </summary>
        public GameObject LoadPrefab(string location)
        {
            GameObject prefab = ResourceService.LoadAsset<GameObject>(location);
            RetainPrefab(prefab);
            return prefab;
        }

        /// <summary>
        /// 异步加载预制体。
        /// </summary>
        public async UniTask<GameObject> LoadPrefabAsync(string location, System.Threading.CancellationToken cancellationToken = default)
        {
            GameObject prefab = await ResourceService.LoadAssetAsync<GameObject>(location, cancellationToken);
            RetainPrefab(prefab);
            return prefab;
        }

        /// <summary>
        /// 卸载预制体（引用计数归零时真正卸载）。
        /// </summary>
        public void UnloadPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            int instanceId = prefab.GetInstanceID();
            if (!_refCounts.TryGetValue(instanceId, out int count) || count <= 0)
            {
                return;
            }

            count--;
            if (count <= 0)
            {
                _refCounts.Remove(instanceId);
                ResourceService?.UnloadAsset(prefab);
            }
            else
            {
                _refCounts[instanceId] = count;
            }
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void RetainPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            int instanceId = prefab.GetInstanceID();
            _refCounts.TryGetValue(instanceId, out int count);
            _refCounts[instanceId] = count + 1;
        }

        #endregion
    }
}
