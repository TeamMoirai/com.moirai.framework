using System;

namespace Moirai.Atropos
{
    public static partial class ServiceSystem
    {
        private struct ServiceEntry
        {
            public RuntimeTypeHandle InterfaceHandle;
            public int AllIndex;
            public int UpdateIndex;
            public int FixedUpdateIndex;
            public int LateUpdateIndex;
            public int GizmoIndex;
            public ServiceScope Scope;

            /// <summary>已入队延迟注销，防止迭代期间重复入队。</summary>
            public bool PendingRemove;
        }
    }
}
