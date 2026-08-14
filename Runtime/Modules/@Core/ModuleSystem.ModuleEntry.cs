using System;

namespace Moirai.Atropos
{
    public static partial class ModuleSystem
    {
        private struct ModuleEntry
        {
            public RuntimeTypeHandle InterfaceHandle;
            public int AllIndex;
            public int UpdateIndex;
            public int FixedUpdateIndex;
            public int LateUpdateIndex;
            public int GizmoIndex;
            public ModuleScope Scope;
        }
    }
}
