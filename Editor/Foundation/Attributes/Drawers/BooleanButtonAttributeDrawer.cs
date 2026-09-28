using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(BooleanButtonAttribute))]
    public class BooleanButtonAttributeDrawer : PropertyDrawer
    {
        private const string ENABLED_STYLE_NAME = "flow node 2";
        private const string DISABLED_STYLE_NAME = "flow node 0";

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);


            var enabledStyle = new GUIStyle(EditorStyles.miniButton);
            enabledStyle.fontStyle = FontStyle.Bold;

            var disabledStyle = new GUIStyle(EditorStyles.miniButton);
            disabledStyle.fontStyle = FontStyle.Normal;
            var textColor = disabledStyle.normal.textColor;     textColor.a = 0.4f;
            disabledStyle.normal.textColor = textColor;

            var at = attribute as BooleanButtonAttribute;

            GUIContent labelContent = at.Label != null ? new GUIContent(at.Label) : label;
            Rect fieldRect = EditorGUI.PrefixLabel(position, labelContent);

            fieldRect.width *= 0.5f;

            bool value = property.boolValue;
            if (at.FalseLabelFirst)
            {
                if (value)
                {
                    if (GUI.Button(fieldRect, at.FalseLabel, disabledStyle))
                        value = false;

                    fieldRect.x += fieldRect.width;

                    GUI.Button(fieldRect, at.TrueLabel, enabledStyle);
                }
                else
                {
                    GUI.Button(fieldRect, at.FalseLabel, enabledStyle);

                    fieldRect.x += fieldRect.width;

                    if (GUI.Button(fieldRect, at.TrueLabel, disabledStyle))
                        value = true;
                }
            }
            else
            {
                if (value)
                {
                    GUI.Button(fieldRect, at.TrueLabel, enabledStyle);

                    fieldRect.x += fieldRect.width;

                    if (GUI.Button(fieldRect, at.FalseLabel, disabledStyle))
                        value = false;
                }
                else
                {
                    if (GUI.Button(fieldRect, at.TrueLabel, disabledStyle))
                        value = true;

                    fieldRect.x += fieldRect.width;

                    GUI.Button(fieldRect, at.FalseLabel, enabledStyle);
                }
            }

            property.boolValue = value;

            EditorGUI.EndProperty();
        }

        //public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        //{

        //    return 1.2f * (EditorGUIUtility.singleLineHeight + 2f);
        //}
    }

    /// <summary>
    /// Odin 原生 Drawer，为 <see cref="BooleanButtonAttribute"/> 自动接管 Odin 绘制，
    /// 直接在 Odin 布局上下文中绘制按钮组，不经过 Unity PropertyField / SerializedProperty。
    /// <para>无需在每个字段上手动添加 <c>[DrawWithUnity]</c>。</para>
    /// </summary>
    [DrawerPriority(0, 10001, 0)]
    internal sealed class BooleanButtonOdinDrawer : OdinAttributeDrawer<BooleanButtonAttribute>
    {
        private GUIStyle _enabledStyle;
        private GUIStyle _disabledStyle;

        protected override void Initialize()
        {
            _enabledStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = FontStyle.Bold
            };

            _disabledStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = FontStyle.Normal
            };
            var c = _disabledStyle.normal.textColor;
            c.a = 0.4f;
            _disabledStyle.normal.textColor = c;
        }

        protected override bool CanDrawAttributeProperty(InspectorProperty property)
        {
            return property.ValueEntry != null
                && property.ValueEntry.BaseValueType == typeof(bool);
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            var attr = Attribute;
            var valueEntry = Property.ValueEntry;

            GUIContent labelContent = attr.Label != null ? new GUIContent(attr.Label) : label;

            Rect rect = EditorGUILayout.GetControlRect(labelContent != GUIContent.none);
            Rect fieldRect = EditorGUI.PrefixLabel(rect, labelContent);
            fieldRect.width *= 0.5f;

            bool value = (bool)valueEntry.WeakSmartValue;

            if (attr.FalseLabelFirst)
            {
                if (value)
                {
                    if (GUI.Button(fieldRect, attr.FalseLabel, _disabledStyle))
                        value = false;

                    fieldRect.x += fieldRect.width;

                    GUI.Button(fieldRect, attr.TrueLabel, _enabledStyle);
                }
                else
                {
                    GUI.Button(fieldRect, attr.FalseLabel, _enabledStyle);

                    fieldRect.x += fieldRect.width;

                    if (GUI.Button(fieldRect, attr.TrueLabel, _disabledStyle))
                        value = true;
                }
            }
            else
            {
                if (value)
                {
                    GUI.Button(fieldRect, attr.TrueLabel, _enabledStyle);

                    fieldRect.x += fieldRect.width;

                    if (GUI.Button(fieldRect, attr.FalseLabel, _disabledStyle))
                        value = false;
                }
                else
                {
                    if (GUI.Button(fieldRect, attr.TrueLabel, _disabledStyle))
                        value = true;

                    fieldRect.x += fieldRect.width;

                    GUI.Button(fieldRect, attr.FalseLabel, _enabledStyle);
                }
            }

            if (value != (bool)valueEntry.WeakSmartValue)
                valueEntry.WeakSmartValue = value;
        }
    }
}