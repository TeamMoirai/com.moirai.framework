using UnityEngine;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// 组件查找器。
	/// <para>按泛型参数声明顺序在指定 <see cref="MonoBehaviour"/> 所在对象上查找组件，返回第一个匹配的组件。</para>
	/// </summary>
	public static class ComponentFinder
	{
		/// <summary>
		/// 查找第一个匹配的组件。
		/// </summary>
		/// <typeparam name="T1">候选组件类型。</typeparam>
		/// <param name="behaviour">用于定位目标对象的组件。</param>
		/// <returns>第一个匹配的组件；不存在时返回 <c>null</c>。</returns>
		public static Component Find<T1>(MonoBehaviour behaviour)
			where T1 : Component
		{
			if (behaviour.GetComponent<T1>())
			{
				return behaviour.GetComponent<T1>();
			}

			return null;
		}

		/// <summary>
		/// 查找第一个匹配的组件。
		/// </summary>
		/// <typeparam name="T1">候选组件类型。</typeparam>
		/// <typeparam name="T2">候选组件类型。</typeparam>
		/// <param name="behaviour">用于定位目标对象的组件。</param>
		/// <returns>第一个匹配的组件；不存在时返回 <c>null</c>。</returns>
		public static Component Find<T1, T2>(MonoBehaviour behaviour)
			where T1 : Component
			where T2 : Component
		{
			if (behaviour.GetComponent<T1>())
			{
				return behaviour.GetComponent<T1>();
			}

			if (behaviour.GetComponent<T2>())
			{
				return behaviour.GetComponent<T2>();
			}

			return null;
		}

		/// <summary>
		/// 查找第一个匹配的组件。
		/// </summary>
		/// <typeparam name="T1">候选组件类型。</typeparam>
		/// <typeparam name="T2">候选组件类型。</typeparam>
		/// <typeparam name="T3">候选组件类型。</typeparam>
		/// <param name="behaviour">用于定位目标对象的组件。</param>
		/// <returns>第一个匹配的组件；不存在时返回 <c>null</c>。</returns>
		public static Component Find<T1, T2, T3>(MonoBehaviour behaviour)
			where T1 : Component
			where T2 : Component
			where T3 : Component
		{
			if (behaviour.GetComponent<T1>())
			{
				return behaviour.GetComponent<T1>();
			}

			if (behaviour.GetComponent<T2>())
			{
				return behaviour.GetComponent<T2>();
			}

			if (behaviour.GetComponent<T3>())
			{
				return behaviour.GetComponent<T3>();
			}

			return null;
		}

		/// <summary>
		/// 查找第一个匹配的组件。
		/// </summary>
		/// <typeparam name="T1">候选组件类型。</typeparam>
		/// <typeparam name="T2">候选组件类型。</typeparam>
		/// <typeparam name="T3">候选组件类型。</typeparam>
		/// <typeparam name="T4">候选组件类型。</typeparam>
		/// <param name="behaviour">用于定位目标对象的组件。</param>
		/// <returns>第一个匹配的组件；不存在时返回 <c>null</c>。</returns>
		public static Component Find<T1, T2, T3, T4>(MonoBehaviour behaviour)
			where T1 : Component
			where T2 : Component
			where T3 : Component
			where T4 : Component
		{
			if (behaviour.GetComponent<T1>())
			{
				return behaviour.GetComponent<T1>();
			}

			if (behaviour.GetComponent<T2>())
			{
				return behaviour.GetComponent<T2>();
			}

			if (behaviour.GetComponent<T3>())
			{
				return behaviour.GetComponent<T3>();
			}

			if (behaviour.GetComponent<T4>())
			{
				return behaviour.GetComponent<T4>();
			}

			return null;
		}
	}
}
