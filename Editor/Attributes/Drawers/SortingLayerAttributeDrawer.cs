using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using Moirai.Atropos.Attributes.Editor.Utils;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
#endif

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(SortingLayerAttribute))]
    public class SortingLayerAttributeDrawer : PropertyDrawer
    {
        private static string[] GetLayers()
        {
            // Type internalEditorUtilityType = typeof(UnityEditorInternal.InternalEditorUtility);
            // PropertyInfo sortingLayersProperty = internalEditorUtilityType.GetProperty("sortingLayerNames", BindingFlags.Static | BindingFlags.NonPublic);
            // Debug.Assert(sortingLayersProperty != null);
            // return (string[])sortingLayersProperty.GetValue(null, Array.Empty<object>());
            return SortingLayer.layers.Select(each => each.name).ToArray();
        }
        
        private static void OpenSortingLayerInspector()
        {
            // TagManagerInspector.ShowWithInitialExpansion(TagManagerInspector.InitialExpansionState.Layers)
            Type tagManagerInspectorType = Type.GetType("UnityEditor.TagManagerInspector, UnityEditor");
            // Get the method Info for the ShowWithInitialExpansion method
            if (tagManagerInspectorType == null)
            {
                return;
            }

            MethodInfo showWithInitialExpansionMethod = tagManagerInspectorType.GetMethod("ShowWithInitialExpansion", BindingFlags.Static | BindingFlags.NonPublic);
            if (showWithInitialExpansionMethod == null)
            {
                return;
            }

            Type initialExpansionStateType = tagManagerInspectorType.GetNestedType("InitialExpansionState", BindingFlags.NonPublic);
            object layersEnumValue = Enum.Parse(initialExpansionStateType, "SortingLayers");
            // Invoke the ShowWithInitialExpansion method with the Layers enum value
            showWithInitialExpansionMethod.Invoke(null, new object[] { layersEnumValue });
        }

        #region IMGUI
        public override float GetPropertyHeight(SerializedProperty property, GUIContent guiContent)
        {
            return EditorGUIUtility.singleLineHeight;
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            string[] layers = GetLayers();

            int selectedIndex = property.propertyType == SerializedPropertyType.Integer ? property.intValue : Array.IndexOf(layers, property.stringValue);

            // ReSharper disable once ConvertToUsingDeclaration
            using (EditorGUI.ChangeCheckScope changed = new EditorGUI.ChangeCheckScope())
            {
                int newIndex = EditorGUI.Popup(position, label, selectedIndex,
                    layers
                        .Select(each => each == ""? new GUIContent("<empty string>") : new GUIContent(each))
                        .Concat(new[]{GUIContent.none, new GUIContent("Edit Sorting Layers...")})
                        .ToArray());
                // ReSharper disable once InvertIf
                if (changed.changed)
                {
                    if (newIndex >= layers.Length)
                    {
                        OpenSortingLayerInspector();
                        return;
                    }

                    if (property.propertyType == SerializedPropertyType.Integer)
                    {
                        property.intValue = newIndex;
                    }
                    else
                    {
                        property.stringValue = layers[newIndex];
                    }
                }
            }
        }
        #endregion

//         #region UIToolkit
//
// #if UNITY_2021_3_OR_NEWER
//         private static string NameButtonField(SerializedProperty property) => $"{property.propertyPath}__SortingLayer_Button";
//
//         public override VisualElement CreatePropertyGUI(SerializedProperty property)
//         {
//             UIToolkitUtils.DropdownButtonField dropdownButton =
//                 UIToolkitUtils.MakeDropdownButtonUIToolkit(property.displayName);
//             dropdownButton.style.flexGrow = 1;
//             dropdownButton.name = NameButtonField(property);
//
//             (string[] _, int _, string displayName) = GetSelected(property);
//             dropdownButton.ButtonLabelElement.text = displayName;
//             
//             dropdownButton.ButtonElement.clicked += () =>
//                 ShowDropdown(property, dropdownButton);
//             
//             return dropdownButton;
//         }
//         
//         private static void ShowDropdown(SerializedProperty property,
//             UIToolkitUtils.DropdownButtonField buttonLabel)
//         {
//             (string[] layers, int selectedIndex, string _) = GetSelected(property);
//
//             GenericDropdownMenu genericDropdownMenu = new GenericDropdownMenu();
//             // if (layers.Length == 0)
//             // {
//             //     genericDropdownMenu.AddDisabledItem("No layers", false);
//             //     genericDropdownMenu.DropDown(button.worldBound, button, true);
//             //     return;
//             // }
//             
//             foreach (int index in Enumerable.Range(0, layers.Length))
//             {
//                 int curIndex = index;
//                 string curItem = layers[index];
//
//                 string curName = $"{index}: {curItem}";
//
//                 genericDropdownMenu.AddItem(curName, index == selectedIndex, () =>
//                 {
//
//                     if (property.propertyType == SerializedPropertyType.String)
//                     {
//                         property.stringValue = curItem;
//                         property.serializedObject.ApplyModifiedProperties();
//                     }
//                     else
//                     {
//                         property.intValue = curIndex;
//                         property.serializedObject.ApplyModifiedProperties();
//                     }
//                     buttonLabel.ButtonLabelElement.text = curName;
//                 });
//             }
//
//             if (layers.Length > 0)
//             {
//                 genericDropdownMenu.AddSeparator("");
//             }
//             genericDropdownMenu.AddItem("Edit Sorting Layers...", false, OpenSortingLayerInspector);
//
//             genericDropdownMenu.DropDown(buttonLabel.ButtonElement.worldBound, buttonLabel, true);
//         }
//         
//         private static (string[] layers, int index, string display) GetSelected(SerializedProperty property)
//         {
//             string[] layers = GetLayers();
//             if(property.propertyType == SerializedPropertyType.String)
//             {
//                 string value = property.stringValue;
//                 int index = Array.IndexOf(layers, value);
//                 return (layers, index, index == -1? $"?: {value}": $"{index}: {value}");
//             }
//             else
//             {
//                 int index = property.intValue;
//                 if(index < 0 || index >= layers.Length)
//                 {
//                     return (layers, -1, $"{index}: ?");
//                 }
//                 return (layers, index, $"{index}: {layers[index]}");
//             }
//         }
// #endif
//
//         #endregion

    }
}