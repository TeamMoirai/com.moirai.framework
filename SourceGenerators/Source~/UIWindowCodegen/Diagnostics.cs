using Microsoft.CodeAnalysis;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// UIWindowCodegen 诊断描述符（MIRAI5xx 系列，与 ServiceDependency 的 1xx/2xx、Save 的 3xx/4xx 错开）。
    /// </summary>
    internal static class Diagnostics
    {
        /// <summary>诊断类别。</summary>
        private const string Category = "UI";

        /// <summary>MIRAI500：[Window] 标在非 UIWindow 派生类上。</summary>
        public static readonly DiagnosticDescriptor WindowAttributeOnNonWindow = new(
            "MIRAI500",
            "[Window] 标在了非窗口类上",
            "'{0}' 标了 [Window] 但不从 Moirai.Atropos.UI.UIWindow 派生：特性只登记窗口类，请移除或改挂",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI501：[Window] 窗口类缺公共无参构造。</summary>
        public static readonly DiagnosticDescriptor WindowMissingPublicParameterlessCtor = new(
            "MIRAI501",
            "[Window] 窗口类缺公共无参构造函数",
            "窗口类 '{0}' 必须提供 public 无参构造函数：注册表工厂按 new {0}() 生成实例",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI502：[Window] 窗口类是抽象类或泛型类。</summary>
        public static readonly DiagnosticDescriptor WindowAbstractOrGeneric = new(
            "MIRAI502",
            "[Window] 窗口类不可实例化",
            "窗口类 '{0}' 不得为抽象类或泛型类：注册表按类型登记可实例化的窗口",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI503：[Window] 窗口类嵌套在私有类型内。</summary>
        public static readonly DiagnosticDescriptor WindowNestedInaccessible = new(
            "MIRAI503",
            "[Window] 窗口类嵌套在私有类型内",
            "窗口类 '{0}' 及其外层类型链不得为 private：生成的模块初始化器够不到它，改为 internal 或公开、或移出嵌套",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
