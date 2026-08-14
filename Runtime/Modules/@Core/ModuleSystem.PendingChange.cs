using System;

namespace Moirai.Atropos
{
    public static partial class ModuleSystem
    {
        /// <summary>
        /// 迭代期间产生的延迟变更（注册/注销），本轮迭代结束后统一应用。
        /// </summary>
        internal struct PendingChange
        {
            public readonly bool IsRegister;
            public readonly Module Module;
            public readonly Type InterfaceType;
            public readonly ModuleScope Scope;

            private PendingChange(bool isRegister, Module module, Type interfaceType, ModuleScope scope)
            {
                IsRegister = isRegister;
                Module = module;
                InterfaceType = interfaceType;
                Scope = scope;
            }

            public static PendingChange Register(Module module, Type interfaceType, ModuleScope scope)
                => new PendingChange(true, module, interfaceType, scope);

            /// <summary>注销无需接口与作用域信息，由模块 entry 直接持有。</summary>
            public static PendingChange Unregister(Module module)
                => new PendingChange(false, module, null, default);
        }
    }
}
