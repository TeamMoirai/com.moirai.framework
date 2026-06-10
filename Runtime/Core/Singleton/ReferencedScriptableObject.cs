using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
	/// <summary>
	/// 自动引用 T 类型的 ScriptableObject 实例<br/>
	/// 可用于继承自 <see cref="ReferenceHolder{T}"/> 的任意类
	/// </summary>
	// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
	public class ReferencedScriptableObject<T> : ScriptableObject where T : ScriptableObject
	{
		private ReferenceHolder<T> _instances;
		private T _typed;
		protected virtual T Typed => _typed ??= this as T;

		protected virtual void OnReferenced() {}
		protected virtual void OnEnable()
		{
			_instances.Reference(Typed);
			OnReferenced();
			// Log.Info(ReferenceHolder<T>.Any != null);
		}

		protected virtual void OnDisposed() {}
		protected virtual void OnDisable()
		{
			_instances.Dispose();
			OnDisposed();
			// Log.Info(ReferenceHolder<T>.Any != null);
		}
	}

	/// <summary>
	/// 使用弱引用(WeakReference)让 GC 在引擎不再使用它们时回收
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public struct ReferenceHolder<T> : IDisposable where T : class
	{
		private static List<WeakReference<T>> _instances = new List<WeakReference<T>>(2);

		private WeakReference<T> _instance;
		public void Reference(T instance, bool cleanUp = false)
		{
			_instances ??= new List<WeakReference<T>>(1);
			if (cleanUp) CleanUp();
			if (instance != null)
			{
				_instance = new WeakReference<T>(instance);
				_instances.Add(_instance); // 总是在最后添加，以保证低性能
			}
		}
		public void Dispose()
		{
			if (_instance != null) _instances?.Remove(_instance);
		}

		public static void CleanUp() => RepackNonNullReferences();
		static void RepackNonNullReferences()
		{
			if (_instances == null) return;
			for(int n=_instances.Count-1; n >=0; --n)
			{
				if (!_instances[n].TryGetTarget(out T target))
				{
					_instances.RemoveAt(n);
				}
			}
		}

		public static T Any => _instances != null && _instances.Count > 0 && _instances[0].TryGetTarget(out T target) ? target : null;
		public static IEnumerator<T> All
		{
			get
			{
				if (_instances == null) yield break;
				foreach (var inst in _instances)
				{
					if (inst.TryGetTarget(out T target))
					{
						yield return target;
					}
				}
			}
		}
		
		public static T First(System.Func<T,bool> selector)
		{
			if (_instances == null) return null;
			if (selector == null) return Any;
			foreach (var inst in _instances)
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