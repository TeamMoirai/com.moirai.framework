using System;
using System.Collections.Generic;
using System.IO;
using Moirai.Atropos.Attributes.Editor.Utils;
using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using System.Reflection;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
#endif
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(ResourcePathAttribute))]
    public class ResourcePathAttributeDrawer : PropertyDrawer
    {

        #region IMGUI

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            ResourcePathAttribute resourcePathAttribute = (ResourcePathAttribute)attribute;
            Type requiredComp = resourcePathAttribute.RequiredType;
            // Debug.Log(requiredComp);
            EStr eStr = resourcePathAttribute.EStr;
            string curStrValue = property.stringValue;
            Object requiredValue = GetObjFromStr(curStrValue, requiredComp, eStr);

            if (!string.IsNullOrEmpty(curStrValue) && requiredValue == null)
            {
                EditorGUILayout.LabelField(label.text, $"Can't find asset in {property.stringValue}", EditorStyles.boldLabel);
                if (GUILayout.Button("Clear"))
                {
                    property.stringValue = string.Empty;
                }

                return;
            }

            // ReSharper disable once ConvertToUsingDeclaration
            using (EditorGUI.ChangeCheckScope changed = new EditorGUI.ChangeCheckScope())
            {
                Object fieldResult = EditorGUI.ObjectField(position, label, requiredValue, requiredComp, false);
                // ReSharper disable once InvertIf
                if (changed.changed)
                {
                    string result = GetNewValue(fieldResult, eStr);
                    // Debug.Log($"field change {fieldResult} -> {result}, null={result==null}");
                    property.stringValue = result;
                    // has issue on null, need to use reflection
                    // nah... not work... still get an empty string if it's null
                    (_, object parent) = SerializedUtils.GetAttributesAndDirectParent<ResourcePathAttribute>(property);
                    ReflectUtils.SetValue(property.propertyPath, property.serializedObject.targetObject, fieldInfo, parent, result);
                    property.serializedObject.ApplyModifiedProperties();
                    // Debug.Log($"set value to {result}");
                }
            }
        }

        #endregion
        
//         #region UIToolkit
//
// #if UNITY_2021_3_OR_NEWER
//         
//         private static string NameObjectField(SerializedProperty property) => $"{property.propertyPath}__ResourcePath_ObjectField";
//
//         public override VisualElement CreatePropertyGUI(SerializedProperty property)
//         {
//             ResourcePathAttribute fieldTypeAttribute = (ResourcePathAttribute)attribute;
//             Type requiredComp = fieldTypeAttribute.RequiredType;
//             Object requiredValue = GetObjFromStr(property.stringValue, requiredComp, fieldTypeAttribute.EStr);
//
//             ObjectField objectField = new ObjectField(property.displayName)
//             {
//                 name = NameObjectField(property),
//                 objectType = requiredComp,
//                 allowSceneObjects = true,
//                 value = requiredValue,
//                 style =
//                 {
//                     flexShrink = 1,
//                 },
//             };
//
//             objectField.AddToClassList(ObjectField.alignedFieldUssClassName);
//
//             objectField.Bind(property.serializedObject);
//             
//             (_, object parent) = SerializedUtils.GetAttributesAndDirectParent<ResourcePathAttribute>(property);
//             objectField.RegisterValueChangedCallback(evt =>
//             {
//                 OnValueChangeObjectField(property, fieldInfo, evt.newValue, fieldTypeAttribute, parent);
//             });
//             
//             return objectField;
//         }
//         
//         private static void OnValueChangeObjectField(SerializedProperty property, FieldInfo info, Object newValue, ResourcePathAttribute resourcePathAttribute, object parent)
//         {
//             // ObjectField target = container.Q<ObjectField>(NameObjectField(property));
//             string newStringValue = property.stringValue = GetNewValue(newValue, resourcePathAttribute.EStr);
//             property.serializedObject.ApplyModifiedProperties();
//             ReflectUtils.SetValue(property.propertyPath, property.serializedObject.targetObject, info, parent, newStringValue);
//         }
//         
// #endif
//
//         #endregion

        private static string GetNewValue(Object value, EStr eStr)
        {
            if (value == null)
            {
                return null;
            }

            switch (eStr)
            {
                case EStr.Resource:
                {
                    string resourcePath = AssetDatabase.GetAssetPath(value);
                    List<string> pathParts = new List<string>();
                    foreach (string pathPart in resourcePath.Split('/'))
                    {
                        if (pathPart == "Resources")
                        {
                            pathParts.Clear();
                        }
                        else
                        {
                            pathParts.Add(pathPart);
                        }
                    }
                    Debug.Assert(pathParts.Count > 0);
                    int lastIndex = pathParts.Count - 1;
                    string last = pathParts[lastIndex];
                    pathParts[lastIndex] = Path.GetFileNameWithoutExtension(last);

                    return string.Join("/", pathParts);
                }

                case EStr.AssetDatabase:
                {
                    return AssetDatabase.GetAssetPath(value);
                }

                case EStr.Guid:
                {
                    return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value));
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(eStr), eStr, null);
            }

        }

        private static Object GetObjFromStr(string curStrValue, Type requiredType, EStr eStr)
        {
            if (string.IsNullOrEmpty(curStrValue))
            {
                return null;
            }

            Object obj = null;

            switch (eStr)
            {
                case EStr.Resource:
                    obj = Resources.Load(curStrValue);
                    break;
                case EStr.AssetDatabase:
                    obj = AssetDatabase.LoadAssetAtPath<Object>(curStrValue);
                    break;
                case EStr.Guid:
                    obj = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(curStrValue));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(eStr), eStr, null);
            }

            return obj == null ? null : Util.GetTypeFromObj(obj, requiredType);
        }
    }
}