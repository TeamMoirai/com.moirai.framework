using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// <c>ISaveMigrator</c> 实现类模型（模块初始化器自注册生成用）。
    /// <para>抽象基类合法存在但不注册（静默跳过）；无法实例化注册的实现报 MIRAI302。</para>
    /// </summary>
    internal sealed class MigratorModel
    {
        /// <summary>ISaveMigrator 接口全名。</summary>
        private const string MigratorInterfaceName = "Moirai.Atropos.Save.ISaveMigrator";

        /// <summary>类型全限定名（global:: 前缀，可用于 new）。</summary>
        public string TypeFqn { get; private set; }

        /// <summary>类型显示名（诊断消息用）。</summary>
        public string TypeDisplay { get; private set; }

        /// <summary>是否可生成自注册代码。</summary>
        public bool IsRegistrable { get; private set; }

        /// <summary>类型位置（诊断报告用）。</summary>
        public Location Location { get; private set; }

        /// <summary>
        /// 从语法上下文创建迁移器模型（非迁移器实现/抽象类返回 null）。
        /// </summary>
        /// <param name="context">语法提供上下文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>迁移器模型。</returns>
        public static MigratorModel Create(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)context.Node, cancellationToken) is not INamedTypeSymbol classSymbol)
            {
                return null;
            }

            bool implements = false;
            foreach (INamedTypeSymbol interfaceSymbol in classSymbol.AllInterfaces)
            {
                if (interfaceSymbol.ToDisplayString() == MigratorInterfaceName)
                {
                    implements = true;
                    break;
                }
            }

            if (!implements || classSymbol.IsAbstract)
            {
                return null;
            }

            bool registrable = !classSymbol.IsGenericType
                && HasAccessibleParameterlessConstructor(classSymbol)
                && HasAccessibleContainingChain(classSymbol);
            return new MigratorModel
            {
                TypeFqn = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TypeDisplay = classSymbol.ToDisplayString(),
                IsRegistrable = registrable,
                Location = classSymbol.Locations.Length > 0 ? classSymbol.Locations[0] : Location.None,
            };
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
