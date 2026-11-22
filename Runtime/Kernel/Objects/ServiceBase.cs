using System.Runtime.CompilerServices;

namespace Moirai.Atropos
{
    /// <summary>
    /// 纯 C# 服务基类。不依赖 MonoBehaviour，生命周期由 <see cref="ServiceWorld"/> 控制。
    /// </summary>
    /// <remarks>
    /// 状态机与装配/释放钩子来自 <see cref="HandlerBase"/>，本类只补容器侧的契约面。 <br />
    /// 依赖通过 <c>[ServiceDependency]</c> 特性声明，世界初始化时按依赖图拓扑排序驱动 <see cref="HandlerBase.OnInit"/>。 <br />
    /// 运行时延迟解析统一走 <see cref="GameServices.GetRequiredService{T}"/> / <see cref="GameServices.TryGetService{T}"/>。
    /// </remarks>
    public abstract class ServiceBase : HandlerBase, IService, IServiceLifecycle, IServiceContextAware
    {
        #region 属性 [PROPERTIES]

        /// <summary>轮询优先级（降序：数值越大越先 Tick，同值按注册先后）。</summary>
        /// <remarks>
        /// 框架内置服务统一 ≤ -1000（见 <see cref="ServicePriorityOrder"/>）；业务服务默认 0 及以上。
        /// </remarks>
        public virtual int Priority => 0;
        public virtual EServiceScopeKind Scope => EServiceScopeKind.App;

        #endregion

        #region 上下文注入 [CONTEXT INJECTION]

        /// <summary>服务上下文（容器在注册入图时经 <see cref="IServiceContextAware"/> 注入；派生在 <see cref="HandlerBase.OnInit"/> 中按契约接口解析依赖）。</summary>
        protected IServiceContext Context { get; private set; }

        void IServiceContextAware.SetContext(IServiceContext context) => Context = context;

        #endregion 上下文注入 [CONTEXT INJECTION]

        #region IService 实现 [SERVICE CONTRACT]

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void IService.OnInit() => OnInit();

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void IService.OnShutdown() => OnShutdown();

        #endregion IService 实现 [SERVICE CONTRACT]

        #region IServiceLifecycle 实现 [RUNTIME LIFECYCLE]

        /// <inheritdoc />
        EServiceState IServiceLifecycle.StateInternal => State;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void IServiceLifecycle.Initialize() => DriveInitialize();

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void IServiceLifecycle.Destroy() => DriveShutdown();

        #endregion IServiceLifecycle 实现 [RUNTIME LIFECYCLE]
    }
}
