using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Collections
{
    // ReSharper disable once InconsistentNaming
    internal class IOCContainer
    {
        private readonly Dictionary<Type, object> _instances = new Dictionary<Type, object>();
        
        /// <summary>
        /// 注册指定类型的实例。
        /// </summary>
        /// <param name="instance">要注册的实例。</param>
        /// <typeparam name="T">实例的类型。</typeparam>
        public void Register<T>(T instance)
        {
            var type = typeof(T);
            _instances[type] = instance;
        }
        
        /// <summary>
        /// 注销指定类型的实例。
        /// </summary>
        /// <param name="instance">要注销的实例，需与当前注册实例相等才会移除。</param>
        /// <typeparam name="T">实例的类型。</typeparam>
        public void Unregister<T>(T instance)
        {
            var type = typeof(T);
            if (_instances.ContainsKey(type) && _instances[type].Equals(instance))
            {
                _instances.Remove(type);
            }
        }
        
        /// <summary>
        /// 解析并获取已注册的实例。
        /// </summary>
        /// <typeparam name="T">要获取的实例类型。</typeparam>
        public T Resolve<T>() where T : class
        {
            var type = typeof(T);
            if (_instances.TryGetValue(type, out var obj))
            {
                return obj as T;
            }
            return null;
        }
        
        /// <summary>
        /// 清除所有已注册的实例。
        /// </summary>
        public void Clear()
        {
            _instances.Clear();
        }
    }
}
