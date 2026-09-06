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
    ///     private static LogHandler GetHandlerFromSettings() => ...; // 可选：优先从 FrameworkSettings 获取
    ///     private static LogHandler CreateDefaultHandler() => new DefaultLogHandler();
    ///     // ... facade 方法
    /// }
    /// </code>
    /// <para>源生成器会生成 s_Handler 字段（private，partial 同类可直接访问）、
    /// IsValid 属性（s_Handler != null）和 Handler 属性（get/set）。</para>
    /// <para>工厂契约三档：① 同时声明两者——懒加载优先调用 <c>GetHandlerFromSettings</c>，
    /// 返回 null 回退到 <c>CreateDefaultHandler</c>；② 仅声明 <c>CreateDefaultHandler</c>——懒加载直接调用工厂；
    /// ③ 仅声明 <c>GetHandlerFromSettings</c>（settings-only，MIRAI102 Info 提示）——懒加载调用它并要求返回非空值，
    /// 返回 null 即抛出 <see cref="InvalidOperationException"/>。</para>
    /// <para>两者都未声明时（MIRAI101 警告）访问 <c>Handler</c> 抛出异常；显式 setter 赋值始终可用。</para>
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
