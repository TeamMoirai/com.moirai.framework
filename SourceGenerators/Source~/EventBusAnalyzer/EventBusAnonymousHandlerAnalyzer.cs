using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Moirai.EventBus.Roslyn.Analyzers;

/// <summary>
/// MIRAI700：EventBus.Subscribe / EventBus.Unsubscribe 不得传入匿名函数。
/// </summary>
/// <remarks>
/// 匿名委托每次转换生成新实例，Unsubscribe 的值等价匹配（同方法同目标）永不命中——订阅无法退订即泄漏。
/// 命名方法方法组转换不受影响。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventBusAnonymousHandlerAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Diagnostics.AnonymousHandler);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var argumentList = invocation.ArgumentList;
        if (argumentList is null || argumentList.Arguments.Count == 0) return;

        var tree = invocation.SyntaxTree;
        if (EventBusFacts.IsGeneratedCode(tree)) return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method) return;
        if (!EventBusFacts.IsSubscribeOrUnsubscribe(method)) return;

        foreach (var argument in argumentList.Arguments)
        {
            if (FindAnonymousFunction(argument.Expression) is not { } anonymous) continue;

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.AnonymousHandler,
                argument.GetLocation(),
                method.Name));
        }
    }

    /// <summary>剥开括号/强转/显式委托构造等包装，定位其中的匿名函数节点。</summary>
    private static SyntaxNode? FindAnonymousFunction(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LambdaExpressionSyntax:
            case AnonymousMethodExpressionSyntax:
                return expression;

            case ParenthesizedExpressionSyntax parenthesized:
                return FindAnonymousFunction(parenthesized.Expression);

            case CastExpressionSyntax cast:
                return FindAnonymousFunction(cast.Expression);

            case CheckedExpressionSyntax checkedExpr:
                return FindAnonymousFunction(checkedExpr.Expression);

            case ObjectCreationExpressionSyntax creation
                when creation.ArgumentList is { Arguments.Count: > 0 }:
                return FindAnonymousFunction(creation.ArgumentList.Arguments[0].Expression);

            default:
                return null;
        }
    }
}
