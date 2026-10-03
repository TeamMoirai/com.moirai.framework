using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// <c>[RegisterSerializer]</c> 标注的序列化后端模型：自注册代码与编号判重共用同一份静态读数。
    /// </summary>
    /// <remarks>
    /// 与迁移器（无特性锚点、按接口扫）不同，本类只认显式标注的实现——测试桩与第三方包装类不该被静默扫进注册表。 <br />
    /// 编号取自实现的 <c>BackendId</c> 成员（表达式体 / 初始化器 / 单个 return 的 get 访问器），经 <c>GetConstantValue</c> 静态求值； <br />
    /// 求不出来即判不了重，由调用方报 MIRAI310 并跳过注册。
    /// </remarks>
    internal sealed class SaveSerializerModel
    {
        /// <summary>标记特性全名。</summary>
        private const string MarkerAttributeName = "Moirai.Atropos.Save.RegisterSerializerAttribute";

        /// <summary>序列化后端接口全名。</summary>
        private const string SerializerInterfaceName = "Moirai.Atropos.Save.ISaveSerializer";

        /// <summary>后端标识成员名（含显式接口实现形态）。</summary>
        private const string BackendIdMemberName = "BackendId";

        /// <summary>类型全限定名（global:: 前缀，可用于 new）。</summary>
        public string TypeFqn { get; private set; }

        /// <summary>类型显示名（诊断消息用）。</summary>
        public string TypeDisplay { get; private set; }

        /// <summary>类型位置（诊断报告用）。</summary>
        public Location Location { get; private set; }

        /// <summary>是否可生成自注册代码（实现接口、非抽象、非泛型、可访问无参构造、嵌套链可访问）。</summary>
        public bool IsRegistrable { get; private set; }

        /// <summary>后端标识是否静态求值成功。</summary>
        public bool HasBackendId { get; private set; }

        /// <summary>静态求出的后端标识（仅 <see cref="HasBackendId"/> 为真时有意义）。</summary>
        public int BackendId { get; private set; }

        /// <summary>
        /// 从语法上下文创建序列化后端模型（未标注标记特性返回 <c>null</c>）。
        /// </summary>
        /// <param name="context">语法提供上下文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>序列化后端模型。</returns>
        public static SaveSerializerModel Create(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)context.Node, cancellationToken) is not INamedTypeSymbol classSymbol)
            {
                return null;
            }

            if (!HasMarkerAttribute(classSymbol))
            {
                return null;
            }

            var model = new SaveSerializerModel
            {
                TypeFqn = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TypeDisplay = classSymbol.ToDisplayString(),
                Location = classSymbol.Locations.Length > 0 ? classSymbol.Locations[0] : Location.None,
                IsRegistrable = ImplementsSerializer(classSymbol)
                    && !classSymbol.IsAbstract
                    && !classSymbol.IsGenericType
                    && HasAccessibleParameterlessConstructor(classSymbol)
                    && HasAccessibleContainingChain(classSymbol),
            };
            model.ReadBackendId(context.SemanticModel, classSymbol, cancellationToken);
            return model;
        }

        /// <summary>
        /// 类型是否标注 <c>[RegisterSerializer]</c>。
        /// </summary>
        private static bool HasMarkerAttribute(INamedTypeSymbol classSymbol)
        {
            foreach (AttributeData attribute in classSymbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == MarkerAttributeName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 类型是否实现 <c>ISaveSerializer</c>。
        /// </summary>
        private static bool ImplementsSerializer(INamedTypeSymbol classSymbol)
        {
            foreach (INamedTypeSymbol interfaceSymbol in classSymbol.AllInterfaces)
            {
                if (interfaceSymbol.ToDisplayString() == SerializerInterfaceName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 静态求出 <c>BackendId</c> 的常量值（跨 partial 文件取该成员所在树的语义模型）。
        /// </summary>
        private void ReadBackendId(SemanticModel model, INamedTypeSymbol classSymbol, CancellationToken cancellationToken)
        {
            foreach (ISymbol member in classSymbol.GetMembers())
            {
                if (member is not IPropertySymbol property || !IsBackendIdMember(property))
                {
                    continue;
                }

                foreach (SyntaxReference reference in property.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax(cancellationToken) is not PropertyDeclarationSyntax declaration)
                    {
                        continue;
                    }

                    ExpressionSyntax candidate = GetBackendIdExpression(declaration);
                    if (candidate == null)
                    {
                        continue;
                    }

                    SemanticModel owner = candidate.SyntaxTree == model.SyntaxTree
                        ? model
                        : model.Compilation.GetSemanticModel(candidate.SyntaxTree);
                    var constant = owner.GetConstantValue(candidate, cancellationToken);
                    if (!constant.HasValue || !TryToBackendId(constant.Value, out int backendId))
                    {
                        continue;
                    }

                    HasBackendId = true;
                    BackendId = backendId;
                    return;
                }
            }
        }

        /// <summary>
        /// 成员是否即后端标识（普通实现或显式接口实现）。
        /// </summary>
        private static bool IsBackendIdMember(IPropertySymbol property)
        {
            if (property.Name == BackendIdMemberName)
            {
                return true;
            }

            foreach (IPropertySymbol implemented in property.ExplicitInterfaceImplementations)
            {
                if (implemented.Name == BackendIdMemberName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 取后端标识的取值表达式：表达式体、初始化器，或只含一条 return 的 get 访问器。
        /// </summary>
        private static ExpressionSyntax? GetBackendIdExpression(PropertyDeclarationSyntax declaration)
        {
            if (declaration.ExpressionBody != null)
            {
                return declaration.ExpressionBody.Expression;
            }

            if (declaration.Initializer != null)
            {
                return declaration.Initializer.Value;
            }

            if (declaration.AccessorList == null)
            {
                return null;
            }

            foreach (AccessorDeclarationSyntax accessor in declaration.AccessorList.Accessors)
            {
                if (!accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                {
                    continue;
                }

                if (accessor.ExpressionBody != null)
                {
                    return accessor.ExpressionBody.Expression;
                }

                if (accessor.Body == null)
                {
                    continue;
                }

                foreach (StatementSyntax statement in accessor.Body.Statements)
                {
                    if (statement is ReturnStatementSyntax returnStatement)
                    {
                        return returnStatement.Expression;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 常量值折算为后端标识（须落在 ushort 范围内；字面量按 int 装箱、const ushort 按自身类型装箱，两者都收）。
        /// </summary>
        private static bool TryToBackendId(object? value, out int backendId)
        {
            backendId = value switch
            {
                byte number => number,
                sbyte number => number,
                short number => number,
                ushort number => number,
                int number => number,
                uint number => checked((int)number),
                long number => checked((int)number),
                _ => -1,
            };
            return backendId >= 0 && backendId <= ushort.MaxValue;
        }

        /// <summary>
        /// 是否具有程序集内可访问的无参构造（隐式默认构造或显式 public/internal 无参构造）。
        /// </summary>
        private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol classSymbol)
        {
            if (classSymbol.InstanceConstructors.Length == 0)
            {
                return true;
            }

            foreach (IMethodSymbol constructor in classSymbol.InstanceConstructors)
            {
                if (constructor.Parameters.Length != 0)
                {
                    continue;
                }

                if (constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.NotApplicable)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 类型及其嵌套链在程序集内可访问（无 private/protected/private-protected 层级；protected-internal 同程序集可访问放行）。
        /// </summary>
        private static bool HasAccessibleContainingChain(INamedTypeSymbol classSymbol)
        {
            for (INamedTypeSymbol current = classSymbol; current != null; current = current.ContainingType)
            {
                switch (current.DeclaredAccessibility)
                {
                    case Accessibility.Public:
                    case Accessibility.Internal:
                    case Accessibility.ProtectedOrInternal:
                    case Accessibility.NotApplicable:
                        continue;
                    default:
                        return false;
                }
            }

            return true;
        }
    }
}
