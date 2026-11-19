using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.EventBus.Roslyn.Generators;

/// <summary>单个 OnXxxEvent 回调的语义模型（可等价比较，供增量缓存）。</summary>
internal sealed class CallbackModel
{
    public readonly string MethodName;
    public readonly string EventTypeDisplay;
    /// <summary>true = 无参回调（OnXxxEvent()），需要缓存包装委托桥接。</summary>
    public readonly bool IsParameterless;
    /// <summary>静态回调——包装委托缓存为 static 字段。</summary>
    public readonly bool IsStatic;
    /// <summary>true = 带参回调签名与 EventDelegate&lt;T&gt;(in T) 精确匹配（in 修饰），可方法组直连；否则生成时跳过。</summary>
    public readonly bool MatchesContract;

    public CallbackModel(string methodName, string eventTypeDisplay, bool isParameterless, bool isStatic, bool matchesContract = true)
    {
        MethodName = methodName;
        EventTypeDisplay = eventTypeDisplay;
        IsParameterless = isParameterless;
        IsStatic = isStatic;
        MatchesContract = matchesContract;
    }

    public override bool Equals(object? obj)
        => obj is CallbackModel other
            && MethodName == other.MethodName
            && EventTypeDisplay == other.EventTypeDisplay
            && IsParameterless == other.IsParameterless
            && IsStatic == other.IsStatic
            && MatchesContract == other.MatchesContract;

    public override int GetHashCode()
        => unchecked(((MethodName.GetHashCode() * 397) ^ EventTypeDisplay.GetHashCode()) * 31
            + (IsParameterless ? 2 : 0) + (MatchesContract ? 1 : 0));
}

/// <summary>宿主类型（含 OnXxxEvent 回调的 partial 类型声明）的可等价模型。</summary>
internal sealed class OwnerModel
{
    public readonly string Namespace;
    public readonly List<ContainerModel> Containers;
    public readonly string TypeKeyword;   // class / struct / record / record struct
    public readonly string TypeName;
    public bool IsStaticClass;
    /// <summary>宿主全部方法名（生成成员命名冲突规避用）。</summary>
    public readonly List<string> ExistingMemberNames;
    public readonly List<CallbackModel> Callbacks;

    /// <summary>全限定标识（跨分部声明合并键）。</summary>
    public string FullName => Namespace.Length == 0
        ? string.Join(".", Containers.Select(c => c.Name).Append(TypeName))
        : Namespace + "." + string.Join(".", Containers.Select(c => c.Name).Append(TypeName));

    public OwnerModel(
        string @namespace,
        List<ContainerModel> containers,
        string typeKeyword,
        string typeName,
        bool isStaticClass,
        List<string> existingMemberNames,
        List<CallbackModel> callbacks)
    {
        Namespace = @namespace;
        Containers = containers;
        TypeKeyword = typeKeyword;
        TypeName = typeName;
        IsStaticClass = isStaticClass;
        ExistingMemberNames = existingMemberNames;
        Callbacks = callbacks;
    }

