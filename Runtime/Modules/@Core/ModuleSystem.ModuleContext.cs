using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    public static partial class ModuleSystem
    {
        /// <summary>
        /// 模块上下文，提供跨模块依赖解析能力。
        /// 查找时按 Gameplay > Scene > App 优先返回。
        /// </summary>
        internal readonly struct ModuleContext
        {
            private readonly Dictionary<RuntimeTypeHandle, ScopeBindings> _modules;

            internal ModuleContext(Dictionary<RuntimeTypeHandle, ScopeBindings> modules, ModuleScope scope)
            {
                _modules = modules;
                Scope = scope;
            }

            internal ModuleScope Scope { get; }

            internal T Require<T>() where T : class
            {
                if (_modules.TryGetValue(typeof(T).TypeHandle, out var bindings))
                {
                    var best = bindings.GetBest();
                    if (best != null) return best as T;
                }
                throw new GameException(StringUtility.Format("Module {0} not found.", typeof(T).FullName));
            }

            internal bool TryGet<T>(out T module) where T : class
            {
                if (_modules.TryGetValue(typeof(T).TypeHandle, out var bindings))
                {
                    var best = bindings.GetBest();
                    if (best != null)
                    {
                        module = best as T;
                        return module != null;
                    }
                }
                module = null;
                return false;
            }
        }
    }
}