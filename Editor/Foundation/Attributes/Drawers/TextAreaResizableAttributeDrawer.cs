using Moirai.Atropos.Attributes.Editor.Utils;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(TextAreaResizableAttribute))]
    public class TextAreaResizableAttributeDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            Rect indented = EditorGUI.IndentedRect(new Rect(0, 0, EditorGUIUtility.currentViewWidth, EditorGUIUtility.singleLineHeight));
            float viewWidth = indented.width;
            
            return GetHeight(
                property.stringValue,
                viewWidth
            ) + EditorGUIUtility.singleLineHeight * 2; // 多 2 行的高度，方便编辑
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using (EditorGUI.ChangeCheckScope changed = new EditorGUI.ChangeCheckScope())
            {
                // Debug.Log(position);
                GUIStyle style = new GUIStyle(EditorStyles.textField)
                {
                    wordWrap = true,
                };
                EditorStyles.textField.wordWrap = true;
                if (label.text != "")
                {
                    (Rect labelFieldRect, Rect textAreaRect) = RectUtils.SplitHeightRect(position, EditorGUIUtility.singleLineHeight);
                    EditorGUI.LabelField(labelFieldRect, label);
                    position = textAreaRect;
                }
                
                string textAreaValue = EditorGUI.TextArea(position, property.stringValue, style);
                if (changed.changed)
                {
                    property.stringValue = textAreaValue;
                }
            }
        }
        
        private float GetHeight(string text, float width)
        {
            GUIStyle style = new GUIStyle(EditorStyles.textField)
            {
                wordWrap = true,
            };
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "F";
            }
            float areaHeight = style.CalcHeight(new GUIContent(text), width);
            return Mathf.Max(areaHeight, EditorGUIUtility.singleLineHeight);
        }
    }
}