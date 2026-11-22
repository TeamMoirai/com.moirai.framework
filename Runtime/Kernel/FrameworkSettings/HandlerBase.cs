using System;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos
{
    /// <summary>
    /// 处理器与服务共同的生命期基座：状态机、就绪判据与装配/释放钩子。
    /// </summary>
    /// <remarks>
    /// 转换语义在 <see cref="LifecycleMachine"/>，本类只是类侧宿主。 <br />
    /// 容器归属见 <see cref="ServiceBase"/>，无容器直驱见 <see cref="FrameworkHandler"/>。 <br />
    /// 钩子一律 <c>protected</c>：装配与释放只由框架驱动，外部直调会绕过状态机。 <br />
    /// 本类不带 <c>[Serializable]</c>：注解只落在被 <c>[SerializeReference]</c> 持有的具体后端与 <see cref="FrameworkHandler"/> 上。
    /// </remarks>
    public abstract class HandlerBase : LifecycleMachine.IHost
    {
        /// <summary>生成宿主与服务门面共用的冷初始化互斥锁（唯一的监视器）；可重入以允许同步解析跨宿主依赖。</summary>
        internal static readonly object InitializationLock = new object();

        /// <summary>当前状态；不序列化，域重载后回到 <see cref="EServiceState.Created"/>，避免快照状态污染。</summary>
        [NonSerialized] private EServiceState _state;

        /// <summary>当前生命周期状态（只读出口；写入只经 <see cref="LifecycleMachine"/> 走显式接缝）。</summary>
        public EServiceState State => _state;

        /// <summary>实例是否处于就绪态。</summary>
        protected internal bool IsReady => LifecycleMachine.IsReady(_state);

        /// <summary>已关闭实例是否允许重新装配；拒绝复活的宿主覆写为 false。</summary>
        protected internal virtual bool AllowsRevival => true;

        /// <summary>
        /// 驱动装配（幂等）。
        /// </summary>
        /// <remarks>
        /// 容器经 <see cref="IServiceLifecycle"/>，生成宿主经 <see cref="FrameworkHandler.Internal_Init"/>。
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void DriveInitialize() => LifecycleMachine.Initialize(this);

        /// <summary>
        /// 驱动关停（幂等）。
        /// </summary>
        /// <remarks>
        /// 容器经 <see cref="IServiceLifecycle"/>，生成宿主经 <see cref="FrameworkHandler.Internal_Shutdown"/>。
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void DriveShutdown() => LifecycleMachine.Shutdown(this);

        /// <summary>
        /// 装配回调，用于接管后端资源。
        /// </summary>
        protected virtual void OnInit()
        {
        }

        /// <summary>
        /// 释放回调，用于归还后端资源。
        /// </summary>
        protected virtual void OnShutdown()
        {
        }

        /// <inheritdoc />
        EServiceState LifecycleMachine.IHost.State
        {
            get => _state;
            set => _state = value;
        }

        /// <inheritdoc />
        bool LifecycleMachine.IHost.AllowsRevival => AllowsRevival;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void LifecycleMachine.IHost.OnInit() => OnInit();

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void LifecycleMachine.IHost.OnShutdown() => OnShutdown();
    }
}
