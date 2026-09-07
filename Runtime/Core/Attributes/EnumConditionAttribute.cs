using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
	/// <summary>
	/// 枚举条件特性：根据关联枚举值控制目标成员在 Inspector 中的显示或隐藏。
	/// </summary>
	[Conditional("UNITY_EDITOR")]
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct)]
	public class EnumConditionAttribute : PropertyAttribute
	{
		/// <summary>
		/// 用作条件的布尔成员名称。
		/// </summary>
		public string ConditionEnum = "";
		/// <summary>
		/// 枚举值命中时是否直接隐藏目标成员（否则仅禁用编辑）。
		/// </summary>
		public bool Hidden;

		private readonly BitArray _bitArray = new BitArray(32);
		/// <summary>
		/// 获取指定枚举值是否被本特性标记（即是否满足显示条件）。
		/// </summary>
		/// <param name="enumValue">要检查的枚举值（按位索引，有效范围为 0~31）。</param>
		/// <returns>已被标记返回 true，否则返回 false。</returns>
		public bool ContainsBitFlag(int enumValue)
		{
			return _bitArray.Get(enumValue);
		}

		/// <summary>
		/// 创建枚举条件特性实例。
		/// </summary>
		/// <param name="conditionBoolean">用作条件的布尔成员名称。</param>
		/// <param name="enumValues">参与条件判断的枚举值列表，任意一个值命中即条件成立。</param>
		public EnumConditionAttribute(string conditionBoolean, params int[] enumValues)
		{
			ConditionEnum = conditionBoolean;
			Hidden = true;

			for (int i = 0; i < enumValues.Length; i++)
			{
				_bitArray.Set(enumValues[i], true);
			}
		}
	}
}