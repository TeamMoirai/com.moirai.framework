using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
namespace Moirai.EventBus.Roslyn.Analyzers;

/// <summary>
/// MA7004–MA7006：[GameEvent] 标记的类型必须满足 struct + IGameEvent 约束，且不得含实例方法。
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

/// <summary>[GameEvent] 契约诊断族：MA7004–MA7006（MA7000–MA7003 由订阅规范占用，ID 不得复用）。</summary>
internal static class GameEventDiagnostics
{
    /// <summary>[GameEvent] 标记类型必须为 struct。</summary>
    public const string NotStructId = "MA7004";
    /// <summary>[GameEvent] 标记类型必须实现 IGameEvent。</summary>
    public const string MissingIGameEventId = "MA7005";
    /// <summary>[GameEvent] 标记类型不得含实例方法。</summary>
    public const string HasInstanceMethodsId = "MA7006";

    public static readonly DiagnosticDescriptor NotStruct = new(
        NotStructId, "[GameEvent] 必须为 struct",
        "[GameEvent] 标记的类型 '{0}' 必须为 struct（值语义零拷贝发布）",
        "Moirai.EventBus", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MissingIGameEvent = new(
        MissingIGameEventId, "[GameEvent] 必须实现 IGameEvent",
        "[GameEvent] 标记的类型 '{0}' 必须实现 IGameEvent 标记接口",
        "Moirai.EventBus", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor HasInstanceMethods = new(
        HasInstanceMethodsId, "[GameEvent] 不得含实例方法",
        "[GameEvent] 标记的类型 '{0}' 含实例方法 '{1}'——事件对象是无状态数据包，行为放订阅方",
        "Moirai.EventBus", DiagnosticSeverity.Warning, true);
}
