using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Moirai.EventBus.Roslyn.Analyzers;

/// <summary>
/// MA7001 / MA7002：OnXxxEvent 事件回调签名规范。
/// </summary>
/// <remarks>
/// 约定：带参回调必须是 <c>OnXxxEvent(in XxxEvent evt)</c>（in 零拷贝，且方法组转换要求 ref-kind 精确匹配）。 <br />
/// 订阅胶水不再由生成器产出——Subscribe/Unsubscribe 由用户自行编写，本分析器只查规范。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OnEventCallbackSignatureAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            Diagnostics.InParameterRequired,
            Diagnostics.CallbackNameMismatch);

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

        switch (context.Node)
        {
            case MethodDeclarationSyntax method:
                methodName = method.Identifier.ValueText;
                parameterList = method.ParameterList;
                break;
            case LocalFunctionStatementSyntax localFunction:
                methodName = localFunction.Identifier.ValueText;
                parameterList = localFunction.ParameterList;
                break;
            default:
                return;
        }

        if (!EventBusFacts.IsOnEventCallbackName(methodName, out var eventName)) return;
        if (parameterList is null) return;
        var parameters = parameterList.Parameters;
        if (parameters.Count != 1) return; // 无参形态由用户自行决定如何订阅，不做规范约束

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
}
