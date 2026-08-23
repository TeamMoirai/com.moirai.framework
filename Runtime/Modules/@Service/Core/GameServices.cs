using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace Moirai.Atropos
{
    /// <summary>
    /// 静态服务管理门面。统一管理容器生命周期、轮询驱动和拦截器。
    /// <para><b>线程契约</b>：所有公共方法仅限 Unity 主线程调用。
    /// 后台线程请通过 <c>MainThreadDispatcher.Post(Action)</c> / <c>MainThreadDispatcher.Send(Action)</c> 切回。</para>
    /// </summary>
    public static partial class GameServices
    {
        #region 状态 [STATE]

        private static int s_MainThreadId;

        private static ServiceWorld s_World;

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

        #region 公共属性 [PUBLIC PROPERTIES]

        /// <summary>
        /// 最深层活跃的服务提供者（Gameplay > Scene > App）。
        /// <para>服务类应优先使用构造注入而非此属性。此属性主要用于非服务代码（MonoBehaviour、UI 脚本等）。</para>
        /// </summary>
        public static IServiceProvider Provider => HasAnyScope ? s_World : null;

        private static bool HasAnyScope => HasApp || HasScene || HasGameplay;

        #endregion

        #region 事件 [EVENTS]

        /// <summary>
        /// 服务注册完成（OnInit 已调用）后触发。
        /// </summary>
        public static event Action<IService, Type, EServiceScopeKind> onServiceRegistered;

        /// <summary>
        /// 服务注销完成（Shutdown 已调用）后触发。
        /// </summary>
        public static event Action<IService> onServiceUnregistered;

        #endregion

        #region 拦截器 [INTERCEPTORS]

        private static readonly List<IServiceInterceptor> s_Interceptors = new();

        /// <summary>
        /// 当前已注册的拦截器（只读视图）。
        /// </summary>
        public static IReadOnlyList<IServiceInterceptor> Interceptors => s_Interceptors;

        /// <summary>
        /// 是否存在已注册的拦截器。轮询热路径据此选择快/慢路径（无拦截器时跳过逐服务通知）。
        /// </summary>
        internal static bool HasInterceptors => s_Interceptors.Count > 0;

        /// <summary>
        /// 添加服务拦截器。按 <see cref="IServiceInterceptor.Priority"/> 降序插入。
        /// </summary>
        public static void AddInterceptor(IServiceInterceptor interceptor)
        {
            EnsureMainThread();
            if (interceptor == null) return;

            int priority = interceptor.Priority;
            int insertAt = s_Interceptors.Count;
            for (int i = 0; i < s_Interceptors.Count; i++)
            {
                if (priority > s_Interceptors[i].Priority) { insertAt = i; break; }
            }
            s_Interceptors.Insert(insertAt, interceptor);
        }

        /// <summary>
        /// 移除服务拦截器。
        /// </summary>
        public static void RemoveInterceptor(IServiceInterceptor interceptor)
        {
            EnsureMainThread();
            s_Interceptors.Remove(interceptor);
        }

        // ──── 拦截器与事件分发（由 ServiceScope 调用） ────

        internal static void InvokeRegistering(IService service, Type interfaceType, EServiceScopeKind scope)
        {
            if (s_Interceptors.Count == 0) return;
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceRegistering(service, interfaceType, scope);
        }

        internal static void InvokeRegistered(IService service, Type interfaceType, EServiceScopeKind scope)
        {
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceRegistered(service, interfaceType, scope);
            onServiceRegistered?.Invoke(service, interfaceType, scope);
        }

        internal static void InvokeUnregistered(IService service)
        {
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceUnregistered(service);
            onServiceUnregistered?.Invoke(service);
        }

        internal static void InvokeTick(IService service, float elapseSeconds, float realElapseSeconds)
        {
            if (s_Interceptors.Count == 0) return;
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceTick(service, elapseSeconds, realElapseSeconds);
        }

        internal static void InvokeShutdown(IService service)
        {
            if (s_Interceptors.Count == 0) return;
            for (int i = 0; i < s_Interceptors.Count; i++)
                s_Interceptors[i].OnServiceShutdown(service);
        }

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

        #region 容器管理 [CONTAINER MANAGEMENT]

        /// <summary>
        /// 异步构建指定作用域的服务：拓扑排序 → 创建实例 → 构造注入 → OnInit → OnInitAsync。
        /// <para>若同作用域已有服务，先关闭再重建。</para>
        /// </summary>
        /// <param name="scope">作用域种类。</param>
        /// <param name="collection">服务注册集合。</param>
        public static async UniTask BuildAsync(
            EServiceScopeKind scope,
            ServiceCollection collection)
        {
            EnsureMainThread();
            s_World ??= new ServiceWorld();
            await s_World.BuildAsync(scope, collection?.Descriptors);
        }

        /// <summary>
        /// 关闭指定作用域。服务按逆拓扑序（依赖方先）关闭。
        /// </summary>
        public static void ShutdownContainer(EServiceScopeKind scope)
        {
            EnsureMainThread();
            s_World?.ShutdownScope(scope);
        }

        /// <summary>
        /// 关闭全部作用域。逆序：Gameplay → Scene → App（依赖方先于被依赖方释放）。
        /// </summary>
        public static void Shutdown()
        {
            EnsureMainThread();
            ShutdownContainer(EServiceScopeKind.Gameplay);
            ShutdownContainer(EServiceScopeKind.Scene);
            ShutdownContainer(EServiceScopeKind.App);
            s_World?.Dispose();
            s_World = null;
            ClearAll();
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
            else if (service is ServiceMonoBase mono) mono.State = state;
        }

        internal static EServiceState GetState(IService service)
        {
            if (service is ServiceBase sb) return sb.State;
            if (service is ServiceMonoBase mono) return mono.State;
            return EServiceState.Created;
        }

        #endregion

        #region 清理 [CLEANUP]

        private static void ClearAll()
        {
            // 拦截器和事件在全部作用域关闭后清理——此时无活跃服务可触发事件
            s_Interceptors.Clear();
            onServiceRegistered = null;
            onServiceUnregistered = null;

            // MemoryPool 和 MarshalUtility 缓存清理在全部服务关闭后执行——
            // 此时无活跃的池化对象引用（所有 Service 已 Shutdown），安全清空。
            // 这确保域重载或重新初始化时不会残留过期对象。
            MemoryPool.ClearAll();
        }

        #endregion
    }
}
