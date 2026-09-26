using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 自动引用 T 类型的 ScriptableObject 实例基类。
    /// </summary>
    /// <typeparam name="T">目标 ScriptableObject 类型。</typeparam>
    /// <remarks>
    /// 可用于继承自 <see cref="ReferenceHolder{T}"/> 的任意类；
    /// 以弱引用登记所有存活实例，供静态查询（<see cref="ReferenceHolder{T}.Any"/> / <see cref="ReferenceHolder{T}.All"/>）。
    /// </remarks>
    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
    public class ReferencedScriptableObject<T> : ScriptableObject where T : ScriptableObject
    {
        /// <summary>本实例的弱引用登记句柄（OnEnable 登记、OnDisable 注销）。</summary>
        private ReferenceHolder<T> _referenceHolder;

        /// <summary>强类型化自身实例（惰性缓存）。</summary>
        protected virtual T Typed => _typed ??= this as T;

        private T _typed;

        /// <summary>
        /// 实例被引擎引用登记后的回调。
        /// </summary>
        protected virtual void OnReferenced() { }

        /// <summary>
        /// 引擎加载实例时登记弱引用。
        /// </summary>
        protected virtual void OnEnable()
        {
            _referenceHolder.Reference(Typed);
            OnReferenced();
        }

        /// <summary>
        /// 实例被引擎释放引用前的回调。
        /// </summary>
        protected virtual void OnDisposed() { }

        /// <summary>
        /// 引擎卸载实例时注销弱引用。
        /// </summary>
        protected virtual void OnDisable()
        {
            _referenceHolder.Dispose();
            OnDisposed();
        }
    }

    /// <summary>
    /// 弱引用登记表：让 GC 在引擎不再使用实例时自动回收。
    /// </summary>
    /// <typeparam name="T">登记的实例类型。</typeparam>
    public struct ReferenceHolder<T> : IDisposable where T : class
    {
        /// <summary>所有存活实例的弱引用登记表（静态共享）。</summary>
        private static List<WeakReference<T>> s_Instances = new List<WeakReference<T>>(2);

        /// <summary>本句柄登记的弱引用。</summary>
        private WeakReference<T> _instance;

        /// <summary>
        /// 登记一个实例（可选先清理已失效条目）。
        /// </summary>
        /// <param name="instance">待登记实例。</param>
        /// <param name="cleanUp">登记前是否清理已失效弱引用。</param>
        public void Reference(T instance, bool cleanUp = false)
        {
            s_Instances ??= new List<WeakReference<T>>(1);
            if (cleanUp) CleanUp();
            if (instance != null)
            {
                _instance = new WeakReference<T>(instance);
                // 总是在最后添加，以保证低性能
                s_Instances.Add(_instance);
            }
        }

        /// <summary>
        /// 注销本句柄登记的弱引用。
        /// </summary>
        public void Dispose()
        {
            if (_instance != null) s_Instances?.Remove(_instance);
        }

        /// <summary>
        /// 清理登记表中已失效的弱引用。
        /// </summary>
        public static void CleanUp() => RepackNonNullReferences();

        /// <summary>
        /// 移除所有已失效的弱引用条目。
        /// </summary>
        private static void RepackNonNullReferences()
        {
            if (s_Instances == null) return;
            for (int n = s_Instances.Count - 1; n >= 0; --n)
            {
                if (!s_Instances[n].TryGetTarget(out T target))
                {
                    s_Instances.RemoveAt(n);
                }
            }
        }

        /// <summary>
        /// 最早登记的存活实例（无则 null）。
        /// </summary>
        public static T Any => s_Instances != null && s_Instances.Count > 0 && s_Instances[0].TryGetTarget(out T target) ? target : null;

        /// <summary>
        /// 遍历所有存活实例（跳过已失效弱引用）。
        /// </summary>
        public static IEnumerator<T> All
        {
            get
            {
                if (s_Instances == null) yield break;
                foreach (var inst in s_Instances)
                {
                    if (inst.TryGetTarget(out T target))
                    {
                        yield return target;
                    }
                }
            }
        }

        /// <summary>
        /// 取第一个满足选择器的存活实例。
        /// </summary>
        /// <param name="selector">实例选择器；null 时等效 <see cref="Any"/>。</param>
        public static T First(Func<T, bool> selector)
        {
            if (s_Instances == null) return null;
            if (selector == null) return Any;
            foreach (var inst in s_Instances)
            {
                if (inst.TryGetTarget(out T target) && selector(target))
                {
                    return target;
                }
            }
            return null;
        }
    }
}
