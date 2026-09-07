using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
	/// <summary>
	/// <see cref="EnumConditionAttribute"/> 绘制器：根据所依赖枚举字段的位标志决定字段的启用或隐藏。
	/// </summary>
	[CustomPropertyDrawer(typeof(EnumConditionAttribute))]
	public class EnumConditionAttributeDrawer : PropertyDrawer
	{
		/// <summary>
		/// 绘制属性；若条件不满足且特性配置为隐藏，则不绘制该字段。
		/// </summary>
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EnumConditionAttribute enumConditionAttribute = (EnumConditionAttribute)attribute;
			bool enabled = GetConditionAttributeResult(enumConditionAttribute, property);
			bool previouslyEnabled = GUI.enabled;
			GUI.enabled = enabled;
			if (!enumConditionAttribute.Hidden || enabled)
			{
				EditorGUI.PropertyField(position, property, label, true);
			}
			GUI.enabled = previouslyEnabled;
		}

		private bool GetConditionAttributeResult(EnumConditionAttribute enumConditionAttribute, SerializedProperty property)
		{
			bool enabled = true;
			string propertyPath = property.propertyPath;
			string conditionPath = propertyPath.Replace(property.name, enumConditionAttribute.ConditionEnum);
			SerializedProperty sourcePropertyValue = property.serializedObject.FindProperty(conditionPath);

			if ((sourcePropertyValue != null) && (sourcePropertyValue.propertyType == SerializedPropertyType.Enum))
			{
				int currentEnum = sourcePropertyValue.enumValueIndex;
				enabled = enumConditionAttribute.ContainsBitFlag(currentEnum);
			}
			else
			{
				Debug.LogWarning("No matching enum prop found for ConditionAttribute in object: " + enumConditionAttribute.ConditionEnum);
			}

			return enabled;
		}

		/// <summary>
		/// 获取属性高度；条件不满足时返回负值（抵消纵向间距）以收起该属性。
		/// </summary>
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			EnumConditionAttribute enumConditionAttribute = (EnumConditionAttribute)attribute;
			bool enabled = GetConditionAttributeResult(enumConditionAttribute, property);
            
			if (!enumConditionAttribute.Hidden || enabled)
			{
				return EditorGUI.GetPropertyHeight(property, label);
			}
			else
			{
				/*int multiplier = 1; // 该乘数用于修复 MMFeedbacks 与 MMF_Player 之间属性间距不一致的问题
				if (property.depth > 0)
				{
					multiplier = property.depth;
				}*/
				return -EditorGUIUtility.standardVerticalSpacing /** multiplier*/;
			}
		}
	}
}