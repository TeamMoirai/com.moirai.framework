using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 窗口注册生成器：扫描 <c>[Window]</c> 标注的 <c>UIWindow</c> 派生类，把描述符与编译期工厂写进 <c>UIWindowModuleInit</c>。
    /// </summary>
    /// <remarks>
    /// 登记跑在模块初始化期：运行期开窗只查表取工厂与元数据，不再走 <c>Activator</c> 与特性反射（IL2CPP 同构）。 <br />
    /// 形状非法（非窗口类 / 缺公共无参构造 / 抽象或泛型）分别报 MIRAI500/501/502 一律 Error：放过去等于交付一枚开窗即炸的注册。 <br />
    /// <c>ModuleInitializerAttribute</c> 补丁由同包 <c>SaveServiceCodegen</c> 的 shim 生成器唯一持有，本生成器不发同名副本。
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class UIWindowRegistryGenerator : IIncrementalGenerator
    {
        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var windows = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax declaration && declaration.AttributeLists.Count > 0,
                static (ctx, ct) => WindowRegistryModel.Create(ctx, ct));

            var collected = windows.Collect();
            context.RegisterSourceOutput(collected, static (spc, source) => Execute(spc, source));
        }

        /// <summary>
        /// 校验标注窗口并产出自注册代码与诊断。
        /// </summary>
        /// <param name="context">源生成上下文。</param>
        /// <param name="allWindows">扫描到的标注窗口。</param>
        private static void Execute(SourceProductionContext context, ImmutableArray<WindowRegistryModel?> allWindows)
        {
            if (allWindows.IsDefaultOrEmpty)
            {
                return;
            }

            var registrationLines = new List<string>();
            foreach (WindowRegistryModel? model in allWindows)
            {
                if (model == null)
                {
                    continue;
                }

                if (model.InvalidReason != null)
                {
                    context.ReportDiagnostic(model.InvalidReason);
                    continue;
                }

                Diagnostic? shapeError = ValidateShape(model);
                if (shapeError != null)
                {
                    context.ReportDiagnostic(shapeError);
                    continue;
                }

                registrationLines.Add(BuildRegistrationLine(model));
            }

            if (registrationLines.Count > 0)
            {
                context.AddSource("UIWindowModuleInit.g.cs",
                    SourceText.From(ModuleInitializerFile.Emit("UIWindowModuleInit", registrationLines), Encoding.UTF8));
            }
        }

        /// <summary>
        /// 校验可实例化形状：抽象、泛型与缺公共无参构造都发不出编译期工厂。
        /// </summary>
        /// <param name="model">候选窗口模型。</param>
        /// <returns>非法时的成因诊断；合法时为 null。</returns>
        private static Diagnostic? ValidateShape(WindowRegistryModel model)
        {
            var location = model.Type.Locations.Length > 0 ? model.Type.Locations[0] : Microsoft.CodeAnalysis.Location.None;
            var display = model.Type.ToDisplayString();
            if (model.Type.IsAbstract || model.Type.IsGenericType)
            {
                return Diagnostic.Create(Diagnostics.WindowAbstractOrGeneric, location, display);
            }

            if (!HasAccessibleParameterlessConstructor(model.Type))
            {
                return Diagnostic.Create(Diagnostics.WindowMissingPublicParameterlessCtor, location, display);
            }

            if (IsNestedInPrivate(model.Type))
            {
                return Diagnostic.Create(Diagnostics.WindowNestedInaccessible, location, display);
            }

            return null;
        }

        /// <summary>判定窗口类或其外层类型链上有 private 声明：生成的顶层初始化器够不到它。</summary>
        private static bool IsNestedInPrivate(INamedTypeSymbol type)
        {
            for (var current = type; current != null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility == Accessibility.Private && current.ContainingType != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>判定窗口类有本装配可访问的无参构造函数（生成工厂按 new X() 调用）。</summary>
        private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
        {
            foreach (IMethodSymbol constructor in type.Constructors)
            {
                if (constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>组装一行注册语句：类型、描述符与编译期工厂一起交给注册表。</summary>
        private static string BuildRegistrationLine(WindowRegistryModel model)
        {
            var typeRef = model.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new StringBuilder(256)
                .Append("            global::Moirai.Atropos.UI.UIWindowRegistry.Register(typeof(").Append(typeRef).Append("),")
                .Append(" new global::Moirai.Atropos.UI.UIWindowDescriptor(")
                .AppendStringLiteral(model.ReflectionFullName).Append(", ")
                .AppendStringLiteral(model.Location).Append(", ")
                .Append(model.WindowLayer).Append(", ")
                .Append(model.FromResources ? "true" : "false").Append(", ")
                .Append(model.FullScreen ? "true" : "false").Append(", ")
                .Append(model.HideTimeToClose).Append(", ")
                .Append(model.CacheInstance ? "true" : "false")
                .Append("), static () => new ").Append(typeRef).Append("());")
                .ToString();
        }
    }

    /// <summary>发射辅助的字符串字面量扩展（引号与反斜杠转义）。</summary>
    internal static class StringBuilderLiteralExtension
    {
        /// <summary>追加一个 C# 字符串字面量（含定界引号）。</summary>
        /// <param name="builder">目标构建器。</param>
        /// <param name="value">原始字符串。</param>
        /// <returns>同一构建器。</returns>
        internal static StringBuilder AppendStringLiteral(this StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.Append('"');
        }
    }
}
