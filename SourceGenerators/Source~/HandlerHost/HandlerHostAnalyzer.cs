using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 诊断分析器：检查 [HandlerHost] 标记的类的 Handler 工厂方法契约。
    /// <para>MIRAI101（Warning）：CreateDefaultHandler 与 GetHandlerFromSettings 均未提供——懒加载无来源。</para>
    /// <para>MIRAI102（Info）：仅提供 GetHandlerFromSettings（settings-only）——懒加载依赖其返回非空值。</para>
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class HandlerHostAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor MissingHandlerFactoryRule = new DiagnosticDescriptor(
            id: "MIRAI101",
            title: "缺少 Handler 工厂方法",
            messageFormat: "[HandlerHost] 标记的类 '{0}' 未提供 'private static {1} CreateDefaultHandler()' 或 'private static {1} GetHandlerFromSettings()' 方法，未显式设置 Handler 前访问将抛出运行时异常",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Provide at least one handler factory: CreateDefaultHandler (code default, used as fallback when GetHandlerFromSettings returns null) or GetHandlerFromSettings (settings-driven source). Without either, Handler.get throws InvalidOperationException at runtime.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        public static readonly DiagnosticDescriptor SettingsOnlyHandlerRule = new DiagnosticDescriptor(
            id: "MIRAI102",
            title: "Handler 懒加载依赖 GetHandlerFromSettings",
            messageFormat: "[HandlerHost] 标记的类 '{0}' 仅提供 GetHandlerFromSettings（未提供 CreateDefaultHandler）：Handler 懒加载将调用它并要求返回非空值，settings 未配置或返回 null 时访问 Handler 将抛出运行时异常",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "Settings-only HandlerHost: lazy loading resolves the handler exclusively via GetHandlerFromSettings. A null return (e.g. settings field not configured) fails fast with InvalidOperationException. Adding a CreateDefaultHandler turns this into a settings-first / code-fallback contract.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(MissingHandlerFactoryRule, SettingsOnlyHandlerRule);

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

            // 工厂契约检查：CreateDefaultHandler（代码兜底）/ GetHandlerFromSettings（settings 来源）至少其一
            var hasFactory = HasHandlerFactoryMethod(typeSymbol, Constants.CreateDefaultHandlerMethodName, handlerType);
            var hasSettingsSource = HasHandlerFactoryMethod(typeSymbol, Constants.GetHandlerFromSettingsMethodName, handlerType);

            if (hasFactory && hasSettingsSource)
                return;

            // 诊断定位到 [HandlerHost] 特性处（partial 类的 typeSymbol.Locations 可能落在无关分部文件，用户难以对应到自己的修改）
            var location = handlerHostAttr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? typeSymbol.Locations.FirstOrDefault();
            if (location == null)
                return;

            if (hasFactory)
            {
                // 仅代码工厂：懒加载走 CreateDefaultHandler，无诊断
                return;
            }

            if (hasSettingsSource)
            {
                // settings-only：懒加载唯一来源是 GetHandlerFromSettings，提示其非空契约
                var settingsOnlyDiagnostic = Diagnostic.Create(
                    SettingsOnlyHandlerRule,
                    location,
                    ImmutableDictionary<string, string?>.Empty.Add("HandlerTypeName", handlerType.Name),
                    typeSymbol.Name,
                    handlerType.Name);

                context.ReportDiagnostic(settingsOnlyDiagnostic);
                return;
            }

            // 两者都缺：懒加载无来源，报告警告
            var diagnostic = Diagnostic.Create(
                MissingHandlerFactoryRule,
                location,
                ImmutableDictionary<string, string?>.Empty.Add("HandlerTypeName", handlerType.Name),
                typeSymbol.Name,
                handlerType.Name);

            context.ReportDiagnostic(diagnostic);
        }

        private static bool HasHandlerFactoryMethod(INamedTypeSymbol typeSymbol, string methodName, INamedTypeSymbol handlerType)
        {
            return typeSymbol.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Any(m => m.IsStatic
                    && !m.IsAbstract
                    && SymbolEqualityComparer.Default.Equals(m.ReturnType, handlerType)
                    && m.Parameters.IsEmpty);
        }
    }
}
