using System;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期状态。
    /// </summary>
    public enum EServiceState : byte
    {
        /// <summary>已创建但未初始化。</summary>
        Created = 0,
        /// <summary>已初始化，正在运行。</summary>
        Initialized = 1,
        /// <summary>正在关闭（Shutdown 调用中）。</summary>
        ShuttingDown = 2,
        /// <summary>已完全关闭并从注册表移除。</summary>
        Disposed = 3,
    }

    /// <summary>
    /// 服务生命周期作用域种类。
    /// </summary>
    public enum EServiceScopeKind : byte
    {
        /// <summary>应用级，生命周期最长，适合资源、音频、UI、计时器等全局服务。</summary>
        App = 0,
        /// <summary>场景级，主场景切换时会重置，适合当前场景状态。</summary>
        Scene = 1,
        /// <summary>玩法级，适合一局战斗或一个玩法实例的服务。</summary>
        Gameplay = 2,
    }

    /// <summary>
    /// 服务核心契约。
    /// <para><b>依赖声明方式</b>：纯 C# 服务通过构造函数参数声明；MonoBehaviour 服务通过 <c>Inject(IServiceProvider)</c> 声明。</para>
    /// <para>依赖由 <see cref="ServiceContainer"/> 在构建期拓扑排序并注入，编译期即可验证。</para>
    /// </summary>
    public interface IService
    {
        /// <summary>轮询优先级（降序，高优先先轮询、后关闭）。</summary>
        int Priority { get; }

        /// <summary>所属作用域。</summary>
        EServiceScopeKind Scope { get; }

        /// <summary>当前生命周期状态。</summary>
        EServiceState State { get; }

        /// <summary>注册完成后调用（同步）。</summary>
        void OnInit();

        /// <summary>注销或作用域关闭时调用。</summary>
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

    /// <summary>
    /// 异步初始化服务。由 <see cref="ServiceContainer.BuildAsync"/> 按拓扑序驱动，
    /// 被依赖服务的 OnInitAsync 先于依赖方执行。
    /// </summary>
    public interface IAsyncInitService
    {
        UniTask OnInitAsync();
    }

    /// <summary>
    /// 纯 C# 服务基类。不依赖 MonoBehaviour，生命周期由 <see cref="ServiceContainer"/> 精确控制。
    /// <para><b>依赖注入方式</b>：通过构造函数参数声明，容器在创建时自动解析并注入。</para>
    /// <para>若需运行时延迟解析（如可选依赖），可在构造函数中注入 <see cref="IServiceProvider"/>。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// public class AudioService : ServiceBase, IAudioService
    /// {
    ///     private readonly IResourceService _resource;
    ///     private readonly IServiceProvider _provider;
    ///
    ///     public AudioService(IResourceService resource, IServiceProvider provider)
    ///     {
    ///         _resource = resource;     // 构造期注入——编译期明确
    ///         _provider = provider;     // 可选：运行时延迟解析
    ///     }
    ///
    ///     public override void OnInit() { }
    ///     public override void Shutdown() { }
    /// }
    /// </code>
    /// </example>
    public abstract class ServiceBase : IService
    {
        #region 属性 [PROPERTIES]

        public virtual int Priority => 0;
        public virtual EServiceScopeKind Scope => EServiceScopeKind.App;

        /// <summary>当前生命周期状态。由容器维护，子类只读。</summary>
        public EServiceState State { get; internal set; } = EServiceState.Created;

        #endregion

        #region 生命周期 [LIFECYCLE]

        public abstract void OnInit();
        public abstract void Shutdown();

        #endregion
    }
}
