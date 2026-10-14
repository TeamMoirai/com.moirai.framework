using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 为标记 <c>[ProviderHandlerHost(typeof(THandler))]</c> 的 partial class 生成 <c>s_Handler</c> 字段、 <br />
    /// <c>IsValid</c>/<c>Handler</c> 属性、<c>RequireHandler</c> 方法，以及声明默认处理器时的 <c>CreateDefaultHandler</c> 工厂。
    /// </summary>
    /// <remarks>
    /// 生成的成员均为 static，但类声明沿用源类的修饰符（static 或非 static）。 <br />
    /// 默认工厂三档：声明默认处理器且结构合法时生成 <c>CreateDefaultHandler</c>（宿主手写工厂存在时跳过生成——手写生效，MIRAI104 提示冗余）； <br />
    /// 非法声明（未继承契约/抽象/无可访问无参构造）不生成工厂（MIRAI103 由分析器报告）；懒加载先调 <c>GetHandlerFromSettings</c>、返回 null 回退默认工厂； <br />
    /// 工厂链末端 null 抛 <see cref="System.InvalidOperationException"/>；工厂全缺（MIRAI101）时 <c>Handler.get</c> 抛该异常； <br />
    /// 仅声明 <c>GetHandlerFromSettings</c>（MIRAI102）时要求返回非空值。 <br />
    /// <c>RequireHandler</c> 读 <c>s_Handler</c> 且不触发懒加载，未就绪抛 GameException——写路径 fail-fast 入口。
    /// </remarks>
    [Generator]
    public class ProviderHandlerHostGenerator : IIncrementalGenerator
    {
        /// <summary>生成代码里 new 声明默认处理器用的全限定显示格式（global:: 前缀防宿主命名空间遮蔽）。</summary>
        private static readonly SymbolDisplayFormat GlobalQualifiedNameFormat = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var provider = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    Constants.AttributeFullyQualifiedName,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, _) =>
                    {
                        var classDecl = (ClassDeclarationSyntax)ctx.TargetNode;
                        var classSymbol = (INamedTypeSymbol)ctx.SemanticModel.GetDeclaredSymbol(classDecl)!;

                        // 特性 AllowMultiple=false，命中即唯一
                        var attributeData = ctx.Attributes[0];
                        var handlerType = (INamedTypeSymbol)attributeData.ConstructorArguments[0].Value!;

                        var hasUserCreateDefaultHandler = HasHandlerFactoryMethod(classSymbol, Constants.CreateDefaultHandlerMethodName, handlerType);
                        var hasGetHandlerFromSettings = HasHandlerFactoryMethod(classSymbol, Constants.GetHandlerFromSettingsMethodName, handlerType);
                        var declaredDefaultFqn = GetValidDeclaredDefaultFqn(attributeData, classSymbol, handlerType);

                        return new ProviderHandlerHostInfo(
                            classSymbol.Name,
                            classSymbol.ContainingNamespace.ToDisplayString(),
                            handlerType.Name,
                            hasUserCreateDefaultHandler,
                            declaredDefaultFqn,
                            hasGetHandlerFromSettings,
                            classSymbol.IsStatic);
                    });

            context.RegisterSourceOutput(provider, static (spc, info) =>
            {
                spc.AddSource(
                    $"{info.ClassName}.g.cs",
                    GenerateCode(in info));
            });
        }

        private static string? GetValidDeclaredDefaultFqn(AttributeData attributeData, INamedTypeSymbol hostSymbol, INamedTypeSymbol handlerType)
        {
            if (attributeData.ConstructorArguments.Length < 2
                || attributeData.ConstructorArguments[1].Value is not INamedTypeSymbol declaredDefault)
            {
                return null;
            }

            // 与宿主同程序集时 internal/protected internal 亦可见；跨程序集仅 public 可实例化
            var sameAssembly = SymbolEqualityComparer.Default.Equals(declaredDefault.ContainingAssembly, hostSymbol.ContainingAssembly);
            return HandlerContract.IsValidDefaultHandler(declaredDefault, handlerType, sameAssembly, out _)
                ? declaredDefault.ToDisplayString(GlobalQualifiedNameFormat)
                : null;
        }

        private static bool HasHandlerFactoryMethod(INamedTypeSymbol classSymbol, string methodName, INamedTypeSymbol handlerType)
        {
            return classSymbol.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Any(m => m.IsStatic
                    && !m.IsAbstract
                    && SymbolEqualityComparer.Default.Equals(m.ReturnType, handlerType)
                    && m.Parameters.IsEmpty);
        }

        private static string GenerateCode(in ProviderHandlerHostInfo info)
        {
            var classModifier = info.IsStatic ? "static partial class" : "partial class";
            // 兜底工厂可来自宿主手写或声明默认的生成，懒加载链只关心方法是否存在
            var hasFactory = info.HasUserCreateDefaultHandler || info.DeclaredDefaultFqn != null;

            var sb = new StringBuilder(2048);
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//   ProviderHandlerHost Source Generator — do not edit manually.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine();
            sb.AppendLine($"namespace {info.Namespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    {classModifier} {info.ClassName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        private static volatile {info.HandlerTypeName} s_Handler;");
            sb.AppendLine();

            if (info.DeclaredDefaultFqn != null && !info.HasUserCreateDefaultHandler)
            {
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// 创建默认处理器（settings 未配置时的代码兜底）。");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine($"        internal static {info.HandlerTypeName} CreateDefaultHandler() => new {info.DeclaredDefaultFqn}();");
                sb.AppendLine();
            }

            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Handler 是否可用。");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static bool IsValid => s_Handler != null;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 取就绪处理器；未就绪时抛 <see cref=\"Moirai.Atropos.GameException\"/>（写路径 fail-fast——禁止静默降级）。");
            sb.AppendLine("        /// <para>读 <c>s_Handler</c> 不触发懒加载：服务未注册/未初始化时立即失败，避免写操作静默丢档。</para>");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        /// <returns>已就绪的处理器实例。</returns>");
            sb.AppendLine($"        private static {info.HandlerTypeName} RequireHandler()");
            sb.AppendLine("        {");
            sb.AppendLine("            var handler = s_Handler;");
            sb.AppendLine("            if (handler == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                throw new Moirai.Atropos.GameException(");
            sb.AppendLine($"                    \"ProviderHandlerHost: '{info.ClassName}' handler is not ready. Register and initialize the service before writing.\");");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            return handler;");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine($"        internal static {info.HandlerTypeName} Handler");
            sb.AppendLine("        {");
            sb.AppendLine("            get");
            sb.AppendLine("            {");
            sb.AppendLine("                var handler = s_Handler;");
            sb.AppendLine("                if (handler != null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    return handler;");
            sb.AppendLine("                }");
            sb.AppendLine();

            if (hasFactory)
            {
                // 直接调用工厂方法：s_DefaultFactory 委托字段恒非 Null 且无任何再赋值方，判空与字段均为死代码
                sb.AppendLine(info.HasGetHandlerFromSettings
                    ? "                var created = GetHandlerFromSettings() ?? CreateDefaultHandler()"
                    : "                var created = CreateDefaultHandler()");

                // 工厂链末端兜底：settings 缺值且工厂返回 null（或仅工厂且返回 null）时 fail-fast，
                // 避免 null 流入 CAS 后在 Internal_Init 处 NRE 且每次访问复现
                sb.AppendLine("                    ?? throw new InvalidOperationException(");
                sb.AppendLine(info.HasGetHandlerFromSettings
                    ? $"                        \"ProviderHandlerHost: '{info.ClassName}' GetHandlerFromSettings returned null and CreateDefaultHandler returned null.\");"
                    : $"                        \"ProviderHandlerHost: '{info.ClassName}' CreateDefaultHandler returned null.\");");

                sb.AppendLine("                if (Interlocked.CompareExchange(ref s_Handler, created, null) == null)");
                sb.AppendLine("                {");
                sb.AppendLine("                    created.Internal_Init();");
                sb.AppendLine("                }");
            }
            else if (info.HasGetHandlerFromSettings)
            {
                // settings-only：懒加载唯一来源是 GetHandlerFromSettings，返回 null 即 fail-fast
                sb.AppendLine("                var created = GetHandlerFromSettings()");
                sb.AppendLine("                    ?? throw new InvalidOperationException(");
                sb.AppendLine($"                        \"ProviderHandlerHost: '{info.ClassName}' has no CreateDefaultHandler and GetHandlerFromSettings returned null. Set Handler manually or configure the settings source.\");");
                sb.AppendLine();

                sb.AppendLine("                if (Interlocked.CompareExchange(ref s_Handler, created, null) == null)");
                sb.AppendLine("                {");
                sb.AppendLine("                    created.Internal_Init();");
                sb.AppendLine("                }");
            }
            else
            {
                sb.AppendLine($"                throw new InvalidOperationException(");
                sb.AppendLine($"                    \"ProviderHandlerHost: '{info.ClassName}' has no CreateDefaultHandler or GetHandlerFromSettings, set Handler manually before use.\");");
            }

            if (hasFactory || info.HasGetHandlerFromSettings)
            {
                // 懒加载路径：CAS 成功的一侧补 Internal_Init 后返回装配结果
                sb.AppendLine();
                sb.AppendLine("                return s_Handler;");
            }
            // else：getter 以无条件 throw 结束，块尾不可达即满足 CS0161；不追加 return（避免 CS0162 不可达代码）

            sb.AppendLine("            }");
            sb.AppendLine("            set");
            sb.AppendLine("            {");
            sb.AppendLine("                if (value == null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    throw new ArgumentNullException(nameof(value));");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                value.Internal_Init();");
            sb.AppendLine();
            sb.AppendLine("                var previous = Interlocked.Exchange(ref s_Handler, value);");
            sb.AppendLine("                if (previous != null && !ReferenceEquals(previous, value))");
            sb.AppendLine("                {");
            sb.AppendLine("                    previous.Internal_Shutdown();");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");

            // 无损换入换出的接缝：公开 setter 会 Internal_Init 且拒收 null，
            // 于是「降级到空处理器」与「测试里临时换后端」只能反射私有字段——那是运行期才炸的耦合。
            sb.AppendLine();
            sb.AppendLine("        /// <summary>读当前处理器：不触发懒加载，也不产生任何副作用。</summary>");
            sb.AppendLine($"        internal static {info.HandlerTypeName} Internal_PeekHandler() => s_Handler;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 换入处理器并返回原值，允许传 null；不调用 <c>Internal_Init</c> 也不关停原处理器。");
            sb.AppendLine("        /// <para>框架内部门面与测试用它做无损换入换出，用完请把原值换回去。</para>");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        internal static {info.HandlerTypeName} Internal_UseHandler({info.HandlerTypeName} next) => Interlocked.Exchange(ref s_Handler, next);");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private readonly struct ProviderHandlerHostInfo
        {
            public readonly string ClassName;
            public readonly string Namespace;
            public readonly string HandlerTypeName;
            public readonly bool HasUserCreateDefaultHandler;
            public readonly string? DeclaredDefaultFqn;
            public readonly bool HasGetHandlerFromSettings;
            public readonly bool IsStatic;

            public ProviderHandlerHostInfo(string className, string @namespace, string handlerTypeName, bool hasUserCreateDefaultHandler, string? declaredDefaultFqn, bool hasGetHandlerFromSettings, bool isStatic)
            {
                ClassName = className;
                Namespace = @namespace;
                HandlerTypeName = handlerTypeName;
                HasUserCreateDefaultHandler = hasUserCreateDefaultHandler;
                DeclaredDefaultFqn = declaredDefaultFqn;
                HasGetHandlerFromSettings = hasGetHandlerFromSettings;
                IsStatic = isStatic;
            }
        }
    }
}
