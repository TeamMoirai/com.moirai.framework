using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 标记静态类为处理器宿主，由源生成器自动生成 <c>Handler</c> 属性和线程安全懒加载。
    /// </summary>
    /// <remarks>
    /// 生成 <c>s_Handler</c> 字段（private，partial 同类可访问）、<c>IsValid</c>、<c>Handler</c>（get/set）与 <c>RequireHandler</c>（不触发懒加载， <br />
    /// 未就绪抛 <see cref="GameException"/>，写路径 fail-fast 入口）。 <br />
    /// 默认工厂三档：声明 <see cref="DefaultHandlerType"/>（第二构造参数）时生成器生成 <c>CreateDefaultHandler</c> 兜底工厂， <br />
    /// 非法声明（未继承处理器类型/抽象/无可访问无参构造）报 MOIRAI103；手写 <c>CreateDefaultHandler</c> 仍合法（引擎宏分派宿主）， <br />
    /// 与声明默认共存时手写生效、声明冗余（MOIRAI104）；懒加载先调 <c>GetHandlerFromSettings</c>、返回 null 回退默认工厂。 <br />
    /// 工厂全缺（MOIRAI101）时访问 <c>Handler</c> 抛异常；仅 <c>GetHandlerFromSettings</c>（MOIRAI102）必须返回非空，null 即抛 <see cref="InvalidOperationException"/>。显式 setter 赋值始终可用。
    /// </remarks>
    /// <example>
    /// <code>
    /// [ProviderHandlerHost(typeof(LogHandler), typeof(DefaultLogHandler))]
    /// public static partial class LogUtility
    /// {
    ///     private static LogHandler GetHandlerFromSettings() => ...; // 可选：settings 优先，返回 null 回退声明的默认处理器
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ProviderHandlerHostAttribute : Attribute
    {
        /// <summary>处理器类型，必须继承 <see cref="FrameworkHandler"/>。</summary>
        public Type HandlerType { get; }

        /// <summary>声明的默认处理器类型：生成器据此生成 <c>CreateDefaultHandler</c>；null 表示不生成（手写工厂或 settings-only 宿主）。</summary>
        public Type DefaultHandlerType { get; }

        /// <param name="handlerType">处理器类型。</param>
        public ProviderHandlerHostAttribute(Type handlerType)
            : this(handlerType, null)
        {
        }

        /// <param name="handlerType">处理器类型。</param>
        /// <param name="defaultHandlerType">默认处理器类型：须继承处理器类型、非抽象且含可访问的无参构造。</param>
        public ProviderHandlerHostAttribute(Type handlerType, Type defaultHandlerType)
        {
            HandlerType = handlerType;
            DefaultHandlerType = defaultHandlerType;
        }
    }
}
