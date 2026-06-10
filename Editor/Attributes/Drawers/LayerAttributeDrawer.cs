using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using UnityEditor.UIElements;
using UnityEngine.UIElements;
#endif

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(LayerAttribute))]
    internal class LayerAttributeDrawer : PropertyDrawer
    {
        #region IMGUI
        public override float GetPropertyHeight(SerializedProperty property, GUIContent guiContent)
        {
            return EditorGUIUtility.singleLineHeight;
        }
        
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