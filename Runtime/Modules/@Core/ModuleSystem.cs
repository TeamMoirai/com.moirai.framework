using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架模块实现类管理系统。
    /// </summary>
    public static partial class ModuleSystem
    {
        private const int DESIGN_MODULE_COUNT = 16;
        private const int MISSING_INDEX = -1;

        // 主线程守卫：编辑器加载 / 运行时子系统注册阶段捕获（均在主线程触发）
        private static int s_MainThreadId;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureMainThreadId()
        {
            s_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// 断言当前处于主线程（仅编辑器与开发构建生效，发布版零开销）。
        /// </summary>
        /// <remarks>
        /// ModuleSystem 主线程亲和。后台线程/异步回调需调用时，
        /// 请显式通过 <see cref="MainThreadDispatcher"/> 的 Dispatch/DispatchAsync 切回主线程，
        /// 而非由框架内部静默调度（会破坏返回值语义与读己之写顺序）。
        /// </remarks>
        private static void EnsureMainThread()
        {
            Assert.IsTrue(
                s_MainThreadId == 0 || System.Threading.Thread.CurrentThread.ManagedThreadId == s_MainThreadId,
                "ModuleSystem must only be used from the main thread. " +
                "From a background thread/callback, wrap the call with MainThreadDispatcher.Dispatch/DispatchAsync.");
        }

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

        // 迭代安全 — PendingChanges（注册与注销在迭代期间均延迟应用）
        internal static readonly List<PendingChange> s_PendingChanges = new List<PendingChange>();
        internal static bool s_IsIterating;

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        /// <remarks>由 <see cref="GameModule"/>（MonoBehaviour 生命周期）驱动，Unity 契约保证主线程调用，无需守护。</remarks>
        public static void Update(float elapseSeconds, float realElapseSeconds)
        {
            s_IsIterating = true;
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
                s_IsIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        /// <remarks>由 <see cref="GameModule"/>（MonoBehaviour 生命周期）驱动，Unity 契约保证主线程调用，无需守护。</remarks>
        public static void FixedUpdate(float elapseSeconds, float realElapseSeconds)
        {
            s_IsIterating = true;
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
                s_IsIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        /// <remarks>由 <see cref="GameModule"/>（MonoBehaviour 生命周期）驱动，Unity 契约保证主线程调用，无需守护。</remarks>
        public static void LateUpdate(float elapseSeconds, float realElapseSeconds)
        {
            s_IsIterating = true;
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
                s_IsIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 所有游戏框架模块绘制 Gizmos。
        /// </summary>
        public static void DrawGizmos()
        {
            s_IsIterating = true;
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
                s_IsIterating = false;
                FlushPendingChanges();
            }
        }

        /// <summary>
        /// 关闭并清理所有游戏框架模块。按 Gameplay → Scene → App 逆序关闭。
        /// </summary>
        public static void Shutdown()
        {
            EnsureMainThread();
            ShutdownScope(ModuleScope.Gameplay);
            ShutdownScope(ModuleScope.Scene);
            ShutdownScope(ModuleScope.App);
            ClearAll();
        }

        /// <summary>
        /// 关闭指定作用域的所有模块。
        /// </summary>
        /// <param name="scope">要关闭的作用域。</param>
        /// <remarks>迭代期间调用时移除操作延迟到本轮迭代结束后应用。</remarks>
        public static void ShutdownScope(ModuleScope scope)
        {
            EnsureMainThread();

            for (int i = s_Modules.Count - 1; i >= 0; i--)
            {
                var module = s_Modules[i];
                if (!s_Entries.TryGetValue(module, out var entry)) continue;
                if (entry.Scope != scope) continue;

                if (s_IsIterating)
                {
                    // 迭代期间不直接移除，延迟应用（PendingRemove 防止重复入队）
                    if (entry.PendingRemove) continue;
                    entry.PendingRemove = true;
                    s_Entries[module] = entry;
                    s_PendingChanges.Add(PendingChange.Unregister(module));
                    continue;
                }

                ShutdownModule(module);
            }
        }

        /// <summary>
        /// 注销模块。按接口类型查找当前最高优先作用域（Gameplay &gt; Scene &gt; App）中的绑定。
        /// </summary>
        /// <typeparam name="T">模块接口类型。</typeparam>
        /// <returns>是否找到并成功注销。</returns>
        public static bool UnregisterModule<T>() where T : class
        {
            EnsureMainThread();

            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new GameException(StringUtility.Format("You must unregister module by interface, but '{0}' is not.", interfaceType.FullName));
            }

            if (!s_ModuleMaps.TryGetValue(interfaceType.TypeHandle, out var bindings)) return false;
            var module = bindings.GetBest();
            if (module == null) return false;

            return UnregisterModuleInternal(module);
        }

        /// <summary>
        /// 注销指定模块实例。
        /// </summary>
        /// <param name="module">要注销的模块。</param>
        /// <returns>是否找到并成功注销。</returns>
        public static bool UnregisterModule(Module module)
        {
            if (module == null) return false;
            EnsureMainThread();
            return UnregisterModuleInternal(module);
        }

        private static bool UnregisterModuleInternal(Module module)
        {
            if (!s_Entries.TryGetValue(module, out var entry)) return false;

            if (s_IsIterating)
            {
                if (entry.PendingRemove) return true;
                entry.PendingRemove = true;
                s_Entries[module] = entry;
                s_PendingChanges.Add(PendingChange.Unregister(module));
                return true;
            }

            ShutdownModule(module);
            return true;
        }

        /// <summary>
        /// 关闭单个模块并从系统中移除。单个模块关闭异常不中断其余模块的清理。
        /// </summary>
        private static void ShutdownModule(Module module)
        {
            if (!s_Entries.TryGetValue(module, out var entry)) return;

            try
            {
                module.Shutdown();
            }
            catch (Exception exception)
            {
                LogUtility.Error(exception.ToString());
            }

            entry.PendingRemove = false;
            RemoveModuleInternal(module, entry);
        }

        /// <summary>
        /// 获取游戏框架模块。
        /// </summary>
        /// <typeparam name="T">要获取的游戏框架模块类型。</typeparam>
        /// <returns>要获取的游戏框架模块。</returns>
        /// <remarks>
        /// 如果要获取的游戏框架模块不存在，则自动创建该游戏框架模块。
        /// <para>查找顺序：Gameplay &gt; Scene &gt; App（跨作用域遮蔽）。</para>
        /// <para>
        /// 反射回退约定：未注册时按 <c>IXxxModule → 命名空间.XxxModule（同程序集）</c> 自动创建。
        /// 内置模块在 <c>AppSettings.Initiation()</c>（AfterAssembliesLoaded 阶段）由配置注册，
        /// 早于任何游戏代码调用本方法，因此配置实现优先；仅在接口从未被注册时才会触发反射回退。
        /// 自定义模块若不遵循此命名约定，必须先显式 <see cref="RegisterModule{T}"/>。
        /// </para>
        /// </remarks>
        public static T GetModule<T>() where T : class
        {
            EnsureMainThread();

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
            EnsureMainThread();

            Type interfaceType = typeof(T);

            if (!interfaceType.IsInterface)
            {
                throw new GameException(StringUtility.Format("You must get module by interface, but '{0}' is not.", interfaceType.FullName));
            }

            // 快速失败：模块必须实现所注册的接口，否则 GetModule<T> 返回 as T = null 会在远处炸出
            if (!interfaceType.IsInstanceOfType(module))
            {
                throw new GameException(StringUtility.Format("Module '{0}' does not implement interface '{1}'.", module.GetType().FullName, interfaceType.FullName));
            }

            var handle = interfaceType.TypeHandle;
            if (s_ModuleMaps.TryGetValue(handle, out var existing))
            {
                // 重复检查限定在同一作用域内：不同 Scope 可注册同一接口（跨作用域遮蔽）
                var occupied = existing.Get(module.Scope);
                if (occupied != null)
                {
                    LogUtility.Warning("{0} has already been registered in {1} scope.", interfaceType.FullName, module.Scope);
                    return occupied as T;
                }
            }

            if (s_IsIterating)
            {
                s_PendingChanges.Add(PendingChange.Register(module, interfaceType, module.Scope));
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
            if (s_PendingChanges.Count == 0) return;

            for (int i = 0; i < s_PendingChanges.Count; i++)
            {
                var change = s_PendingChanges[i];
                if (change.IsRegister)
                {
                    if (!s_ModuleMaps.TryGetValue(change.InterfaceType.TypeHandle, out var b) || b.Get(change.Scope) == null)
                    {
                        RegisterModuleInternal(change.InterfaceType, change.Module, change.Scope);
                    }
                }
                else
                {
                    // 注销：ShutdownModule 内部自带 PendingRemove/entry 存在性检查
                    ShutdownModule(change.Module);
                }
            }

            s_PendingChanges.Clear();
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
            s_PendingChanges.Clear();

            MemoryPool.ClearAll();
            MarshalUtility.FreeCachedHGlobal();
        }
    }
}
