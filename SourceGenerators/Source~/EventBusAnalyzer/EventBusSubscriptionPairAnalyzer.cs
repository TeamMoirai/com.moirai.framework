using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Moirai.EventBus.Roslyn.Analyzers;

/// <summary>
/// MA7003：同一宿主类型内，每个 <c>EventBus.Subscribe&lt;TEvent&gt;(handler)</c> 都应有值等价的成对
/// <c>EventBus.Unsubscribe&lt;TEvent&gt;(handler)</c>。
/// </summary>
/// <remarks>
/// EventBus 的对称 API 没有订阅句柄、没有 BeginScope 批量退订；退订靠 Delegate.Equals 值等价匹配
/// （同方法同目标）。因此按「事件类型 + 处理器符号」在同一宿主类型（含 partial 分部）内做多集配对，
/// 多出来的 Subscribe 报 Warning。匿名处理器由 MA7000 单独负责，此处跳过避免重复报警。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventBusSubscriptionPairAnalyzer : DiagnosticAnalyzer
{
    /// <summary>处理器符号显示格式：含参数/ref-kind，用于区分重载（Delegate.Equals 同方法同目标语义）。</summary>
    private static readonly SymbolDisplayFormat HandlerFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeContainingType
            | SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Diagnostics.UnpairedSubscription);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        // 语法树分析阶段收集，符号分析阶段按宿主类型配对上报；partial 分部归一到同一符号。
        var sitesByOwner = new ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<CallSite>>(
            SymbolEqualityComparer.Default);

        context.RegisterSyntaxNodeAction(ctx => Collect(ctx, sitesByOwner), SyntaxKind.InvocationExpression);
        context.RegisterCompilationEndAction(ctx => Report(ctx, sitesByOwner));
    }

    private static void Collect(
        SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<CallSite>> sitesByOwner)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (EventBusFacts.IsGeneratedCode(invocation.SyntaxTree)) return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method) return;
        if (!EventBusFacts.IsSubscribeOrUnsubscribe(method)) return;
        if (FindOwnerType(invocation, context.SemanticModel, context.CancellationToken) is not { } owner) return;

        var site = CallSite.TryCreate(invocation, method, context.SemanticModel, context.CancellationToken);
        if (site is null) return;

        var bag = sitesByOwner.GetOrAdd(owner, _ => new ConcurrentBag<CallSite>());
        bag.Add(site);
    }

    private static void Report(
        CompilationAnalysisContext context,
        ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<CallSite>> sitesByOwner)
    {
        var ct = context.CancellationToken;
        foreach (var entry in sitesByOwner)
        {
            ct.ThrowIfCancellationRequested();
            ReportOwner(context, entry.Value, ct);
        }
    }

    private static void ReportOwner(CompilationAnalysisContext context, ConcurrentBag<CallSite> sites, CancellationToken ct)
    {
        // 按 (事件类型, 处理器) 分组；组内按源码顺序做多集配对，多出的 Subscribe 报警
        foreach (var group in sites.GroupBy(s => (s.EventType, s.Handler)))
        {
            ct.ThrowIfCancellationRequested();
            var unsubscribes = group.Where(s => !s.IsSubscribe)
                .OrderBy(s => s.SortKey, StringComparer.Ordinal).ToList();
            var matched = 0;
            foreach (var subscribe in group.Where(s => s.IsSubscribe)
                .OrderBy(s => s.SortKey, StringComparer.Ordinal))
            {
                if (matched < unsubscribes.Count)
                {
                    matched++;
                    continue;
                }
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.UnpairedSubscription,
                    subscribe.Location,
                    subscribe.EventType,
                    subscribe.Handler));
            }
        }
    }

    /// <summary>沿语法祖先链定位最近的类型声明符号（partial 分部归一到同一符号）。</summary>
    private static INamedTypeSymbol? FindOwnerType(
        SyntaxNode node, SemanticModel model, CancellationToken ct)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is TypeDeclarationSyntax declaration
                && model.GetDeclaredSymbol(declaration, ct) is INamedTypeSymbol owner)
            {
                return owner;
            }
        }
        return null;
    }

    private sealed class CallSite
    {
        public readonly Location Location;
        public readonly string EventType;
        public readonly string Handler;
        public readonly bool IsSubscribe;
        public readonly string SortKey;

        private CallSite(Location location, string eventType, string handler, bool isSubscribe, string sortKey)
        {
            Location = location;
            EventType = eventType;
            Handler = handler;
            IsSubscribe = isSubscribe;
            SortKey = sortKey;
        }

        public static CallSite? TryCreate(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SemanticModel model,
            CancellationToken ct)
        {
            // 构造后的泛型方法携带 TEvent；显式/推断写法均可取到
            if (method.TypeArguments.Length != 1) return null;
            var eventType = method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (invocation.ArgumentList.Arguments.Count == 0) return null;

            // 处理器按符号判定值等价；lambda / 匿名方法无符号，交给 MA7000，此处跳过
            var expression = invocation.ArgumentList.Arguments[0].Expression;
            if (model.GetSymbolInfo(expression, ct).Symbol is not ISymbol handler) return null;
            var handlerKey = handler.Kind + ":" + handler.ToDisplayString(HandlerFormat);

            // 方法组的目标实例参与值等价：this.OnFoo 与 other.OnFoo 不可互相退订
            if (expression is MemberAccessExpressionSyntax memberAccess)
            {
                handlerKey += "@" + memberAccess.Expression.ToString().Replace(" ", string.Empty);
            }

            var location = invocation.GetLocation();
            var sortKey = (location.SourceTree?.FilePath ?? string.Empty)
                + ":" + location.SourceSpan.Start.ToString("D10");

            return new CallSite(
                location,
                eventType,
                handlerKey,
                method.Name.Equals("Subscribe", StringComparison.Ordinal),
                sortKey);
        }
    }
}
