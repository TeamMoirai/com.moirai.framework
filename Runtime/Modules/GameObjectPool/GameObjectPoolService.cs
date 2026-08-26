using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos.GameObjectPool
{
    /// <summary>
    /// 游戏对象池服务门面（Facade）。
    /// <para>统一的静态对象池访问入口，通过替换 <see cref="Handler"/> 即可在不同对象池后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="GameObjectPoolSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(GameObjectPoolHandler))]
    [ServiceDependency(typeof(ResourceService))]
    public partial class GameObjectPoolService : ServiceBase, IServiceTickable
    {
        #region 处理器 [HANDLER]

        /// <summary>
        /// 从 <see cref="GameObjectPoolSettings"/> 创建默认对象池处理器。
        /// </summary>
        /// <returns>默认对象池处理器实例。</returns>
        private static GameObjectPoolHandler CreateDefaultHandler()
        {
            return GameObjectPoolSettings.GameObjectPoolHandler;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 获取服务优先级。
        /// </summary>
        public override int Priority => 6;

        /// <summary>
        /// 初始化对象池服务。由容器在构建期调用。
        /// <para>确保 <c>GameObjectPoolService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭对象池服务。由容器在关闭期调用。
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
            s_Handler?.Tick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 对象池操作 [POOL OPERATIONS]

        /// <summary>
        /// 同步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>游戏对象。</returns>
        public static GameObject Spawn(string location, Transform parent = null) =>
            s_Handler?.Spawn(location, parent);

        /// <summary>
        /// 同步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件。</returns>
        public static T Spawn<T>(string location, Transform parent = null) where T : Component =>
            s_Handler?.Spawn<T>(location, parent);

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="instance">获取的游戏对象。</param>
        /// <returns>是否成功。</returns>
        public static bool TrySpawn(string location, Transform parent, out GameObject instance)
        {
            instance = s_Handler?.Spawn(location, parent);
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
            s_Handler != null
                ? s_Handler.SpawnAsync(location, parent, cancellationToken)
                : UniTask.FromResult<GameObject>(null);

        /// <summary>
        /// 异步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>组件。</returns>
        public static UniTask<T> SpawnAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component =>
            s_Handler != null
                ? s_Handler.SpawnAsync<T>(location, parent, cancellationToken)
                : UniTask.FromResult<T>(null);

        /// <summary>
        /// 异步预热指定地址的对象池。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="count">预热数量。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public static UniTask WarmupAsync(string location, int count, CancellationToken cancellationToken = default) =>
            s_Handler != null
                ? s_Handler.WarmupAsync(location, count, cancellationToken)
                : UniTask.CompletedTask;

        /// <summary>
        /// 回收游戏对象。
        /// </summary>
        /// <param name="instance">游戏对象。</param>
        public static void Despawn(GameObject instance) =>
            s_Handler?.Despawn(instance);

        /// <summary>
        /// 通过句柄回收游戏对象。
        /// </summary>
        /// <param name="handle">句柄。</param>
        public static void Despawn(GameObjectPoolHandle handle) =>
            s_Handler?.Despawn(handle);

        /// <summary>
        /// 刷新指定地址的池。
        /// </summary>
        /// <param name="location">资源地址。</param>
        public static void Flush(string location) =>
            s_Handler?.Flush(location);

        /// <summary>
        /// 刷新指定分组的所有池。
        /// </summary>
        /// <param name="group">分组名称。</param>
        public static void FlushGroup(string group) =>
            s_Handler?.FlushGroup(group);

        /// <summary>
        /// 刷新所有池。
        /// </summary>
        public static void FlushAll() =>
            s_Handler?.FlushAll();

        /// <summary>
        /// 加载池配置。
        /// </summary>
        /// <param name="config">配置 ScriptableObject。</param>
        public static void LoadCatalog(PoolConfigScriptableObject config) =>
            s_Handler?.LoadCatalog(config);

        #endregion

        #region 调试接口 [DEBUG INTERFACE]

        /// <summary>
        /// 获取调试摘要。
        /// </summary>
        public static GameObjectPoolSummarySnapshot GetDebugSummary() =>
            s_Handler?.GetDebugSummary() ?? default;

        /// <summary>
        /// 获取调试快照。
        /// </summary>
        public static int GetDebugSnapshots(GameObjectPoolSnapshot[] snapshots) =>
            s_Handler?.GetDebugSnapshots(snapshots) ?? 0;

        /// <summary>
        /// 填充实例级调试快照。
        /// </summary>
        public static void FillDebugInstances(GameObjectPoolSnapshot snapshot) =>
            s_Handler?.FillDebugInstances(snapshot);

        #endregion
    }
}
