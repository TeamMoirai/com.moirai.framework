using Cysharp.Threading.Tasks;

namespace Moirai.Atropos
{
    /// <summary>服务生命周期状态。由容器（<see cref="ServiceWorld"/>）统一维护，服务侧仅只读投影。</summary>
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
    /// <para>依赖通过 <c>[ServiceDependency]</c> 特性声明；世界初始化（<see cref="ServiceWorld.Initialize"/>）时
    /// 按依赖图拓扑排序驱动 <see cref="OnInit"/>——初始化顺序由声明决定，与注册顺序无关。</para>
    /// <para>缺失依赖与循环依赖在初始化期即抛 <see cref="GameException"/>（fail-fast）。</para>
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
        /// 初始化回调。由容器按依赖图拓扑序驱动（两阶段世界构建的第二阶段）。
        /// </summary>
        void OnInit();

        /// <summary>
        /// 注销或作用域关闭时调用。严格逆初始化序执行。
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
    /// 异步初始化服务。由 <see cref="ServiceWorld.InitializeAsync"/> 在拓扑序位置处等待完成。
    /// <para>约束：实现本接口的服务必须在世界初始化前注册（两阶段的第一阶段）；
    /// 世界已初始化后的运行时注册将抛 <see cref="GameException"/>（fail-fast）——运行时注册无法等待异步初始化。</para>
    /// <para>同步 <see cref="ServiceWorld.Initialize"/> 遇到本接口实现者同样抛异常——改用 <see cref="ServiceWorld.InitializeAsync"/>。</para>
    /// </summary>
    public interface IServiceInitializableAsync : IService
    {
        /// <summary>
        /// 异步初始化。在同步 <c>OnInit</c> 位置处调用（拓扑序中的同一位置）。
        /// </summary>
        UniTask OnInitAsync();
    }

    /// <summary>
    /// 异步关闭服务。由 <see cref="ServiceWorld.ShutdownScopeAsync"/> / DisposeAsync
    /// 在 <c>OnShutdown</c> 调用前按逆拓扑序异步关闭。
    /// <para>用于资源异步卸载、网络连接优雅关闭等场景。</para>
    /// </summary>
    public interface IAsyncShutdownService : IService
    {
        /// <summary>
        /// 异步关闭。在同步 <c>OnShutdown</c> 调用前执行。
        /// </summary>
        UniTask OnShutdownAsync();
    }
}
