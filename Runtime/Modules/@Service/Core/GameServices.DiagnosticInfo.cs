using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    public static partial class GameServices
    {
        internal struct DiagnosticInfo
        {
            public string InterfaceType;
            public string ImplementationType;
            public EServiceScopeKind Scope;
            public int Priority;
            public bool HasUpdate;
            public bool HasFixedUpdate;
            public bool HasLateUpdate;
            public bool HasGizmo;
        }

        internal static List<DiagnosticInfo> GetDiagnosticInfo()
        {
            var result = new List<DiagnosticInfo>();
            foreach (var kvp in s_ServiceMaps)
            {
                var bindings = kvp.Value;
                if (bindings.App != null) result.Add(BuildDiagInfo(kvp.Key, bindings.App, EServiceScopeKind.App));
                if (bindings.Scene != null) result.Add(BuildDiagInfo(kvp.Key, bindings.Scene, EServiceScopeKind.Scene));
                if (bindings.Gameplay != null) result.Add(BuildDiagInfo(kvp.Key, bindings.Gameplay, EServiceScopeKind.Gameplay));
            }
            return result;
        }

        private static DiagnosticInfo BuildDiagInfo(RuntimeTypeHandle handle, IService service, EServiceScopeKind scope)
        {
            var type = Type.GetTypeFromHandle(handle);
            return new DiagnosticInfo
            {
                InterfaceType = type != null ? type.FullName : "<unknown>",
                ImplementationType = service.GetType().FullName,
                Scope = scope,
                Priority = service.Priority,
                HasUpdate = service is IServiceTickable,
                HasFixedUpdate = service is IServiceFixedTickable,
                HasLateUpdate = service is IServiceLateTickable,
                HasGizmo = service is IServiceGizmoDrawable,
            };
        }
    }
}
