using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Drawers
{
    [CustomPropertyDrawer(typeof(CustomLabelAttribute))]
    public class CustomLabelAttributeDrawer : PropertyDrawer
    {
        /// <summary>
        /// 嵌套类型不能沿用 base 的单行高度；与 OnGUI 中 PropertyField(includeChildren) 对齐。
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            CustomLabelAttribute customLabel = (CustomLabelAttribute)attribute;
            // 将原标签文本替换为我们自定义的文本
            label.text = customLabel.label;

            // 绘制属性字段
            EditorGUI.PropertyField(position, property, label, true);
        }
    }
}