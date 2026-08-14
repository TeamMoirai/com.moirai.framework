using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    public static partial class ModuleSystem
    {
        // --- 诊断 API（InternalsVisibleTo → Moirai.Atropos.Editor）---

        internal struct DiagnosticInfo
        {
            public string InterfaceType;
            public string ImplementationType;
            public ModuleScope Scope;
            public int Priority;
            public bool HasUpdate;
            public bool HasFixedUpdate;
            public bool HasLateUpdate;
            public bool HasGizmo;
        }

        internal static List<DiagnosticInfo> GetDiagnosticInfo()
        {
            var result = new List<DiagnosticInfo>(s_ModuleMaps.Count);
            foreach (var kvp in s_ModuleMaps)
            {
                var bindings = kvp.Value;
                // 每个非 null 的 scope 绑定生成一条诊断记录
                if (bindings.App != null)
                    result.Add(BuildDiagInfo(kvp.Key, bindings.App, ModuleScope.App));
                if (bindings.Scene != null)
                    result.Add(BuildDiagInfo(kvp.Key, bindings.Scene, ModuleScope.Scene));
                if (bindings.Gameplay != null)
                    result.Add(BuildDiagInfo(kvp.Key, bindings.Gameplay, ModuleScope.Gameplay));
            }
            return result;
        }

        private static DiagnosticInfo BuildDiagInfo(RuntimeTypeHandle handle, Module module, ModuleScope scope)
        {
            // RuntimeTypeHandle.ToString() 不返回类型名，必须用 GetTypeFromHandle 还原
            var type = Type.GetTypeFromHandle(handle);

            return new DiagnosticInfo
            {
                InterfaceType = type.FullName,
                ImplementationType = module.GetType().FullName,
                Scope = scope,
                Priority = module.Priority,
                HasUpdate = module is IUpdateModule,
                HasFixedUpdate = module is IFixedUpdateModule,
                HasLateUpdate = module is ILateUpdateModule,
                HasGizmo = module is IGizmoModule,
            };
        }
    }
}