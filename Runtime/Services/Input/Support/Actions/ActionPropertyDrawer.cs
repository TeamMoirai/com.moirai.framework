#if UNITY_EDITOR
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入动作属性绘制器基类：查找 <c>m_Value</c> 属性、绘制标签与值字段（<see cref="BoolAction"/>、<see cref="FloatAction"/>、 <br />
    /// <see cref="Vector2Action"/> 共用）。
    /// </summary>
    public abstract class ActionPropertyDrawer : UnityEditor.PropertyDrawer
    {
        public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
        {
            UnityEditor.EditorGUI.BeginProperty(position, label, property);

            UnityEditor.SerializedProperty value = property.FindPropertyRelative("m_Value");

            Rect fieldRect = position;
            fieldRect.height = UnityEditor.EditorGUIUtility.singleLineHeight;
            fieldRect.width = 100;

            UnityEditor.EditorGUI.LabelField(fieldRect, label);

            fieldRect.x += 110;

            UnityEditor.EditorGUI.PropertyField(fieldRect, value, GUIContent.none);

            UnityEditor.EditorGUI.EndProperty();
        }
    }
}
#endif
