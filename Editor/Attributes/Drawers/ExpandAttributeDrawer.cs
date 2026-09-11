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
                // Object 引用：仅一行引用字段。SO 内容由 ExpandOdinDrawer 展开，
                // Unity 侧再展开会导致 Entries 画两遍。
                height += EditorGUIUtility.singleLineHeight + CHILD_GAP;
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
                // 禁止 PropertyField(property)：会重入本 Drawer，导致内容画两遍。
                // 不在此展开 SO：由 ExpandOdinDrawer 统一展开，避免双份。
                float prevFieldWidth = EditorGUIUtility.fieldWidth;
                EditorGUIUtility.fieldWidth = 0f;
                DrawObjectFieldDirect(childRect, property);
                EditorGUIUtility.fieldWidth = prevFieldWidth;
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
    /// Object 引用目标必须用 Odin <see cref="PropertyTree"/> 展开：Unity SerializedProperty
    /// 不会应用子类型上的 LabelText / Min / EnumCondition 等 Odin 特性。
    /// 禁止 PropertyField(本属性)——会重入导致内容×2。
    /// </remarks>
    [DrawerPriority(1, 0, 0)]
    internal sealed class ExpandOdinDrawer : OdinAttributeDrawer<ExpandAttribute>
    {
        /// <summary>已做过「首次默认展开」的 propertyPath，之后允许用户收起。</summary>
        private static readonly System.Collections.Generic.HashSet<string> s_FoldoutInitialized = new();

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

            string titleText = ResolveTitle(label);

            if (ExpandAttributeDrawer.IsObjectReference(prop))
            {
                DrawObjectReferencePath(prop, titleText);
                return;
            }

            bool isNullRef = Property.ValueEntry != null && Property.ValueEntry.WeakSmartValue == null;
            bool hasOdinChildren = !isNullRef && HasVisibleOdinChildren();

            if (hasOdinChildren)
            {
                DrawOdinPath(titleText, isNullRef);
                return;
            }

            if (prop != null && prop.propertyType != SerializedPropertyType.ManagedReference)
            {
                DrawUnityPath(prop, titleText);
                return;
            }

            DrawOdinPath(titleText, isNullRef);
        }

        /// <summary>
        /// 标题：优先 LabelText（高优先级 Drawer 不会走 LabelText 的 CallNextDrawer 链）。
        /// </summary>
        private string ResolveTitle(GUIContent label)
        {
            if (Property.Label != null && !string.IsNullOrEmpty(Property.Label.text)
                && !string.Equals(Property.Label.text, Property.NiceName, System.StringComparison.Ordinal))
                return Property.Label.text;

            try
            {
                var attrs = Property.Attributes;
                for (int i = 0; i < attrs.Count; i++)
                {
                    if (attrs[i] is Sirenix.OdinInspector.LabelTextAttribute lt && !string.IsNullOrEmpty(lt.Text))
                    {
                        if (!lt.Text.StartsWith("@"))
                            return lt.Text;
                    }
                }
            }
            catch
            {
                // ignore
            }

            if (label != null && !string.IsNullOrEmpty(label.text)
                && !string.Equals(label.text, Property.NiceName, System.StringComparison.Ordinal))
                return label.text;

            if (Property.Label != null && !string.IsNullOrEmpty(Property.Label.text))
                return Property.Label.text;

            return Property.NiceName;
        }

        private bool HasVisibleOdinChildren()
        {
            var children = Property.Children;
            for (int i = 0; i < children.Count; i++)
                if (children[i].State.Visible)
                    return true;
            return false;
        }

        private static void DrawTitleBar(string titleText)
        {
            Rect row = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight + 4f, GUILayout.ExpandWidth(true));
            GUI.Box(row, GUIContent.none, EditorStyles.helpBox);
            Rect titleRect = row;
            titleRect.x += 7f;
            titleRect.width -= 14f;
            GUI.Label(titleRect, titleText, TitleStyle);
        }

        /// <summary>
        /// Object 引用：标题 + 引用框 + 展开目标 SO。
        /// 顶层字段走 Unity SerializedObject；列表/数组的自定义元素用 PropertyTree.Create(boxedValue)
        /// 绘制，使 PoolEntry 等类型上的 LabelText / Min / EnumCondition 生效。
        /// </summary>
        private static void DrawObjectReferencePath(SerializedProperty prop, string titleText)
        {
            DrawTitleBar(titleText);

            Rect fieldRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            ExpandAttributeDrawer.DrawObjectFieldDirect(fieldRect, prop);

            Object target = prop.objectReferenceValue;
            if (target == null)
                return;

            GUIHelper.PushIndentLevel(1);
            try
            {
                using (SerializedObject targetSo = new SerializedObject(target))
                {
                    targetSo.Update();
                    EditorGUI.BeginChangeCheck();
                    SerializedProperty it = targetSo.GetIterator();
                    bool enterChildren = true;
                    while (it.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (it.name == "m_Script")
                            continue;

                        DrawTargetProperty(it);
                    }

                    if (EditorGUI.EndChangeCheck())
                        targetSo.ApplyModifiedProperties();
                }
            }
            finally
            {
                GUIHelper.PopIndentLevel();
            }
        }

        /// <summary>
        /// 数组/列表：逐元素展开，子字段用反射读 LabelText 后交给 Unity PropertyField。
        /// 不用 PropertyTree.Create/Dispose（每帧建树会拖垮 Inspector）。
        /// </summary>
        private static void DrawTargetProperty(SerializedProperty property)
        {
            bool isEnumerable = property.isArray
                                && property.propertyType != SerializedPropertyType.String;

            if (!isEnumerable || property.arraySize == 0)
            {
                EditorGUILayout.PropertyField(property, true);
                return;
            }

            // 首次绘制默认展开，之后允许点击收起（禁止每帧强制 true）
            if (s_FoldoutInitialized.Add(property.propertyPath))
                property.isExpanded = true;

            property.isExpanded = EditorGUILayout.Foldout(
                property.isExpanded,
                new GUIContent(property.displayName, property.tooltip),
                true);

            if (!property.isExpanded)
                return;

            EditorGUI.indentLevel++;
            try
            {
                for (int i = 0; i < property.arraySize; i++)
                {
                    SerializedProperty element = property.GetArrayElementAtIndex(i);
                    DrawEnumerableElement(element);
                }
            }
            finally
            {
                EditorGUI.indentLevel--;
            }
        }

        /// <summary>
        /// 列表元素：默认展开，子字段标签取自字段上的 LabelText（无则用 Unity 名称）。
        /// </summary>
        private static void DrawEnumerableElement(SerializedProperty element)
        {
            if (s_FoldoutInitialized.Add(element.propertyPath))
                element.isExpanded = true;

            element.isExpanded = EditorGUILayout.Foldout(
                element.isExpanded,
                new GUIContent(element.displayName),
                true);
            if (!element.isExpanded)
                return;

            EditorGUI.indentLevel++;
            try
            {
                System.Type elementType = null;
                try
                {
                    object boxed = element.boxedValue;
                    if (boxed != null)
                        elementType = boxed.GetType();
                }
                catch
                {
                    // boxedValue 不可用时退回纯 Unity 绘制
                }

                SerializedProperty end = element.GetEndProperty();
                SerializedProperty child = element.Copy();
                bool enterChildren = true;
                while (child.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (SerializedProperty.EqualContents(child, end))
                        break;
                    if (child.name == "m_Script")
                        continue;

                    string label = ResolveFieldLabelText(elementType, child.name);
                    var content = string.IsNullOrEmpty(label)
                        ? new GUIContent(child.displayName, child.tooltip)
                        : new GUIContent(label, child.tooltip);
                    EditorGUILayout.PropertyField(child, content, true);
                }
            }
            finally
            {
                EditorGUI.indentLevel--;
            }
        }

        /// <summary>从字段上的 LabelText.Text 读显示名（不解析 @ 表达式）。</summary>
        private static string ResolveFieldLabelText(System.Type elementType, string fieldName)
        {
            if (elementType == null || string.IsNullOrEmpty(fieldName))
                return null;

            try
            {
                var field = elementType.GetField(fieldName,
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (field == null)
                    return null;

                foreach (var attr in field.GetCustomAttributes(true))
                {
                    if (attr is Sirenix.OdinInspector.LabelTextAttribute lt
                        && !string.IsNullOrEmpty(lt.Text)
                        && !lt.Text.StartsWith("@"))
                        return lt.Text;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static void DrawUnityPath(SerializedProperty prop, string titleText)
        {
            DrawTitleBar(titleText);

            EditorGUI.indentLevel++;
            try
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
            finally
            {
                EditorGUI.indentLevel--;
            }
        }

        private void DrawOdinPath(string titleText, bool isNullRef)
        {
            DrawTitleBar(isNullRef ? $"{titleText}  ·  Null" : titleText);

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
