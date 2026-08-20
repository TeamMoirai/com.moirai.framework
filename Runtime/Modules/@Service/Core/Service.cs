using System;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期状态。
    /// </summary>
    public enum EServiceState : byte
    {
        /// <summary>已创建但未注册。</summary>
        Created = 0,
        /// <summary>已注册并初始化，正在运行。</summary>
        Initialized = 1,
        /// <summary>正在关闭（Shutdown 调用中）。</summary>
        ShuttingDown = 2,
        /// <summary>已完全关闭并从注册表移除。</summary>
        Disposed = 3,
    }

    /// <summary>
    /// 服务核心契约。
    /// </summary>
    public interface IService
    {
        /// <summary>轮询优先级（降序，高优先先轮询、后关闭）。</summary>
        int Priority { get; }

        /// <summary>所属作用域。</summary>
        EServiceScopeKind Scope { get; }

        /// <summary>
        /// 此服务依赖的合约接口类型列表。注册时验证依赖已就绪，未满足则抛出 <see cref="GameException"/>。
        /// 声明的依赖强制先于本服务注册，因此注册顺序即依赖拓扑序，逆序关闭即逆拓扑序。
        /// 建议以 static readonly 数组返回，避免每次访问分配。
        /// </summary>
        Type[] Dependencies { get; }

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

    /// <summary>异步初始化服务。注册完成后由 <see cref="GameServices.InitializeAsync"/> 统一驱动。</summary>
    public interface IAsyncInitService
    {
        UniTask OnInitAsync();
    }

    /// <summary>
    /// 纯 C# 服务基类。不依赖 MonoBehaviour，生命周期由 <see cref="GameServices"/> 精确控制。
    /// </summary>
    public abstract class ServiceBase : IService
    {
        #region 字段 [FIELDS]

        private GameServices.ServiceContext _context;

        #endregion

        #region 属性 [PROPERTIES]

        public virtual int Priority => 0;
        public virtual EServiceScopeKind Scope => EServiceScopeKind.App;

        /// <summary>当前生命周期状态。</summary>
        public EServiceState State { get; internal set; } = EServiceState.Created;

        /// <summary>
        /// 此服务依赖的合约接口类型列表。注册时验证依赖已就绪，未满足则抛出 <see cref="GameException"/>。
        /// 默认为空。子类覆写以声明依赖（如 <c>typeof(IResourceService)</c>）。
        /// 建议以 static readonly 数组返回，避免每次访问分配。
        /// </summary>
        public virtual Type[] Dependencies => Array.Empty<Type>();

        #endregion

        #region 生命周期 [LIFECYCLE]

        public abstract void OnInit();
        public abstract void Shutdown();

        #endregion

        #region 跨服务依赖 [CROSS-SERVICE DEPENDENCIES]

        // DI 辅助方法与 ServiceMonoBase 中的实现完全一致——两者无法共享基类（一个继承 object，一个继承 MonoBehaviour），
        // 刻意保持重复以避免组合带来的间接调用开销。

        /// <summary>获取依赖服务（查找顺序：当前作用域 → GetBest 回退）。未找到抛 <see cref="GameException"/>。</summary>
        protected T Require<T>() where T : class => _context.Require<T>();

        /// <summary>尝试获取依赖服务。</summary>
        protected bool TryGet<T>(out T service) where T : class => _context.TryGet(out service);

        /// <summary>从 App 作用域获取服务。未找到抛 <see cref="GameException"/>。</summary>
        protected T RequireApp<T>() where T : class => _context.RequireApp<T>();

        /// <summary>从 Scene 作用域获取服务。未找到抛 <see cref="GameException"/>。</summary>
        protected T RequireScene<T>() where T : class => _context.RequireScene<T>();

        /// <summary>从 Gameplay 作用域获取服务。未找到抛 <see cref="GameException"/>。</summary>
        protected T RequireGameplay<T>() where T : class => _context.RequireGameplay<T>();

        #endregion

        internal void SetContext(GameServices.ServiceContext context) => _context = context;
    }
}
