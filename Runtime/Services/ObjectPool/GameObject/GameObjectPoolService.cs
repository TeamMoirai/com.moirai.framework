using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// GameObject 池服务外观（Facade）。
    /// <para>统一的静态游戏对象池访问入口，通过替换 <see cref="Handler"/> 即可在不同对象池后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="GameObjectPoolServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// <para>支持两种池化来源：资源地址（经 ResourceService 加载）、外部 Prefab 引用，经 <see cref="GameObjectPoolSource"/> 统一入口。</para>
    /// </summary>
    [HandlerHost(typeof(GameObjectPoolServiceHandler))]
    [ServiceDependency(typeof(DebuggerService), typeof(ResourceService))]
    [UnityEngine.Scripting.Preserve]
    public partial class GameObjectPoolService : ServiceBase, IServiceTickable
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="GameObjectPoolServiceSettings"/> 创建默认游戏对象池处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认游戏对象池处理器实例。</returns>
        private static GameObjectPoolServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<GameObjectPoolService>();
            return GameObjectPoolServiceSettings.GameObjectPoolServiceHandler;
        }

        /// <summary>
        /// 获取服务优先级。
        /// </summary>
        public override int Priority => 6;

        /// <summary>
        /// 初始化游戏对象池服务。由容器在构建期调用。
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
            
            DebuggerService.RegisterDebuggerWindow("Profiler/GameObject Pool", new GameObjectPoolServiceDebuggerWindow());
        }

        /// <summary>
        /// 关闭游戏对象池服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器处理到期的维护操作（未就绪时静默降级）。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            s_Handler?.Tick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 获取 [SPAWN]

        /// <summary>
        /// 同步获取游戏对象。
        /// </summary>
        /// <param name="source">池化来源（地址或 Prefab，string/GameObject 隐式转换）。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>游戏对象。</returns>
        public static GameObject Spawn(GameObjectPoolSource source, Transform parent = null) =>
            source.IsValid ? s_Handler?.Spawn(source, parent) : null;

        /// <summary>
        /// 同步获取游戏对象并设置姿态。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="position">位置。</param>
        /// <param name="rotation">旋转。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="useLocalPosition">是否使用本地位置而不是世界位置。</param>
        /// <returns>游戏对象。</returns>
        public static GameObject Spawn(
            GameObjectPoolSource source,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null,
            bool useLocalPosition = false)
        {
            GameObject instance = Spawn(source, parent);
            if (instance == null)
            {
                return null;
            }

            ApplyPose(instance.transform, position, rotation, useLocalPosition);
            return instance;
        }

        /// <summary>
        /// 同步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件（未就绪时为 null）。</returns>
        public static T Spawn<T>(GameObjectPoolSource source, Transform parent = null) where T : Component =>
            source.IsValid ? s_Handler?.Spawn<T>(source, parent) : null;

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="instance">获取的游戏对象。</param>
        /// <returns>是否成功。</returns>
        public static bool TrySpawn(GameObjectPoolSource source, Transform parent, out GameObject instance)
        {
            instance = null;
            if (!source.IsValid || s_Handler == null)
            {
                return false;
            }

            return s_Handler.TrySpawn(source, parent, out instance);
        }

        /// <summary>
        /// 异步获取游戏对象。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>游戏对象（未就绪时为 null）。</returns>
        public static UniTask<GameObject> SpawnAsync(GameObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default)
        {
            if (!source.IsValid || s_Handler == null)
            {
                return UniTask.FromResult<GameObject>(null);
            }

            return s_Handler.SpawnAsync(source, parent, cancellationToken);
        }

        /// <summary>
        /// 异步获取组件。
        /// </summary>
        /// <typeparam name="T">组件类型。</typeparam>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>组件（未就绪时为 null）。</returns>
        public static UniTask<T> SpawnAsync<T>(GameObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
        {
            if (!source.IsValid || s_Handler == null)
            {
                return UniTask.FromResult<T>(null);
            }

            return s_Handler.SpawnAsync<T>(source, parent, cancellationToken);
        }

        #endregion

        #region 池化租约 [POOLED LEASE]

        /// <summary>
        /// 同步获取池化租约。Dispose 时自动 <see cref="Despawn(GameObject)"/>。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>池化租约。</returns>
        public static PooledGameObject SpawnPooled(GameObjectPoolSource source, Transform parent = null) =>
            PooledGameObject.Wrap(Spawn(source, parent));

        /// <summary>
        /// 同步获取池化租约并设置姿态。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="position">位置。</param>
        /// <param name="rotation">旋转。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="useLocalPosition">是否使用本地位置而不是世界位置。</param>
        /// <returns>池化租约。</returns>
        public static PooledGameObject SpawnPooled(
            GameObjectPoolSource source,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null,
            bool useLocalPosition = false) =>
            PooledGameObject.Wrap(Spawn(source, position, rotation, parent, useLocalPosition));

        /// <summary>
        /// 异步获取池化租约。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>池化租约。</returns>
        public static async UniTask<PooledGameObject> SpawnPooledAsync(GameObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default) =>
            PooledGameObject.Wrap(await SpawnAsync(source, parent, cancellationToken));

        /// <summary>
        /// 同步获取组件池化租约。
        /// </summary>
        /// <typeparam name="TComponent">组件类型。</typeparam>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件池化租约。</returns>
        public static Pooled<TComponent> SpawnPooled<TComponent>(GameObjectPoolSource source, Transform parent = null) where TComponent : Component =>
            Pooled<TComponent>.Wrap(Spawn(source, parent));

        /// <summary>
        /// 同步获取组件池化租约并设置姿态。
        /// </summary>
        /// <typeparam name="TComponent">组件类型。</typeparam>
        /// <param name="source">池化来源。</param>
        /// <param name="position">位置。</param>
        /// <param name="rotation">旋转。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="useLocalPosition">是否使用本地位置而不是世界位置。</param>
        /// <returns>组件池化租约。</returns>
        public static Pooled<TComponent> SpawnPooled<TComponent>(
            GameObjectPoolSource source,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null,
            bool useLocalPosition = false) where TComponent : Component =>
            Pooled<TComponent>.Wrap(Spawn(source, position, rotation, parent, useLocalPosition));

        /// <summary>
        /// 异步获取组件池化租约。
        /// </summary>
        /// <typeparam name="TComponent">组件类型。</typeparam>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>组件池化租约。</returns>
        public static async UniTask<Pooled<TComponent>> SpawnPooledAsync<TComponent>(GameObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default) where TComponent : Component =>
            Pooled<TComponent>.Wrap(await SpawnAsync(source, parent, cancellationToken));

        #endregion

        #region 预制体与预热 [PREFAB & WARMUP]

        /// <summary>
        /// 同步加载预制体（仅资源地址源）。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>预制体。</returns>
        public static GameObject LoadPrefab(string location) =>
            s_Handler?.LoadPrefab(location);

        /// <summary>
        /// 异步加载预制体（仅资源地址源）。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>预制体（未就绪时为 null）。</returns>
        public static UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default) =>
            s_Handler?.LoadPrefabAsync(location, cancellationToken) ?? UniTask.FromResult<GameObject>(null);

        /// <summary>
        /// 异步预热指定来源的池。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="count">预热数量。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务（未就绪时为 CompletedTask）。</returns>
        public static UniTask WarmupAsync(GameObjectPoolSource source, int count, CancellationToken cancellationToken = default)
        {
            if (!source.IsValid || s_Handler == null)
            {
                return UniTask.CompletedTask;
            }

            return s_Handler.WarmupAsync(source, count, cancellationToken);
        }

        #endregion

        #region 回收与刷新 [DESPAWN & FLUSH]

        /// <summary>
        /// 回收游戏对象。仅外来对象会 Destroy。
        /// </summary>
        /// <param name="instance">游戏对象。</param>
        public static void Despawn(GameObject instance) =>
            s_Handler?.Despawn(instance);

        /// <summary>
        /// 通过租约回收游戏对象。
        /// </summary>
        /// <param name="pooled">池化租约。</param>
        public static void Despawn(PooledGameObject pooled) =>
            s_Handler?.Despawn(pooled);

        /// <summary>
        /// 尝试解析实例身份（内部：租约包装）。
        /// </summary>
        internal static bool TryResolveInstance(GameObject instance, out RuntimeGameObjectPool pool, out int slotIndex)
        {
            pool = null;
            slotIndex = -1;
            return s_Handler != null && s_Handler.TryResolveInstance(instance, out pool, out slotIndex);
        }

        /// <summary>
        /// 刷新指定来源的池。
        /// </summary>
        /// <param name="source">池化来源。</param>
        public static void Flush(GameObjectPoolSource source)
        {
            if (source.IsValid)
            {
                s_Handler?.Flush(source);
            }
        }

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
        /// 加载池配置（重建全部池）。
        /// </summary>
        /// <param name="config">配置 ScriptableObject。</param>
        public static void LoadCatalog(PoolConfigScriptableObject config) =>
            s_Handler?.LoadCatalog(config);

        /// <summary>
        /// 从资源地址加载池配置（重建全部池）。
        /// </summary>
        /// <param name="poolConfigPath">池配置资源地址。</param>
        public static void LoadCatalog(string poolConfigPath) =>
            s_Handler?.LoadCatalog(poolConfigPath);

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private static void ApplyPose(Transform transform, Vector3 position, Quaternion rotation, bool useLocalPosition)
        {
            if (useLocalPosition)
            {
                transform.SetLocalPositionAndRotation(position, rotation);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }
        }

        #endregion
    }
}
