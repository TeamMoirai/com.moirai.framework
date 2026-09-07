using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    /// <summary>
    /// <see cref="BreakVector2Attribute"/> 绘制器：将 Vector2 序列化属性按 X/Y 拆分为两行独立浮点字段绘制。
    /// </summary>
    [CustomPropertyDrawer(typeof(BreakVector2Attribute))]
    public class BreakVector2AttributeDrawer : PropertyDrawer
    {
        /// <summary>
        /// 依次绘制 X/Y 两个浮点字段，任一字段变化时写回 <paramref name="property"/> 的 vector2Value。
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = position;
            fieldRect.height = EditorGUIUtility.singleLineHeight;

            var at = attribute as BreakVector2Attribute;

            EditorGUI.BeginChangeCheck();
            
            float x = EditorGUI.FloatField(fieldRect, at.XLabel, property.vector2Value.x);

            fieldRect.y += fieldRect.height + 2f;
            float y = EditorGUI.FloatField(fieldRect, at.YLabel, property.vector2Value.y);

            property.vector2Value = new Vector2(x, y);

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 获取属性总高度：两行浮点字段加行间距。
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return 2f * (EditorGUIUtility.singleLineHeight + 2f);
        }
    }
}