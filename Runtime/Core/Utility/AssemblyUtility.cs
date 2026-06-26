using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Moirai.Atropos
{
    /// <summary>
    /// 程序集相关的实用函数。
    /// </summary>
    public static class AssemblyUtility
    {
        private static readonly Assembly[] _assemblies = null;
        private static readonly Dictionary<string, Type> _cachedTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        
        static AssemblyUtility()
        {
            _assemblies = AppDomain.CurrentDomain.GetAssemblies();
        }

        /// <summary>
        /// 获取已加载的程序集。
        /// </summary>
        /// <returns>已加载的程序集。</returns>
        public static Assembly[] GetAssemblies()
        {
            return _assemblies;
        }

        /// <summary>
        /// 获取已加载的程序集中的所有类型。
        /// </summary>
        /// <returns>已加载的程序集中的所有类型。</returns>
        public static Type[] GetTypes()
        {
            List<Type> results = new List<Type>();
            foreach (Assembly assembly in _assemblies)
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
            foreach (Assembly assembly in _assemblies)
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

            if (_cachedTypes.TryGetValue(typeName, out Type type))
            {
                return type;
            }

            type = Type.GetType(typeName);
            if (type != null)
            {
                _cachedTypes.Add(typeName, type);
                return type;
            }

            foreach (Assembly assembly in _assemblies)
            {
                type = Type.GetType(TextUtility.Format("{0}, {1}", typeName, assembly.FullName));
                if (type != null)
                {
                    _cachedTypes.Add(typeName, type);
                    return type;
                }
            }

            return null;
        }
        
        /// <summary>
        /// 获取已加载的程序集中的指定类型的子类列表。
        /// </summary>
        /// <param name="type">指定类型</param>
        /// <returns></returns>
        public static List<string> GetRuntimeTypeNames(Type type)
        {
            var types = GetTypes();
            List<string> results = new List<string>();
            foreach (var t in types)
            {
                if (t.IsAbstract || !t.IsClass)
                {
                    continue;
                }

                if (t.IsSubclassOf(type) || t.IsImplWithInterface(type))
                {
                    results.Add(t.FullName);
                }
            }

            return results;
        }

        /// <summary>
        /// 获取已加载的程序集中的指定类型的子类列表。
        /// </summary>
        /// <param name="typeBase">指定类型</param>
        /// <returns></returns>
        public static List<Type> GetRuntimeTypes(Type typeBase)
        {
            var types = GetTypes();
            List<Type> results = new List<Type>();
            foreach (var t in types)
            {
                if (t.IsAbstract || !t.IsClass)
                {
                    continue;
                }

                if (t.IsSubclassOf(typeBase) || t.IsImplWithInterface(typeBase))
                {
                    results.Add(t);
                }
            }

            return results;
        }
    }
}