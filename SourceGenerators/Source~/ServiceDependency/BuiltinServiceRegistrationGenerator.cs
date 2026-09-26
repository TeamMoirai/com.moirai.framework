using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 推送式服务注册源生成器：收集当前编译单元内全部标记 [AutoRegisterService] 的服务类，
    /// 生成程序集级 internal 清单类 <c>BuiltinServiceRegistration.RegisterAll(ServiceWorld)</c>，
    /// 供组合根调用——替代手写的逐服务 RegisterService 调用。
    /// <para>初始化顺序仍由 [ServiceDependency] 依赖图在世界初始化时拓扑排序决定，与注册顺序无关。</para>
    /// <para>无有效标记类型时不产出任何源文件（避免空清单类污染其他程序集）。</para>
    /// </summary>
    [Generator]
    public class BuiltinServiceRegistrationGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// MIRAI203: AutoRegisterService 目标类型未实现 IService。
        /// </summary>
        private static readonly DiagnosticDescriptor s_MustImplementIServiceRule = new DiagnosticDescriptor(
            id: "MIRAI203",
            title: "AutoRegisterService 目标类型未实现 IService",
            messageFormat: "[AutoRegisterService] 标记的类型 '{0}' 未实现 'Moirai.Atropos.IService'，无法作为服务注册",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Types marked with [AutoRegisterService] must implement IService.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        /// <summary>
        /// MIRAI204: AutoRegisterService 目标类型形状非法（抽象/泛型/静态/缺可访问无参构造）。
        /// </summary>
        private static readonly DiagnosticDescriptor s_InvalidTargetShapeRule = new DiagnosticDescriptor(
            id: "MIRAI204",
            title: "AutoRegisterService 目标类型形状非法",
            messageFormat: "[AutoRegisterService] 标记的类型 '{0}' 必须是非抽象、非泛型且具有可访问无参构造函数的类（{1}）",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Auto-registration instantiates the service via a parameterless constructor; abstract, static, generic or ctor-less types are rejected.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        /// <summary>
        /// MIRAI205: AutoRegisterService 作用域值非法。
        /// </summary>
        private static readonly DiagnosticDescriptor s_InvalidScopeRule = new DiagnosticDescriptor(
            id: "MIRAI205",
            title: "AutoRegisterService 作用域值非法",
            messageFormat: "[AutoRegisterService] 标记的类型 '{0}' 声明的作用域值 {1} 超出 EServiceScopeKind 合法范围 [App, Scene, Gameplay]",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The scope value must be a valid EServiceScopeKind member.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        private const string AttributeFullyQualifiedName = "Moirai.Atropos.AutoRegisterServiceAttribute";
        private const string IServiceFullyQualifiedName = "Moirai.Atropos.IService";

        private static readonly string[] s_ScopeNames = { "App", "Scene", "Gameplay" };

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var provider = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    AttributeFullyQualifiedName,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, _) => AnalyzeTarget(ctx));

            var collected = provider.Collect();

            context.RegisterSourceOutput(collected, static (spc, infos) =>
            {
                // 无标记类型时不产出源文件
                if (infos.IsDefaultOrEmpty)
                    return;

                var valid = new List<ServiceRegistrationInfo>(infos.Length);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var info in infos)
                {
                    foreach (var diagnostic in info.Diagnostics)
                        spc.ReportDiagnostic(diagnostic);

                    // partial 多处声明重复贴特性会去重（注册以类型为键，首个生效）
                    if (info.IsValid && seen.Add(info.FullyQualifiedName))
                        valid.Add(info);
                }

                if (valid.Count == 0)
                    return;

                // 确定性输出：先按作用域序、再按全限定名字典序（避免生成内容随编译顺序抖动）
                valid.Sort(static (a, b) =>
                {
                    int byScope = a.ScopeValue.CompareTo(b.ScopeValue);
                    return byScope != 0
                        ? byScope
                        : string.CompareOrdinal(a.FullyQualifiedName, b.FullyQualifiedName);
                });

                spc.AddSource("BuiltinServiceRegistration.g.cs", GenerateCode(valid));
            });
        }

        private static ServiceRegistrationInfo AnalyzeTarget(GeneratorAttributeSyntaxContext ctx)
        {
            var classDecl = (ClassDeclarationSyntax)ctx.TargetNode;
            var classSymbol = (INamedTypeSymbol)ctx.SemanticModel.GetDeclaredSymbol(classDecl)!;

            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>(0);
            bool isValid = true;

            var attr = ctx.Attributes[0];
            int scopeValue = 0;
            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value != null)
                scopeValue = Convert.ToInt32(attr.ConstructorArguments[0].Value);

            var location = attr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                           ?? classDecl.Identifier.GetLocation();
            string displayName = classSymbol.ToDisplayString();

            if (scopeValue < 0 || scopeValue >= s_ScopeNames.Length)
            {
                diagnostics.Add(Diagnostic.Create(s_InvalidScopeRule, location, displayName, scopeValue));
                isValid = false;
            }

            if (!ImplementsIService(classSymbol, ctx.SemanticModel.Compilation))
            {
                diagnostics.Add(Diagnostic.Create(s_MustImplementIServiceRule, location, displayName));
                isValid = false;
            }

            string? shapeError = ValidateShape(classSymbol);
            if (shapeError != null)
            {
                diagnostics.Add(Diagnostic.Create(s_InvalidTargetShapeRule, location, displayName, shapeError));
                isValid = false;
            }

            return new ServiceRegistrationInfo(
                classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                scopeValue,
                isValid,
                diagnostics.ToImmutable());
        }

        private static bool ImplementsIService(INamedTypeSymbol classSymbol, Compilation compilation)
        {
            var iServiceSymbol = compilation.GetTypeByMetadataName(IServiceFullyQualifiedName);
            if (iServiceSymbol == null)
                return false;

            foreach (var iface in classSymbol.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface, iServiceSymbol))
                    return true;
            }

            return false;
        }

        private static string? ValidateShape(INamedTypeSymbol classSymbol)
        {
            if (classSymbol.IsStatic)
                return "静态类无法实例化";
            if (classSymbol.IsAbstract)
                return "抽象类无法实例化";
            if (classSymbol.IsGenericType)
                return "泛型类型无法以无参构造实例化";

            bool hasAccessibleParameterlessCtor = false;
            foreach (var ctor in classSymbol.InstanceConstructors)
            {
                if (!ctor.Parameters.IsEmpty)
                    continue;
                if (ctor.DeclaredAccessibility == Accessibility.Public ||
                    ctor.DeclaredAccessibility == Accessibility.Internal ||
                    ctor.DeclaredAccessibility == Accessibility.ProtectedOrInternal)
                {
                    hasAccessibleParameterlessCtor = true;
                    break;
                }
            }

            if (!hasAccessibleParameterlessCtor)
                return "缺少可访问的无参构造函数";

            return null;
        }

        private static string GenerateCode(List<ServiceRegistrationInfo> infos)
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//   BuiltinServiceRegistration Source Generator — do not edit manually.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("namespace Moirai.Atropos");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 内置服务注册清单（按 [AutoRegisterService] 标记由源生成器生成，勿手改）。");
            sb.AppendLine("    /// <para>仅负责注册（两阶段第一阶段：入图）；OnInit 顺序由 [ServiceDependency] 依赖图拓扑排序决定。</para>");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    internal static class BuiltinServiceRegistration");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 将全部标记服务注册到指定世界（仅入图，OnInit 由世界初始化拓扑统一驱动）。");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"world\">目标服务世界。</param>");
            sb.AppendLine("        internal static void RegisterAll(ServiceWorld world)");
            sb.AppendLine("        {");

            foreach (var info in infos)
            {
                sb.Append("            world.Register(EServiceScopeKind.");
                sb.Append(s_ScopeNames[info.ScopeValue]);
                sb.Append(", new ");
                sb.Append(info.FullyQualifiedName);
                sb.AppendLine("());");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private sealed class ServiceRegistrationInfo
        {
            public readonly string FullyQualifiedName;
            public readonly int ScopeValue;
            public readonly bool IsValid;
            public readonly ImmutableArray<Diagnostic> Diagnostics;

            public ServiceRegistrationInfo(
                string fullyQualifiedName,
                int scopeValue,
                bool isValid,
                ImmutableArray<Diagnostic> diagnostics)
            {
                FullyQualifiedName = fullyQualifiedName;
                ScopeValue = scopeValue;
                IsValid = isValid;
                Diagnostics = diagnostics;
            }
        }
    }
}
