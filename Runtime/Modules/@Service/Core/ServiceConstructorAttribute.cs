using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 标记容器应使用此构造函数进行依赖注入。
    /// <para>当服务有多个公共构造函数时，容器优先选择标记了此特性的构造函数；
    /// 若无标记，则回退到参数最多的公共构造函数。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = true)]
    public sealed class ServiceConstructorAttribute : Attribute
    {
    }
}
