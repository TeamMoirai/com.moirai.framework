using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
	// original implementation by http://www.brechtos.com/hiding-or-disabling-inspector-properties-using-propertydrawers-within-unity-5/
	[CustomPropertyDrawer(typeof(ConditionAttribute))]
	public class ConditionAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            ConditionAttribute conditionAttribute = (ConditionAttribute)attribute;

            bool result = CheckCondition(conditionAttribute, property);
            if (conditionAttribute.visibilityType == ConditionAttribute.VisibilityType.NotEditable)
            {
                GUI.enabled = result;
                EditorGUI.PropertyField(position, property, label, true);
                GUI.enabled = true;
            }
            else if (result)
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }

        private bool CheckCondition(ConditionAttribute conditionAttribute, SerializedProperty property)
        {
            bool output = true;
            for (int i = 0; i < conditionAttribute.conditionPropertyNames.Length; i++)
            {
                output &= EvaluateCondition(property, conditionAttribute.conditionPropertyNames[i], conditionAttribute.conditionTypes[i], conditionAttribute.values[i]);
            }

            return output;
        }

        private bool EvaluateCondition(SerializedProperty property, string conditionPropertyName, ConditionAttribute.ConditionType conditionType, float value)
        {
            SerializedProperty conditionProperty = property.serializedObject.FindProperty(conditionPropertyName);

            // 如果 “conditionProperty” 为空，那么该属性可能属于一个普通的 C# 序列化类。
            // 如果是这样，那就找出该属性的根路径，然后再次查找目标条件属性。
            if (conditionProperty == null)
            {
                string propertyPath = property.propertyPath;
                int lastIndex = propertyPath.LastIndexOf('.');

                if (lastIndex == -1)
                    return true;


                string propertyParentPath = propertyPath.Substring(0, lastIndex);

                conditionProperty = property.serializedObject.FindProperty(propertyParentPath).FindPropertyRelative(conditionPropertyName);

                if (conditionProperty == null)
                    return true;
            }


            bool result = false;

            SerializedPropertyType conditionPropertyType = conditionProperty.propertyType;

            if (conditionPropertyType == SerializedPropertyType.Boolean)
            {
                if (conditionType == ConditionAttribute.ConditionType.IsTrue)
                    result = conditionProperty.boolValue;
                else if (conditionType == ConditionAttribute.ConditionType.IsFalse)
                    result = !conditionProperty.boolValue;

            }
            else if (conditionPropertyType == SerializedPropertyType.Float)
            {

                float conditionPropertyFloatValue = conditionProperty.floatValue;
                float argumentFloatValue = value;

                switch (conditionType)
                {
                    case ConditionAttribute.ConditionType.IsTrue:
                        result = conditionPropertyFloatValue != 0f;
                        break;
                    case ConditionAttribute.ConditionType.IsFalse:
                        result = conditionPropertyFloatValue == 0f;
                        break;
                    case ConditionAttribute.ConditionType.IsEqualTo:
                        result = conditionPropertyFloatValue == argumentFloatValue;
                        break;
                    case ConditionAttribute.ConditionType.IsNotEqualTo:
                        result = conditionPropertyFloatValue != argumentFloatValue;
                        break;
                    case ConditionAttribute.ConditionType.IsGreaterThan:
                        result = conditionPropertyFloatValue > argumentFloatValue;
                        break;
                    case ConditionAttribute.ConditionType.IsLessThan:
                        result = conditionPropertyFloatValue < argumentFloatValue;
                        break;
                }

            }
            else if (conditionPropertyType == SerializedPropertyType.Integer || conditionPropertyType == SerializedPropertyType.Enum)
            {
                int conditionPropertyIntValue = conditionProperty.intValue;
                int argumentIntValue = (int)value;

                switch (conditionType)
                {
                    case ConditionAttribute.ConditionType.IsTrue:
                        result = conditionPropertyIntValue != 0;
                        break;
                    case ConditionAttribute.ConditionType.IsFalse:
                        result = conditionPropertyIntValue == 0;
                        break;
                    case ConditionAttribute.ConditionType.IsEqualTo:
                        result = conditionPropertyIntValue == argumentIntValue;
                        break;
                    case ConditionAttribute.ConditionType.IsNotEqualTo:
                        result = conditionPropertyIntValue != argumentIntValue;
                        break;
                    case ConditionAttribute.ConditionType.IsGreaterThan:
                        result = conditionPropertyIntValue > argumentIntValue;
                        break;
                    case ConditionAttribute.ConditionType.IsLessThan:
                        result = conditionPropertyIntValue < argumentIntValue;
                        break;
                }

            }
            else if (conditionPropertyType == SerializedPropertyType.ObjectReference)
            {
                UnityEngine.Object conditionPropertyObjectValue = conditionProperty.objectReferenceValue;

                switch (conditionType)
                {
                    case ConditionAttribute.ConditionType.IsNull:
                        result = conditionPropertyObjectValue == null;
                        break;
                    case ConditionAttribute.ConditionType.IsNotNull:
                        result = conditionPropertyObjectValue != null;
                        break;
                }
            }

            return result;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            ConditionAttribute conditionAttribute = (ConditionAttribute)attribute;

            return conditionAttribute.visibilityType == ConditionAttribute.VisibilityType.Hidden && !CheckCondition(conditionAttribute, property) ? 0f : EditorGUI.GetPropertyHeight(property);
        }
	}
}