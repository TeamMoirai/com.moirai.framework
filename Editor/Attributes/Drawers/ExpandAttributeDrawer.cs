#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(ExpandAttribute), true)]
    public class ExpandAttributeDrawer : PropertyDrawer
    {
        private Color _fontColor = new Color(0.15f, 0.15f, 0.15f);
        private Color _arrowColor = new Color(0.15f, 0.15f, 0.15f, 0.75f);

        private GUIStyle _textStyle = new GUIStyle();

        private const float TITLE_HEIGHT = 19;
        private const float POST_TITLE_SPACE = 0;
        private const float RIGHT_SPACE = 1;
        private const float ARROW_MARGIN = 5;
        private const float IS_ENABLED_WIDTH = 20;

#if UNITY_6000_0_OR_NEWER
        [System.Obsolete]
#endif
        public override bool CanCacheInspectorGUI(SerializedProperty property) => false;


        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float space = 0;

            if (property.isExpanded)
            {
                space = EditorGUI.GetPropertyHeight(property) + TITLE_HEIGHT * 2;
            }
            else
            {
                space = TITLE_HEIGHT;
            }

            return space;
        }


        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SetColors();

            _textStyle.normal.textColor = _fontColor;
            _textStyle.alignment = TextAnchor.MiddleLeft;


            int initialIndent = EditorGUI.indentLevel;
            float initialfieldWidth = EditorGUIUtility.fieldWidth;
            float initialLabelWidth = EditorGUIUtility.labelWidth;

            EditorGUI.indentLevel = 0;
            EditorGUIUtility.fieldWidth = 60;

            Rect referenceRect = position;
            referenceRect.height = TITLE_HEIGHT;


            Rect backgroundRect = position;
            backgroundRect.position = referenceRect.position;
            backgroundRect.width -= RIGHT_SPACE;

            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.Box(backgroundRect, GUIContent.none, EditorStyles.helpBox); // (GUIStyle)"IN ThumbnailShadow" );  
            GUI.color = Color.white;

            Rect titleRect = referenceRect;
            titleRect.width -= IS_ENABLED_WIDTH;
            titleRect.x += 7f;

            // if( GUI.Button( titleRect , GUIContent.none , EditorStyles.label ) )
            // 	property.isExpanded = !property.isExpanded;
            property.isExpanded = true;

            GUI.Label(titleRect, property.displayName, _textStyle);


            // Rect arrowRect = referenceRect;

            // arrowRect.width = 0.5f * referenceRect.height;
            // arrowRect.height = arrowRect.width;
            // arrowRect.x += ArrowMargin;
            // arrowRect.y += referenceRect.height / 2 - arrowRect.height / 2;


            // Texture arrowTexture = Resources.Load<Texture>("Icons/whiteArrowFilledIcon");

            // if( property.isExpanded )
            // 	GUIUtility.RotateAroundPivot( 90 , arrowRect.center );		

            // GUI.color = arrowColor;
            // GUI.DrawTexture( arrowRect , arrowTexture );
            // GUI.color = Color.white;

            // if( property.isExpanded )
            // 	GUIUtility.RotateAroundPivot( -90 , arrowRect.center );


            if (property.isExpanded)
            {
                EditorGUI.indentLevel = 1;

                SerializedProperty itr = property.Copy();

                bool enterChildren = true;


                Rect childRect = referenceRect;
                childRect.y += 2 * EditorGUIUtility.singleLineHeight + POST_TITLE_SPACE;
                childRect.height = EditorGUIUtility.singleLineHeight;
                childRect.width -= 10;

                while (itr.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    if (SerializedProperty.EqualContents(itr, property.GetEndProperty()))
                        break;


                    EditorGUI.PropertyField(childRect, itr);

                    childRect.y += 1.2f * EditorGUI.GetPropertyHeight(itr, null, false);
                }
            }

            EditorGUI.indentLevel = initialIndent;
            EditorGUIUtility.fieldWidth = initialfieldWidth;
            EditorGUIUtility.labelWidth = initialLabelWidth;

            EditorGUI.EndProperty();
        }


        private void SetColors()
        {
            if (EditorGUIUtility.isProSkin)
            {
                _fontColor = new Color(0.75f, 0.75f, 0.75f);
                _arrowColor = new Color(0.75f, 0.75f, 0.75f, 0.75f);
            }
            else
            {
                _fontColor = Color.black;
                _arrowColor = new Color(0.15f, 0.15f, 0.15f, 0.75f);
            }
        }
    }
}
#endif