using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// GameObject 池服务契约（策略模式契约接口）：池后端的唯一实现契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 默认实现为 <see cref="DefaultObjectPoolHandler"/>（分页槽位 + 代系租约 + 最小堆维护调度，PoolCatalog 数据驱动）， <br />
    /// 可在 <see cref="ObjectPoolServiceSettings"/> 中替换。实现约束：实现类须继承 <see cref="Moirai.Atropos.ServiceBase"/>（绑定即注册进容器，生命周期由容器拓扑驱动）。 <br />
    /// <see cref="TryResolveInstance"/> 是框架内部回调面（租约包装与 <c>Despawn</c> 使用），不由门面暴露。
    /// </remarks>
    public interface IObjectPoolService
    {
        #region 获取 [SPAWN]

        /// <summary>
        /// 同步获取游戏对象。
        /// </summary>
        GameObject Spawn(ObjectPoolSource source, Transform parent = null);

        /// <summary>
        /// 同步获取组件。
        /// </summary>
        T Spawn<T>(ObjectPoolSource source, Transform parent = null) where T : Component;

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        bool TrySpawn(ObjectPoolSource source, Transform parent, out GameObject instance);

        /// <summary>
        /// 异步获取游戏对象。
        /// </summary>
        UniTask<GameObject> SpawnAsync(ObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步获取组件。
        /// </summary>
        UniTask<T> SpawnAsync<T>(ObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default) where T : Component;

        #endregion

        #region 预制体与预热 [PREFAB & WARMUP]

        /// <summary>
        /// 同步加载预制体。
        /// </summary>
        GameObject LoadPrefab(string location);

        /// <summary>
        /// 异步加载预制体。
        /// </summary>
        UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步预热指定来源的池。
        /// </summary>
        UniTask WarmupAsync(ObjectPoolSource source, int count, CancellationToken cancellationToken = default);

        #endregion

        #region 回收与刷新 [DESPAWN & FLUSH]

        /// <summary>
        /// 回收游戏对象。
        /// </summary>
        void Despawn(GameObject instance);

        /// <summary>
        /// 通过租约回收游戏对象。
        /// </summary>
        void Despawn(PooledGameObject pooled);

        /// <summary>
        /// 刷新指定来源的池。
        /// </summary>
        void Flush(ObjectPoolSource source);

        /// <summary>
        /// 刷新指定分组的所有池。
        /// </summary>
        void FlushGroup(string group);

        /// <summary>
        /// 刷新所有池。
        /// </summary>
        void FlushAll();

        #endregion

        #region 调试接口 [DEBUG INTERFACE]

        /// <summary>
        /// 获取调试摘要。
        /// </summary>
        ObjectPoolSummarySnapshot GetDebugSummary();

        /// <summary>
        /// 获取调试快照。
        /// </summary>
        int GetDebugSnapshots(ObjectPoolSnapshot[] snapshots);

        /// <summary>
        /// 填充实例级调试快照。
        /// </summary>
        void FillDebugInstances(ObjectPoolSnapshot snapshot);

        #endregion
    }

    /// <summary>
    /// GameObject 池后端的内部回调面：实例身份解析的参数类型（<see cref="RuntimeObjectPool"/>）是 internal，不进公共契约。
    /// </summary>
    internal interface IObjectPoolInternals
    {
        /// <summary>
        /// 尝试解析实例身份（供租约包装与 Despawn 使用）。
        /// </summary>
        bool TryResolveInstance(GameObject instance, out RuntimeObjectPool pool, out int slotIndex);
    }
}
