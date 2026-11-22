using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Debugger;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// GameObject 池服务外观（Facade）：全框架统一的静态游戏对象池访问入口。
    /// </summary>
    /// <remarks>
    /// 通过替换 <see cref="Handler"/> 可在不同对象池后端之间切换；支持资源地址与外部 Prefab 两种来源，经 <see cref="ObjectPoolSource"/> 统一入口。 <br />
    /// 未显式设置处理器时，懒加载优先经 <c>GetHandlerFromSettings</c> 从 <see cref="ObjectPoolServiceSettings"/> 解析； <br />
    /// settings 未配置则回退 <see cref="CreateDefaultHandler"/>。
    /// <c>Handler</c> 属性由 <c>ProviderHandlerHostGenerator</c> 源生成器生成（线程安全懒加载）。
    /// </remarks>
    [ProviderHandlerHost(typeof(IObjectPoolService), typeof(DefaultObjectPoolHandler))]
    [UnityEngine.Scripting.Preserve]
    public static partial class ObjectPoolService
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="ObjectPoolServiceSettings"/> 解析游戏对象池处理器。
        /// </summary>
        /// <remarks>
        /// 由生成的懒装配链调用（预热在入图期、懒加载在世界就绪后）；解析出的实例经容器登记契约绑定。
        /// </remarks>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static IObjectPoolService GetHandlerFromSettings()
        {
            return ObjectPoolServiceSettings.ObjectPoolServiceHandler;
        }

        /// <summary>
        /// 容器编排（后端 OnInit 经 Manager 钩子调用）：注册调试面板。
        /// </summary>
        internal static void OnHandlerInitialized(IDebuggerService debugger)
        {
            debugger.RegisterDebuggerWindow("Profiler/GameObject Pool", new ObjectPoolServiceDebuggerWindow());
        }

        /// <summary>
        /// 容器收尾（后端 OnShutdown 经 Manager 钩子调用）：注销调试面板。
        /// </summary>
        internal static void OnHandlerShuttingDown(IDebuggerService debugger)
        {
            debugger?.UnregisterDebuggerWindow("Profiler/GameObject Pool");
        }

        #endregion

        #region 获取 [SPAWN]

        /// <summary>
        /// 同步获取游戏对象。
        /// </summary>
        /// <param name="source">池化来源（地址或 Prefab，string/GameObject 隐式转换）。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>游戏对象。</returns>
        [HotPath]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GameObject Spawn(ObjectPoolSource source, Transform parent = null) =>
            source.IsValid ? Internal_PeekHandler()?.Spawn(source, parent) : null;

        /// <summary>
        /// 同步获取游戏对象并设置姿态。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="position">位置。</param>
        /// <param name="rotation">旋转。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="useLocalPosition">是否使用本地位置而不是世界位置。</param>
        /// <returns>游戏对象。</returns>
        [HotPath]
        public static GameObject Spawn(
            ObjectPoolSource source,
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
        [HotPath]
        public static T Spawn<T>(ObjectPoolSource source, Transform parent = null) where T : Component =>
            source.IsValid ? Internal_PeekHandler()?.Spawn<T>(source, parent) : null;

        /// <summary>
        /// 尝试同步获取游戏对象。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="instance">获取的游戏对象。</param>
        /// <returns>是否成功。</returns>
        public static bool TrySpawn(ObjectPoolSource source, Transform parent, out GameObject instance)
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
        public static UniTask<GameObject> SpawnAsync(ObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default)
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
        public static UniTask<T> SpawnAsync<T>(ObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
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
        public static PooledGameObject SpawnPooled(ObjectPoolSource source, Transform parent = null) =>
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
            ObjectPoolSource source,
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
        public static async UniTask<PooledGameObject> SpawnPooledAsync(ObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default) =>
            PooledGameObject.Wrap(await SpawnAsync(source, parent, cancellationToken));

        /// <summary>
        /// 同步获取组件池化租约。
        /// </summary>
        /// <typeparam name="TComponent">组件类型。</typeparam>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件池化租约。</returns>
        public static Pooled<TComponent> SpawnPooled<TComponent>(ObjectPoolSource source, Transform parent = null) where TComponent : Component =>
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
            ObjectPoolSource source,
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
        public static async UniTask<Pooled<TComponent>> SpawnPooledAsync<TComponent>(ObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default) where TComponent : Component =>
            Pooled<TComponent>.Wrap(await SpawnAsync(source, parent, cancellationToken));

        #endregion

        #region 预制体与预热 [PREFAB & WARMUP]

        /// <summary>
        /// 同步加载预制体（仅资源地址源）。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>预制体。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GameObject LoadPrefab(string location) =>
            Internal_PeekHandler()?.LoadPrefab(location);

        /// <summary>
        /// 异步加载预制体（仅资源地址源）。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>预制体（未就绪时为 null）。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default) =>
            Internal_PeekHandler()?.LoadPrefabAsync(location, cancellationToken) ?? UniTask.FromResult<GameObject>(null);

        /// <summary>
        /// 异步预热指定来源的池。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="count">预热数量。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务（未就绪时为 CompletedTask）。</returns>
        public static UniTask WarmupAsync(ObjectPoolSource source, int count, CancellationToken cancellationToken = default)
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
        [HotPath]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Despawn(GameObject instance) =>
            Internal_PeekHandler()?.Despawn(instance);

        /// <summary>
        /// 通过租约回收游戏对象。
        /// </summary>
        /// <param name="pooled">池化租约。</param>
        [HotPath]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Despawn(PooledGameObject pooled) =>
            Internal_PeekHandler()?.Despawn(pooled);

        /// <summary>
        /// 尝试解析实例身份（内部：租约包装）。
        /// </summary>
        internal static bool TryResolveInstance(GameObject instance, out RuntimeObjectPool pool, out int slotIndex)
        {
            pool = null;
            slotIndex = -1;
            return s_Handler is IObjectPoolInternals internals && internals.TryResolveInstance(instance, out pool, out slotIndex);
        }

        /// <summary>
        /// 刷新指定来源的池。
        /// </summary>
        /// <param name="source">池化来源。</param>
        public static void Flush(ObjectPoolSource source)
        {
            if (source.IsValid)
            {
                Internal_PeekHandler()?.Flush(source);
            }
        }

        /// <summary>
        /// 刷新指定分组的所有池。
        /// </summary>
        /// <param name="group">分组名称。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FlushGroup(string group) =>
            Internal_PeekHandler()?.FlushGroup(group);

        /// <summary>
        /// 刷新所有池。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FlushAll() =>
            Internal_PeekHandler()?.FlushAll();
        
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
