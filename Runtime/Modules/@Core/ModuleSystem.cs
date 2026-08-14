using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架模块实现类管理系统。
    /// </summary>
    public static partial class ModuleSystem
    {
        private const int DESIGN_MODULE_COUNT = 16;
        private const int MISSING_INDEX = -1;

        // 每个接口类型可注册在不同 Scope 中，查找时按 Gameplay > Scene > App 优先返回
        private static readonly Dictionary<RuntimeTypeHandle, ScopeBindings> s_ModuleMaps
            = new Dictionary<RuntimeTypeHandle, ScopeBindings>(DESIGN_MODULE_COUNT);

        // 按优先级排序的全量模块列表
        private static readonly List<Module> s_Modules = new List<Module>(DESIGN_MODULE_COUNT);

        // 生命周期列表 — 元素限定为对应接口：编译期防止误注册，轮询热路径零类型转换
        private static readonly List<IUpdateModule> s_UpdateModules = new List<IUpdateModule>(DESIGN_MODULE_COUNT);
        private static readonly List<IFixedUpdateModule> s_FixedUpdateModules = new List<IFixedUpdateModule>(DESIGN_MODULE_COUNT);
        private static readonly List<ILateUpdateModule> s_LateUpdateModules = new List<ILateUpdateModule>(DESIGN_MODULE_COUNT);
        private static readonly List<IGizmoModule> s_GizmoModules = new List<IGizmoModule>(DESIGN_MODULE_COUNT);

        // 模块 → 在各列表中的索引（用于 O(1) swap-remove）
        private static readonly Dictionary<Module, ModuleEntry> s_Entries
            = new Dictionary<Module, ModuleEntry>(DESIGN_MODULE_COUNT, ReferenceComparer<Module>.Instance);

        // 迭代安全 — PendingChanges
        internal static readonly List<PendingChange> pendingChanges = new List<PendingChange>();
        internal static bool isIterating;

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public static void Update(float elapseSeconds, float realElapseSeconds)
        {
            isIterating = true;
            try
            {
                int count = s_UpdateModules.Count;
                for (int i = 0; i < count; i++)
                {
                    s_UpdateModules[i].Update(elapseSeconds, realElapseSeconds);
                }
            }
            finally
            {
                isIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        public static void FixedUpdate(float elapseSeconds, float realElapseSeconds)
        {
            isIterating = true;
            try
            {
                int count = s_FixedUpdateModules.Count;
                for (int i = 0; i < count; i++)
                {
                    s_FixedUpdateModules[i].FixedUpdate(elapseSeconds, realElapseSeconds);
                }
            }
            finally
            {
                isIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        public static void LateUpdate(float elapseSeconds, float realElapseSeconds)
        {
            isIterating = true;
            try
            {
                int count = s_LateUpdateModules.Count;
                for (int i = 0; i < count; i++)
                {
                    s_LateUpdateModules[i].LateUpdate(elapseSeconds, realElapseSeconds);
                }
            }
            finally
            {
                isIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 所有游戏框架模块绘制 Gizmos。
        /// </summary>
        public static void DrawGizmos()
        {
            isIterating = true;
            try
            {
                int count = s_GizmoModules.Count;
                for (int i = 0; i < count; i++)
                {
                    s_GizmoModules[i].OnDrawGizmos();
                }
            }
            finally
            {
                isIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 关闭并清理所有游戏框架模块。按 Gameplay → Scene → App 逆序关闭。
        /// </summary>
        public static void Shutdown()
        {
            ShutdownScope(ModuleScope.Gameplay);
            ShutdownScope(ModuleScope.Scene);
            ShutdownScope(ModuleScope.App);
            ClearAll();
        }

        /// <summary>
        /// 关闭指定作用域的所有模块。
        /// </summary>
        /// <param name="scope">要关闭的作用域。</param>
        public static void ShutdownScope(ModuleScope scope)
        {
            for (int i = s_Modules.Count - 1; i >= 0; i--)
            {
                var module = s_Modules[i];
                if (!s_Entries.TryGetValue(module, out var entry)) continue;
                if (entry.Scope != scope) continue;

                module.Shutdown();
                RemoveModuleInternal(module, entry);
            }
        }

        /// <summary>
        /// 获取游戏框架模块。
        /// </summary>
        /// <typeparam name="T">要获取的游戏框架模块类型。</typeparam>
        /// <returns>要获取的游戏框架模块。</returns>
        /// <remarks>如果要获取的游戏框架模块不存在，则自动创建该游戏框架模块。</remarks>
        public static T GetModule<T>() where T : class
        {
            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new GameException(StringUtility.Format("You must get module by interface, but '{0}' is not.", interfaceType.FullName));
            }

            if (s_ModuleMaps.TryGetValue(interfaceType.TypeHandle, out var bindings))
            {
                var best = bindings.GetBest();
                if (best != null) return best as T;
            }

            // 如果要获取的游戏框架模块不存在，则自动创建该游戏框架模块。
            string moduleName = StringUtility.Format("{0}.{1}, {2}", interfaceType.Namespace, interfaceType.Name.Substring(1), interfaceType.Assembly.GetName().Name);
            Type moduleType = Type.GetType(moduleName);
            if (moduleType == null)
            {
                throw new GameException(StringUtility.Format("Can not find Game Framework module type '{0}'.", moduleName));
            }

            Module module = (Module)Activator.CreateInstance(moduleType);
            if (module == null)
            {
                throw new GameException(StringUtility.Format("Can not create module '{0}'.", moduleType.FullName));
            }

            RegisterModuleInternal(interfaceType, module, module.Scope);

            return module as T;
        }

        /// <summary>
        /// 注册自定义Module。
        /// </summary>
        /// <param name="module">Module。</param>
        /// <returns>Module实例。</returns>
        /// <exception cref="GameException">框架异常。</exception>
        public static T RegisterModule<T>(Module module) where T : class
        {
            Type interfaceType = typeof(T);

            if (!interfaceType.IsInterface)
            {
                throw new GameException(StringUtility.Format("You must get module by interface, but '{0}' is not.", interfaceType.FullName));
            }

            var handle = interfaceType.TypeHandle;
            if (s_ModuleMaps.TryGetValue(handle, out var existing))
            {
                // 重复检查限定在同一作用域内：不同 Scope 可注册同一接口（跨作用域遮蔽）
                var occupied = existing.Get(module.Scope);
                if (occupied != null)
                {
                    Log.Warning("{0} has already been registered in {1} scope.", interfaceType.FullName, module.Scope);
                    return occupied as T;
                }
            }

            if (isIterating)
            {
                pendingChanges.Add(PendingChange.Register(module, interfaceType, module.Scope));
                return module as T;
            }

            RegisterModuleInternal(interfaceType, module, module.Scope);

            return module as T;
        }

        private static void RegisterModuleInternal(Type interfaceType, Module module, ModuleScope scope)
        {
            var handle = interfaceType.TypeHandle;
            if (!s_ModuleMaps.TryGetValue(handle, out var bindings))
                bindings = default;

            switch (scope)
            {
                case ModuleScope.App: bindings.App = module; break;
                case ModuleScope.Scene: bindings.Scene = module; break;
                case ModuleScope.Gameplay: bindings.Gameplay = module; break;
            }
            s_ModuleMaps[handle] = bindings;

            module.SetContext(new ModuleContext(s_ModuleMaps, scope));

            // 先占位 entry（索引在全部插入完成后统一重建）
            s_Entries[module] = new ModuleEntry
            {
                InterfaceHandle = handle,
                AllIndex = MISSING_INDEX,
                UpdateIndex = MISSING_INDEX,
                FixedUpdateIndex = MISSING_INDEX,
                LateUpdateIndex = MISSING_INDEX,
                GizmoIndex = MISSING_INDEX,
                Scope = scope,
            };

            // 注册时一次性转换到各生命周期接口（每模块仅一次，热路径零转换）
            var updateModule = module as IUpdateModule;
            var fixedUpdateModule = module as IFixedUpdateModule;
            var lateUpdateModule = module as ILateUpdateModule;
            var gizmoModule = module as IGizmoModule;

            InsertSorted(s_Modules, module);
            if (updateModule != null) InsertSorted(s_UpdateModules, updateModule);
            if (fixedUpdateModule != null) InsertSorted(s_FixedUpdateModules, fixedUpdateModule);
            if (lateUpdateModule != null) InsertSorted(s_LateUpdateModules, lateUpdateModule);
            if (gizmoModule != null) InsertSorted(s_GizmoModules, gizmoModule);

            // InsertSorted 会移动已有元素，统一重建全部列表索引（含新模块自身），保证 swap-remove 使用的索引始终有效
            RebuildAllIndices();

            module.OnInit();
        }

        /// <summary>
        /// 按优先级将模块插入排序列表，返回插入索引。
        /// </summary>
        private static int InsertSorted<T>(List<T> list, T item) where T : class
        {
            int priority = GetPriority(item);
            int insertAt = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                if (priority > GetPriority(list[i]))
                {
                    insertAt = i;
                    break;
                }
            }
            list.Insert(insertAt, item);
            return insertAt;
        }

        private static int GetPriority<T>(T item) where T : class => (item as Module)?.Priority ?? 0;

        /// <summary>
        /// 重建全部列表的索引（InsertSorted 会移动已有元素，导致其 entry 缓存的索引失效）。
        /// 每次注册后调用，保证 swap-remove 使用的索引始终正确。
        /// </summary>
        private static void RebuildAllIndices()
        {
            for (int i = 0; i < s_Modules.Count; i++)
                if (s_Entries.TryGetValue(s_Modules[i], out var e)) { e.AllIndex = i; s_Entries[s_Modules[i]] = e; }

            for (int i = 0; i < s_UpdateModules.Count; i++)
                if (s_UpdateModules[i] is Module m && s_Entries.TryGetValue(m, out var e)) { e.UpdateIndex = i; s_Entries[m] = e; }

            for (int i = 0; i < s_FixedUpdateModules.Count; i++)
                if (s_FixedUpdateModules[i] is Module m && s_Entries.TryGetValue(m, out var e)) { e.FixedUpdateIndex = i; s_Entries[m] = e; }

            for (int i = 0; i < s_LateUpdateModules.Count; i++)
                if (s_LateUpdateModules[i] is Module m && s_Entries.TryGetValue(m, out var e)) { e.LateUpdateIndex = i; s_Entries[m] = e; }

            for (int i = 0; i < s_GizmoModules.Count; i++)
                if (s_GizmoModules[i] is Module m && s_Entries.TryGetValue(m, out var e)) { e.GizmoIndex = i; s_Entries[m] = e; }
        }

        /// <summary>
        /// Swap-Remove — O(1) 删除。被移动的元素需要更新索引。
        /// </summary>
        private static void SwapRemoveAt<T>(List<T> list, int index) where T : class
        {
            int lastIndex = list.Count - 1;
            if (index == lastIndex)
            {
                list.RemoveAt(lastIndex);
                return;
            }

            T moved = list[lastIndex];
            list[index] = moved;
            list.RemoveAt(lastIndex);

            if (moved is Module movedModule && s_Entries.TryGetValue(movedModule, out var movedEntry))
            {
                // 更新被移动模块的索引
                if (ReferenceEquals(list, s_Modules)) movedEntry.AllIndex = index;
                else if (ReferenceEquals(list, s_UpdateModules)) movedEntry.UpdateIndex = index;
                else if (ReferenceEquals(list, s_FixedUpdateModules)) movedEntry.FixedUpdateIndex = index;
                else if (ReferenceEquals(list, s_LateUpdateModules)) movedEntry.LateUpdateIndex = index;
                else if (ReferenceEquals(list, s_GizmoModules)) movedEntry.GizmoIndex = index;
                s_Entries[movedModule] = movedEntry;
            }
        }

        private static void RemoveModuleInternal(Module module, ModuleEntry entry)
        {
            // 清除对应 Scope 的绑定（接口句柄由 entry 直接持有，O(1)）
            if (s_ModuleMaps.TryGetValue(entry.InterfaceHandle, out var bindings))
            {
                switch (entry.Scope)
                {
                    case ModuleScope.App:     bindings.App = null; break;
                    case ModuleScope.Scene:    bindings.Scene = null; break;
                    case ModuleScope.Gameplay: bindings.Gameplay = null; break;
                }
                if (bindings.IsEmpty)
                    s_ModuleMaps.Remove(entry.InterfaceHandle);
                else
                    s_ModuleMaps[entry.InterfaceHandle] = bindings;
            }

            if (entry.AllIndex >= 0) SwapRemoveAt(s_Modules, entry.AllIndex);
            if (entry.UpdateIndex >= 0) SwapRemoveAt(s_UpdateModules, entry.UpdateIndex);
            if (entry.FixedUpdateIndex >= 0) SwapRemoveAt(s_FixedUpdateModules, entry.FixedUpdateIndex);
            if (entry.LateUpdateIndex >= 0) SwapRemoveAt(s_LateUpdateModules, entry.LateUpdateIndex);
            if (entry.GizmoIndex >= 0) SwapRemoveAt(s_GizmoModules, entry.GizmoIndex);

            s_Entries.Remove(module);
        }

        private static void FlushPendingChanges()
        {
            if (pendingChanges.Count == 0) return;

            for (int i = 0; i < pendingChanges.Count; i++)
            {
                var change = pendingChanges[i];
                if (change.IsRegister)
                {
                    if (!s_ModuleMaps.TryGetValue(change.InterfaceType.TypeHandle, out var b) || b.Get(change.Scope) == null)
                    {
                        RegisterModuleInternal(change.InterfaceType, change.Module, change.Scope);
                    }
                }
            }

            pendingChanges.Clear();
        }

        private static void ClearAll()
        {
            s_Modules.Clear();
            s_ModuleMaps.Clear();
            s_UpdateModules.Clear();
            s_FixedUpdateModules.Clear();
            s_LateUpdateModules.Clear();
            s_GizmoModules.Clear();
            s_Entries.Clear();
            pendingChanges.Clear();

            MemoryPool.ClearAll();
            MarshalUtility.FreeCachedHGlobal();
        }
    }
}
