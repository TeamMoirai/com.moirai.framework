#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    /// <summary>
    /// <see cref="ExpandAttribute"/> 绘制器：以带标题栏的样式绘制属性，并手动排布其子字段。
    /// </summary>
    [CustomPropertyDrawer(typeof(ExpandAttribute), true)]
    public class ExpandAttributeDrawer : PropertyDrawer
    {
        private Color _fontColor = new Color(0.15f, 0.15f, 0.15f);

        private readonly GUIStyle _textStyle = new GUIStyle();

        private const float TITLE_HEIGHT = 19;
        private const float CHILD_GAP = 2f;
        private const float RIGHT_SPACE = 1;
        private const float IS_ENABLED_WIDTH = 20;

        /// <summary>
        /// 禁用 Inspector GUI 缓存，确保属性每次都重新绘制。
        /// </summary>
#if UNITY_6000_0_OR_NEWER
        [System.Obsolete]
#endif
        public override bool CanCacheInspectorGUI(SerializedProperty property) => false;

        /// <summary>
        /// 获取属性高度：始终按展开态计算（OnGUI 会强制展开），子高度按可见子属性逐项累加。
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // 禁止对同一属性调用 EditorGUI.GetPropertyHeight(property)：
            // 会触发本 Drawer 重入并返回 0，标题以下区域高度塌陷 → 子字段被裁切显示为空白。
            float height = TITLE_HEIGHT * 2f;

            if (IsObjectReference(property))
            {
                height += EditorGUIUtility.singleLineHeight + CHILD_GAP;
                if (property.objectReferenceValue != null)
                    height += GetObjectTargetChildrenHeight(property);
                return height;
            }

            return height + GetChildrenHeight(property);
        }

        /// <summary>
        /// 绘制标题栏背景与属性名，并逐行绘制子字段（含 SerializeReference 多态子字段；
        /// Object 引用会展开目标 ScriptableObject/资产的序列化字段）。
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SetColors();

            _textStyle.normal.textColor = _fontColor;
            _textStyle.alignment = TextAnchor.MiddleLeft;

            int initialIndent = EditorGUI.indentLevel;
            float initialFieldWidth = EditorGUIUtility.fieldWidth;
            float initialLabelWidth = EditorGUIUtility.labelWidth;

            EditorGUI.indentLevel = 0;
            EditorGUIUtility.fieldWidth = 60;

            Rect referenceRect = position;
            referenceRect.height = TITLE_HEIGHT;

            Rect backgroundRect = position;
            backgroundRect.position = referenceRect.position;
            backgroundRect.width -= RIGHT_SPACE;

            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.Box(backgroundRect, GUIContent.none, EditorStyles.helpBox);
            GUI.color = Color.white;

            Rect titleRect = referenceRect;
            titleRect.width -= IS_ENABLED_WIDTH;
            titleRect.x += 7f;

            property.isExpanded = true;

            GUI.Label(titleRect, property.displayName, _textStyle);

            EditorGUI.indentLevel = 1;

            Rect childRect = referenceRect;
            childRect.y += TITLE_HEIGHT + CHILD_GAP;
            childRect.height = EditorGUIUtility.singleLineHeight;
            childRect.width -= 10;

            if (IsObjectReference(property))
            {
                // 禁止 PropertyField(property)：会重入本 Drawer，导致内容画两遍
                float prevFieldWidth = EditorGUIUtility.fieldWidth;
                EditorGUIUtility.fieldWidth = 0f;
                DrawObjectFieldDirect(childRect, property);
                EditorGUIUtility.fieldWidth = prevFieldWidth;
                childRect.y += EditorGUIUtility.singleLineHeight + CHILD_GAP;

                DrawObjectTargetFields(property, ref childRect);
            }
            else
            {
                bool drewChild = false;
                ForEachChild(property, (child) =>
                {
                    EditorGUI.PropertyField(childRect, child, true);
                    childRect.y += EditorGUI.GetPropertyHeight(child, true) + CHILD_GAP;
                    drewChild = true;
                });

                if (!drewChild && IsNullManagedReference(property))
                {
                    EditorGUI.indentLevel = 0;
                    Rect nullRect = childRect;
                    nullRect.x += 7f;
                    EditorGUI.LabelField(nullRect, "(Null)", EditorStyles.miniLabel);
                }
            }

            EditorGUI.indentLevel = initialIndent;
            EditorGUIUtility.fieldWidth = initialFieldWidth;
            EditorGUIUtility.labelWidth = initialLabelWidth;

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 遍历属性的可见子字段（在结束属性处停止），兼容 SerializeReference 多态实例。
        /// </summary>
        internal static void ForEachChild(SerializedProperty property, System.Action<SerializedProperty> action)
        {
            SerializedProperty end = property.GetEndProperty();
            SerializedProperty child = property.Copy();
            bool enterChildren = true;

            while (child.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (SerializedProperty.EqualContents(child, end))
                    break;
                action(child);
            }
        }

        internal static float GetChildrenHeight(SerializedProperty property)
        {
            float height = 0f;
            ForEachChild(property, (child) =>
            {
                height += EditorGUI.GetPropertyHeight(child, true) + CHILD_GAP;
            });
            return height;
        }

        internal static bool IsNullManagedReference(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.ManagedReference
                   && property.managedReferenceValue == null;
        }

        internal static bool IsObjectReference(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.ObjectReference;
        }

        /// <summary>
        /// 直接绘制 Object 引用字段。
        /// 不能用 PropertyField(property)——属性带 Expand 时会重入 Drawer，内容翻倍。
        /// </summary>
        internal static void DrawObjectFieldDirect(Rect rect, SerializedProperty property)
        {
            EditorGUI.BeginChangeCheck();
            Object next = EditorGUI.ObjectField(rect, property.objectReferenceValue, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
                property.objectReferenceValue = next;
        }

        /// <summary>
        /// 展开 Object 引用目标的可见序列化字段（跳过 m_Script）。
        /// </summary>
        internal static void DrawObjectTargetFields(SerializedProperty property, ref Rect childRect)
        {
            Object target = property.objectReferenceValue;
            if (target == null)
                return;

            using (SerializedObject targetSo = new SerializedObject(target))
            {
                targetSo.Update();
                SerializedProperty it = targetSo.GetIterator();
                bool enterChildren = true;
                while (it.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (it.name == "m_Script")
                        continue;

                    EditorGUI.PropertyField(childRect, it, true);
                    childRect.y += EditorGUI.GetPropertyHeight(it, true) + CHILD_GAP;
                }

                targetSo.ApplyModifiedProperties();
            }
        }

        internal static float GetObjectTargetChildrenHeight(SerializedProperty property)
        {
            Object target = property.objectReferenceValue;
            if (target == null)
                return 0f;

            float height = 0f;
            using (SerializedObject targetSo = new SerializedObject(target))
            {
                SerializedProperty it = targetSo.GetIterator();
                bool enterChildren = true;
                while (it.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (it.name == "m_Script")
                        continue;

                    height += EditorGUI.GetPropertyHeight(it, true) + CHILD_GAP;
                }
            }

            return height;
        }

        private void SetColors()
        {
            if (EditorGUIUtility.isProSkin)
            {
                _fontColor = new Color(0.75f, 0.75f, 0.75f);
            }
            else
            {
                _fontColor = Color.black;
            }
        }
    }

    /// <summary>
    /// Odin 原生 Drawer，为 <see cref="ExpandAttribute"/> 接管 Odin 绘制。
    /// </summary>
    /// <remarks>
    /// 优先级 super=1，优先于默认 managed reference drawer 与 DrawWithUnity(10000)。
    /// Object 引用字段必须用 ObjectField 直绘，禁止 PropertyField(本属性)——会重入导致内容×2。
    /// </remarks>
    [DrawerPriority(1, 0, 0)]
    internal sealed class ExpandOdinDrawer : OdinAttributeDrawer<ExpandAttribute>
    {
        private static GUIStyle s_TitleStyle;

        private static GUIStyle TitleStyle
        {
            get
            {
                if (s_TitleStyle == null)
                {
                    Color font = EditorGUIUtility.isProSkin
                        ? new Color(0.75f, 0.75f, 0.75f)
                        : Color.black;
                    s_TitleStyle = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontStyle = FontStyle.Normal
                    };
                    s_TitleStyle.normal.textColor = font;
                }
                return s_TitleStyle;
            }
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            SerializedProperty prop = null;
            try { prop = Property.Tree.GetUnityPropertyForPath(Property.UnityPropertyPath); }
            catch { prop = null; }

            string titleText = label != null && !string.IsNullOrEmpty(label.text)
                ? label.text
                : Property.NiceName;

            if (prop != null && prop.propertyType != SerializedPropertyType.ManagedReference)
            {
                DrawUnityPath(prop, titleText);
                return;
            }

            bool isNullRef = Property.ValueEntry != null && Property.ValueEntry.WeakSmartValue == null;
            bool hasOdinChildren = !isNullRef && HasVisibleOdinChildren();

            if (!hasOdinChildren && prop != null)
            {
                DrawUnityPath(prop, titleText);
                return;
            }

            DrawOdinPath(titleText, isNullRef);
        }

        private bool HasVisibleOdinChildren()
        {
            var children = Property.Children;
            for (int i = 0; i < children.Count; i++)
                if (children[i].State.Visible)
                    return true;
            return false;
        }

        private static void DrawUnityPath(SerializedProperty prop, string titleText)
        {
            Rect row = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight + 4f, GUILayout.ExpandWidth(true));
            GUI.Box(row, GUIContent.none, EditorStyles.helpBox);
            Rect titleRect = row;
            titleRect.x += 7f;
            titleRect.width -= 14f;
            GUI.Label(titleRect, titleText, TitleStyle);

            EditorGUI.indentLevel++;
            try
            {
                if (ExpandAttributeDrawer.IsObjectReference(prop))
                {
                    // 禁止 PropertyField(prop)：重入 ExpandAttributeDrawer → 内容×2
                    Rect fieldRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                    ExpandAttributeDrawer.DrawObjectFieldDirect(fieldRect, prop);

                    Object target = prop.objectReferenceValue;
                    if (target == null)
                        return;

                    using (SerializedObject targetSo = new SerializedObject(target))
                    {
                        targetSo.Update();
                        SerializedProperty it = targetSo.GetIterator();
                        bool enterChildren = true;
                        while (it.NextVisible(enterChildren))
                        {
                            enterChildren = false;
                            if (it.name == "m_Script")
                                continue;

                            EditorGUILayout.PropertyField(it, true);
                        }

                        targetSo.ApplyModifiedProperties();
                    }
                }
                else
                {
                    bool drewChild = false;
                    ExpandAttributeDrawer.ForEachChild(prop, (child) =>
                    {
                        EditorGUILayout.PropertyField(child, true);
                        drewChild = true;
                    });

                    if (!drewChild && ExpandAttributeDrawer.IsNullManagedReference(prop))
                        EditorGUILayout.LabelField("(Null)", EditorStyles.miniLabel);
                }
            }
            finally
            {
                EditorGUI.indentLevel--;
            }
        }

        private void DrawOdinPath(string titleText, bool isNullRef)
        {
            Rect row = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight + 4f, GUILayout.ExpandWidth(true));
            GUI.Box(row, GUIContent.none, EditorStyles.helpBox);

            Rect titleRect = row;
            titleRect.x += 7f;
            titleRect.width -= 14f;

            string displayTitle = isNullRef ? $"{titleText}  ·  Null" : titleText;
            GUI.Label(titleRect, displayTitle, TitleStyle);

            if (isNullRef)
                return;

            GUIHelper.PushIndentLevel(1);
            try
            {
                var children = Property.Children;
                for (int i = 0; i < children.Count; i++)
                {
                    InspectorProperty child = children[i];
                    if (!child.State.Visible)
                        continue;
                    child.Draw();
                }
            }
            finally
            {
                GUIHelper.PopIndentLevel();
            }
        }
    }
}
#endif