    public static OwnerModel? Create(GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;

        // 非分部类型无法注入 partial 胶水（MA7003 由分析器提示）
        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword)) return null;
        if (declaration.TypeParameterList is not null) return null; // 泛型宿主暂不支持
        if (declaration.Kind() is SyntaxKind.InterfaceDeclaration) return null;

        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(declaration, ct) as INamedTypeSymbol;
        if (typeSymbol is null) return null;
        if (EventBusFacts.IsGeneratedCode(declaration.SyntaxTree)) return null;

        var gameEventInterface = context.SemanticModel.Compilation.GetTypeByMetadataName(
            EventBusFacts.GameEventInterfaceMetadataName);
        if (gameEventInterface is null) return null;

        var isStaticClass = declaration is ClassDeclarationSyntax classDecl
            && classDecl.Modifiers.Any(SyntaxKind.StaticKeyword);

        var callbacks = new List<CallbackModel>();
        foreach (var method in declaration.Members.OfType<MethodDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (!EventBusFacts.IsOnEventCallbackName(method.Identifier.ValueText, out var eventName)) continue;
            if (method.TypeParameterList is not null) continue;

            var parameters = method.ParameterList.Parameters;
            if (parameters.Count > 1) continue;
            var isStatic = method.Modifiers.Any(SyntaxKind.StaticKeyword);

            if (parameters.Count == 0)
            {
                // 无参回调：按名解析编译内声明的总线事件类型
                var compilation = context.SemanticModel.Compilation;
                var resolved = EventBusFacts.ResolveBusEventByName(compilation, eventName);
                if (resolved is null) continue;
                callbacks.Add(new CallbackModel(
                    method.Identifier.ValueText,
                    resolved.ToDisplayString(SymbolFormats.GlobalQualified),
                    isParameterless: true,
                    isStatic));
            }
            else
            {
                var parameter = parameters[0];
                var parameterType = context.SemanticModel.GetTypeInfo(parameter.Type!, ct).Type;
                if (parameterType is null) continue;
                if (!EventBusFacts.IsBusEventStruct(parameterType)) continue;

                // 方法组转换要求 ref-kind 精确匹配：仅 in 参数可直连 EventDelegate<T>(in T)
                var matchesContract = parameter.Modifiers.Any(SyntaxKind.InKeyword);
                callbacks.Add(new CallbackModel(
                    method.Identifier.ValueText,
                    parameterType.ToDisplayString(SymbolFormats.GlobalQualified),
                    isParameterless: false,
                    isStatic,
                    matchesContract));
            }
        }

        if (callbacks.Count == 0) return null;

        // 嵌套容器链（含各层类型关键字——partial 胶水必须按原关键字再声明）
        var containers = new List<ContainerModel>();
        for (var parent = declaration.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TypeDeclarationSyntax outer)
            {
                var keyword = outer switch
                {
                    RecordDeclarationSyntax rec when rec.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "record struct",
                    RecordDeclarationSyntax => "record",
                    StructDeclarationSyntax => "struct",
                    ClassDeclarationSyntax cd when cd.Modifiers.Any(SyntaxKind.StaticKeyword) => "static class",
                    ClassDeclarationSyntax => "class",
                    _ => "class",
                };
                containers.Insert(0, new ContainerModel(keyword, outer.Identifier.ValueText));
            }
        }
        var @namespace = NamespaceUtils.GetNamespaceName(declaration);

        var existingNames = declaration.Members.OfType<MemberDeclarationSyntax>()
            .Where(m => m is MethodDeclarationSyntax or FieldDeclarationSyntax or PropertyDeclarationSyntax)
            .Select(m => m switch
            {
                MethodDeclarationSyntax md => md.Identifier.ValueText,
                FieldDeclarationSyntax fd => fd.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? string.Empty,
                PropertyDeclarationSyntax pd => pd.Identifier.ValueText,
                _ => string.Empty,
            })
            .Where(n => n.Length > 0)
            .ToList();

        return new OwnerModel(
            @namespace,
            containers,
            TypeKeywordOf(declaration),
            typeSymbol.Name,
            isStaticClass,
            existingNames,
            callbacks);
    }

    private static string TypeKeywordOf(TypeDeclarationSyntax declaration)
    {
        if (declaration is RecordDeclarationSyntax record)
        {
            return record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record";
        }
        return declaration switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            _ => "class",
        };
    }
}

/// <summary>嵌套容器层（生成 partial 时按原关键字再声明）。</summary>
internal sealed class ContainerModel
{
    public readonly string Keyword;
    public readonly string Name;

    public ContainerModel(string keyword, string name)
    {
        Keyword = keyword;
        Name = name;
    }
}

/// <summary>符号显示格式（global:: 全限定，生成代码中免 using）。</summary>
internal static class SymbolFormats
{
    public static readonly SymbolDisplayFormat GlobalQualified = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static string PrefixGlobal(string display)
        => display.StartsWith("global::", System.StringComparison.Ordinal) ? display : "global::" + display;
}

internal static class NamespaceUtils
{
    public static string GetNamespaceName(SyntaxNode node)
    {
        var parts = new List<string>();
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case NamespaceDeclarationSyntax ns:
                    parts.Insert(0, ns.Name.ToString());
                    break;
                case FileScopedNamespaceDeclarationSyntax scoped:
                    parts.Insert(0, scoped.Name.ToString());
                    break;
            }
        }
        return string.Join(".", parts);
    }
}
