using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 校验 <c>[ProviderHandlerHost]</c> 声明的默认处理器能否被生成的兜底工厂实例化。
    /// </summary>
    internal static class HandlerContract
    {
        /// <summary>声明的默认类型须继承处理器类型、为具体类且含可访问的无参构造。</summary>
        /// <param name="declaredDefault">声明的默认处理器类型。</param>
        /// <param name="handlerType">宿主的处理器契约类型。</param>
        /// <param name="sameAssembly">默认类型与宿主是否同程序集（决定 internal 可见性口径）。</param>
        /// <param name="reasons">全部不满足项的分号描述；校验通过时为空串。</param>
        /// <returns>可实例化返回 true。</returns>
        internal static bool IsValidDefaultHandler(INamedTypeSymbol declaredDefault, INamedTypeSymbol handlerType, bool sameAssembly, out string reasons)
        {
            var failures = new List<string>();

            if (!InheritsHandlerType(declaredDefault, handlerType))
            {
                failures.Add($"未继承处理器类型 '{handlerType.Name}'");
            }

            if (declaredDefault.IsAbstract || declaredDefault.TypeKind != TypeKind.Class)
            {
                failures.Add("须为非抽象 class");
            }

            if (!HasAccessibleParameterlessCtor(declaredDefault, sameAssembly))
            {
                failures.Add("无可访问的无参构造函数");
            }

            reasons = string.Join("；", failures);
            return failures.Count == 0;
        }

        private static bool InheritsHandlerType(INamedTypeSymbol declaredDefault, INamedTypeSymbol handlerType)
        {
            var current = declaredDefault;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, handlerType))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static bool HasAccessibleParameterlessCtor(INamedTypeSymbol declaredDefault, bool sameAssembly)
        {
            foreach (var ctor in declaredDefault.InstanceConstructors)
            {
                if (ctor.Parameters.IsEmpty && IsAccessibleFromHost(ctor, sameAssembly))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>构造函数可达性 = 自身声明级别 + 全部包含类型的声明级别均对宿主可见（4.3.0 无 EffectiveAccessibility 扩展，按包含链逐级判定）。</summary>
        private static bool IsAccessibleFromHost(ISymbol symbol, bool sameAssembly)
        {
            if (!IsAccessibleLevel(symbol.DeclaredAccessibility, sameAssembly))
            {
                return false;
            }

            var container = symbol.ContainingType;
            while (container != null)
            {
                if (!IsAccessibleLevel(container.DeclaredAccessibility, sameAssembly))
                {
                    return false;
                }

                container = container.ContainingType;
            }

            return true;
        }

        private static bool IsAccessibleLevel(Accessibility accessibility, bool sameAssembly)
        {
            return sameAssembly
                ? accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
                : accessibility == Accessibility.Public;
        }
    }
}
