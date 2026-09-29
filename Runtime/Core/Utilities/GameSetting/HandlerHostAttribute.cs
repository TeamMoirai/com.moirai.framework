using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 标记静态类为处理器宿主，由源生成器自动生成 <c>Handler</c> 属性和线程安全懒加载。
    /// </summary>
    /// <remarks>
    /// 生成 <c>s_Handler</c> 字段（private，partial 同类可访问）、<c>IsValid</c>、<c>Handler</c>（get/set）与 <c>RequireHandler</c>（不触发懒加载， <br />
    /// 未就绪抛 <see cref="GameException"/>，写路径 fail-fast 入口）。
    /// 工厂契约三档：两者皆声明时懒加载先调 <c>GetHandlerFromSettings</c>、返回 null 回退 <c>CreateDefaultHandler</c>；仅声明后者则直接调用； <br />
    /// 仅声明前者（MIRAI102）必须返回非空，null 即抛 <see cref="InvalidOperationException"/>。
    /// 两者都未声明（MIRAI101）时访问 <c>Handler</c> 抛异常；显式 setter 赋值始终可用。
    /// </remarks>
    /// <example>
    /// <code>
    /// [HandlerHost(typeof(LogHandler))]
    /// public static partial class LogUtility
    /// {
    ///     private static LogHandler GetHandlerFromSettings() => ...; // 可选：优先从 FrameworkSettings 获取
    ///     private static LogHandler CreateDefaultHandler() => new DefaultLogHandler();
    /// }
    /// </code>
    /// </example>
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
