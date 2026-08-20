using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期拦截器。在注册、轮询、注销等关键点插入横切逻辑（日志、性能监控、缓存等）。
    /// <para>使用默认接口方法实现空体——拦截器只需覆写关注的切面。</para>
    /// <para>多个拦截器按 <see cref="Priority"/> 降序执行（高优先先执行）。</para>
    /// <para><b>线程契约</b>：所有方法在 Unity 主线程调用，与 <see cref="GameServices"/> 一致。</para>
    /// </summary>
    public interface IServiceInterceptor
    {
        /// <summary>执行优先级（降序，高优先先执行）。默认 0。</summary>
        int Priority => 0;

        /// <summary>服务即将注册（OnInit 调用前）。可用于拒绝注册（抛异常）。</summary>
        void OnServiceRegistering(IService service, Type interfaceType, EServiceScopeKind scope) { }

        /// <summary>服务已注册（OnInit 已调用，状态已切换为 Initialized）。</summary>
        void OnServiceRegistered(IService service, Type interfaceType, EServiceScopeKind scope) { }

        /// <summary>服务已注销（Shutdown 已调用，已从注册表移除）。</summary>
        void OnServiceUnregistered(IService service) { }

        /// <summary>服务即将 Tick（每帧 Update 轮询前）。</summary>
        void OnServiceTick(IService service, float elapseSeconds, float realElapseSeconds) { }

        /// <summary>服务即将 Shutdown（Shutdown 调用前）。</summary>
        void OnServiceShutdown(IService service) { }
    }
}
