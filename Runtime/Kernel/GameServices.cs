using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace Moirai.Atropos
{
    /// <summary>
    /// 静态服务管理外观，是默认 <see cref="ServiceWorld"/> 实例的投影。
    /// </summary>
    /// <remarks>
    /// 全部操作转发到 <see cref="Default"/> 世界；需要隔离世界的场景（测试并行/沙盒）直接 <c>new ServiceWorld()</c>，不触碰本类。 <br />
    /// 线程契约：所有公共方法仅限 Unity 主线程调用，由 <see cref="EnsureMainThread"/> 在编辑器/开发构建断言（发布构建内联为无操作）； <br />
    /// 后台线程请经 <c>MainThreadDispatcher.Post(Action)</c> / <c>MainThreadDispatcher.Send(Action)</c> 切回。
    /// </remarks>
    public static partial class GameServices
    {
        #region 状态 [STATE]

        private static int s_MainThreadId;

        private static ServiceWorld s_World;

        /// <summary>默认服务世界（首次访问时创建）。</summary>
        public static ServiceWorld Default => s_World ??= new ServiceWorld();

        /// <summary>App 作用域是否活跃。</summary>
        public static bool HasApp => s_World?.HasScope(EServiceScopeKind.App) ?? false;

        /// <summary>Scene 作用域是否活跃。</summary>
        public static bool HasScene => s_World?.HasScope(EServiceScopeKind.Scene) ?? false;

        /// <summary>Gameplay 作用域是否活跃。</summary>
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

#if UNITY_EDITOR
            // 关闭 Domain Reload 时，异常退出可能让上一会话的服务世界残留；进 Play 时丢弃，
            // 由启动流程经 Default 重建（优雅退出路径本就在 GameServices.Shutdown 置空）。
            s_World = null;
#endif
        }

        /// <summary>
        /// 主线程亲和断言：非主线程调用在编辑器/开发构建触发断言失败。
        /// </summary>
        /// <remarks>
        /// 仅编辑器/开发构建参与编译，发布构建方法体为空、内联后零开销。
        /// <c>s_MainThreadId == 0</c> 是捕获钩子尚未运行的启动窗口（SubsystemRegistration 之前的其它程序集静态构造），此窗口内放行；
        /// 真正的捕获由 <see cref="CaptureMainThreadId"/> 在钩子内完成。
        /// </remarks>
        [HotPath]
        internal static void EnsureMainThread()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.IsTrue(
                s_MainThreadId == 0 ||
                System.Threading.Thread.CurrentThread.ManagedThreadId == s_MainThreadId,
                "GameServices must only be used from the main thread. " +
                "From a background thread/callback, wrap the call with MainThreadDispatcher.Post/Send.");
#endif
        }

        #endregion

        #region 拦截器 [INTERCEPTORS]

        /// <summary>当前已注册的拦截器（只读视图）。</summary>
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
        /// 异步关闭指定作用域，对实现 <see cref="IAsyncShutdownService"/> 的服务先异步关闭再执行同步 <c>OnShutdown</c>。
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
            InvokeHandlerResetters();
            if (s_World == null) return;
            s_World.Dispose();
            s_World = null;
        }

        /// <summary>
        /// 异步关闭全部作用域，逆序 Gameplay → Scene → App。
        /// </summary>
        /// <remarks>
        /// 对实现 <see cref="IAsyncShutdownService"/> 的服务先异步关闭。 <br />
        /// 游戏驱动的优雅退出应在 OnApplicationQuit 之前调用本方法——Unity 的退出回调无法等待异步操作，OnApplicationQuit 内只做同步兜底关闭。
        /// </remarks>
        public static async UniTask ShutdownAsync()
        {
            EnsureMainThread();
            InvokeHandlerResetters();
            if (s_World == null) return;
            await s_World.DisposeAsync();
            s_World = null;
        }

        #endregion

        #region 重复契约策略 [DUPLICATE CONTRACT POLICY]

        /// <summary>重复契约注册处置策略，仅作用于"同作用域内已占用契约再次显式注册不同实例"的场景。</summary>
        /// <remarks>
        /// 同实例重复注册的幂等返回与多契约绑定不受本策略影响。
        /// </remarks>
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
        /// </summary>
        /// <remarks>
        /// 世界未初始化时仅入图，由世界初始化（组合根 <c>InitializeAsync</c>）按依赖拓扑统一驱动 OnInit；世界已初始化时立即 OnInit，依赖必须已就绪、缺失即 fail-fast。 <br />
        /// 迭代中（Tick）调用时默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）。
        /// </remarks>
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
        /// </summary>
        /// <remarks>
        /// 同一实例可依次以多个契约注册（多契约绑定）——首个调用创建条目，后续调用仅附加契约句柄。
        /// </remarks>
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
        /// 条件注销：仅当契约当前绑定的就是该实例时注销并关闭，否则空操作。
        /// </summary>
        /// <remarks>
        /// 生成 setter 的旧实例收尾路径；槽位/测试换入的未注册实例不误伤契约上的其它绑定。
        /// </remarks>
        /// <param name="contractType">契约类型。</param>
        /// <param name="handler">待注销的处理器实例。</param>
        /// <returns>执行了注销为真；未绑定该实例（或世界已弃置）为假。</returns>
        internal static bool UnregisterHandler(Type contractType, IService handler)
        {
            EnsureMainThread();
            return Default.UnregisterIfBound(contractType, handler);
        }

        /// <summary>已登记清槽回调的服务门面，世界关停时逐个落槽。</summary>
        private static System.Collections.Generic.List<System.Action> s_HandlerResetters;

        /// <summary>
        /// 登记一枚服务门面的清槽回调。
        /// </summary>
        /// <remarks>由 <c>[ProviderHandlerHost]</c> 生成的装配路径在首次装配时登记（宿主侧自带去重门）；关停路径据此把静态槽与容器绑定一并收口。</remarks>
        /// <param name="resetter">清槽回调。</param>
        internal static void RegisterHandlerResetter(System.Action resetter)
        {
            (s_HandlerResetters ??= new System.Collections.Generic.List<System.Action>(16)).Add(resetter);
        }

        /// <summary>
        /// 落尽全部已登记服务门面的缓存槽。
        /// </summary>
        /// <remarks>世界关停即「槽位 ≡ 容器实例」的失效点：登记过的槽位由内核推送清空，读侧不再逐次回查容器。</remarks>
        private static void InvokeHandlerResetters()
        {
            var resetters = s_HandlerResetters;
            if (resetters == null) return;

            for (var i = 0; i < resetters.Count; i++)
            {
                resetters[i]();
            }
        }

        /// <summary>
        /// 判定服务门面此刻能否自行装配后端——组合根预热之外的懒加载入口闸。
        /// </summary>
        /// <remarks>
        /// 世界由组合根「先入图 → 再拓扑驱动」两段式建立，放开懒装配的窗口只有已初始化与初始化在途两种。 <br />
        /// 世界未建＝启动序被越过；已建未初始化且不在初始化中＝预热未发生；已关闭＝退出途中的迟到读。三者一律判红，不静默返回未初始化实例。
        /// </remarks>
        /// <param name="hostName">门面类名（异常正文用）。</param>
        /// <exception cref="GameException">世界不可用或尚未初始化就绪。</exception>
        internal static void EnsureFacadeAssemblyAllowed(string hostName)
        {
            var world = s_World;
            if (world != null && (world.IsInitialized || world.IsInitializing)) return;

            if (GameApp.IsShutdown)
            {
                throw new GameException(StringUtility.Format(
                    "ProviderHandlerHost: '{0}' blocked: GameApp is shut down. Rebuild the world via explicit RegisterService.",
                    hostName));
            }

            throw new GameException(StringUtility.Format(
                world == null
                    ? "ProviderHandlerHost: '{0}' was accessed before the service world exists. Boot the app before using service facades."
                    : "ProviderHandlerHost: '{0}' was accessed before the world was initialized - warm the backend from the composition root instead of relying on lazy registration.",
                hostName));
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
        /// </summary>
        /// <remarks>
        /// 按 Gameplay &gt; Scene &gt; App 优先级返回最优服务；容器未构建时同样抛出。
        /// </remarks>
        [HotPath]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetRequiredService<T>() where T : class
        {
            EnsureMainThread();
            if (s_World != null && s_World.TryGet(out T service)) return service;
            throw new GameException(StringUtility.Format(
                "Service '{0}' was not found in any active scope.", typeof(T).FullName));
        }

        /// <summary>
        /// 获取服务（未找到返回 null）。
        /// </summary>
        [HotPath]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetService<T>() where T : class
        {
            EnsureMainThread();
            return s_World != null && s_World.TryGet(out T service) ? service : null;
        }

        /// <summary>
        /// 尝试获取服务。
        /// </summary>
        [HotPath]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetService<T>(out T service) where T : class
        {
            EnsureMainThread();
            if (s_World != null && s_World.TryGet(out service)) return true;
            service = null;
            return false;
        }

        #endregion

        #region 轮询驱动 [TICK DRIVERS]

        public static void Tick(float elapseSeconds, float realElapseSeconds)
        {
            EnsureMainThread();
            s_World?.Tick(elapseSeconds, realElapseSeconds);
        }

        public static void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            EnsureMainThread();
            s_World?.FixedTick(elapseSeconds, realElapseSeconds);
        }

        public static void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            EnsureMainThread();
            s_World?.LateTick(elapseSeconds, realElapseSeconds);
        }

        public static void DrawGizmos()
        {
            EnsureMainThread();
            s_World?.DrawGizmos();
        }

        #endregion

        #region 状态辅助 [STATE HELPERS]

        /// <summary>服务是否处于就绪态（判据在 <see cref="LifecycleMachine.IsReady"/>，本方法只负责取状态）。</summary>
        /// <remarks>门面读数与家族基座的关停门控一律转调本方法，不另抄比较。</remarks>
        /// <param name="service">待判定的服务实例。</param>
        /// <returns>处于就绪态为真。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsReady(IService service) => LifecycleMachine.IsReady(GetState(service));

        internal static EServiceState GetState(IService service)
        {
            // 注册门（ServiceWorld.Register）以同一类型条件放行，入图服务必然可读
            return service is IServiceLifecycle lifecycle ? lifecycle.StateInternal : EServiceState.Created;
        }

        #endregion
    }
}
