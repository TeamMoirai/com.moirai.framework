using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace Moirai.Atropos
{
    /// <summary>
    /// 静态服务管理外观——默认 <see cref="ServiceWorld"/> 实例的投影。
    /// <para>全部操作转发到 <see cref="Default"/> 世界；需要隔离世界的场景（测试并行/沙盒）直接
    /// <c>new ServiceWorld()</c>，不触碰本类。</para>
    /// <para><b>线程契约</b>：所有公共方法仅限 Unity 主线程调用。
    /// 后台线程请通过 <c>MainThreadDispatcher.Post(Action)</c> / <c>MainThreadDispatcher.Send(Action)</c> 切回。</para>
    /// </summary>
    public static partial class GameServices
    {
        #region 状态 [STATE]

        private static int s_MainThreadId;

        private static ServiceWorld s_World;

        /// <summary>
        /// 默认服务世界（首次访问时创建）。
        /// </summary>
        public static ServiceWorld Default => s_World ??= new ServiceWorld();

        /// <summary>
        /// App 作用域是否活跃。
        /// </summary>
        public static bool HasApp => s_World?.HasScope(EServiceScopeKind.App) ?? false;

        /// <summary>
        /// Scene 作用域是否活跃。
        /// </summary>
        public static bool HasScene => s_World?.HasScope(EServiceScopeKind.Scene) ?? false;

        /// <summary>
        /// Gameplay 作用域是否活跃。
        /// </summary>
        public static bool HasGameplay => s_World?.HasScope(EServiceScopeKind.Gameplay) ?? false;

        #endregion

        #region 初始化 [INITIALIZATION]

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureMainThreadId()
        {
            s_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        internal static void EnsureMainThread()
        {
            Assert.IsTrue(
                s_MainThreadId == 0 ||
                System.Threading.Thread.CurrentThread.ManagedThreadId == s_MainThreadId,
                "GameServices must only be used from the main thread. " +
                "From a background thread/callback, wrap the call with MainThreadDispatcher.Post/Send.");
        }

        #endregion

        #region 拦截器 [INTERCEPTORS]

        /// <summary>
        /// 当前已注册的拦截器（只读视图）。
        /// </summary>
        public static IReadOnlyList<IServiceInterceptor> Interceptors => Default.Interceptors;

        /// <summary>
        /// 添加服务拦截器。按 <see cref="IServiceInterceptor.Priority"/> 降序插入。
        /// </summary>
        public static void AddInterceptor(IServiceInterceptor interceptor)
        {
            EnsureMainThread();
            Default.AddInterceptor(interceptor);
        }

        /// <summary>
        /// 移除服务拦截器。
        /// </summary>
        public static void RemoveInterceptor(IServiceInterceptor interceptor)
        {
            EnsureMainThread();
            if (s_World == null) return;
            s_World.RemoveInterceptor(interceptor);
        }

        #endregion

        #region 容器管理 [CONTAINER MANAGEMENT]

        /// <summary>
        /// 关闭指定作用域。服务按逆初始化序（依赖方先）关闭。
        /// </summary>
        public static void ShutdownContainer(EServiceScopeKind scope)
        {
            EnsureMainThread();
            s_World?.ShutdownScope(scope);
        }

        /// <summary>
        /// 异步关闭指定作用域。对实现 <see cref="IAsyncShutdownService"/> 的服务先异步关闭，
        /// 再执行同步 <c>OnShutdown</c>。
        /// </summary>
        public static async UniTask ShutdownContainerAsync(EServiceScopeKind scope)
        {
            EnsureMainThread();
            if (s_World == null) return;
            await s_World.ShutdownScopeAsync(scope);
        }

        /// <summary>
        /// 关闭全部作用域。逆序：Gameplay → Scene → App（依赖方先于被依赖方释放）。
        /// </summary>
        public static void Shutdown()
        {
            EnsureMainThread();
            if (s_World == null) return;
            s_World.Dispose();
            s_World = null;
        }

        /// <summary>
        /// 异步关闭全部作用域。逆序：Gameplay → Scene → App。
        /// 对实现 <see cref="IAsyncShutdownService"/> 的服务先异步关闭。
        /// <para>游戏驱动的优雅退出应在 OnApplicationQuit 之前调用本方法——
        /// Unity 的退出回调无法等待异步操作，OnApplicationQuit 内只做同步兜底关闭。</para>
        /// </summary>
        public static async UniTask ShutdownAsync()
        {
            EnsureMainThread();
            if (s_World == null) return;
            await s_World.DisposeAsync();
            s_World = null;
        }

        #endregion

        #region 重复契约策略 [DUPLICATE CONTRACT POLICY]

        /// <summary>
        /// 重复契约注册处置策略。仅作用于"同作用域内已占用契约再次显式注册不同实例"的场景；
        /// 同实例幂等与多契约绑定不受影响。
        /// </summary>
        public static EDuplicateContractPolicy DuplicateContractPolicy
        {
            get => Default.DuplicateContractPolicy;
            set
            {
                EnsureMainThread();
                Default.DuplicateContractPolicy = value;
            }
        }

        #endregion

        #region 服务注册 [SERVICE REGISTRATION]

        /// <summary>
        /// 注册服务到指定作用域（统一入口）。
        /// <para>世界未初始化时仅入图——由世界初始化（组合根 <c>InitializeAsync</c>）按依赖拓扑统一驱动 OnInit；
        /// 世界已初始化时立即 OnInit（依赖必须已就绪，缺失即 fail-fast）。</para>
        /// <para>迭代中（Tick）调用时默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）。</para>
        /// </summary>
        /// <typeparam name="T">服务具体类型（契约即类型本身）。</typeparam>
        /// <param name="scope">目标作用域。</param>
        /// <param name="service">要注册的服务实例。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>注册的服务实例（重复注册时返回既有实例）。</returns>
        public static T RegisterService<T>(
            EServiceScopeKind scope,
            T service,
            EDeferMode deferMode = EDeferMode.Defer) where T : class, IService
        {
            EnsureMainThread();
            return Default.Register(scope, typeof(T), service, deferMode) as T;
        }

        /// <summary>
        /// 以显式契约类型注册服务实例（运行时 Type 版本）。
        /// <para>同一实例可依次以多个契约注册（多契约绑定）——首个调用创建条目，后续调用仅附加契约句柄。</para>
        /// </summary>
        /// <param name="scope">目标作用域。</param>
        /// <param name="contractType">契约类型（注册键与解析键）。</param>
        /// <param name="service">要注册的服务实例。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>注册的服务实例。</returns>
        public static IService RegisterService(
            EServiceScopeKind scope,
            Type contractType,
            IService service,
            EDeferMode deferMode = EDeferMode.Defer)
        {
            EnsureMainThread();
            return Default.Register(scope, contractType, service, deferMode);
        }

        /// <summary>
        /// 确保服务已注册到指定作用域——未注册时创建默认实例并注册（幂等）。
        /// <para>HandlerHost 外观懒加载路径（<c>CreateDefaultHandler</c>）调用：首次经外观访问服务时
        /// 自动完成世界注册——世界未初始化时挂入待初始化图（依赖校验在初始化拓扑时统一执行），
        /// 已初始化时立即注册并初始化。</para>
        /// <para>关闭态阻断懒加载复活——显式 RegisterService 是关闭后重建世界的唯一路径。</para>
        /// </summary>
        /// <typeparam name="T">服务具体类型（契约即类型本身，须有无参构造函数）。</typeparam>
        /// <param name="scope">目标作用域。</param>
        internal static void EnsureRegistered<T>(EServiceScopeKind scope = EServiceScopeKind.App)
            where T : class, IService, new()
        {
            var world = Default;

            if (world.TryGetScope(scope, out var targetScope) &&
                targetScope.TryGet(typeof(T), out _))
            {
                // 已注册（含待初始化）：懒加载契约=可用——世界未初始化且不在初始化循环内时补提交初始化
                if (!world.IsInitialized && !world.IsInitializing)
                    world.Initialize();
                return;
            }

            if (GameApp.IsShutdown)
            {
                throw new GameException(StringUtility.Format(
                    "EnsureRegistered<{0}> blocked: GameApp is shut down. Rebuild the world via explicit RegisterService.",
                    typeof(T).FullName));
            }

            RegisterService<T>(scope, new T());

            // 注册即可用：世界未初始化且不在初始化循环内时立即提交初始化
            // （初始化循环内的重入由外层循环按索引推进接管，禁止嵌套初始化）
            if (!world.IsInitialized && !world.IsInitializing)
                world.Initialize();
        }

        /// <summary>
        /// 运行时注销并关闭指定作用域中的单个服务。
        /// </summary>
        public static bool UnregisterService<T>(
            EServiceScopeKind scope,
            EDeferMode deferMode = EDeferMode.Defer) where T : class, IService
        {
            return UnregisterService(scope, typeof(T), deferMode);
        }

        /// <summary>
        /// 以显式契约类型运行时注销单个服务（运行时 Type 版本）。
        /// </summary>
        public static bool UnregisterService(
            EServiceScopeKind scope,
            Type contractType,
            EDeferMode deferMode = EDeferMode.Defer)
        {
            EnsureMainThread();
            if (s_World == null) return false;
            return s_World.Unregister(scope, contractType, deferMode);
        }

        /// <summary>
        /// 获取内部默认世界实例（未创建时返回 null——不触发懒创建）。
        /// </summary>
        internal static ServiceWorld GetWorldInternal() => s_World;

        #endregion

        #region 查找 [LOOKUP]

        /// <summary>
        /// 获取服务（未找到抛 <see cref="GameException"/>）。
        /// <para>按 Gameplay &gt; Scene &gt; App 优先级返回最优服务；容器未构建时同样抛出。</para>
        /// </summary>
        public static T GetRequiredService<T>() where T : class
        {
            if (s_World != null && s_World.TryGet(out T service)) return service;
            throw new GameException(StringUtility.Format(
                "Service '{0}' was not found in any active scope.", typeof(T).FullName));
        }

        /// <summary>
        /// 获取服务（未找到返回 null）。
        /// </summary>
        public static T GetService<T>() where T : class
        {
            return s_World != null && s_World.TryGet(out T service) ? service : null;
        }

        /// <summary>
        /// 尝试获取服务。
        /// </summary>
        public static bool TryGetService<T>(out T service) where T : class
        {
            if (s_World != null && s_World.TryGet(out service)) return true;
            service = null;
            return false;
        }

        #endregion

        #region 轮询驱动 [TICK DRIVERS]

        public static void Tick(float elapseSeconds, float realElapseSeconds)
            => s_World?.Tick(elapseSeconds, realElapseSeconds);

        public static void FixedTick(float elapseSeconds, float realElapseSeconds)
            => s_World?.FixedTick(elapseSeconds, realElapseSeconds);

        public static void LateTick(float elapseSeconds, float realElapseSeconds)
            => s_World?.LateTick(elapseSeconds, realElapseSeconds);

        public static void DrawGizmos()
            => s_World?.DrawGizmos();

        #endregion

        #region 状态辅助 [STATE HELPERS]

        internal static void SetState(IService service, EServiceState state)
        {
            if (service is ServiceBase sb) sb.State = state;
            else if (service is ServiceMonoMarker sm) sm.SetStateInternal(state);
        }

        internal static EServiceState GetState(IService service)
        {
            if (service is ServiceBase sb) return sb.State;
            if (service is ServiceMonoMarker sm) return sm.GetStateInternal();
            return EServiceState.Created;
        }

        #endregion
    }

    /// <summary>
    /// ServiceMono 状态访问标记（避免泛型基类反射——GameServices.GetState/SetState 零反射直读）。
    /// </summary>
    internal interface ServiceMonoMarker
    {
        EServiceState GetStateInternal();
        void SetStateInternal(EServiceState state);
    }
}
