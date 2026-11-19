using Microsoft.CodeAnalysis;

namespace Moirai.EventBus.Roslyn;

/// <summary>
/// 诊断描述符集中定义：MA7000–MA7003（EventBus 使用规范，7xx 段——与 ServiceDependency 1xx/2xx、Save 3xx/4xx、UI 5xx、HotPath 6xx 错开）。
/// 默认严重级别均为 Warning——可通过 .editorconfig 中 <c>dotnet_diagnostic.MA7xxx.severity</c> 升级为 error / 降级为 none。
/// </summary>
internal static class Diagnostics
{
    public const string Category = "Moirai.EventBus";

    public const string AnonymousHandlerId = "MA7000";
    public const string InParameterRequiredId = "MA7001";
    public const string CallbackNameMismatchId = "MA7002";
    public const string PartialOwnerRequiredId = "MA7003";

    /// <summary>MA7000：EventBus.Subscribe/Unsubscribe 传入匿名函数。</summary>
    public static readonly DiagnosticDescriptor AnonymousHandler = new(
        AnonymousHandlerId,
        title: "禁止以匿名函数订阅或退订 EventBus 事件",
        messageFormat: "EventBus.{0} 不能传入匿名函数（lambda/匿名方法）——匿名委托每次生成新实例，Unsubscribe 的值等价匹配（同方法同目标）无法命中，将导致订阅泄漏。请提取为命名方法（约定形如 OnXxxEvent）。",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "EventBus 的订阅/退订采用对称方法形态（值等价匹配），匿名函数无法被 Unsubscribe 命中。");

    /// <summary>MA7001：OnXxxEvent 带参回调参数缺少 in 修饰。</summary>
    public static readonly DiagnosticDescriptor InParameterRequired = new(
        InParameterRequiredId,
        title: "OnXxxEvent 带参回调必须以 in 修饰事件参数",
        messageFormat: "事件回调 '{0}' 的参数必须以 in 修饰——OnXxxEvent(in XxxEvent evt)，避免 struct 事件按值拷贝。",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "EventDelegate<TEvent> 的参数为 in 修饰；回调按值传参会对 struct 事件产生拷贝开销。");

    /// <summary>MA7002：OnXxxEvent 参数类型与方法名后缀不匹配。</summary>
    public static readonly DiagnosticDescriptor CallbackNameMismatch = new(
        CallbackNameMismatchId,
        title: "OnXxxEvent 参数类型与方法名约定不一致",
        messageFormat: "事件回调 '{0}' 的参数类型 '{1}' 与方法名约定的事件类型 '{2}' 不一致——请统一为 OnXxxEvent(in {2} evt) 或调整命名。",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "OnXxxEvent 命名约定要求参数类型与方法名后缀一一对应。");

    /// <summary>MA7003：无参 OnXxxEvent 所在类型未声明 partial，Source Generator 无法生成订阅胶水。</summary>
    public static readonly DiagnosticDescriptor PartialOwnerRequired = new(
        PartialOwnerRequiredId,
        title: "无参事件回调所在类型应声明 partial",
        messageFormat: "类型 '{0}' 未声明 partial——无参事件回调 '{1}' 仅能由 Source Generator 生成的订阅胶水桥接为 EventDelegate<{2}>，请为类型添加 partial 声明后使用生成的 SubscribeEvents()/UnsubscribeEvents()。",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "无参回调无法直接转换为 EventDelegate<T>，必须经生成器缓存包装桥接，而生成器只能注入 partial 类型。");
}
