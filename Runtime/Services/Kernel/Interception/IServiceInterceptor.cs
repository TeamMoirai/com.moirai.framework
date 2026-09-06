using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期拦截器。在注册、关闭与轮询帧边界插入横切逻辑（日志、性能监控等）。
    /// <para>多个拦截器按 <see cref="Priority"/> 降序执行。</para>
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
        /// 服务即将注册（OnInit 调用前）。
        /// </summary>
        void OnServiceRegistering(IService service, Type contractType, EServiceScopeKind scope) { }

        /// <summary>
        /// 服务已注册（OnInit 已调用）。
        /// </summary>
        void OnServiceRegistered(IService service, Type contractType, EServiceScopeKind scope) { }

        /// <summary>
        /// 服务已注销（Shutdown 已调用，已从注册表移除）。
        /// </summary>
        void OnServiceUnregistered(IService service) { }

        /// <summary>
        /// 服务即将 Shutdown（Shutdown 调用前）。
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
