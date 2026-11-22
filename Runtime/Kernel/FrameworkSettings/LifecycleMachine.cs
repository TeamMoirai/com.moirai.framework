using System;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos
{
    /// <summary>
    /// 生命周期状态转换的唯一语义实现。
    /// </summary>
    /// <remarks>
    /// 包内宿主共用本机：类侧基座 <see cref="HandlerBase"/>（容器服务与 <see cref="FrameworkHandler"/>）与 Mono 侧 <br />
    /// <see cref="ServiceMono{TScope}"/>，后者经 <see cref="IHost"/> 接缝接入；主线程调用。 <br />
    /// 复活口径由宿主自报（<see cref="IHost.AllowsRevival"/>），Mono 取假——实例随 GameObject 而亡，没有复活来源。
    /// </remarks>
    internal static class LifecycleMachine
    {
        /// <summary>
        /// 状态机宿主：暴露可写的状态位与两个回调。
        /// </summary>
        internal interface IHost
        {
            /// <summary>当前状态（读写口，就绪判定见 <see cref="LifecycleMachine.IsReady"/>）。</summary>
            EServiceState State { get; set; }

            /// <summary>已关闭实例是否允许重新初始化。</summary>
            bool AllowsRevival { get; }

            /// <summary>
            /// 装配回调。
            /// </summary>
            void OnInit();

            /// <summary>
            /// 释放回调。
            /// </summary>
            void OnShutdown();
        }

        /// <summary>
        /// 就绪判定的全仓唯一落点：严格取 <see cref="EServiceState.Initialized"/>。
        /// </summary>
        /// <remarks>
        /// 关停中与已关闭都不算就绪——持留的 <c>[SerializeReference]</c> 后端带 <c>Disposed</c> 进新世界时， <br />
        /// 按区间判定会被当成就绪、跳过依赖边。
        /// </remarks>
        /// <param name="state">待判定的生命周期状态。</param>
        /// <returns>处于就绪态为真。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsReady(EServiceState state) => state == EServiceState.Initialized;

        /// <summary>
        /// 驱动装配。
        /// </summary>
        /// <param name="host">状态机宿主。</param>
        /// <remarks>
        /// 幂等：已就绪与关停中直接返回，已关闭实例放行与否看 <see cref="IHost.AllowsRevival"/>。 <br />
        /// 容器层的关停后重建阻断在 <see cref="GameServices.EnsureFacadeAssemblyAllowed"/>，不依赖本方法。
        /// </remarks>
        internal static void Initialize(IHost host)
        {
            // 起点读数即快照，区分两条路：停在起点才落位就绪；OnInit 途中被外部关闭则保持被关闭的结果，不盖回。
            var state = host.State;
            if (state == EServiceState.Initialized || state == EServiceState.ShuttingDown) return;
            if (state == EServiceState.Disposed && !host.AllowsRevival) return;

            host.OnInit();
            if (host.State == state) host.State = EServiceState.Initialized;
        }

        /// <summary>
        /// 驱动关停。
        /// </summary>
        /// <param name="host">状态机宿主。</param>
        /// <remarks>
        /// 幂等：已进入 <see cref="EServiceState.ShuttingDown"/> 及之后的状态直接返回。 <br />
        /// <c>OnShutdown</c> 抛出的异常就地经 <c>LogUtility.Error</c> 记录，实例照常落 <c>Disposed</c>，不截断同批其余关停。
        /// </remarks>
        internal static void Shutdown(IHost host)
        {
            if (host.State >= EServiceState.ShuttingDown) return;

            host.State = EServiceState.ShuttingDown;
            try { host.OnShutdown(); }
            catch (Exception ex) { LogUtility.Error(ex.ToString()); }
            host.State = EServiceState.Disposed;
        }
    }
}
