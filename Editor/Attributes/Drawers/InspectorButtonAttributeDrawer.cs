using System.Reflection;
using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using UnityEngine.UIElements;
#endif

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    /// <summary>
    /// <see cref="InspectorButtonAttribute"/> 绘制器：绘制一个按钮，点击时通过反射调用目标对象上指定的方法（支持 IMGUI 与 UI Toolkit）。
    /// </summary>
    [CustomPropertyDrawer(typeof(InspectorButtonAttribute))]
    public class InspectorButtonAttributeDrawer : PropertyDrawer
    {
        // 缓存目标方法的反射信息，避免每次绘制都重复查找。
        private MethodInfo _eventMethodInfo = null;

        #region IMGUI

        /// <summary>
        /// 绘制按钮，点击时通过反射调用 <paramref name="property"/> 目标对象上指定的方法。
        /// </summary>
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

        /// <summary>
        /// 使用 UI Toolkit 创建按钮控件，点击时通过反射调用目标对象上指定的方法。
        /// </summary>
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