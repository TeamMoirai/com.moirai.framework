using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 标记静态类为处理器宿主，由源生成器自动生成 <c>Handler</c> 属性和线程安全懒加载。
    /// </summary>
    /// <remarks>
    /// <para>使用方式：</para>
    /// <code>
    /// [HandlerHost(typeof(LogHandler))]
    /// public static partial class LogUtility
    /// {
    ///     private static LogHandler CreateDefaultHandler() => new DefaultLogHandler();
    ///     // ... facade 方法
    /// }
    /// </code>
    /// <para>源生成器会生成 s_Handler 字段（private，partial 同类可直接访问）、
    /// s_DefaultFactory 字段和 Handler 属性（get/set）。</para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class HandlerHostAttribute : Attribute
    {
        /// <summary>
        /// 处理器类型，必须继承 <see cref="FrameworkHandler"/>。
        /// </summary>
        public Type HandlerType { get; }

        /// <param name="handlerType">处理器类型。</param>
        public HandlerHostAttribute(Type handlerType)
        {
            HandlerType = handlerType;
        }
    }
}
