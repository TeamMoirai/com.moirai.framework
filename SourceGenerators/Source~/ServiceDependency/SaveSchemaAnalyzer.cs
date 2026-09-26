using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 存档二进制后端模式快照分析器（MIRAI4xx 系列）。
    /// <para>MIRAI400：[SaveData(Backend=MessagePack/MemoryPack/Protobuf)] 类型的成员键序号（[Key]/[MemoryPackOrder]/[ProtoMember]）
    /// 与快照不符告警——二进制线格式按序号寻址，跨版本重排破坏旧档读取。</para>
    /// <para>MIRAI401：快照成员被删除且类型未重写 <c>OnMigrate</c> 迁移钩子告警——旧档字段将静默丢失。</para>
    /// <para>快照来自附加文件 <c>.SaveSchemaSnapshot</c>（行格式：<c>类型全限定名|成员名:序号;成员名:序号…</c>，序号 -1 = 字符串键）。
    /// 快照文件不存在时分析器完全静默（自行加入版本控制即可启用；Unity 编辑器无 AdditionalFiles 界面时经 csc.rsp 的
    /// <c>/additionalfile:</c> 或 CI 的 dotnet build 接线）。</para>
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SaveSchemaAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>MIRAI400：二进制后端成员键序号与快照不符。</summary>
        public static readonly DiagnosticDescriptor KeyOrderMismatchRule = new DiagnosticDescriptor(
            id: "MIRAI400",
            title: "二进制存档类型键序号与快照不符",
            messageFormat: "[SaveData] 类型 '{0}' 的成员 '{1}' 键序号 {2} 与快照记录 {3} 不符——二进制后端键序跨版本不得重排（会破坏旧档读取）",
            category: "SaveSchema",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Integer-keyed binary serializers (MessagePack/MemoryPack/Protobuf) address fields by ordinal; reordering keys between versions breaks reading old saves.");

        /// <summary>MIRAI401：二进制后端快照成员被删除且未提供迁移钩子。</summary>
        public static readonly DiagnosticDescriptor MemberDeletedRule = new DiagnosticDescriptor(
            id: "MIRAI401",
            title: "二进制存档类型成员被删除且未提供迁移钩子",
            messageFormat: "[SaveData] 类型 '{0}' 删除了快照成员 '{1}' 且未重写 OnMigrate——旧档该字段数据将静默丢失",
            category: "SaveSchema",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Deleting a member of a binary-backend save type without an OnMigrate hook silently loses the old field data.");

        private const string SaveDataAttributeName = "Moirai.Atropos.Save.SaveDataAttribute";
        private const string SaveDataBlockBaseName = "Moirai.Atropos.Save.SaveDataBlock";
        private const string SnapshotFileName = ".SaveSchemaSnapshot";
        private const string KeyAttributeName = "MessagePack.KeyAttribute";
        private const string MessagePackObjectAttributeName = "MessagePack.MessagePackObjectAttribute";
        private const string MemoryPackOrderAttributeName = "MemoryPack.MemoryPackOrderAttribute";
        private const string MemoryPackIgnoreAttributeName = "MemoryPack.MemoryPackIgnoreAttribute";
        private const string ProtoMemberAttributeName = "ProtoBuf.ProtoMemberAttribute";

        /// <summary>类型名显示格式（快照键：命名空间.嵌套链，无 global:: 前缀）。</summary>
        private static readonly SymbolDisplayFormat s_SnapshotTypeFormat =
            SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(KeyOrderMismatchRule, MemberDeletedRule);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(StartAnalysis);
        }

        /// <summary>
        /// 编译起始：解析快照附加文件后注册类型符号分析。
        /// </summary>
        private static void StartAnalysis(CompilationStartAnalysisContext context)
        {
#pragma warning disable CS0618 // CodeAnalysis 4.3 的 AdditionalFiles 属性标记过时（后续版本更名 AdditionalTexts）；本包锁定 4.3.0
            Dictionary<string, Dictionary<string, int>> snapshot = LoadSnapshot(context.Options.AdditionalFiles, context.CancellationToken);
#pragma warning restore CS0618
            if (snapshot.Count == 0)
            {
                return;
            }

            context.RegisterSymbolAction(symbolContext => AnalyzeType(symbolContext, snapshot), SymbolKind.NamedType);
        }

        /// <summary>
        /// 解析快照附加文件（行格式：类型全限定名|成员:序号;成员:序号…；容忍空行/注释/畸形行）。
        /// </summary>
        private static Dictionary<string, Dictionary<string, int>> LoadSnapshot(ImmutableArray<AdditionalText> additionalTexts, System.Threading.CancellationToken cancellationToken)
        {
            var snapshot = new Dictionary<string, Dictionary<string, int>>(System.StringComparer.Ordinal);
            foreach (AdditionalText text in additionalTexts)
            {
                if (!string.Equals(Path.GetFileName(text.Path), SnapshotFileName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SourceText sourceText = text.GetText(cancellationToken);
                if (sourceText == null)
                {
                    continue;
                }

                foreach (TextLine line in sourceText.Lines)
                {
                    string content = line.ToString().Trim();
                    if (content.Length == 0 || content.StartsWith("#", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separator = content.IndexOf('|');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string typeName = content.Substring(0, separator);
                    var members = new Dictionary<string, int>(System.StringComparer.Ordinal);
                    foreach (string pair in content.Substring(separator + 1).Split(';'))
                    {
                        int colon = pair.LastIndexOf(':');
                        if (colon <= 0)
                        {
                            continue;
                        }

                        if (int.TryParse(pair.Substring(colon + 1), out int number))
                        {
                            members[pair.Substring(0, colon)] = number;
                        }
                    }

                    snapshot[typeName] = members;
                }
            }

            return snapshot;
        }

        /// <summary>
        /// 分析单个类型符号：二进制后端 [SaveData] 类型与快照比对。
        /// </summary>
        private static void AnalyzeType(SymbolAnalysisContext context, Dictionary<string, Dictionary<string, int>> snapshot)
        {
            if (context.Symbol is not INamedTypeSymbol typeSymbol || typeSymbol.TypeKind != TypeKind.Class)
            {
                return;
            }

            AttributeData saveData = null;
            foreach (AttributeData attribute in typeSymbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == SaveDataAttributeName)
                {
                    saveData = attribute;
                    break;
                }
            }

            if (saveData == null)
            {
                return;
            }

            int backend = 0;
            foreach (KeyValuePair<string, TypedConstant> namedArgument in saveData.NamedArguments)
            {
                if (namedArgument.Key == "Backend" && namedArgument.Value.Value is int backendValue)
                {
                    backend = backendValue;
                }
            }

            // 仅二进制后端（1=MessagePack 2=MemoryPack 3=Protobuf）受键序契约约束
            if (backend < 1 || backend > 3)
            {
                return;
            }

            string typeName = typeSymbol.ToDisplayString(s_SnapshotTypeFormat);
            if (!snapshot.TryGetValue(typeName, out Dictionary<string, int> snapshotMembers))
            {
                return;
            }

            Dictionary<string, int> currentMembers = ExtractMemberKeys(typeSymbol, backend);
            Location location = typeSymbol.Locations.Length > 0 ? typeSymbol.Locations[0] : Location.None;

            foreach (KeyValuePair<string, int> pair in currentMembers)
            {
                if (snapshotMembers.TryGetValue(pair.Key, out int snapshotNumber) && snapshotNumber != pair.Value)
                {
                    context.ReportDiagnostic(Diagnostic.Create(KeyOrderMismatchRule, location, typeName, pair.Key, pair.Value, snapshotNumber));
                }
            }

            bool hasMigrateHook = HasOnMigrateOverride(typeSymbol);
            if (hasMigrateHook)
            {
                return;
            }

            foreach (KeyValuePair<string, int> pair in snapshotMembers)
            {
                if (!currentMembers.ContainsKey(pair.Key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(MemberDeletedRule, location, typeName, pair.Key));
                }
            }
        }

        /// <summary>
        /// 提取类型的成员键映射（成员名 → 序号；MessagePack 字符串键模式序号记 -1）。
        /// </summary>
        private static Dictionary<string, int> ExtractMemberKeys(INamedTypeSymbol typeSymbol, int backend)
        {
            var members = new Dictionary<string, int>(System.StringComparer.Ordinal);

            if (backend == 1 && IsMessagePackStringKeyed(typeSymbol))
            {
                foreach (ISymbol member in EnumerateDataMembers(typeSymbol, null))
                {
                    members[member.Name] = -1;
                }

                return members;
            }

            string keyAttributeName = backend switch
            {
                1 => KeyAttributeName,
                2 => MemoryPackOrderAttributeName,
                _ => ProtoMemberAttributeName,
            };

            int implicitIndex = 0;
            foreach (ISymbol member in EnumerateDataMembers(typeSymbol, backend == 2 ? MemoryPackIgnoreAttributeName : null))
            {
                int? number = TryGetAttributeIntArgument(member, keyAttributeName);
                if (number.HasValue)
                {
                    members[member.Name] = number.Value;
                }
                else if (backend == 2)
                {
                    // MemoryPack 未显式标注 Order 时按声明序隐式编号
                    members[member.Name] = implicitIndex;
                }

                implicitIndex++;
            }

            return members;
        }

        /// <summary>
        /// 枚举序列化数据成员（实例字段/属性，排除 static/const 与被忽略成员）。
        /// </summary>
        private static IEnumerable<ISymbol> EnumerateDataMembers(INamedTypeSymbol typeSymbol, string ignoreAttributeName)
        {
            foreach (ISymbol member in typeSymbol.GetMembers())
            {
                if (member.IsStatic)
                {
                    continue;
                }

                if (member is not IFieldSymbol && member is not IPropertySymbol)
                {
                    continue;
                }

                if (member is IFieldSymbol field && field.IsConst)
                {
                    continue;
                }

                if (ignoreAttributeName != null && HasAttribute(member, ignoreAttributeName))
                {
                    continue;
                }

                yield return member;
            }
        }

        /// <summary>
        /// MessagePack 类型是否字符串键模式（[MessagePackObject(true)]）。
        /// </summary>
        private static bool IsMessagePackStringKeyed(INamedTypeSymbol typeSymbol)
        {
            foreach (AttributeData attribute in typeSymbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == MessagePackObjectAttributeName
                    && attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is bool keyAsPropertyName
                    && keyAsPropertyName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 读取成员特性首个整型构造参数。
        /// </summary>
        private static int? TryGetAttributeIntArgument(ISymbol member, string attributeName)
        {
            foreach (AttributeData attribute in member.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == attributeName
                    && attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is int number)
                {
                    return number;
                }
            }

            return null;
        }

        /// <summary>
        /// 成员是否标注指定特性。
        /// </summary>
        private static bool HasAttribute(ISymbol member, string attributeName)
        {
            foreach (AttributeData attribute in member.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == attributeName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 类型或其基类链（直至 SaveDataBlock）是否重写 OnMigrate。
        /// </summary>
        private static bool HasOnMigrateOverride(INamedTypeSymbol typeSymbol)
        {
            for (INamedTypeSymbol current = typeSymbol; current != null; current = current.BaseType)
            {
                if (current.ToDisplayString() == SaveDataBlockBaseName)
                {
                    return false;
                }

                foreach (ISymbol member in current.GetMembers("OnMigrate"))
                {
                    if (member is IMethodSymbol method && method.IsOverride)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
