using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    /// <summary>
    /// <see cref="BreakVector3Attribute"/> 绘制器：将 Vector3 序列化属性按 X/Y/Z 拆分为三行独立浮点字段绘制。
    /// </summary>
    [CustomPropertyDrawer(typeof(BreakVector3Attribute))]
    public class BreakVector3AttributeDrawer : PropertyDrawer
    {
        /// <summary>
        /// 依次绘制 X/Y/Z 三个浮点字段，任一字段变化时写回 <paramref name="property"/> 的 vector3Value。
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = position;
            fieldRect.height = EditorGUIUtility.singleLineHeight;

            var at = attribute as BreakVector3Attribute;

            float x = EditorGUI.FloatField(fieldRect, at.XLabel, property.vector3Value.x);

            fieldRect.y += fieldRect.height + 2f;
            float y = EditorGUI.FloatField(fieldRect, at.YLabel, property.vector3Value.y);

            fieldRect.y += fieldRect.height + 2f;
            float z = EditorGUI.FloatField(fieldRect, at.ZLabel, property.vector3Value.z);

            property.vector3Value = new Vector3(x, y, z);

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 获取属性总高度：三行浮点字段加行间距。
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return 3f * (EditorGUIUtility.singleLineHeight + 2f);
        }
    }
}