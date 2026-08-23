using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.GameObjectPool
{
    /// <summary>
    /// 游戏对象池服务接口。
    /// </summary>
    public interface IGameObjectPoolService : IService
    {
        /// <summary>
        /// 同步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>游戏对象。</returns>
        GameObject Spawn(string location, Transform parent = null);

        /// <summary>
        /// 同步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件。</returns>
        T Spawn<T>(string location, Transform parent = null) where T : Component;

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="instance">获取的游戏对象。</param>
        /// <returns>是否成功。</returns>
        bool TrySpawn(string location, Transform parent, out GameObject instance);

        /// <summary>
        /// 异步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>游戏对象。</returns>
        UniTask<GameObject> SpawnAsync(string location, Transform parent = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>组件。</returns>
        UniTask<T> SpawnAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component;

        /// <summary>
        /// 异步预热指定地址的对象池。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="count">预热数量。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        UniTask WarmupAsync(string location, int count, CancellationToken cancellationToken = default);

        /// <summary>
        /// 回收游戏对象。
        /// </summary>
        /// <param name="instance">游戏对象。</param>
        void Despawn(GameObject instance);

        /// <summary>
        /// 通过句柄回收游戏对象。
        /// </summary>
        /// <param name="handle">句柄。</param>
        void Despawn(GameObjectPoolHandle handle);

        /// <summary>
        /// 刷新指定地址的池。
        /// </summary>
        /// <param name="location">资源地址。</param>
        void Flush(string location);

        /// <summary>
        /// 刷新指定分组的所有池。
        /// </summary>
        /// <param name="group">分组名称。</param>
        void FlushGroup(string group);

        /// <summary>
        /// 刷新所有池。
        /// </summary>
        void FlushAll();

        /// <summary>
        /// 加载池配置。
        /// </summary>
        /// <param name="config">配置 ScriptableObject。</param>
        void LoadCatalog(PoolConfigScriptableObject config);
    }
}
