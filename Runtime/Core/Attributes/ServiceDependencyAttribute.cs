using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 声明服务依赖。注册器据此在注册期校验依赖——依赖必须先行手动注册（服务实例不由框架隐式创建），
    /// 注册序即依赖链序，未注册的依赖使注册立即失败（fail-fast）。
    /// <para>支持单特性多类型声明（类似 <c>RequireComponent</c>）；声明顺序即依赖校验顺序。</para>
    /// <para>所有依赖类型必须实现 <see cref="IService"/>——由 <c>ServiceDependencyAnalyzer</c>（MIRAI002）在编译期校验。</para>
    /// <para>循环依赖在注册期即抛 <see cref="GameException"/>（fail-fast）。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 单依赖
    /// [ServiceDependency(typeof(ResourceService))]
    /// public sealed class AudioService : ServiceBase, IServiceTickable { ... }
    ///
    /// // 多依赖（单特性）
    /// [ServiceDependency(typeof(ResourceService), typeof(TimerService))]
    /// public sealed class UIService : ServiceBase, IServiceTickable { ... }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ServiceDependencyAttribute : Attribute
    {
        /// <summary>
        /// 依赖的服务类型数组（每个类型须为实现 <see cref="IService"/> 的具体类型）。
        /// </summary>
        public Type[] DependencyTypes { get; }

        /// <param name="dependencyTypes">依赖的服务类型列表（至少一个）。</param>
        public ServiceDependencyAttribute(params Type[] dependencyTypes)
        {
            DependencyTypes = dependencyTypes ?? throw new ArgumentNullException(nameof(dependencyTypes));
        }
    }
}
