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
    /// <para>依赖通过 <c>[ServiceDependency]</c> 特性声明，由 <c>RegisterService</c> 在注册期校验
    /// （依赖必须先行手动注册，服务实例不由框架隐式创建）。</para>
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
        void OnShutdown();
    }

    /// <summary>
    /// 每帧 Update 轮询能力接口。实现者必须为已注册的 <see cref="IService"/> 服务。
    /// </summary>
    public interface IServiceTickable : IService
    {
        /// <summary>
        /// 每帧 Update 轮询回调。
        /// </summary>
        void Tick(float elapseSeconds, float realElapseSeconds);
    }

    /// <summary>
    /// 每帧 FixedUpdate 轮询能力接口。实现者必须为已注册的 <see cref="IService"/> 服务。
    /// </summary>
    public interface IServiceFixedTickable : IService
    {
        /// <summary>
        /// 每帧 FixedUpdate 轮询回调。
        /// </summary>
        void FixedTick(float elapseSeconds, float realElapseSeconds);
    }

    /// <summary>
    /// 每帧 LateUpdate 轮询能力接口。实现者必须为已注册的 <see cref="IService"/> 服务。
    /// </summary>
    public interface IServiceLateTickable : IService
    {
        /// <summary>
        /// 每帧 LateUpdate 轮询回调。
        /// </summary>
        void LateTick(float elapseSeconds, float realElapseSeconds);
    }

    /// <summary>
    /// 编辑器 Gizmos 绘制能力接口。实现者必须为已注册的 <see cref="IService"/> 服务。
    /// </summary>
    public interface IServiceGizmoDrawable : IService
    {
        /// <summary>
        /// 编辑器 Gizmos 绘制回调。
        /// </summary>
        void OnDrawGizmos();
    }

    /// <summary>
    /// 异步关闭服务。由 <see cref="ServiceScope"/>.DisposeAsync / <see cref="GameServices"/>.ShutdownContainerAsync
    /// 在 <c>Shutdown</c> 调用前按逆注册序异步关闭。
    /// <para>用于资源异步卸载、网络连接优雅关闭等场景。</para>
    /// </summary>
    public interface IAsyncShutdownService : IService
    {
        /// <summary>
        /// 异步关闭。在同步 <c>Shutdown</c> 调用前执行。
        /// </summary>
        UniTask OnShutdownAsync();
    }

    /// <summary>
    /// 纯 C# 服务基类。不依赖 MonoBehaviour，生命周期由 <see cref="ServiceWorld"/> 控制。
    /// <para>依赖通过 <c>[ServiceDependency]</c> 特性声明，由注册器在注册期校验（须先行手动注册）。</para>
    /// <para>运行时延迟解析统一走 <see cref="GameServices.GetRequiredService{T}"/> / <see cref="GameServices.TryGetService{T}"/>。</para>
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

        #region 生命周期 [LIFECYCLE]

        public abstract void OnInit();
        public abstract void OnShutdown();

        #endregion

        #region IServiceLifecycle 实现 [RUNTIME LIFECYCLE]

        void IServiceLifecycle.Initialize(ServiceScope scope)
        {
            if (State >= EServiceState.Initialized) return;

            OnInit();
            State = EServiceState.Initialized;
            GameServices.InvokeRegistered(this, GetType(), scope.Kind);
        }

        void IServiceLifecycle.Destroy()
        {
            if (State >= EServiceState.ShuttingDown) return;

            State = EServiceState.ShuttingDown;
            GameServices.InvokeShutdown(this);
            try { OnShutdown(); }
            catch (Exception ex) { LogUtility.Error(ex.ToString()); }
            State = EServiceState.Disposed;
        }

        #endregion
    }
}
