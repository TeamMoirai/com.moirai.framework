using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    public static partial class GameServices
    {
        /// <summary>
        /// 服务级依赖解析上下文。每个服务实例持有一份，封装跨服务查找逻辑。
        /// 引用 <see cref="GameServices.s_ServiceMaps"/>（字典引用，始终看到最新状态）。
        /// </summary>
        internal readonly struct ServiceContext
        {
            private readonly Dictionary<RuntimeTypeHandle, ScopeBindings> _services;
            private readonly ServiceScope _preferredScope;

            internal ServiceContext(Dictionary<RuntimeTypeHandle, ScopeBindings> services, ServiceScope preferredScope)
            {
                _services = services;
                _preferredScope = preferredScope;
            }

            #region 查找 [LOOKUP]

            /// <summary>获取依赖服务（查找顺序：当前作用域 → GetBest 回退）。未找到抛 <see cref="GameException"/>。</summary>
            internal T Require<T>() where T : class
            {
                if (TryGet(out T service)) return service;
                throw new GameException(StringUtility.Format("Service {0} not found.", typeof(T).FullName));
            }

            /// <summary>尝试获取依赖服务。</summary>
            internal bool TryGet<T>(out T service) where T : class
            {
                // 优先查当前作用域
                if (_preferredScope != null && _preferredScope.TryGet<T>(out service))
                    return true;

                // 回退到 GetBest（Gameplay > Scene > App）
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

            #endregion

            #region 指定作用域查找 [SCOPED LOOKUP]

            internal T RequireApp<T>() where T : class => RequireScope<T>(EServiceScopeKind.App);
            internal T RequireScene<T>() where T : class => RequireScope<T>(EServiceScopeKind.Scene);
            internal T RequireGameplay<T>() where T : class => RequireScope<T>(EServiceScopeKind.Gameplay);

            private T RequireScope<T>(EServiceScopeKind scope) where T : class
            {
                if (_services.TryGetValue(typeof(T).TypeHandle, out var bindings))
                {
                    var service = bindings.Get(scope);
                    if (service != null) return service as T;
                }
                throw new GameException(StringUtility.Format("Service {0} not found in {1} scope.", typeof(T).FullName, scope));
            }

            #endregion
        }
    }
}
