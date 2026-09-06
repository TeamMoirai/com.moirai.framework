using System;
using Cysharp.Threading.Tasks;

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

        public virtual int Priority => 0;
        public virtual EServiceScopeKind Scope => EServiceScopeKind.App;

        /// <summary>
        /// 当前生命周期状态（只读投影——唯一事实源在容器侧，由容器驱动转换）。
        /// </summary>
        public EServiceState State { get; internal set; } = EServiceState.Created;

        #endregion

        #region 生命周期 [LIFECYCLE]

        public abstract void OnInit();
        public abstract void OnShutdown();

        #endregion

        #region IServiceLifecycle 实现 [RUNTIME LIFECYCLE]

        void IServiceLifecycle.Initialize(ServiceWorld world, ServiceScope scope)
        {
            if (State >= EServiceState.Initialized) return;

            OnInit();
            State = EServiceState.Initialized;
            world.InvokeRegistered(this, GetType(), scope.Kind);
        }

        void IServiceLifecycle.Destroy(ServiceWorld world)
        {
            if (State >= EServiceState.ShuttingDown) return;

            State = EServiceState.ShuttingDown;
            world.InvokeShutdown(this);
            try { OnShutdown(); }
            catch (Exception ex) { LogUtility.Error(ex.ToString()); }
            State = EServiceState.Disposed;
        }

        #endregion
    }
}
