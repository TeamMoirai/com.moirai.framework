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
    /// 两个读数分档：<see cref="IsReady"/> 答「此刻是否就绪」，<see cref="WasInitialized"/> 答「是否装配过」——关停体的编排收尾只看后者。 <br />
    /// 本类不带 <c>[Serializable]</c>：注解只落在被 <c>[SerializeReference]</c> 持有的具体后端与 <see cref="FrameworkHandler"/> 上。
    /// </remarks>
    public abstract class HandlerBase : LifecycleMachine.IHost
    {
        /// <summary>生成宿主与服务门面共用的冷初始化互斥锁（唯一的监视器）；可重入以允许同步解析跨宿主依赖。</summary>
        internal static readonly object InitializationLock = new object();

        /// <summary>当前状态；不序列化，域重载后回到 <see cref="EServiceState.Created"/>，避免快照状态污染。</summary>
        [NonSerialized] private EServiceState _state;

        /// <summary>是否落位过就绪态；单向锁存，同样不序列化，置位点见 <see cref="WasInitialized"/>。</summary>
        [NonSerialized] private bool _wasInitialized;

        /// <summary>当前生命周期状态（只读出口；写入只经 <see cref="LifecycleMachine"/> 走显式接缝）。</summary>
        public EServiceState State => _state;

        /// <summary>实例是否处于就绪态。</summary>
        protected internal bool IsReady => LifecycleMachine.IsReady(_state);

        /// <summary>本实例是否落位过 <see cref="EServiceState.Initialized"/>——「装配过」的口径，关停期内仍为真。</summary>
        /// <remarks>
        /// 存在理由：机器在驱动 <c>OnShutdown</c> 之前已把状态落 <see cref="EServiceState.ShuttingDown"/>， <br />
        /// 关停体里欠做的编排收尾（摘订阅、注销调试窗）若挂在 <see cref="IsReady"/> 上便永不执行。 <br />
        /// <see cref="IsReady"/> 不能兼差：它是拓扑解析与取用的就绪闸，必须严格等号，放宽即让持留的已关闭后端冒充就绪； <br />
        /// 本属性只回答「这趟收尾欠不欠」，与「此刻能不能用」是两个问题。 <br />
        /// 锁存语义：只在机器落 <see cref="EServiceState.Initialized"/> 的那一刻置位，此后不清除——复活重新落位即再武装， <br />
        /// 而释放回调要求状态低于 <see cref="EServiceState.ShuttingDown"/>，故「锁存为真却从未在本趟装配过」不可达。
        /// </remarks>
        protected internal bool WasInitialized => _wasInitialized;

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
            set
            {
                // 写入口只有机器一处，故「机器落 Initialized」与「锁存武装」同一刻发生
                _state = value;
                if (value == EServiceState.Initialized) _wasInitialized = true;
            }
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
