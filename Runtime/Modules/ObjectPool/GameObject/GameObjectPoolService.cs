using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// GameObject 池服务外观（Facade）。
    /// <para>统一的静态游戏对象池访问入口，通过替换 <see cref="Handler"/> 即可在不同对象池后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="GameObjectPoolServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// <para>按资源地址管理池化 GameObject 实例；任意 CLR 对象池化请使用 <see cref="ObjectPoolService"/>。</para>
    /// </summary>
    [HandlerHost(typeof(GameObjectPoolServiceHandler))]
    [ServiceDependency(typeof(ResourceService))]
    [UnityEngine.Scripting.Preserve]
    public partial class GameObjectPoolService : ServiceBase, IServiceTickable
    {
        #region 处理器 [HANDLER]

        /// <summary>
        /// 从 <see cref="GameObjectPoolServiceSettings"/> 创建默认游戏对象池处理器。
        /// </summary>
        /// <returns>默认游戏对象池处理器实例。</returns>
        private static GameObjectPoolServiceHandler CreateDefaultHandler()
        {
            return GameObjectPoolServiceSettings.GameObjectPoolServiceHandler;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用。
        /// </summary>
        public static bool IsValid => s_Handler != null;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 获取服务优先级。
        /// </summary>
        public override int Priority => 6;

        /// <summary>
        /// 初始化游戏对象池服务。由容器在构建期调用。
        /// <para>确保 <c>GameObjectPoolService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭游戏对象池服务。由容器在关闭期调用。
        /// </summary>
        public override void Shutdown()
        {
            s_Handler?.Internal_Shutdown();
            s_Handler = null;
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器处理到期的维护操作。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            Handler.Tick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 获取 [SPAWN]

        /// <summary>
        /// 同步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>游戏对象。</returns>
        public static GameObject Spawn(string location, Transform parent = null) =>
            Handler.Spawn(location, parent);

        /// <summary>
        /// 同步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件。</returns>
        public static T Spawn<T>(string location, Transform parent = null) where T : Component =>
            Handler.Spawn<T>(location, parent);

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="instance">获取的游戏对象。</param>
        /// <returns>是否成功。</returns>
        public static bool TrySpawn(string location, Transform parent, out GameObject instance)
        {
            instance = Handler.Spawn(location, parent);
            return instance != null;
        }

        /// <summary>
        /// 异步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>游戏对象。</returns>
        public static UniTask<GameObject> SpawnAsync(string location, Transform parent = null, CancellationToken cancellationToken = default) =>
            Handler.SpawnAsync(location, parent, cancellationToken);

        /// <summary>
        /// 异步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>组件。</returns>
        public static UniTask<T> SpawnAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component =>
            Handler.SpawnAsync<T>(location, parent, cancellationToken);

        #endregion

        #region 预制体与预热 [PREFAB & WARMUP]

        /// <summary>
        /// 同步加载预制体。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>预制体。</returns>
        public static GameObject LoadPrefab(string location) =>
            Handler.LoadPrefab(location);

        /// <summary>
        /// 异步加载预制体。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>预制体。</returns>
        public static UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default) =>
            Handler.LoadPrefabAsync(location, cancellationToken);

        /// <summary>
        /// 异步预热指定地址的池。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="count">预热数量。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public static UniTask WarmupAsync(string location, int count, CancellationToken cancellationToken = default) =>
            Handler.WarmupAsync(location, count, cancellationToken);

        #endregion

        #region 回收与刷新 [DESPAWN & FLUSH]

        /// <summary>
        /// 回收游戏对象。
        /// </summary>
        /// <param name="instance">游戏对象。</param>
        public static void Despawn(GameObject instance) =>
            Handler.Despawn(instance);

        /// <summary>
        /// 通过句柄回收游戏对象。
        /// </summary>
        /// <param name="handle">句柄。</param>
        public static void Despawn(GameObjectPoolHandle handle) =>
            Handler.Despawn(handle);

        /// <summary>
        /// 刷新指定地址的池。
        /// </summary>
        /// <param name="location">资源地址。</param>
        public static void Flush(string location) =>
            Handler.Flush(location);

        /// <summary>
        /// 刷新指定分组的所有池。
        /// </summary>
        /// <param name="group">分组名称。</param>
        public static void FlushGroup(string group) =>
            Handler.FlushGroup(group);

        /// <summary>
        /// 刷新所有池。
        /// </summary>
        public static void FlushAll() =>
            Handler.FlushAll();

        /// <summary>
        /// 加载池配置（重建全部池）。
        /// </summary>
        /// <param name="config">配置 ScriptableObject。</param>
        public static void LoadCatalog(PoolConfigScriptableObject config) =>
            Handler.LoadCatalog(config);

        /// <summary>
        /// 从资源地址加载池配置（重建全部池）。
        /// </summary>
        /// <param name="poolConfigPath">池配置资源地址。</param>
        public static void LoadCatalog(string poolConfigPath) =>
            Handler.LoadCatalog(poolConfigPath);

        #endregion
    }
}
