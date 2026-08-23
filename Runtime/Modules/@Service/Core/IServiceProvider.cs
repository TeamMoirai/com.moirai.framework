using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务提供者。所有服务访问的统一入口。
    /// <para>纯 C# 服务通过构造函数注入此接口；非服务代码通过 <see cref="GameApp"/> 缓存属性（如 <see cref="GameApp.Audio"/>）访问。</para>
    /// <para>跨作用域查找通过 <see cref="ServiceWorld.ContractBindings"/> 实现 O(1) 查找。</para>
    /// </summary>
    public interface IServiceProvider
    {
        /// <summary>
        /// 获取服务（未找到抛 <see cref="GameException"/>）。
        /// <para>按 Gameplay > Scene > App 优先级返回最优服务。</para>
        /// </summary>
        T GetRequiredService<T>() where T : class;

        /// <summary>
        /// 获取服务（未找到返回 null）。
        /// <para>按 Gameplay > Scene > App 优先级返回最优服务。</para>
        /// </summary>
        T GetService<T>() where T : class;

        /// <summary>
        /// 尝试获取服务。
        /// <para>按 Gameplay > Scene > App 优先级返回最优服务。</para>
        /// </summary>
        bool TryGetService<T>(out T service) where T : class;

        /// <summary>
        /// 在指定作用域中获取服务（未找到抛 <see cref="GameException"/>）。
        /// </summary>
        /// <param name="scope">目标作用域。</param>
        T GetRequiredServiceInScope<T>(EServiceScopeKind scope) where T : class;

        /// <summary>
        /// 在指定作用域中尝试获取服务。
        /// </summary>
        /// <param name="scope">目标作用域。</param>
        /// <param name="service">获取到的服务。</param>
        bool TryGetServiceInScope<T>(EServiceScopeKind scope, out T service) where T : class;

        /// <summary>
        /// 按运行时类型获取服务（未找到抛 <see cref="GameException"/>）。用于反射场景。
        /// </summary>
        /// <param name="serviceType">服务契约类型。</param>
        IService GetRequiredService(Type serviceType);

        /// <summary>
        /// 按运行时类型获取服务（未找到返回 null）。用于反射场景。
        /// </summary>
        /// <param name="serviceType">服务契约类型。</param>
        IService GetService(Type serviceType);
    }
}
