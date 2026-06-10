using System.Reflection;
using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using UnityEngine.UIElements;
#endif

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(InspectorButtonAttribute))]
    public class InspectorButtonAttributeDrawer : PropertyDrawer
    {
        private MethodInfo _eventMethodInfo = null;

        #region IMGUI

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            InspectorButtonAttribute inspectorButtonAttribute = (InspectorButtonAttribute)attribute;

            float buttonLength = position.width;
            Rect buttonRect = new Rect(position.x, position.y, buttonLength, position.height);
            
            if (GUI.Button(buttonRect, inspectorButtonAttribute.buttonLabel))
            {
                System.Type eventOwnerType = property.serializedObject.targetObject.GetType(); 
                string eventName = inspectorButtonAttribute.methodName;

                if (_eventMethodInfo == null)
                {
                    _eventMethodInfo = eventOwnerType.GetMethod(eventName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }

                if (_eventMethodInfo != null)
                {
                    _eventMethodInfo.Invoke(property.serializedObject.targetObject, null);
                }
                else
                {
                    Debug.LogWarning($"InspectorButton: Unable to find method {eventName} in {eventOwnerType}");
                }
            }
        }

        #endregion
        
        #region UI Toolkit

#if UNITY_2021_3_OR_NEWER

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            InspectorButtonAttribute inspectorButtonAttribute = (InspectorButtonAttribute)attribute;

            var root = new VisualElement();

            var newButton = new Button
            {
                text = inspectorButtonAttribute.buttonLabel,
                style =
                {
                    display = DisplayStyle.Flex,
                    flexGrow = 1,
                }
            };

            System.Type eventOwnerType = property.serializedObject.targetObject.GetType(); 
            string eventName = inspectorButtonAttribute.methodName;
            if (_eventMethodInfo == null)
            {
                _eventMethodInfo = eventOwnerType.GetMethod(eventName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (_eventMethodInfo != null)
            {
                newButton.clicked += () => _eventMethodInfo.Invoke(property.serializedObject.targetObject, null);
            }
            else
            {
                Debug.LogWarning($"InspectorButton: Unable to find method {eventName} in {eventOwnerType}");
            }
            
            root.Add(newButton);
            return root;
        }
        
#endif
        
        #endregion
    }
}