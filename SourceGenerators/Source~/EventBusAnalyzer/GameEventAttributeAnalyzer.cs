using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
namespace Moirai.EventBus.Roslyn.Analyzers;

/// <summary>
/// MA7001：[GameEvent] 标记的类型必须满足 struct + IGameEvent 约束，且不得含实例方法。
/// </summary>
/// <remarks>
/// struct 事件是无状态数据包：字段 public 或 readonly；实例方法/事件成员属契约违反
/// （事件对象不应有行为——行为放订阅方）。class 事件（池化负载）不标本特性，不在本分析器范围。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GameEventAttributeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(GameEventDiagnostics.NotStruct, GameEventDiagnostics.MissingIGameEvent, GameEventDiagnostics.HasInstanceMethods);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var attr = type.GetAttributes();
        if (!HasGameEventAttribute(attr)) return;

        if (type.TypeKind != TypeKind.Struct)
        {
            context.ReportDiagnostic(Diagnostic.Create(GameEventDiagnostics.NotStruct, type.Locations[0], type.Name));
            return;
        }
        if (!ImplementsIGameEvent(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(GameEventDiagnostics.MissingIGameEvent, type.Locations[0], type.Name));
        }
        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary && !m.IsStatic)
            {
                context.ReportDiagnostic(Diagnostic.Create(GameEventDiagnostics.HasInstanceMethods, member.Locations[0], type.Name, m.Name));
            }
        }
    }

    private static bool HasGameEventAttribute(ImmutableArray<AttributeData> attributes)
    {
        foreach (var a in attributes)
        {
            if (a.AttributeClass?.Name == "GameEvent") return true;
        }
        return false;
    }

    private static bool ImplementsIGameEvent(INamedTypeSymbol type)
    {
        foreach (var i in type.AllInterfaces)
        {
            if (i.Name == "IGameEvent") return true;
        }
        return false;
    }
}

/// <summary>MA70xx 诊断族描述符。</summary>
internal static class GameEventDiagnostics
{
    public static readonly DiagnosticDescriptor NotStruct = new(
        "MA7001", "[GameEvent] 必须为 struct",
        "[GameEvent] 标记的类型 '{0}' 必须为 struct（值语义零拷贝发布）",
        "Moirai.EventBus", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MissingIGameEvent = new(
        "MA7002", "[GameEvent] 必须实现 IGameEvent",
        "[GameEvent] 标记的类型 '{0}' 必须实现 IGameEvent 标记接口",
        "Moirai.EventBus", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor HasInstanceMethods = new(
        "MA7003", "[GameEvent] 不得含实例方法",
        "[GameEvent] 标记的类型 '{0}' 含实例方法 '{1}'——事件对象是无状态数据包，行为放订阅方",
        "Moirai.EventBus", DiagnosticSeverity.Warning, true);
}
