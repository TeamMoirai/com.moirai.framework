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
        /// 获取已加载的程序集中指定的程序集
        /// </summary>
        /// <param name="assemblyName">程序集名</param>
        /// <returns>程序集</returns>
        public static Assembly GetAssembly(string assemblyName)
        {
            foreach (var assembly in _assemblies)
            {
                if (assembly.GetName().Name == assemblyName)
                    return assembly;
            }

            return null;
        }
        
        /// <summary>
        /// 反射工具，得到反射类的对象
        /// 被反射对象必须是有无参公共构造 
        /// </summary>
        /// <param name="typeName">类型名</param>
        /// <returns>实例化后的对象</returns>
        public static object GetTypeInstance(string typeName)
        {
            object inst = null;
            foreach (var a in _assemblies)
            {
                var dstType = a.GetType(typeName);
                if (dstType != null)
                {
                    inst = Activator.CreateInstance(dstType);
                    break;
                }
            }

            return inst;
        }
        
        /// <summary>
        /// 反射工具，得到反射类的对象
        /// </summary>
        /// <param name="typeName">类型名</param>
        /// <param name="args">构造参数</param>
        /// <returns>实例化后的对象</returns>
        public static object GetTypeInstance(string typeName, object[] args)
        {
            object inst = null;
            foreach (var a in _assemblies)
            {
                var dstType = a.GetType(typeName);
                if (dstType != null)
                {
                    inst = Activator.CreateInstance(dstType, args);
                    break;
                }
            }

            return inst;
        }
        
        /// <summary>
        /// 反射工具，得到反射类的对象
        /// 被反射对象必须是有无参公共构造 ，强转至泛型类型。
        /// </summary>
        /// <typeparam name="T">类型</typeparam>
        /// <param name="typeName">类型名</param>
        /// <returns>实例化后的对象</returns>
        public static T GetTypeInstance<T>(string typeName)
        {
            T inst = default;
            foreach (var a in _assemblies)
            {
                var dstType = a.GetType(typeName);
                if (dstType != null)
                {
                    inst = (T)Activator.CreateInstance(dstType);
                    break;
                }
            }

            return inst;
        }
        
        /// <summary>
        /// 获取某类型在指定程序集的所有派生类完全限定名数组
        /// </summary>
        /// <typeparam name="T">基类</typeparam>
        /// <returns>非抽象派生类完全限定名</returns>
        public static string[] GetDerivedTypeNames<T>()
            where T : class
        {
            return ReflectionUtility.GetDerivedTypeNames(typeof(T), _assemblies);
        }
        
        /// <summary>
        /// 执行单例方法
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="parameters">方法参数</param>
        /// <returns>返回值</returns>
        public static object InvokeInstanceMethod(string className, string methodName, object[] parameters = null)
        {
            // 查找指定类名的类型
            var type = _assemblies.SelectMany(assembly => assembly.GetTypes()).FirstOrDefault(t => t.Name == className);
            if (type == null)
            {
                throw new Exception($"需要检查是否有正确生成{className}类!");
            }

            // 获取单例的实例
            var property = type.BaseType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (property == null)
            {
                throw new Exception($"无法获取 {className} 的单例实例!");
            }
            var instance = property.GetValue(null, null);

            // 获取方法并执行
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new Exception($"类中 : {type} 找不到此方法 : {methodName}!");
            }

            return method.Invoke(instance, parameters);
        }
    }
}