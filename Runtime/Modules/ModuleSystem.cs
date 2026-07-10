using System;
using System.Collections.Generic;
using System.Linq;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架模块实现类管理系统。
    /// </summary>
    public static class ModuleSystem
    {
        /// <summary>
        /// 默认设计的模块数量。
        /// <remarks>有增删可以自行修改减少内存分配与GCAlloc。</remarks>
        /// </summary>
        private const int DESIGN_MODULE_COUNT = 16;

        private static readonly Dictionary<Type, Module> s_ModuleMaps = new Dictionary<Type, Module>(DESIGN_MODULE_COUNT);
        private static readonly LinkedList<Module> s_Modules = new LinkedList<Module>();

        private static readonly LinkedList<Module> s_UpdateModules = new LinkedList<Module>();
        private static readonly List<IUpdateModule> s_UpdateExecuteList = new List<IUpdateModule>(DESIGN_MODULE_COUNT);
        private static bool s_IsExecuteListDirty; // 脏标记模式

        private static readonly LinkedList<Module> s_FixedUpdateModules = new LinkedList<Module>();
        private static readonly List<IFixedUpdateModule> s_FixedUpdateExecuteList = new List<IFixedUpdateModule>(DESIGN_MODULE_COUNT);
        private static bool s_IsFixedExecuteListDirty;

        private static readonly LinkedList<Module> s_LateUpdateModules = new LinkedList<Module>();
        private static readonly List<ILateUpdateModule> s_LateUpdateExecuteList = new List<ILateUpdateModule>(DESIGN_MODULE_COUNT);
        private static bool s_IsLateExecuteListDirty;

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public static void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (s_IsExecuteListDirty)
            {
                s_IsExecuteListDirty = false;
                BuildExecuteList(s_UpdateExecuteList, s_UpdateModules);
            }

            int executeCount = s_UpdateExecuteList.Count;
            for (int i = 0; i < executeCount; i++)
            {
                s_UpdateExecuteList[i].Update(elapseSeconds, realElapseSeconds);
            }
        }

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        public static void FixedUpdate(float elapseSeconds, float realElapseSeconds)
        {
            if (s_IsFixedExecuteListDirty)
            {
                s_IsFixedExecuteListDirty = false;
                BuildExecuteList(s_FixedUpdateExecuteList, s_FixedUpdateModules);
            }

            int executeCount = s_FixedUpdateExecuteList.Count;
            for (int i = 0; i < executeCount; i++)
            {
                s_FixedUpdateExecuteList[i].FixedUpdate(elapseSeconds, realElapseSeconds);
            }
        }

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        public static void LateUpdate(float elapseSeconds, float realElapseSeconds)
        {
            if (s_IsLateExecuteListDirty)
            {
                s_IsLateExecuteListDirty = false;
                BuildExecuteList(s_LateUpdateExecuteList, s_LateUpdateModules);
            }

            int executeCount = s_LateUpdateExecuteList.Count;
            for (int i = 0; i < executeCount; i++)
            {
                s_LateUpdateExecuteList[i].LateUpdate(elapseSeconds, realElapseSeconds);
            }
        }

        /// <summary>
        /// 关闭并清理所有游戏框架模块。
        /// </summary>
        public static void Shutdown()
        {
            for (LinkedListNode<Module> current = s_Modules.Last; current != null; current = current.Previous)
            {
                current.Value.Shutdown();
            }

            s_Modules.Clear();
            s_ModuleMaps.Clear();

            s_UpdateModules.Clear();
            s_UpdateExecuteList.Clear();

            s_FixedUpdateModules.Clear();
            s_FixedUpdateExecuteList.Clear();

            s_LateUpdateModules.Clear();
            s_LateUpdateExecuteList.Clear();

            MemoryPool.ClearAll();
            MarshalUtility.FreeCachedHGlobal();
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

            if (s_ModuleMaps.TryGetValue(interfaceType, out Module module))
            {
                return module as T;
            }

            string moduleName = StringUtility.Format("{0}.{1}, {2}", interfaceType.Namespace, interfaceType.Name.Substring(1), interfaceType.Assembly.GetName().Name);
            Type moduleType = Type.GetType(moduleName);
            if (moduleType == null)
            {
                throw new GameException(StringUtility.Format("Can not find Game Framework module type '{0}'.", moduleName));
            }

            return GetModule(moduleType) as T;
        }

        /// <summary>
        /// 获取游戏框架模块。
        /// </summary>
        /// <param name="moduleType">要获取的游戏框架模块类型。</param>
        /// <returns>要获取的游戏框架模块。</returns>
        /// <remarks>如果要获取的游戏框架模块不存在，则自动创建该游戏框架模块。</remarks>
        private static Module GetModule(Type moduleType)
        {
            return s_ModuleMaps.TryGetValue(moduleType, out Module module) ? module : CreateModule(moduleType);
        }

        /// <summary>
        /// 创建游戏框架模块。
        /// </summary>
        /// <param name="moduleType">要创建的游戏框架模块类型。</param>
        /// <returns>要创建的游戏框架模块。</returns>
        private static Module CreateModule(Type moduleType)
        {
            Module module = (Module)Activator.CreateInstance(moduleType);
            if (module == null)
            {
                throw new GameException(StringUtility.Format("Can not create module '{0}'.", moduleType.FullName));
            }

            RegisterModule(moduleType, module);

            return module;
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

            RegisterModule(interfaceType, module);

            return module as T;
        }

        private static void RegisterModule(Type moduleType, Module module)
        {
            s_ModuleMaps[moduleType] = module;

            LinkedListNode<Module> current = s_Modules.First;
            while (current != null)
            {
                if (module.Priority > current.Value.Priority)
                {
                    break;
                }

                current = current.Next;
            }

            if (current != null)
            {
                s_Modules.AddBefore(current, module);
            }
            else
            {
                s_Modules.AddLast(module);
            }

            RegisterUpdate(module, typeof(IUpdateModule), s_UpdateModules, ref s_IsExecuteListDirty);
            RegisterUpdate(module, typeof(IFixedUpdateModule), s_FixedUpdateModules, ref s_IsFixedExecuteListDirty);
            RegisterUpdate(module, typeof(ILateUpdateModule), s_LateUpdateModules, ref s_IsLateExecuteListDirty);
            
            module.OnInit();
        }

        /// <summary>
        /// 注册 Module 到相应的更新队列。
        /// </summary>
        /// <param name="module"></param>
        /// <param name="interfaceType"></param>
        /// <param name="list">更新队列</param>
        /// <param name="dirtyFlag">更新队列的脏标记</param>
        private static void RegisterUpdate(Module module, Type interfaceType, LinkedList<Module> list, ref bool dirtyFlag)
        {
            if (!interfaceType.IsAssignableFrom(module.GetType())) return;

            LinkedListNode<Module> currentNode = list.First;
            while (currentNode != null && module.Priority <= currentNode.Value.Priority)
            {
                currentNode = currentNode.Next;
            }

            if (currentNode != null)
            {
                list.AddBefore(currentNode, module);
            }
            else
            {
                list.AddLast(module);
            }

            dirtyFlag = true;
        }
        
        /// <summary>
        /// 构造执行队列。
        /// </summary>
        private static void BuildExecuteList<T>(List<T> targetList, IEnumerable<object> sourceCollection)
        {
            targetList.Clear();
            targetList.AddRange(sourceCollection.OfType<T>());
        }
    }
}