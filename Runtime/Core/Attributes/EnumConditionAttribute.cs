using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
	[Conditional("UNITY_EDITOR")]
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct)]
	public class EnumConditionAttribute : PropertyAttribute
	{
		public string ConditionEnum = "";
		public bool Hidden;

		private readonly BitArray _bitArray = new BitArray(32);
		public bool ContainsBitFlag(int enumValue)
		{
			return _bitArray.Get(enumValue);
		}

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