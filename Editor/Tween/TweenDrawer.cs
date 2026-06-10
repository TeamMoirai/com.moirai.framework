using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using UnityEditor.UIElements;
using UnityEngine.UIElements;
#endif

namespace Moirai.Atropos.Editor
{
    [CustomPropertyDrawer(typeof(Tween))]
    public class TweenDrawer : PropertyDrawer
    {
        #region IMGUI

        protected const int LINE_HEIGHT = 20;
        protected const int LINE_MARGIN = 2;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return LINE_HEIGHT * 2 + LINE_MARGIN;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            var definitionTypeRect = new Rect(position.x, position.y, position.width, LINE_HEIGHT);
            var curveRect = new Rect(position.x, position.y + LINE_HEIGHT + LINE_MARGIN, position.width, LINE_HEIGHT);

            var type = property.FindPropertyRelative("m_TweenType");
            EditorGUI.PropertyField(definitionTypeRect, type, GUIContent.none);
            if (type.enumValueIndex == (int)TweenTypes.Tween)
            {
                EditorGUI.PropertyField(curveRect, property.FindPropertyRelative("m_EaseType"), GUIContent.none);
            }
            if (type.enumValueIndex == (int)TweenTypes.AnimationCurve)
            {
                EditorGUI.PropertyField(curveRect, property.FindPropertyRelative("m_AnimationCurve"), GUIContent.none);
            }

            EditorGUI.EndProperty();
        }

        #endregion

        #region UI Toolkit

#if UNITY_2021_3_OR_NEWER

        private VisualElement _rootVisualElement;
        private SerializedProperty _tweenDefinitionType;
        private PropertyField _tweenCurveField;
        private PropertyField _curveField;
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            _rootVisualElement = new VisualElement();

            _tweenDefinitionType = property.FindPropertyRelative("m_TweenType");

            var definitionType = new PropertyField(_tweenDefinitionType);
            definitionType.RegisterValueChangeCallback(OnTweenDefinitionTypeChanged);
            _rootVisualElement.Add(definitionType);

            _tweenCurveField = new PropertyField(property.FindPropertyRelative("m_EaseType"));
            _rootVisualElement.Add(_tweenCurveField);

            _curveField = new PropertyField(property.FindPropertyRelative("m_AnimationCurve"));
            _rootVisualElement.Add(_curveField);

            UpdateVisibility();
            return _rootVisualElement;
        }

        private void OnTweenDefinitionTypeChanged(SerializedPropertyChangeEvent evt)
        {
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (_tweenCurveField != null && _curveField != null)
            {
                _tweenCurveField.style.display = _tweenDefinitionType.enumValueIndex == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _curveField.style.display = _tweenDefinitionType.enumValueIndex == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

#endif

        #endregion
    }
}
