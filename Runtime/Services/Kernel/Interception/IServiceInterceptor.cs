using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期拦截器。在注册、关闭与轮询帧边界插入横切逻辑（日志、性能监控等）。
    /// <para>多个拦截器按 <see cref="Priority"/> 降序执行。</para>
    /// <para><b>异常策略</b>：除 <see cref="OnServiceRegistering"/> 外，回调抛出的异常由容器就地记录并隔离，
    /// 不会传播到被观察的服务、其它拦截器或帧主循环——观察器缺陷不得拖垮游戏循环。
    /// <see cref="OnServiceRegistering"/> 是唯一的否决通道：抛出即拒绝本次注册（fail-fast）。</para>
    /// <para><b>粒度契约</b>：轮询回调以「作用域一帧」为边界（<see cref="OnBeforeScopeTick"/>/<see cref="OnAfterScopeTick"/>），
    /// 不提供逐服务回调——逐服务耗时监控由编辑器诊断旁表承担（编译期门控，Release 零成本）。</para>
    /// </summary>
    public interface IServiceInterceptor
    {
        /// <summary>
        /// 执行优先级（降序，高优先先执行）。默认 0。
        /// </summary>
        int Priority => 0;

        /// <summary>
        /// 服务即将注册（契约句柄入表前）。
        /// <para>否决通道：抛出即拒绝本次注册，异常照常向调用方传播（本回调不被容器隔离），
        /// 且注册表不留任何痕迹。</para>
        /// </summary>
        void OnServiceRegistering(IService service, Type contractType, EServiceScopeKind scope) { }

        /// <summary>
        /// 服务已注册（<c>OnInit</c> 已调用）。
        /// <para>粒度是<b>契约</b>：同一实例以 N 个契约注册即收到 N 次回调，<paramref name="contractType"/>
        /// 与 <see cref="OnServiceRegistering"/> 上报的契约一一对应（不是实现类型）。</para>
        /// </summary>
        void OnServiceRegistered(IService service, Type contractType, EServiceScopeKind scope) { }

        /// <summary>
        /// 服务已注销（Shutdown 已调用，已从注册表移除）。
        /// </summary>
        void OnServiceUnregistered(IService service) { }

        /// <summary>
        /// 服务即将 Shutdown（<c>OnShutdown</c> 调用前）。由容器在关闭入口处发出。
        /// <para>此刻服务状态仍为 <see cref="EServiceState.Initialized"/>——状态转换在本次回调返回后才发生。</para>
        /// </summary>
        void OnServiceShutdown(IService service) { }

        /// <summary>
        /// 作用域轮询帧边界——该作用域本帧全部服务 Tick 之前。
        /// </summary>
        void OnBeforeScopeTick(EServiceScopeKind scope, float elapseSeconds, float realElapseSeconds) { }

        /// <summary>
        /// 作用域轮询帧边界——该作用域本帧全部服务 Tick 之后。
        /// </summary>
        void OnAfterScopeTick(EServiceScopeKind scope, float elapseSeconds, float realElapseSeconds) { }
    }
}
