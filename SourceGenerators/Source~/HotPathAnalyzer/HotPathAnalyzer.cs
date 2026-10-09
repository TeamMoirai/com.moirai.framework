using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 诊断分析器：检查 <c>[HotPath]</c> 标注成员的热路径铁律（装箱与匿名函数）。
    /// </summary>
    /// <remarks>
    /// MIRAI600（Warning）：热路径内值类型装箱进 object/接口/System.ValueType/System.Enum 目标——每次装箱都是一次 GC 堆分配，打破 0-Alloc 契约。 <br />
    /// MIRAI601（Warning）：热路径方法体出现匿名函数——委托实例与闭包展示类按次分配，且绕过方法组缓存；改用热路径外缓存的静态委托。 <br />
    /// 类型级标注覆盖该类型声明的全部方法体（构造函数与属性访问器在内）；匿名函数自身的块由外层遍历覆盖，已跳过避免重复报告。
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class HotPathAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>[HotPath] 特性类的完整元数据名。</summary>
        private const string HotPathAttributeFullyQualifiedName = "Moirai.Atropos.HotPathAttribute";

        public static readonly DiagnosticDescriptor BoxingRule = new DiagnosticDescriptor(
            id: "MIRAI600",
            title: "热路径内禁止值类型装箱",
            messageFormat: "热路径 '{0}' 中值类型 '{1}' 装箱到 '{2}'：改用泛型/值类型专用重载直传，无法避免时把装箱实例缓存到热路径外",
            category: "Performance",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Boxing in a [HotPath] method allocates a boxed instance on the GC heap per call, defeating the zero-allocation contract. Pass value types through generic or struct-specific overloads; when a boxed value is unavoidable, cache the box outside the hot path.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        public static readonly DiagnosticDescriptor AnonymousFunctionRule = new DiagnosticDescriptor(
            id: "MIRAI601",
            title: "热路径内禁止匿名函数",
            messageFormat: "热路径 '{0}' 内出现匿名函数：委托实例与闭包按次分配，改用热路径外缓存的方法组或静态委托",
            category: "Performance",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Anonymous functions inside a [HotPath] method allocate a delegate (and a closure display class when capturing) on each execution. Replace them with cached method groups or static delegates prepared outside the hot path.",
            customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(BoxingRule, AnonymousFunctionRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterOperationBlockAction(AnalyzeOperationBlock);
        }

        private static void AnalyzeOperationBlock(OperationBlockAnalysisContext context)
        {
            if (context.OwningSymbol is not IMethodSymbol method)
                return;

            // 匿名函数自身的块已由外层方法体遍历覆盖，跳过避免重复报告
            if (method.MethodKind == MethodKind.AnonymousFunction)
                return;

            if (!IsHotPath(method))
                return;

            var visitor = new HotPathVisitor(method, context.ReportDiagnostic);
            foreach (var block in context.OperationBlocks)
                visitor.Visit(block);
        }

        private static bool IsHotPath(IMethodSymbol method)
        {
            if (HasHotPathAttribute(method))
                return true;

            // 属性访问器：属性上的标注同时约束 get 与 set
            if (method.AssociatedSymbol != null && HasHotPathAttribute(method.AssociatedSymbol))
                return true;

            return method.ContainingType != null && HasHotPathAttribute(method.ContainingType);
        }

        private static bool HasHotPathAttribute(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == HotPathAttributeFullyQualifiedName)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 热路径方法体遍历器：装箱转换与匿名函数节点。
        /// </summary>
        private sealed class HotPathVisitor : OperationWalker
        {
            private readonly IMethodSymbol _method;
            private readonly Action<Diagnostic> _report;

            public HotPathVisitor(IMethodSymbol method, Action<Diagnostic> report)
            {
                _method = method;
                _report = report;
            }

            public override void VisitConversion(IConversionOperation operation)
            {
                var targetType = operation.Type;
                var sourceType = operation.Operand?.Type;

                // 值类型 → object/接口/System.ValueType/System.Enum 在 C# 里只有装箱一种转换形态
                if (targetType != null
                    && sourceType != null
                    && IsBoxTarget(targetType)
                    && IsReportableValueType(sourceType))
                {
                    _report(Diagnostic.Create(
                        BoxingRule,
                        operation.Syntax.GetLocation(),
                        _method.ToDisplayString(),
                        sourceType.ToDisplayString(),
                        targetType.ToDisplayString()));
                }

                base.VisitConversion(operation);
            }

            public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation)
            {
                _report(Diagnostic.Create(
                    AnonymousFunctionRule,
                    operation.Syntax.GetLocation(),
                    _method.ToDisplayString()));

                // 继续下钻：闭包体内的装箱也属于热路径
                base.VisitAnonymousFunction(operation);
            }

            private static bool IsBoxTarget(ITypeSymbol targetType)
            {
                return targetType.SpecialType == SpecialType.System_Object
                    || targetType.SpecialType == SpecialType.System_ValueType
                    || targetType.SpecialType == SpecialType.System_Enum
                    || targetType.TypeKind == TypeKind.Interface;
            }

            private static bool IsReportableValueType(ITypeSymbol sourceType)
            {
                // 泛型参数的装箱形态取决于运行时的共享实例化，先不报告
                if (sourceType.TypeKind == TypeKind.TypeParameter)
                    return false;

                return sourceType.IsValueType
                    && sourceType.SpecialType != SpecialType.System_Void;
            }
        }
    }
}
