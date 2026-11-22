using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 标记处理器宿主（服务门面 / 工具门面），由源生成器自动生成 <c>Handler</c> 缓存属性与线程安全懒加载链。
    /// </summary>
    /// <remarks>
    /// 生成 <c>s_Handler</c> 缓存槽（private，partial 同类可访问）、<c>IsValid</c>、<c>Handler</c>（get/set）、 <br />
    /// <c>InvalidateHandler(handler)</c>（缓存失效钩子，处理器 OnShutdown 调用）与 <c>RequireHandler</c>（不触发懒加载， <br />
    /// 未就绪抛 <see cref="GameException"/>，写路径 fail-fast 入口）。 <br />
    /// 接口契约 = 服务后端：绑定即注册——懒链解析出实例后注册进 <see cref="GameServices"/>（契约绑定，生命周期由容器拓扑驱动）， <br />
    /// 缓存回填注册结果（槽位 ≡ 容器实例）；组合根经生成的 <c>RegisterAll</c> 调 <c>WarmHandler</c> 预热入图。 <br />
    /// 懒链只在世界已初始化或初始化在途的窗口放行（未建/未初始化/已关闭一律抛 <see cref="GameException"/>）； <br />
    /// 装配路径登记清槽回调（每条装配路径登记一次），世界关停时由内核推送落槽——读侧不回查容器。 <br />
    /// 类契约 = 工具处理器：沿用直驱生命周期（Internal_Init/Internal_Shutdown），不进容器也不登记世界清槽。 <br />
    /// 默认工厂三档：声明 <see cref="DefaultHandlerType"/>（第二构造参数）时生成器生成 <c>CreateDefaultHandler</c> 兜底工厂， <br />
    /// 非法声明（未实现处理器契约/生命周期基座不符/抽象/无可访问无参构造）报 MA1003；手写 <c>CreateDefaultHandler</c> 仍合法（引擎宏分派宿主）， <br />
    /// 与声明默认共存时手写生效、声明冗余（MA1004）；懒加载先调 <c>GetHandlerFromSettings</c>、返回 null 回退默认工厂。 <br />
    /// 工厂全缺（MA1001）时访问 <c>Handler</c> 抛异常；仅 <c>GetHandlerFromSettings</c>（MA1002）必须返回非空，null 即抛 <see cref="InvalidOperationException"/>。显式 setter 赋值始终可用。
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
        /// <summary>
        /// 处理器契约类型（类或接口）：生成代码的 <c>s_Handler</c>/<c>Handler</c> 等成员按此类型化。 <br />
        /// 接口契约（服务后端）时默认实现须继承 <see cref="ServiceBase"/>（绑定即注册进容器，装配路径只此一条）， <br />
        /// 类契约（工具处理器）须继承 <see cref="FrameworkHandler"/>。
        /// </summary>
        public Type HandlerType { get; }

        /// <summary>
        /// 声明的默认处理器类型：生成器据此生成 <c>CreateDefaultHandler</c>；须实现处理器契约（类契约继承、接口契约实现） <br />
        /// 并继承对应生命周期基座（接口契约 → <see cref="ServiceBase"/>，类契约 → <see cref="FrameworkHandler"/>）； <br />
        /// null 表示不生成（手写工厂或 settings-only 宿主）。
        /// </summary>
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
