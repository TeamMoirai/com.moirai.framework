using System;

namespace Moirai.Atropos
{
    public static partial class ModuleSystem
    {
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
        }
    }
}