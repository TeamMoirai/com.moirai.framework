using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 诊断分析器：检查 <c>[ProviderHandlerHost]</c> 标记的类的 Handler 工厂方法契约与默认处理器声明。
    /// </summary>
    /// <remarks>
    /// MOIRAI101（Warning）：默认工厂与 settings 来源均缺——懒加载无来源。 <br />
    /// MOIRAI102（Info）：仅提供 <c>GetHandlerFromSettings</c>（settings-only）——懒加载依赖其返回非空值。 <br />
    /// MOIRAI103（Error）：声明的默认处理器不可实例化（未继承契约/抽象/无可访问无参构造）。 <br />
    /// MOIRAI104（Info）：声明默认与手写工厂并存——手写生效、声明不生成，提示删除其一。
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ProviderHandlerHostAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor MissingHandlerFactoryRule = new DiagnosticDescriptor(
            id: "MOIRAI101",
            title: "缺少 Handler 工厂来源",
            messageFormat: "[ProviderHandlerHost] 标记的类 '{0}' 未声明默认处理器（第二构造参数）、未提供 'internal static {1} CreateDefaultHandler()'，也未提供 'private static {1} GetHandlerFromSettings()'，未显式设置 Handler 前访问将抛出运行时异常",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Provide at least one handler factory source: a declared default handler type (second constructor argument, the generator emits CreateDefaultHandler), a hand-written CreateDefaultHandler, or GetHandlerFromSettings. Without any, Handler.get throws InvalidOperationException at runtime.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        public static readonly DiagnosticDescriptor SettingsOnlyHandlerRule = new DiagnosticDescriptor(
            id: "MOIRAI102",
            title: "Handler 懒加载依赖 GetHandlerFromSettings",
            messageFormat: "[ProviderHandlerHost] 标记的类 '{0}' 仅提供 GetHandlerFromSettings（未声明默认处理器也未提供 CreateDefaultHandler）：Handler 懒加载将调用它并要求返回非空值，settings 未配置或返回 null 时访问 Handler 将抛出运行时异常",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "Settings-only ProviderHandlerHost: lazy loading resolves the handler exclusively via GetHandlerFromSettings. A null return (e.g. settings field not configured) fails fast with InvalidOperationException. Declaring a default handler type or adding a CreateDefaultHandler turns this into a settings-first / code-fallback contract.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        public static readonly DiagnosticDescriptor InvalidDeclaredDefaultRule = new DiagnosticDescriptor(
            id: "MOIRAI103",
            title: "声明的默认处理器不可实例化",
            messageFormat: "[ProviderHandlerHost] 声明的默认处理器 '{0}' 不能作为 '{1}' 的兜底工厂：{2}；生成器不会为它生成 CreateDefaultHandler",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The declared default handler type must derive from the handler contract, be a concrete class, and expose an accessible parameterless constructor. Otherwise no CreateDefaultHandler is generated and lazy loading falls back to the remaining factory sources.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        public static readonly DiagnosticDescriptor RedundantDeclaredDefaultRule = new DiagnosticDescriptor(
            id: "MOIRAI104",
            title: "声明默认处理器与手写工厂冗余",
            messageFormat: "[ProviderHandlerHost] 标记的类 '{0}' 同时声明默认处理器 '{1}' 与手写 CreateDefaultHandler——手写工厂生效、声明不会生成兜底工厂，请删除其一",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "A hand-written CreateDefaultHandler takes precedence over the declared default handler type. Keep the declaration (for generated factory) or the hand-written method (for engine-define dispatch), not both.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(MissingHandlerFactoryRule, SettingsOnlyHandlerRule, InvalidDeclaredDefaultRule, RedundantDeclaredDefaultRule);

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

            // 查找 [ProviderHandlerHost] attribute
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

            var declaredDefault = handlerHostAttr.ConstructorArguments.Length >= 2
                ? handlerHostAttr.ConstructorArguments[1].Value as INamedTypeSymbol
                : null;

            // 工厂契约检查：CreateDefaultHandler（手写兜底）/ 声明默认（生成兜底）/ GetHandlerFromSettings（settings 来源）至少其一。
            // 分析器跑在生成代码并入之后的编译上，生成器发的 CreateDefaultHandler 也在 GetMembers 里——须按源文件排除，只认手写
            var hasFactory = HasUserHandlerFactoryMethod(typeSymbol, Constants.CreateDefaultHandlerMethodName, handlerType);
            var hasSettingsSource = HasUserHandlerFactoryMethod(typeSymbol, Constants.GetHandlerFromSettingsMethodName, handlerType);

            // 诊断定位到 [ProviderHandlerHost] 特性处（partial 类的 typeSymbol.Locations 可能落在无关分部文件，用户难以对应到自己的修改）
            var location = handlerHostAttr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? typeSymbol.Locations.FirstOrDefault();
            if (location == null)
                return;

            if (declaredDefault != null)
            {
                // 与宿主同程序集时 internal/protected internal 亦可见；跨程序集仅 public 可实例化
                var sameAssembly = SymbolEqualityComparer.Default.Equals(declaredDefault.ContainingAssembly, typeSymbol.ContainingAssembly);
                if (!HandlerContract.IsValidDefaultHandler(declaredDefault, handlerType, sameAssembly, out var reasons))
                {
                    // 非法声明：报 103 即可，工厂缺失（101）与 settings-only（102）不再叠报
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidDeclaredDefaultRule,
                        location,
                        ImmutableDictionary<string, string?>.Empty
                            .Add("HandlerTypeName", handlerType.Name)
                            .Add("DeclaredDefaultTypeName", declaredDefault.Name),
                        declaredDefault.Name,
                        handlerType.Name,
                        reasons));
                    return;
                }

                if (hasFactory)
                {
                    // 手写工厂生效、声明默认不生成兜底工厂——提示删除其一
                    context.ReportDiagnostic(Diagnostic.Create(
                        RedundantDeclaredDefaultRule,
                        location,
                        ImmutableDictionary<string, string?>.Empty
                            .Add("HandlerTypeName", handlerType.Name)
                            .Add("DeclaredDefaultTypeName", declaredDefault.Name),
                        typeSymbol.Name,
                        declaredDefault.Name));
                    return;
                }

                // 声明默认合法且无手写：生成器提供兜底工厂，无诊断
                return;
            }

            if (hasFactory)
            {
                // 手写代码工厂：懒加载走 CreateDefaultHandler，无诊断
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

            // 工厂来源全缺：懒加载无来源，报告警告
            var diagnostic = Diagnostic.Create(
                MissingHandlerFactoryRule,
                location,
                ImmutableDictionary<string, string?>.Empty.Add("HandlerTypeName", handlerType.Name),
                typeSymbol.Name,
                handlerType.Name);

            context.ReportDiagnostic(diagnostic);
        }

        private static bool HasUserHandlerFactoryMethod(INamedTypeSymbol typeSymbol, string methodName, INamedTypeSymbol handlerType)
        {
            return typeSymbol.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Where(m => m.IsStatic
                    && !m.IsAbstract
                    && SymbolEqualityComparer.Default.Equals(m.ReturnType, handlerType)
                    && m.Parameters.IsEmpty)
                .Any(m => !IsInGeneratedSource(m));
        }

        /// <summary>生成代码经源生成器产出、文件名以 .g.cs 结尾；手写工厂判定须排除它们，否则生成兜底会被误认成手写。</summary>
        private static bool IsInGeneratedSource(IMethodSymbol method)
        {
            var location = method.Locations.FirstOrDefault();
            return location?.SourceTree != null
                && location.SourceTree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal);
        }
    }
}
