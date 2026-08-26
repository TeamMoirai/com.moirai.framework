#if UNITY_EDITOR
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入动作编辑器的基类属性绘制器，提供通用的样板代码。
    /// 所有动作编辑器（<see cref="BoolAction"/>、<see cref="FloatAction"/>、<see cref="Vector2Action"/>）都遵循相同的模式：
    /// 查找 m_Value 属性，绘制标签，然后绘制值字段。
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
