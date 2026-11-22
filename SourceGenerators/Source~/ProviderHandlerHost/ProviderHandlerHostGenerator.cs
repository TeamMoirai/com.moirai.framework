using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 为标记 <c>[ProviderHandlerHost(typeof(THandler))]</c> 的 partial class 生成 <c>s_Handler</c> 字段、 <br />
    /// <c>IsValid</c>/<c>Handler</c> 属性、<c>RequireHandler</c> 方法、<c>InvalidateHandler</c> 缓存失效钩子， <br />
    /// 以及声明默认处理器时的 <c>CreateDefaultHandler</c> 工厂；接口契约（服务后端）另生成 <c>WarmHandler</c> 预热入口。
    /// </summary>
    /// <remarks>
    /// 生成的成员均为 static，但类声明沿用源类的修饰符（static 或非 static）。 <br />
    /// 默认工厂三档：声明默认处理器且结构合法时生成 <c>CreateDefaultHandler</c>（宿主手写工厂存在时跳过生成——手写生效，MA1004 提示冗余）； <br />
    /// 非法声明（未继承契约/抽象/无可访问无参构造）不生成工厂（MA1003 由分析器报告）；懒加载先调 <c>GetHandlerFromSettings</c>、返回 null 回退默认工厂； <br />
    /// 工厂链末端 null 抛 <see cref="System.InvalidOperationException"/>；工厂全缺（MA1001）时 <c>Handler.get</c> 抛该异常； <br />
    /// 仅声明 <c>GetHandlerFromSettings</c>（MA1002）时要求返回非空值。 <br />
    /// <see cref="Moirai.Atropos.ServiceBase"/> 后端（接口契约）：绑定即注册——懒链解析出实例后注册进 <see cref="Moirai.Atropos.GameServices"/>（契约绑定）， <br />
    /// 生命周期由容器拓扑驱动，缓存回填<b>注册结果</b>（槽位 ≡ 容器实例）；接口契约的后端必是 <see cref="Moirai.Atropos.ServiceBase"/> 派生， <br />
    /// 装配路径不按实例类型分派行为，只在转换点上各留一条带宿主名与契约名的 fail-fast 判错。 <br />
    /// 读侧只认槽位本身：一致性由内核在世界关停时推送清槽保证（装配路径登记回调，每条装配路径登记一次），不再逐次回查容器； <br />
    /// 服务门面的懒装配受 <c>GameServices.EnsureFacadeAssemblyAllowed</c> 约束（仅世界已初始化/初始化在途放行）， <br />
    /// <c>WarmHandler</c> 为免闸的组合根入口。 <br />
    /// FrameworkHandler 后端（类契约，工具处理器）：沿用直驱生命周期（Internal_Init/Internal_Shutdown），不进容器也不登记世界清槽。 <br />
    /// <c>RequireHandler</c> 读 <c>s_Handler</c> 且不触发懒加载，未就绪抛 GameException——写路径 fail-fast 入口； <br />
    /// <c>InvalidateHandler(handler)</c> 由处理器 OnShutdown 调用，仅当槽位仍持该实例时清槽。
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
                            handlerType.ToDisplayString(GlobalQualifiedNameFormat),
                            handlerType.TypeKind == TypeKind.Interface,
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

        /// <summary>
        /// 探针入口：对合成宿主信息直接产出宿主源码文本。
        /// </summary>
        /// <remarks>仅供 <c>Source~/ProviderHandlerHost.Probe</c> 断言发射形状，不进入生成管线。</remarks>
        /// <param name="info">合成的宿主信息。</param>
        /// <returns>与生成管线一致的发射文本。</returns>
        internal static string GenerateCodeForTesting(in ProviderHandlerHostInfo info) => GenerateCode(in info);

        private static string GenerateCode(in ProviderHandlerHostInfo info)
        {
            var classModifier = info.IsStatic ? "static partial class" : "partial class";
            // 兜底工厂可来自宿主手写或声明默认的生成，懒加载链只关心方法是否存在
            var hasFactory = info.HasUserCreateDefaultHandler || info.DeclaredDefaultFqn != null;

            var sb = new StringBuilder(4096);
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//   ProviderHandlerHost Source Generator — do not edit manually.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine();
            sb.AppendLine($"namespace {info.Namespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    {classModifier} {info.ClassName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        private static volatile {info.HandlerTypeName} s_Handler;");
            sb.AppendLine("        // 仅在共享初始化锁内访问；工厂阶段不置位，允许重入装配；注册阶段置位（容器 OnInit 重入即抛）。");
            sb.AppendLine("        private static bool s_InitializingHandler;");
            sb.AppendLine();

            if (info.DeclaredDefaultFqn != null && !info.HasUserCreateDefaultHandler)
            {
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// 创建默认处理器（settings 未配置时的代码兜底）。");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"        internal static {info.HandlerTypeName} CreateDefaultHandler() => new {info.DeclaredDefaultFqn}();");
                sb.AppendLine();
            }

            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Handler 是否可用。");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static bool IsValid");
            sb.AppendLine("        {");
            sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("            get => s_Handler != null;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 取就绪处理器；未就绪时抛 <see cref=\"Moirai.Atropos.GameException\"/>（写路径 fail-fast——禁止静默降级）。");
            sb.AppendLine("        /// <para>读 <c>s_Handler</c> 不触发懒加载：服务未注册/未初始化时立即失败，避免写操作静默丢档。</para>");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        /// <returns>已就绪的处理器实例。</returns>");
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
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

            // 读侧只认槽位本身：一致性由内核在世界关停时推送清槽保证，逐读回查容器的形态已废
            sb.AppendLine($"        internal static {info.HandlerTypeName} Handler");
            sb.AppendLine("        {");
            sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("            get => ResolveHandler(false);");
            sb.AppendLine("            set => AssignHandler(value);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static " + (info.IsServiceContract ? "" : " ") + info.HandlerTypeName + " ResolveHandler(bool preInitAllowed)");
            sb.AppendLine("        {");
            sb.AppendLine("            var handler = s_Handler;");
            sb.AppendLine("            if (handler != null)");
            sb.AppendLine("            {");
            sb.AppendLine("                return handler;");
            sb.AppendLine("            }");
            sb.AppendLine();

            // 两族宿主共用 HandlerBase 上的一把可重入锁，避免跨宿主同步依赖导致宿主锁逆序。
            sb.AppendLine("            lock (global::Moirai.Atropos.HandlerBase.InitializationLock)");
            sb.AppendLine("            {");
            sb.AppendLine("                handler = s_Handler;");
            sb.AppendLine("                if (handler != null) return handler;");
            if (info.IsServiceContract)
            {
                // 组合根预热（WarmHandler）免闸；其余懒装配必须落在「已初始化 / 初始化在途」的窗口里，
                // 否则就是越过启动序——静默返回未初始化后端比判红更难查。
                sb.AppendLine("                if (!preInitAllowed) global::Moirai.Atropos.GameServices.EnsureFacadeAssemblyAllowed(\"" + info.ClassName + "\");");
            }
            sb.AppendLine("                if (s_InitializingHandler)");
            sb.AppendLine("                {");
            sb.AppendLine("                    throw new InvalidOperationException(");
            sb.AppendLine($"                        \"ProviderHandlerHost: '{info.ClassName}' recursive handler initialization is not allowed.\");");
            sb.AppendLine("                }");
            sb.AppendLine();

            if (hasFactory || info.HasGetHandlerFromSettings)
            {
                sb.AppendLine(info.HasGetHandlerFromSettings
                    ? "                var created = GetHandlerFromSettings();"
                    : "                var created = CreateDefaultHandler();");
                // 工厂可能经服务注册重入并成功装配；必须先接收其结果，再回退工厂或判空。
                sb.AppendLine("                handler = s_Handler;");
                sb.AppendLine("                if (handler != null) return handler;");
                if (hasFactory && info.HasGetHandlerFromSettings)
                {
                    sb.AppendLine("                if (created == null)");
                    sb.AppendLine("                {");
                    sb.AppendLine("                    created = CreateDefaultHandler();");
                    sb.AppendLine("                    handler = s_Handler;");
                    sb.AppendLine("                    if (handler != null) return handler;");
                    sb.AppendLine("                }");
                }
                sb.AppendLine("                if (created == null)");
                sb.AppendLine("                {");
                sb.AppendLine("                    throw new InvalidOperationException(");
                if (!hasFactory)
                {
                    sb.AppendLine($"                            \"ProviderHandlerHost: '{info.ClassName}' has no CreateDefaultHandler and GetHandlerFromSettings returned null. Set Handler manually or configure the settings source.\");");
                }
                else
                {
                    sb.AppendLine(info.HasGetHandlerFromSettings
                        ? $"                            \"ProviderHandlerHost: '{info.ClassName}' GetHandlerFromSettings returned null and CreateDefaultHandler returned null.\");"
                        : $"                            \"ProviderHandlerHost: '{info.ClassName}' CreateDefaultHandler returned null.\");");
                }
                sb.AppendLine("                }");
                sb.AppendLine();
                sb.AppendLine("                s_InitializingHandler = true;");
                sb.AppendLine("                try");
                sb.AppendLine("                {");
                if (info.IsServiceContract)
                {
                    // 接口契约的后端必是 ServiceBase（非法声明在编译期由 MA1003 拦截），装配路径不按实例类型分派行为。
                    sb.AppendLine("                    // 绑定即注册：容器登记契约绑定，生命周期由容器拓扑驱动；缓存回填注册结果（槽位 ≡ 容器实例）。");
                    sb.AppendLine("                    var service = RequireServiceBackend(created);");
                    sb.AppendLine("                    var registered = "
                        + "global::Moirai.Atropos.GameServices.RegisterService(");
                    sb.AppendLine($"                        service.Scope, typeof({info.HandlerTypeFqn}), service);");
                    sb.AppendLine("                    RegisterHandlerResetterIfNeeded();");
                    sb.AppendLine($"                    s_Handler = ({info.HandlerTypeName})registered;");
                    sb.AppendLine();
                    sb.AppendLine("                    return s_Handler;");
                }
                else
                {
                    sb.AppendLine("                    ((global::Moirai.Atropos.FrameworkHandler)created).Internal_Init();");
                    sb.AppendLine("                    s_Handler = created;");
                    sb.AppendLine("                    return created;");
                }
                sb.AppendLine("                }");
                sb.AppendLine("                finally");
                sb.AppendLine("                {");
                sb.AppendLine("                    s_InitializingHandler = false;");
                sb.AppendLine("                }");
            }
            else
            {
                sb.AppendLine("                throw new InvalidOperationException(");
                sb.AppendLine($"                    \"ProviderHandlerHost: '{info.ClassName}' has no CreateDefaultHandler or GetHandlerFromSettings, set Handler manually before use.\");");
            }
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static void AssignHandler(" + info.HandlerTypeName + " value)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (value == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                throw new ArgumentNullException(nameof(value));");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            lock (global::Moirai.Atropos.HandlerBase.InitializationLock)");
            sb.AppendLine("            {");
            sb.AppendLine("                var previous = s_Handler;");
            sb.AppendLine("                if (ReferenceEquals(previous, value)) return;");
            sb.AppendLine("                if (s_InitializingHandler)");
            sb.AppendLine("                {");
            sb.AppendLine("                    throw new InvalidOperationException(");
            sb.AppendLine($"                        \"ProviderHandlerHost: '{info.ClassName}' recursive handler initialization is not allowed.\");");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                s_InitializingHandler = true;");
            sb.AppendLine("                try");
            sb.AppendLine("                {");
            if (info.IsServiceContract)
            {
                sb.AppendLine("                    // 先摘槽再收旧：旧 OnShutdown 的 InvalidateHandler 不会误清新缓存；注销核验（契约当前绑定才注销）由内核条件门兜底。");
                sb.AppendLine("                    s_Handler = null;");
                sb.AppendLine("                    if (previous != null)");
                sb.AppendLine("                    {");
                sb.AppendLine("                        var oldService = RequireServiceBackend(previous);");
                sb.AppendLine("                        global::Moirai.Atropos.GameServices.UnregisterHandler(");
                sb.AppendLine($"                            typeof({info.HandlerTypeFqn}), oldService);");
                sb.AppendLine("                    }");
                sb.AppendLine();
                // 换入侧与懒加载侧同一条装配序列：契约绑定的后端只经容器登记，注册结果回填槽位。
                sb.AppendLine("                    var newService = RequireServiceBackend(value);");
                sb.AppendLine("                    var registered = "
                    + "global::Moirai.Atropos.GameServices.RegisterService(");
                sb.AppendLine($"                        newService.Scope, typeof({info.HandlerTypeFqn}), newService);");
                sb.AppendLine("                    RegisterHandlerResetterIfNeeded();");
                sb.AppendLine($"                    s_Handler = ({info.HandlerTypeName})registered;");
                sb.AppendLine("                }");
                sb.AppendLine("                finally");
                sb.AppendLine("                {");
                sb.AppendLine("                    s_InitializingHandler = false;");
                sb.AppendLine("                }");
            }
            else
            {
                sb.AppendLine("                    ((global::Moirai.Atropos.FrameworkHandler)value).Internal_Init();");
                sb.AppendLine("                    s_Handler = value;");
                sb.AppendLine("                }");
                sb.AppendLine("                finally");
                sb.AppendLine("                {");
                sb.AppendLine("                    s_InitializingHandler = false;");
                sb.AppendLine("                }");
                sb.AppendLine("                ((global::Moirai.Atropos.FrameworkHandler)previous)?.Internal_Shutdown();");
            }
            sb.AppendLine("            }");
            sb.AppendLine("        }");

            // 服务门面把「落槽」与「世界关停时清槽」配成对：登记一次即长期有效，读侧无需再回查容器。
            if (info.IsServiceContract)
            {
                sb.AppendLine();
                sb.AppendLine("        private static bool s_ResetterRegistered;");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>向世界关停路径登记本门面的清槽回调（仅首次装配登记一次）。</summary>");
                sb.AppendLine("        private static void RegisterHandlerResetterIfNeeded()");
                sb.AppendLine("        {");
                sb.AppendLine("            if (s_ResetterRegistered) return;");
                sb.AppendLine();
                sb.AppendLine("            s_ResetterRegistered = true;");
                sb.AppendLine("            global::Moirai.Atropos.GameServices.RegisterHandlerResetter(static () => s_Handler = null);");
                sb.AppendLine("        }");

                // 出包后端可能既不派生 ServiceBase 也不被 MA1003 看见（手写工厂自报实例），转换点因此各带一条判错。
                var guardHeadLine = "                \"ProviderHandlerHost: '" + info.ClassName + "' contract '"
                    + info.HandlerTypeName + "' \"";
                sb.AppendLine();
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// 取用服务基座视图：实例未派生自 <c>ServiceBase</c> 时带宿主名与契约名抬错，不抛裸转换异常。");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine("        /// <param name=\"candidate\">待装配的后端实例。</param>");
                sb.AppendLine("        /// <returns>同一个实例的服务基座视图。</returns>");
                sb.AppendLine("        /// <exception cref=\"System.InvalidCastException\">实例未派生自服务基座。</exception>");
                sb.AppendLine("        private static global::Moirai.Atropos.ServiceBase "
                    + "RequireServiceBackend(object candidate)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (typeof(global::Moirai.Atropos.ServiceBase)"
                    + ".IsInstanceOfType(candidate))");
                sb.AppendLine("            {");
                sb.AppendLine("                return (global::Moirai.Atropos.ServiceBase)candidate;");
                sb.AppendLine("            }");
                sb.AppendLine();
                sb.AppendLine("            throw new InvalidCastException(");
                sb.AppendLine(guardHeadLine);
                sb.AppendLine("                + \"backend must derive from ServiceBase, got \" "
                    + "+ candidate.GetType().FullName + \".\");");
                sb.AppendLine("        }");
            }

            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 缓存失效钩子：处理器注销/销毁时由其 OnShutdown 调用。");
            sb.AppendLine("        /// <para>仅当槽位仍持该实例时清槽——先换新后关旧的替换序里，旧实例的失效不会误清后继缓存。</para>");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static void InvalidateHandler({info.HandlerTypeName} handler)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (ReferenceEquals(s_Handler, handler))");
            sb.AppendLine("            {");
            sb.AppendLine("                s_Handler = null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");

            // WarmHandler 只发给静态服务宿主：它是唯一免「懒装配闸」的入口——组合根在世界初始化前入图预热走这条，
            // 其余门面读一律落在已初始化/初始化在途的窗口内（见 ResolveHandler 的 EnsureFacadeAssemblyAllowed）
            if (info.IsServiceContract && info.IsStatic)
            {
                sb.AppendLine();
                sb.AppendLine("        /// <summary>主动预热：按 settings 解析（null 回退默认工厂）并注册进容器、回填缓存。组合根在入图期调用。</summary>");
                sb.AppendLine("        internal static void WarmHandler()");
                sb.AppendLine("        {");
                sb.AppendLine("            _ = ResolveHandler(true);");
                sb.AppendLine("        }");
            }

            // 无损换入换出的接缝：公开 setter 会触发注册/初始化且拒收 null，
            // 于是「降级到空处理器」与「测试里临时换后端」只能反射私有字段——那是运行期才炸的耦合。
            sb.AppendLine();
            sb.AppendLine("        /// <summary>读当前处理器：不触发懒加载，也不产生任何副作用。</summary>");
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        internal static {info.HandlerTypeName} Internal_PeekHandler() => s_Handler;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 换入处理器并返回原值，允许传 null；不注册、不初始化也不关停原处理器。");
            sb.AppendLine("        /// <para>框架内部门面与测试用它做无损换入换出，用完请把原值换回去。</para>");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        internal static {info.HandlerTypeName} Internal_UseHandler({info.HandlerTypeName} next) => Interlocked.Exchange(ref s_Handler, next);");

            // 关闭 Domain Reload 的编辑器工作流下，静态 s_Handler 会跨 Play 会话残留并指向上一局已销毁的
            // Unity 对象；优雅退出路径由各服务 OnShutdown 置空兜底，这里覆盖异常退出等未经关停的场景。
            // 仅编辑器生效：Player 构建进程生命周期与会话一致，不存在残留窗口。
            sb.AppendLine();
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 编辑器兼容：进入 Play 时清空上一会话残留的处理器引用（关闭 Domain Reload 工作流）。");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        [global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
            sb.AppendLine("        private static void ResetGeneratedStatics()");
            sb.AppendLine("        {");
            sb.AppendLine("            s_Handler = null;");
            sb.AppendLine("            s_InitializingHandler = false;");
            sb.AppendLine("        }");
            sb.AppendLine("#endif");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 单枚宿主的发射输入：类名、命名空间、契约类型与服务/类契约归属。
        /// </summary>
        /// <remarks>发射文本只依赖这些字符串与布尔，不依赖语义模型。</remarks>
        internal readonly struct ProviderHandlerHostInfo
        {
            /// <summary>宿主类名（生成的分部类与异常正文用）。</summary>
            public readonly string ClassName;

            /// <summary>宿主命名空间。</summary>
            public readonly string Namespace;

            /// <summary>处理器契约类型的简名（槽位与成员签名用）。</summary>
            public readonly string HandlerTypeName;

            /// <summary>处理器契约类型的全限定名（容器注册键用）。</summary>
            public readonly string HandlerTypeFqn;

            /// <summary>契约是接口（服务后端，进容器）还是类（工具处理器，直驱）。</summary>
            public readonly bool IsServiceContract;

            /// <summary>宿主是否自带手写兜底工厂。</summary>
            public readonly bool HasUserCreateDefaultHandler;

            /// <summary>声明默认的合法全限定名；不可用工厂时为 null。</summary>
            public readonly string? DeclaredDefaultFqn;

            /// <summary>宿主是否提供 settings 来源工厂。</summary>
            public readonly bool HasGetHandlerFromSettings;

            /// <summary>宿主是否为 static 类（决定 WarmHandler 预热入口是否发射）。</summary>
            public readonly bool IsStatic;

            public ProviderHandlerHostInfo(string className, string @namespace, string handlerTypeName, string handlerTypeFqn, bool isServiceContract, bool hasUserCreateDefaultHandler, string? declaredDefaultFqn, bool hasGetHandlerFromSettings, bool isStatic)
            {
                ClassName = className;
                Namespace = @namespace;
                HandlerTypeName = handlerTypeName;
                HandlerTypeFqn = handlerTypeFqn;
                IsServiceContract = isServiceContract;
                HasUserCreateDefaultHandler = hasUserCreateDefaultHandler;
                DeclaredDefaultFqn = declaredDefaultFqn;
                HasGetHandlerFromSettings = hasGetHandlerFromSettings;
                IsStatic = isStatic;
            }
        }
    }
}
