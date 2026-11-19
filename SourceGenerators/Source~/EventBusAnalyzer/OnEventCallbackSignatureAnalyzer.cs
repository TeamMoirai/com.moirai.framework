using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Moirai.EventBus.Roslyn.Analyzers;

/// <summary>
/// MA7001 / MA7002 / MA7003：OnXxxEvent 事件回调签名规范。
/// </summary>
/// <remarks>
/// 约定：带参回调必须是 <c>OnXxxEvent(in XxxEvent evt)</c>（in 零拷贝）；无参回调直接 <c>OnXxxEvent()</c>， <br />
/// 无参形态只能经 Source Generator 生成的订阅胶水桥接（需宿主类型声明 partial）。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OnEventCallbackSignatureAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            Diagnostics.InParameterRequired,
            Diagnostics.CallbackNameMismatch,
            Diagnostics.PartialOwnerRequired);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.LocalFunctionStatement);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var tree = context.Node.SyntaxTree;
        if (EventBusFacts.IsGeneratedCode(tree)) return;

        string methodName;
        ParameterListSyntax? parameterList;
        bool isStatic;
        SyntaxToken nameToken;

        switch (context.Node)
        {
            case MethodDeclarationSyntax method:
                methodName = method.Identifier.ValueText;
                nameToken = method.Identifier;
                parameterList = method.ParameterList;
                isStatic = method.Modifiers.Any(SyntaxKind.StaticKeyword);
                break;
            case LocalFunctionStatementSyntax localFunction:
                methodName = localFunction.Identifier.ValueText;
                nameToken = localFunction.Identifier;
                parameterList = localFunction.ParameterList;
                isStatic = localFunction.Modifiers.Any(SyntaxKind.StaticKeyword);
                break;
            default:
                return;
        }

        if (!EventBusFacts.IsOnEventCallbackName(methodName, out var eventName)) return;
        if (parameterList is null) return;
        var parameters = parameterList.Parameters;
        if (parameters.Count != 1)
        {
            // 无参回调：检查宿主类型是否可由 Source Generator 桥接（partial）
            if (parameters.Count == 0)
                CheckParameterlessOwner(context, context.Node, nameToken, isStatic, methodName, eventName);
            return;
        }

        var parameter = parameters[0];
        if (parameter.Type is null) return;

        // ref / out 直接违规：事件回调只允许 in（只读引用）
        if (parameter.Modifiers.Any(SyntaxKind.RefKeyword) || parameter.Modifiers.Any(SyntaxKind.OutKeyword))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.InParameterRequired,
                parameter.GetLocation(),
                methodName));
            return;
        }

        var parameterType = context.SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken).Type;
        if (parameterType is null) return;

        if (!EventBusFacts.IsBusEventStruct(parameterType)) return;

        if (parameter.Modifiers.Any(SyntaxKind.InKeyword)) return; // 合规

        if (string.Equals(parameterType.Name, eventName, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.InParameterRequired,
                parameter.GetLocation(),
                methodName));
        }
        else
        {
            // 参数是总线事件但与方法名后缀不一致——优先报命名不匹配（避免双诊断）
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.CallbackNameMismatch,
                parameter.GetLocation(),
                methodName,
                parameterType.Name,
                eventName));
        }
    }

    /// <summary>MA7003：无参 OnXxxEvent 仅能由 Source Generator 桥接，宿主类型必须声明 partial。</summary>
    private static void CheckParameterlessOwner(
        SyntaxNodeAnalysisContext context,
        SyntaxNode node,
        SyntaxToken nameToken,
        bool isStatic,
        string methodName,
        string eventName)
    {
        if (EventBusFacts.ResolveBusEventByName(context.SemanticModel.Compilation, eventName) is null) return;

        // 沿声明链定位宿主类型
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is not TypeDeclarationSyntax ownerDecl) continue;

            var ownerSymbol = context.SemanticModel.GetDeclaredSymbol(ownerDecl, context.CancellationToken);
            if (ownerSymbol is not INamedTypeSymbol ownerType) return;

            // 已 partial——Source Generator 可生成胶水，无需告警
            if (ownerDecl.Modifiers.Any(SyntaxKind.PartialKeyword)) return;

            // 命名方法可手工订阅（方法组转 EventDelegate），仅无参形态依赖生成器
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.PartialOwnerRequired,
                nameToken.GetLocation(),
                ownerType.Name,
                methodName,
                eventName));
            return;
        }
    }
}
