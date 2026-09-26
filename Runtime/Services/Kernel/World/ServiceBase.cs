using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 纯 C# 服务基类。不依赖 MonoBehaviour，生命周期由 <see cref="ServiceWorld"/> 控制。
    /// <para>依赖通过 <c>[ServiceDependency]</c> 特性声明，世界初始化时按依赖图拓扑排序驱动 <see cref="OnInit"/>。</para>
    /// <para>运行时延迟解析统一走 <see cref="GameServices.GetRequiredService{T}"/> / <see cref="GameServices.TryGetService{T}"/>。</para>
    /// </summary>
    public abstract class ServiceBase : IService, IServiceLifecycle
    {
        #region 属性 [PROPERTIES]

        /// <summary>
        /// 轮询优先级（降序：数值越大越先 Tick，同值按注册先后）。
        /// <para>框架内置服务统一 ≤ -1000（见 <see cref="ServicePriorityOrder"/>）；业务服务默认 0 及以上。</para>
        /// </summary>
        public virtual int Priority => 0;
        public virtual EServiceScopeKind Scope => EServiceScopeKind.App;

        /// <summary>
        /// 当前生命周期状态（只读投影——唯一事实源在容器侧，由容器经 <see cref="IServiceLifecycle"/> 驱动转换；
        /// 写入端口对本类之外的任何代码关闭）。
        /// </summary>
        public EServiceState State { get; private set; } = EServiceState.Created;

        #endregion

        #region 生命周期 [LIFECYCLE]

        public abstract void OnInit();
        public abstract void OnShutdown();

        #endregion

        #region IServiceLifecycle 实现 [RUNTIME LIFECYCLE]

        EServiceState IServiceLifecycle.StateInternal => State;

        void IServiceLifecycle.Initialize()
        {
            if (State >= EServiceState.Initialized) return;

            OnInit();
            // OnInit 期间可能被外部关闭（退出应用时 Dispose 打在初始化途中）：
            // 无条件赋值会把 ShuttingDown/Disposed 盖回 Initialized，让已关闭的服务重新"就绪"
            if (State == EServiceState.Created) State = EServiceState.Initialized;
        }

        void IServiceLifecycle.Destroy()
        {
            if (State >= EServiceState.ShuttingDown) return;

            State = EServiceState.ShuttingDown;
            try { OnShutdown(); }
            catch (Exception ex) { LogUtility.Error(ex.ToString()); }
            State = EServiceState.Disposed;
        }

        #endregion
    }
}
