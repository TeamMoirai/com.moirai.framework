using System;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos
{
    /// <summary>服务生命周期状态。</summary>
    public enum EServiceState : byte
    {
        /// <summary>已创建但未初始化。</summary>
        Created = 0,
        /// <summary>已初始化，正在运行。</summary>
        Initialized = 1,
        /// <summary>正在关闭（Shutdown 调用中）。</summary>
        ShuttingDown = 2,
        /// <summary>已关闭并从注册表移除。</summary>
        Disposed = 3,
    }

    /// <summary>
    /// 运行时注册/注销的迭代延迟策略。
    /// </summary>
    public enum EDeferMode : byte
    {
        /// <summary>
        /// 延迟到当前迭代结束后执行（默认）。适用于 Tick 中注册/注销服务。
        /// </summary>
        Defer = 0,

        /// <summary>
        /// 立即抛出异常（Fail-fast）。用于检测意外的迭代中注册。
        /// </summary>
        Throw = 1,
    }

    /// <summary>服务作用域种类。</summary>
    public enum EServiceScopeKind : byte
    {
        /// <summary>应用级，生命周期最长，随 GameApp 关闭而销毁。</summary>
        App = 0,
        /// <summary>场景级，主场景切换时重置。</summary>
        Scene = 1,
        /// <summary>玩法级，随战斗/玩法实例结束而销毁。</summary>
        Gameplay = 2,
    }

    /// <summary>
    /// 服务核心契约。
    /// <para>依赖通过 <c>[ServiceDependency]</c> 特性声明，由 <c>RegisterService</c> 递归预注册。</para>
    /// <para>依赖由 <see cref="GameServices.RegisterService"/> 递归预注册。</para>
    /// </summary>
    public interface IService
    {
        /// <summary>
        /// 轮询优先级（降序，高优先先轮询、后关闭）。
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// 所属作用域。
        /// </summary>
        EServiceScopeKind Scope { get; }

        /// <summary>
        /// 注册完成后调用（同步）。
        /// </summary>
        void OnInit();

        /// <summary>
        /// 注销或作用域关闭时调用。
        /// </summary>
        void Shutdown();
    }

    /// <summary>每帧 Update 轮询。</summary>
    public interface IServiceTickable
    {
        void Tick(float elapseSeconds, float realElapseSeconds);
    }

    /// <summary>每帧 FixedUpdate 轮询。</summary>
    public interface IServiceFixedTickable
    {
        void FixedTick(float elapseSeconds, float realElapseSeconds);
    }

    /// <summary>每帧 LateUpdate 轮询。</summary>
    public interface IServiceLateTickable
    {
        void LateTick(float elapseSeconds, float realElapseSeconds);
    }

    /// <summary>编辑器 Gizmos 绘制。</summary>
    public interface IServiceGizmoDrawable
    {
        void OnDrawGizmos();
    }

    /// <summary>异步初始化服务。由 <c>RegisterService</c> 在 OnInit 后驱动。</summary>
    public interface IAsyncInitService
    {
        UniTask OnInitAsync();
    }

    /// <summary>
    /// 异步关闭服务。由 <see cref="ServiceScope"/>.DisposeAsync / <see cref="GameServices"/>.ShutdownContainerAsync
    /// 在 <c>Shutdown</c> 调用前按逆注册序异步关闭。
    /// <para>用于资源异步卸载、网络连接优雅关闭等场景。</para>
    /// </summary>
    public interface IAsyncShutdownService
    {
        /// <summary>
        /// 异步关闭。在同步 <c>Shutdown</c> 调用前执行。
        /// </summary>
        UniTask OnShutdownAsync();
    }

    /// <summary>
    /// 纯 C# 服务基类。不依赖 MonoBehaviour，生命周期由 <see cref="ServiceWorld"/> 控制。
    /// <para>依赖通过 <c>[ServiceDependency]</c> 特性声明，由注册器递归预注册。</para>
    /// <para>运行时延迟解析可通过 <see cref="Require{T}"/> / <see cref="TryGet{T}"/> 等方法。</para>
    /// </summary>
    public abstract class ServiceBase : IService, IServiceLifecycle
    {
        #region 属性 [PROPERTIES]

        public virtual int Priority => 0;
        public virtual EServiceScopeKind Scope => EServiceScopeKind.App;

        /// <summary>
        /// 当前生命周期状态。由容器维护，子类只读。
        /// </summary>
        public EServiceState State { get; internal set; } = EServiceState.Created;

        #endregion

        #region 运行时依赖查找 [RUNTIME DEPENDENCY LOOKUP]

        private IServiceProvider _serviceProvider;

        /// <summary>
        /// 在当前作用域中查找服务（未找到抛 <see cref="GameException"/>）。
        /// <para>按 Gameplay > Scene > App 优先级返回最优服务。</para>
        /// </summary>
        protected T Require<T>() where T : class
            => _serviceProvider.GetRequiredService<T>();

        /// <summary>
        /// 在当前作用域中尝试查找服务。
        /// <para>按 Gameplay > Scene > App 优先级返回最优服务。</para>
        /// </summary>
        protected bool TryGet<T>(out T service) where T : class
            => _serviceProvider.TryGetService<T>(out service);

        /// <summary>
        /// 要求 App 作用域中的服务。
        /// </summary>
        protected T RequireApp<T>() where T : class
            => _serviceProvider.GetRequiredServiceInScope<T>(EServiceScopeKind.App);

        /// <summary>
        /// 要求 Scene 作用域中的服务。
        /// </summary>
        protected T RequireScene<T>() where T : class
            => _serviceProvider.GetRequiredServiceInScope<T>(EServiceScopeKind.Scene);

        /// <summary>
        /// 要求 Gameplay 作用域中的服务。
        /// </summary>
        protected T RequireGameplay<T>() where T : class
            => _serviceProvider.GetRequiredServiceInScope<T>(EServiceScopeKind.Gameplay);

        #endregion

        #region 生命周期 [LIFECYCLE]

        public abstract void OnInit();
        public abstract void Shutdown();

        #endregion

        #region 内部初始化 [INTERNAL INITIALIZATION]

        /// <summary>
        /// 由 <see cref="ServiceWorld"/> 在构建期调用，注入服务提供者。
        /// </summary>
        internal void InjectInternal(IServiceProvider provider)
        {
            _serviceProvider = provider;
        }

        #endregion

        #region IServiceLifecycle 实现 [RUNTIME LIFECYCLE]

        void IServiceLifecycle.Initialize(ServiceWorld world, ServiceScope scope)
        {
            if (State >= EServiceState.Initialized) return;

            InjectInternal(world);
            OnInit();
            State = EServiceState.Initialized;
            GameServices.InvokeRegistered(this, GetType(), scope.Kind);
        }

        void IServiceLifecycle.Destroy()
        {
            if (State >= EServiceState.ShuttingDown) return;

            State = EServiceState.ShuttingDown;
            GameServices.InvokeShutdown(this);
            try { Shutdown(); }
            catch (Exception ex) { LogUtility.Error(ex.ToString()); }
            State = EServiceState.Disposed;
        }

        #endregion
    }
}
