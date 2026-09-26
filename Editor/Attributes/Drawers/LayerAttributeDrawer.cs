using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using UnityEditor.UIElements;
using UnityEngine.UIElements;
#endif

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    /// <summary>
    /// <see cref="LayerAttribute"/> 特性绘制器：在 Inspector 上绘制 Unity 层（Layer）选择字段。
    /// </summary>
    [CustomPropertyDrawer(typeof(LayerAttribute))]
    internal class LayerAttributeDrawer : PropertyDrawer
    {
        #region IMGUI
        /// <summary>
        /// 获取属性高度（单行）。
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent guiContent)
        {
            return EditorGUIUtility.singleLineHeight;
        }
        
        /// <summary>
        /// 使用 IMGUI 绘制层选择字段，仅在属性为整数时生效，选中值写回序列化属性。
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Integer)
            {
                return;
            }
            
            // ReSharper disable once ConvertToUsingDeclaration
            using(EditorGUI.ChangeCheckScope changed = new EditorGUI.ChangeCheckScope())
            {
                int result = EditorGUI.LayerField(position, label, property.intValue);
                if (changed.changed)
                {
                    property.intValue = result;
                }
            }
        }
        #endregion
        
//         #region UIToolkit
//         
// #if UNITY_2021_3_OR_NEWER
//         
//         private static string NameTag(SerializedProperty property) => $"{property.propertyPath}__Layer";
//
//         public override VisualElement CreatePropertyGUI(SerializedProperty property)
//         {
//             LayerField layerField = new LayerField(property.displayName)
//             {
//                 value = property.intValue,
//                 name = NameTag(property),
//             };
//
//             // layerField.AddToClassList("unity-base-field__aligned");
//             return layerField;
//         }
//
// #endif
//
//         #endregion
    }
}