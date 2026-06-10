using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
	[CustomPropertyDrawer(typeof(EnumConditionAttribute))]
	public class EnumConditionAttributeDrawer : PropertyDrawer
	{
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
				/*int multiplier = 1; // this multiplier fixes issues in differing property spacing between MMFeedbacks and MMF_Player
				if (property.depth > 0)
				{
					multiplier = property.depth;
				}*/
				return -EditorGUIUtility.standardVerticalSpacing /** multiplier*/;
			}
		}
	}
}