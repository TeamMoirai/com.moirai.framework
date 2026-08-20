using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 标记容器应使用此构造函数进行依赖注入。
    /// <para>当服务类有多个公共构造函数时，<see cref="ServiceContainer"/> 优先选择标记了此特性的构造函数。
    /// 若无标记，则回退到参数最多的公共构造函数。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// public class MyService : ServiceBase, IMyService
    /// {
    ///     public MyService() { /* 默认构造 */ }
    ///
    ///     [ServiceConstructor]
    ///     public MyService(IDependency dep) { /* 容器使用此构造函数 */ }
    ///
    ///     public MyService(IDependency dep, IOther other) { /* 参数最多但无标记 */ }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = true)]
    public sealed class ServiceConstructorAttribute : Attribute
    {
    }
}
