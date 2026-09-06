using System;
using System.Collections.Generic;
using System.Reflection;

namespace Moirai.Atropos
{
    /// <summary>
    /// 程序集相关的实用函数。
    /// </summary>
    public static class AssemblyUtility
    {
        private static readonly Assembly[] s_Assemblies = null;
        private static readonly Dictionary<string, Type> s_CachedTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        
        static AssemblyUtility()
        {
            s_Assemblies = AppDomain.CurrentDomain.GetAssemblies();
        }

        /// <summary>
        /// 获取已加载的程序集。
        /// </summary>
        /// <returns>已加载的程序集。</returns>
        public static Assembly[] GetAssemblies()
        {
            return s_Assemblies;
        }

        /// <summary>
        /// 获取已加载的程序集中的所有类型。
        /// </summary>
        /// <returns>已加载的程序集中的所有类型。</returns>
        public static Type[] GetTypes()
        {
            List<Type> results = new List<Type>();
            foreach (Assembly assembly in s_Assemblies)
            {
                results.AddRange(assembly.GetTypes());
            }

            return results.ToArray();
        }

        /// <summary>
        /// 获取已加载的程序集中的所有类型。
        /// </summary>
        /// <param name="results">已加载的程序集中的所有类型。</param>
        public static void GetTypes(List<Type> results)
        {
            if (results == null)
            {
                throw new GameException("Results is invalid.");
            }

            results.Clear();
            foreach (Assembly assembly in s_Assemblies)
            {
                results.AddRange(assembly.GetTypes());
            }
        }

        /// <summary>
        /// 获取已加载的程序集中的指定类型。
        /// </summary>
        /// <param name="typeName">要获取的类型名。</param>
        /// <returns>已加载的程序集中的指定类型。</returns>
        public static Type GetType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new GameException("Type name is invalid.");
            }

            if (s_CachedTypes.TryGetValue(typeName, out Type type))
            {
                return type;
            }

            type = Type.GetType(typeName);
            if (type != null)
            {
                s_CachedTypes.Add(typeName, type);
                return type;
            }

            foreach (Assembly assembly in s_Assemblies)
            {
                type = Type.GetType(StringUtility.Format("{0}, {1}", typeName, assembly.FullName));
                if (type != null)
                {
                    s_CachedTypes.Add(typeName, type);
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取已加载的程序集中的指定类型（接口或基类）的所有实现类/子类名称。
        /// </summary>
        /// <param name="typeBase">指定接口或基类类型</param>
        /// <returns>所有实现类/子类的完整名称列表</returns>
        public static List<string> GetRuntimeTypeNames(Type typeBase)
        {
            var runtimeTypes = GetRuntimeTypes(typeBase);
            List<string> results = new List<string>();
            foreach (var t in runtimeTypes)
            {
                results.Add(t.FullName);
            }

            return results;
        }

        /// <summary>
        /// 获取已加载的程序集中的指定类型（接口或基类）的所有实现类/子类。
        /// </summary>
        /// <param name="typeBase">指定接口或基类类型</param>
        /// <returns>所有实现类/子类的 Type 列表</returns>
        public static List<Type> GetRuntimeTypes(Type typeBase)
        {
            var types = GetTypes();
            List<Type> results = new List<Type>();
            foreach (var t in types)
            {
                if (t.IsAbstract || // 排除抽象类
                    t.IsInterface || // 虽然 IsAbstract 已经覆盖了接口，但为了追求代码自解释保留
                    t.Assembly.GetName().Name.EndsWith(".Tests") || // 排除测试程序集
                    !typeBase.IsAssignableFrom(t)) // 排除非派生类型
                {
                    continue;
                }

                results.Add(t);
            }

            return results;
        }
    }
}