using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 校验 <c>[ProviderHandlerHost]</c> 声明的默认处理器能否被生成的兜底工厂实例化。
    /// </summary>
    internal static class HandlerContract
    {
        /// <summary>FrameworkHandler 全名：生命周期基座（Internal_Init/Internal_Shutdown 由生成代码强制调用）。</summary>
        private const string FrameworkHandlerFullName = "Moirai.Atropos.FrameworkHandler";

        /// <summary>声明的默认类型须实现处理器契约（继承类或实现接口）、继承 FrameworkHandler 生命周期基座、为具体类且含可访问的无参构造。</summary>
        /// <param name="declaredDefault">声明的默认处理器类型。</param>
        /// <param name="handlerType">宿主的处理器契约类型（类或接口）。</param>
        /// <param name="sameAssembly">默认类型与宿主是否同程序集（决定 internal 可见性口径）。</param>
        /// <param name="reasons">全部不满足项的分号描述；校验通过时为空串。</param>
        /// <returns>可实例化返回 true。</returns>
        internal static bool IsValidDefaultHandler(INamedTypeSymbol declaredDefault, INamedTypeSymbol handlerType, bool sameAssembly, out string reasons)
        {
            var failures = new List<string>();

            if (!IsAssignableToContract(declaredDefault, handlerType))
            {
                failures.Add($"未实现处理器契约 '{handlerType.Name}'");
            }

            if (!InheritsFrameworkHandler(declaredDefault))
            {
                failures.Add($"未继承生命周期基座 '{FrameworkHandlerFullName}'（Internal_Init/Internal_Shutdown 由生成代码强制调用）");
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

        /// <summary>基类链命中（类契约）或接口集合命中（接口契约）都算实现——生成器对两种契约形态统一放行。</summary>
        private static bool IsAssignableToContract(INamedTypeSymbol declaredDefault, INamedTypeSymbol handlerType)
        {
            if (handlerType.TypeKind == TypeKind.Interface)
            {
                return declaredDefault.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, handlerType));
            }

            return InheritsHandlerType(declaredDefault, handlerType);
        }

        /// <summary>沿基类链查找 FrameworkHandler——缺它则生成代码的 Internal_Init/Internal_Shutdown 调用在运行期失效。</summary>
        private static bool InheritsFrameworkHandler(INamedTypeSymbol declaredDefault)
        {
            var current = declaredDefault;
            while (current != null)
            {
                if (current.BaseType != null &&
                    string.Equals(current.BaseType.ToDisplayString(), FrameworkHandlerFullName, StringComparison.Ordinal))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
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
