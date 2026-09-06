using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// GameObject 池处理器抽象基类（策略模式抽象策略）。
    /// <para>默认实现为 <see cref="DefaultGameObjectPoolHandler"/>（分页槽位 + 代系句柄 + 最小堆维护调度，PoolCatalog 数据驱动）。</para>
    /// <para>可在 <see cref="GameObjectPoolServiceSettings"/> 中替换为自定义对象池后端。</para>
    /// </summary>
    [Serializable]
    public abstract class GameObjectPoolServiceHandler : FrameworkHandler
    {
        #region 轮询 [TICK]

        /// <summary>
        /// 每帧轮询——处理到期的池维护操作。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        public abstract void Tick(float elapseSeconds, float realElapseSeconds);

        #endregion

        #region 获取 [SPAWN]

        /// <summary>
        /// 同步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>游戏对象。</returns>
        public abstract GameObject Spawn(string location, Transform parent = null);

        /// <summary>
        /// 同步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件。</returns>
        public abstract T Spawn<T>(string location, Transform parent = null) where T : Component;

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="instance">获取的游戏对象。</param>
        /// <returns>是否成功。</returns>
        public abstract bool TrySpawn(string location, Transform parent, out GameObject instance);

        /// <summary>
        /// 异步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>游戏对象。</returns>
        public abstract UniTask<GameObject> SpawnAsync(string location, Transform parent = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>组件。</returns>
        public abstract UniTask<T> SpawnAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component;

        #endregion

        #region 预制体与预热 [PREFAB & WARMUP]

        /// <summary>
        /// 同步加载预制体。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>预制体。</returns>
        public abstract GameObject LoadPrefab(string location);

        /// <summary>
        /// 异步加载预制体。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>预制体。</returns>
        public abstract UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步预热指定地址的池。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="count">预热数量。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public abstract UniTask WarmupAsync(string location, int count, CancellationToken cancellationToken = default);

        #endregion

        #region 回收与刷新 [DESPAWN & FLUSH]

        /// <summary>
        /// 回收游戏对象。
        /// </summary>
        /// <param name="instance">游戏对象。</param>
        public abstract void Despawn(GameObject instance);

        /// <summary>
        /// 通过句柄回收游戏对象。
        /// </summary>
        /// <param name="handle">句柄。</param>
        public abstract void Despawn(GameObjectPoolHandle handle);

        /// <summary>
        /// 刷新指定地址的池。
        /// </summary>
        /// <param name="location">资源地址。</param>
        public abstract void Flush(string location);

        /// <summary>
        /// 刷新指定分组的所有池。
        /// </summary>
        /// <param name="group">分组名称。</param>
        public abstract void FlushGroup(string group);

        /// <summary>
        /// 刷新所有池。
        /// </summary>
        public abstract void FlushAll();

        #endregion

        #region 目录 [CATALOG]

        /// <summary>
        /// 加载池配置（重建全部池）。
        /// </summary>
        /// <param name="config">配置 ScriptableObject。</param>
        public abstract void LoadCatalog(PoolConfigScriptableObject config);

        /// <summary>
        /// 从资源地址加载池配置（重建全部池）。
        /// </summary>
        /// <param name="poolConfigPath">池配置资源地址。</param>
        public abstract void LoadCatalog(string poolConfigPath);

        #endregion

        #region 调试接口 [DEBUG INTERFACE]

        /// <summary>
        /// 获取调试摘要。
        /// </summary>
        public abstract GameObjectPoolSummarySnapshot GetDebugSummary();

        /// <summary>
        /// 获取调试快照。
        /// </summary>
        public abstract int GetDebugSnapshots(GameObjectPoolSnapshot[] snapshots);

        /// <summary>
        /// 填充实例级调试快照。
        /// </summary>
        public abstract void FillDebugInstances(GameObjectPoolSnapshot snapshot);

        #endregion
    }
}
