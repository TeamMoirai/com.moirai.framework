using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 诊断分析器：检查 [ServiceDependency] 声明的依赖类型是否实现了 IService。
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ServiceDependencyAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// MIRAI002: ServiceDependency 依赖类型未实现 IService。
        /// </summary>
        public static readonly DiagnosticDescriptor DependencyMustImplementIServiceRule = new DiagnosticDescriptor(
            id: "MIRAI002",
            title: "ServiceDependency 依赖类型未实现 IService",
            messageFormat: "[ServiceDependency] 声明的类型 '{0}' 未实现 'Moirai.Atropos.IService'，无法作为服务依赖注册",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "All types passed to [ServiceDependency] must implement IService. Non-IService types cannot be registered via the service container.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        /// <summary>
        /// MIRAI003: ServiceDependency 未声明任何依赖类型。
        /// </summary>
        public static readonly DiagnosticDescriptor EmptyDependencyRule = new DiagnosticDescriptor(
            id: "MIRAI003",
            title: "ServiceDependency 未声明依赖类型",
            messageFormat: "[ServiceDependency] 未声明任何依赖类型，至少需要一个实现 IService 的类型",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "At least one dependency type must be specified.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        private const string IServiceFullyQualifiedName = "Moirai.Atropos.IService";
        private const string ServiceDependencyAttributeFullyQualifiedName = "Moirai.Atropos.ServiceDependencyAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DependencyMustImplementIServiceRule, EmptyDependencyRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSymbolAction(AnalyzeTypeSymbol, SymbolKind.NamedType);
        }

        private static void AnalyzeTypeSymbol(SymbolAnalysisContext context)
        {
            var typeSymbol = (INamedTypeSymbol)context.Symbol;

            if (typeSymbol.TypeKind != TypeKind.Class)
                return;

            // 查找所有 [ServiceDependency] 特性
            var attrs = typeSymbol.GetAttributes()
                .Where(a => a.AttributeClass?.ToDisplayString() == ServiceDependencyAttributeFullyQualifiedName)
                .ToList();

            if (attrs.Count == 0)
                return;

            // IService 符号（用于 IsAssignable 检查）
            INamedTypeSymbol? iServiceSymbol = null;

            foreach (var attr in attrs)
            {
                if (attr.ConstructorArguments.Length == 0)
                {
                    ReportDiagnostic(context, attr, typeSymbol, EmptyDependencyRule, "<none>");
                    continue;
                }

                // params Type[] → ConstructorArguments[0] 是数组
                var arg = attr.ConstructorArguments[0];

                // 单个类型：arg.Kind == TypedConstantKind.Type
                // 多个类型：arg.Kind == TypedConstantKind.Array
                if (arg.Kind == TypedConstantKind.Array)
                {
                    if (arg.Values.IsEmpty)
                    {
                        ReportDiagnostic(context, attr, typeSymbol, EmptyDependencyRule, "<none>");
                        continue;
                    }

                    foreach (var element in arg.Values)
                    {
                        CheckType(context, attr, typeSymbol, element, ref iServiceSymbol);
                    }
                }
                else if (arg.Kind == TypedConstantKind.Type)
                {
                    CheckType(context, attr, typeSymbol, arg, ref iServiceSymbol);
                }
            }
        }

        private static void CheckType(
            SymbolAnalysisContext context,
            AttributeData attr,
            INamedTypeSymbol typeSymbol,
            TypedConstant element,
            ref INamedTypeSymbol? iServiceSymbol)
        {
            if (element.Value is not INamedTypeSymbol depType)
                return;

            // 懒加载 IService 符号
            if (iServiceSymbol == null)
            {
                iServiceSymbol = context.Compilation.GetTypeByMetadataName(IServiceFullyQualifiedName);
                if (iServiceSymbol == null)
                    return; // 找不到 IService 定义，跳过（可能未引用框架程序集）
            }

            // 检查 depType 是否实现了 IService
            bool implementsIService = ImplementsInterface(depType, iServiceSymbol);
            if (!implementsIService)
            {
                ReportDiagnostic(context, attr, typeSymbol, DependencyMustImplementIServiceRule, depType.ToDisplayString());
            }
        }

        private static bool ImplementsInterface(ITypeSymbol typeSymbol, INamedTypeSymbol interfaceSymbol)
        {
            if (SymbolEqualityComparer.Default.Equals(typeSymbol, interfaceSymbol))
                return true;

            if (typeSymbol.BaseType != null && ImplementsInterface(typeSymbol.BaseType, interfaceSymbol))
                return true;

            foreach (var iface in typeSymbol.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface, interfaceSymbol))
                    return true;
            }

            return false;
        }

        private static void ReportDiagnostic(
            SymbolAnalysisContext context,
            AttributeData attr,
            INamedTypeSymbol typeSymbol,
            DiagnosticDescriptor rule,
            string argName)
        {
            var location = attr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                            ?? typeSymbol.Locations.FirstOrDefault();

            if (location == null)
                return;

            context.ReportDiagnostic(Diagnostic.Create(rule, location, argName));
        }
    }
}
