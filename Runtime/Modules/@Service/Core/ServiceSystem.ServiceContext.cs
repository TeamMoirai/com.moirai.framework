using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    public static partial class ServiceSystem
    {
        internal readonly struct ServiceContext
        {
            private readonly Dictionary<RuntimeTypeHandle, ScopeBindings> _services;
            private readonly ServiceScope _preferredScope;

            internal ServiceContext(Dictionary<RuntimeTypeHandle, ScopeBindings> services, ServiceScope preferredScope)
            {
                _services = services;
                _preferredScope = preferredScope;
            }

            internal T Require<T>() where T : class
            {
                if (TryGet(out T service)) return service;
                throw new GameException(StringUtility.Format("Service {0} not found.", typeof(T).FullName));
            }

            internal bool TryGet<T>(out T service) where T : class
            {
                // Preferred scope first
                if (_preferredScope != null && _preferredScope.TryGet<T>(out service))
                    return true;

                // Fallback to GetBest
                if (_services.TryGetValue(typeof(T).TypeHandle, out var bindings))
                {
                    var best = bindings.GetBest();
                    if (best != null)
                    {
                        service = best as T;
                        return service != null;
                    }
                }
                service = null;
                return false;
            }

            internal T RequireApp<T>() where T : class => RequireScope<T>(ServiceScopeKind.App);
            internal T RequireScene<T>() where T : class => RequireScope<T>(ServiceScopeKind.Scene);
            internal T RequireGameplay<T>() where T : class => RequireScope<T>(ServiceScopeKind.Gameplay);

            private T RequireScope<T>(ServiceScopeKind scope) where T : class
            {
                if (_services.TryGetValue(typeof(T).TypeHandle, out var bindings))
                {
                    var service = bindings.Get(scope);
                    if (service != null) return service as T;
                }
                throw new GameException(StringUtility.Format("Service {0} not found in {1} scope.", typeof(T).FullName, scope));
            }
        }
    }
}
