using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using UnityEditor.UIElements;
using UnityEngine.UIElements;
#endif

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(TagAttribute))]
    public class TagAttributeDrawer : PropertyDrawer
    {
        #region IMGUI
        public override float GetPropertyHeight(SerializedProperty property, GUIContent guiContent)
        {
            return EditorGUIUtility.singleLineHeight;
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                return;
            }
            
            // ReSharper disable once ConvertToUsingDeclaration
            using(EditorGUI.ChangeCheckScope changed = new EditorGUI.ChangeCheckScope())
            {
                string result = EditorGUI.TagField(position, label, property.stringValue);
                if (changed.changed)
                {
                    property.stringValue = result;
                }
            }
        }
        #endregion
        

//         #region UIToolkit
//         
// #if UNITY_2021_3_OR_NEWER
//         private static string NameTag(SerializedProperty property) => $"{property.propertyPath}__Tag";
//
//         public override VisualElement CreatePropertyGUI(SerializedProperty property)
//         {
//             TagField tagField = new TagField(property.displayName)
//             {
//                 value = property.stringValue,
//                 name = NameTag(property),
//             };
//
//             // tagField.AddToClassList("unity-base-field__aligned");
//             return tagField;
//         }
// #endif
//
//         #endregion

    }
}