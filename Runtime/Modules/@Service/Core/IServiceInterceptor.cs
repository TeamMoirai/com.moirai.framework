using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期拦截器。在注册、轮询、注销等关键点插入横切逻辑（日志、性能监控、缓存等）。
    /// <para>多个拦截器按 <see cref="Priority"/> 降序执行。</para>
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
        void OnServiceRegistering(IService service, Type interfaceType, EServiceScopeKind scope) { }

        /// <summary>
        /// 服务已注册（OnInit 已调用）。
        /// </summary>
        void OnServiceRegistered(IService service, Type interfaceType, EServiceScopeKind scope) { }

        /// <summary>
        /// 服务已注销（Shutdown 已调用，已从注册表移除）。
        /// </summary>
        void OnServiceUnregistered(IService service) { }

        /// <summary>
        /// 服务即将 Tick（每帧 Update 轮询前）。
        /// </summary>
        void OnServiceTick(IService service, float elapseSeconds, float realElapseSeconds) { }

        /// <summary>
        /// 服务即将 Shutdown（Shutdown 调用前）。
        /// </summary>
        void OnServiceShutdown(IService service) { }
    }
}
