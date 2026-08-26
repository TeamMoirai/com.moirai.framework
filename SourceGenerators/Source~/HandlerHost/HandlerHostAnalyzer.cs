using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 诊断分析器：检查 [HandlerHost] 标记的类是否提供了 CreateDefaultHandler 方法。
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class HandlerHostAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor MissingCreateDefaultHandlerRule = new DiagnosticDescriptor(
            id: "MIRAI001",
            title: "缺少 CreateDefaultHandler 方法",
            messageFormat: "[HandlerHost] 标记的类 '{0}' 未提供 'private static {1} CreateDefaultHandler()' 方法，未显式设置 Handler 前访问将抛出运行时异常",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "CreateDefaultHandler is the fallback factory when Handler is accessed without explicit assignment. Without it, Handler.get throws InvalidOperationException at runtime.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(MissingCreateDefaultHandlerRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSymbolAction(AnalyzeTypeSymbol, SymbolKind.NamedType);
        }

        private static void AnalyzeTypeSymbol(SymbolAnalysisContext context)
        {
            var typeSymbol = (INamedTypeSymbol)context.Symbol;

            // 仅检查 class
            if (typeSymbol.TypeKind != TypeKind.Class)
                return;

            // 查找 [HandlerHost] attribute
            var handlerHostAttr = typeSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == Constants.AttributeFullyQualifiedName);

            if (handlerHostAttr == null)
                return;

            // 从 attribute 获取 HandlerType
            if (handlerHostAttr.ConstructorArguments.Length == 0)
                return;

            var handlerType = handlerHostAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
            if (handlerType == null)
                return;

            // 检查是否声明了 CreateDefaultHandler 方法
            var method = typeSymbol.GetMembers(Constants.CreateDefaultHandlerMethodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.IsStatic
                    && !m.IsAbstract
                    && SymbolEqualityComparer.Default.Equals(m.ReturnType, handlerType)
                    && m.Parameters.IsEmpty);

            if (method != null)
                return;

            // 未找到方法，报告诊断
            var location = typeSymbol.Locations.FirstOrDefault();
            if (location == null)
                return;

            var diagnostic = Diagnostic.Create(
                MissingCreateDefaultHandlerRule,
                location,
                ImmutableDictionary<string, string?>.Empty.Add("HandlerTypeName", handlerType.Name),
                typeSymbol.Name,
                handlerType.Name);

            context.ReportDiagnostic(diagnostic);
        }
    }
}
