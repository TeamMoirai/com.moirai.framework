using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos
{
    [CustomPropertyDrawer(typeof(HelperDropdownAttribute), true)]
    internal sealed class HelperDropdownAttributeDrawer : PropertyDrawer
    {
        private const float PAD = 3f;
        private const float FOLDOUT_W = 16f;
        private static readonly Dictionary<string, bool> s_Foldouts = new Dictionary<string, bool>();

        private Type[] _types;
        private GUIContent[] _names;
        private bool _built;

        private new HelperDropdownAttribute attribute => (HelperDropdownAttribute)base.attribute;

        #region 构建 [BUILD]

        /// <summary>
        /// 懒加载：首次绘制时通过 TypeCache 收集所有非抽象的派生类型，
        /// 按名称排序后生成下拉选项数组。
        /// </summary>
        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            Type baseType = attribute.BaseType ?? fieldInfo.FieldType;

            _types = TypeCache
                .GetTypesDerivedFrom(baseType)
                .Where(t => !t.IsAbstract && !t.Assembly.GetName().Name.EndsWith(".Tests"))
                .ToArray();

            Array.Sort(_types, (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            _names = new GUIContent[_types.Length + 1];
            _names[0] = new GUIContent("(None)");
            for (int i = 0; i < _types.Length; i++)
                _names[i + 1] = new GUIContent(_types[i].Name);
        }

        #endregion

        #region 高度 [HEIGHT]

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            EnsureBuilt();

            // string 模式：单行 popup，无 foldout
            if (property.propertyType == SerializedPropertyType.String)
                return EditorGUIUtility.singleLineHeight;

            float h = EditorGUIUtility.singleLineHeight;

            if (property.managedReferenceValue != null && HasVisibleChildren(property))
            {
                string foldKey = property.propertyPath;
                if (!s_Foldouts.ContainsKey(foldKey))
                    s_Foldouts[foldKey] = true;

                if (s_Foldouts[foldKey])
                {
                    float spacing = EditorGUIUtility.standardVerticalSpacing;
                    h += spacing;

                    var child = property.Copy();
                    var end = child.GetEndProperty();
                    if (child.NextVisible(true))
                    {
                        h += PAD;
                        bool first = true;
                        while (!SerializedProperty.EqualContents(child, end))
                        {
                            if (!first) h += spacing;
                            h += EditorGUI.GetPropertyHeight(child, true);
                            first = false;
                            if (!child.NextVisible(false)) break;
                        }
                        h += PAD;
                    }
                }
            }

            return h;
        }

        #endregion

        #region 绘制 [GUI]

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EnsureBuilt();

            // ── string 模式：类型名 popup ──
            if (property.propertyType == SerializedPropertyType.String)
            {
                DrawStringMode(position, property, label);
                return;
            }

            // ── managed reference 模式：popup + foldout ──
            DrawReferenceMode(position, property, label);
        }

        #endregion

        #region 下拉弹窗 [DROPDOWN POPUP]

        /// <summary>
        /// 自定义下拉弹窗内容：选项列表 + 下方显示悬停项的类型详情。
        /// </summary>
        private sealed class TypeDropdownPopup : PopupWindowContent
        {
            private readonly GUIContent[] _names;
            private readonly Type[] _types;
            private readonly int _currentIndex;
            private readonly Action<int> _onSelected;
            private int _hoverIndex;
            private Vector2 _scroll;

            private const float ITEM_H = 20f;
            private const float LINE_H = 18f;
            private const float INFO_PAD = 8f;
            private const float INFO_LABEL_W = 70f;
            private const float MIN_WIDTH = 300f;
            private const float MAX_HEIGHT = 400f;

            internal TypeDropdownPopup(GUIContent[] names, Type[] types, int currentIndex, Action<int> onSelected)
            {
                _names = names;
                _types = types;
                _currentIndex = currentIndex;
                _onSelected = onSelected;
                _hoverIndex = currentIndex;
            }

            public override Vector2 GetWindowSize()
            {
                float listH = _names.Length * ITEM_H;
                float infoH = GetInfoHeight();
                float totalH = Mathf.Min(listH + infoH, MAX_HEIGHT);
                return new Vector2(MIN_WIDTH, totalH);
            }

            public override void OnGUI(Rect rect)
            {
                // ── 选项列表 ──
                float infoH = GetInfoHeight();
                float listH = rect.height - infoH;

                float scrollBarW = 16f;
                float contentW = rect.width;
                bool needsScroll = _names.Length * ITEM_H > listH;
                float viewW = needsScroll ? rect.width - scrollBarW : rect.width;

                _scroll = GUI.BeginScrollView(
                    new Rect(rect.x, rect.y, rect.width, listH),
                    _scroll,
                    new Rect(0, 0, viewW, _names.Length * ITEM_H));

                for (int i = 0; i < _names.Length; i++)
                {
                    var itemRect = new Rect(0, i * ITEM_H, viewW, ITEM_H);
                    bool isHover = i == _hoverIndex;
                    bool isSelected = i == _currentIndex;

                    if (isHover)
                        EditorGUI.DrawRect(itemRect, new Color(0.24f, 0.38f, 0.58f, 0.3f));

                    var contentRect = new Rect(itemRect.x + 4, itemRect.y, itemRect.width - 8, itemRect.height);
                    EditorGUI.LabelField(contentRect, _names[i], isSelected
                        ? EditorStyles.boldLabel
                        : EditorStyles.label);

                    // 悬停检测
                    var mouseRect = new Rect(itemRect.x + _scroll.x, itemRect.y + _scroll.y, itemRect.width, itemRect.height);
                    if (Event.current.type == EventType.MouseMove && mouseRect.Contains(Event.current.mousePosition))
                    {
                        _hoverIndex = i;
                        Event.current.Use();
                    }

                    // 点击选中
                    if (Event.current.type == EventType.MouseDown
                        && Event.current.button == 0
                        && mouseRect.Contains(Event.current.mousePosition))
                    {
                        _onSelected(i);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                }

                GUI.EndScrollView();

                // ── 分隔线 ──
                float dividerY = rect.y + listH;
                EditorGUI.DrawRect(new Rect(rect.x, dividerY, rect.width, 1), new Color(0.15f, 0.15f, 0.15f));

                // ── 类型详情 ──
                DrawInfoPanel(new Rect(rect.x, dividerY + 1, rect.width, infoH - 1));
            }

            private void DrawInfoPanel(Rect infoRect)
            {
                if (_hoverIndex < 1 || _hoverIndex > _types.Length)
                {
                    EditorGUI.LabelField(new Rect(infoRect.x + INFO_PAD, infoRect.y + 4, infoRect.width - INFO_PAD * 2, LINE_H),
                        "(None)", EditorStyles.miniLabel);
                    return;
                }

                Type type = _types[_hoverIndex - 1];
                float y = infoRect.y + INFO_PAD;

                DrawInfoLine(infoRect, ref y, "Type", type.FullName);
                DrawInfoLine(infoRect, ref y, "Base", type.BaseType?.FullName ?? "(none)");
                DrawInfoLine(infoRect, ref y, "Assembly", type.Assembly.GetName().Name);

                // 点击选中
                if (Event.current.type == EventType.MouseDown && infoRect.Contains(Event.current.mousePosition))
                {
                    _onSelected(_hoverIndex);
                    editorWindow.Close();
                    GUIUtility.ExitGUI();
                }
            }

            private void DrawInfoLine(Rect infoRect, ref float y, string label, string value)
            {
                var labelRect = new Rect(infoRect.x + INFO_PAD, y, INFO_LABEL_W, LINE_H);
                var valueRect = new Rect(infoRect.x + INFO_PAD + INFO_LABEL_W, y, infoRect.width - INFO_PAD * 2 - INFO_LABEL_W, LINE_H);

                var prevColor = GUI.color;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                EditorGUI.LabelField(labelRect, label, EditorStyles.miniLabel);
                GUI.color = prevColor;

                EditorGUI.LabelField(valueRect, value, EditorStyles.miniLabel);
                y += LINE_H;
            }

            private float GetInfoHeight()
            {
                return INFO_PAD * 2 + LINE_H * 3 + 4;
            }
        }

        /// <summary>
        /// 绘制带类型详情的自定义下拉。点击弹出 PopupWindow，悬停项下方显示详情。
        /// </summary>
        private int DrawTypeDropdown(Rect popupRect, int currentIndex, string propertyPath)
        {
            GUIContent currentContent = currentIndex >= 0 && currentIndex < _names.Length
                ? _names[currentIndex]
                : GUIContent.none;

            bool clicked = EditorGUI.DropdownButton(popupRect, currentContent, FocusType.Keyboard, EditorStyles.popup);

            if (clicked)
            {
                int selected = currentIndex;
                PopupWindow.Show(popupRect, new TypeDropdownPopup(
                    _names, _types, currentIndex, idx => selected = idx));

                // PopupWindow.Show 后需 ExitGUI 避免后续 GUI 操作冲突
                // 但我们不能在这里 ExitGUI，因为需要返回值
                // selected 会在 popup 关闭后被读取
            }

            // 检查是否有新选择
            // PopupWindow 回调是同步的——selected 在 lambda 中赋值
            // 但 PopupWindow.Show 不阻塞，所以我们需要另一种方式
            return currentIndex;
        }

        #endregion

        #region string 模式 [STRING MODE]

        private void DrawStringMode(Rect position, SerializedProperty property, GUIContent label)
        {
            float lineH = EditorGUIUtility.singleLineHeight;
            var headerRect = new Rect(position.x, position.y, position.width, lineH);
            var fieldRect = EditorGUI.PrefixLabel(headerRect, GetDisplayName(label));

            int currentIndex = FindCurrentIndexString(property);

            // 绘制 popup 按钮
            GUIContent currentContent = currentIndex >= 0 && currentIndex < _names.Length
                ? _names[currentIndex] : GUIContent.none;

            if (EditorGUI.DropdownButton(fieldRect, currentContent, FocusType.Keyboard, EditorStyles.popup))
            {
                ShowDropdown(fieldRect, currentIndex, newIndex =>
                {
                    property.stringValue = newIndex >= 1 && newIndex <= _types.Length
                        ? _types[newIndex - 1].FullName : null;
                    property.serializedObject.ApplyModifiedProperties();
                });
            }
        }

        private int FindCurrentIndexString(SerializedProperty property)
        {
            string current = property.stringValue;
            if (string.IsNullOrEmpty(current)) return 0;

            for (int i = 0; i < _types.Length; i++)
            {
                if (_types[i].FullName == current || _types[i].Name == current)
                    return i + 1;
            }
            return 0;
        }

        #endregion

        #region 引用模式 [REFERENCE MODE]

        private void DrawReferenceMode(Rect position, SerializedProperty property, GUIContent label)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float lineH = EditorGUIUtility.singleLineHeight;

            int currentIndex = FindCurrentIndexReference(property);
            bool hasChildren = property.managedReferenceValue != null && HasVisibleChildren(property);

            // 公共：绘制 dropdown header
            var headerRect = new Rect(position.x, position.y, position.width, lineH);
            var fieldRect = EditorGUI.PrefixLabel(headerRect, label);

            // 有子属性时需要为 foldout 预留空间
            Rect popupRect, foldoutRect = default;
            if (hasChildren)
            {
                popupRect = new Rect(fieldRect.x, fieldRect.y, fieldRect.width - FOLDOUT_W, lineH);
                foldoutRect = new Rect(fieldRect.xMax - FOLDOUT_W, fieldRect.y, FOLDOUT_W, lineH);
            }
            else
            {
                popupRect = fieldRect;
            }

            // 绘制 dropdown 按钮
            GUIContent currentContent = currentIndex >= 0 && currentIndex < _names.Length
                ? _names[currentIndex] : GUIContent.none;

            if (EditorGUI.DropdownButton(popupRect, currentContent, FocusType.Keyboard, EditorStyles.popup))
            {
                ShowDropdown(popupRect, currentIndex, newIndex =>
                {
                    if (newIndex == 0)
                        property.managedReferenceValue = null;
                    else
                        property.managedReferenceValue = Activator.CreateInstance(_types[newIndex - 1]);
                    property.serializedObject.ApplyModifiedProperties();
                    GUI.changed = true;
                });
            }

            // foldout
            if (hasChildren)
            {
                string foldKey = property.propertyPath;
                if (!s_Foldouts.ContainsKey(foldKey))
                    s_Foldouts[foldKey] = true;
                s_Foldouts[foldKey] = EditorGUI.Foldout(foldoutRect, s_Foldouts[foldKey], GUIContent.none, true);

                if (!s_Foldouts[foldKey]) return;

                // ── 子属性绘制 ──
                float childStartY = position.y + lineH + spacing;
                float boxH = position.yMax - childStartY;
                GUI.Box(new Rect(position.x, childStartY, position.width, boxH), GUIContent.none);

                float y = childStartY + PAD;
                var child = property.Copy();
                var end = child.GetEndProperty();

                if (child.NextVisible(true))
                {
                    int indent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel++;
                    bool first = true;

                    while (!SerializedProperty.EqualContents(child, end))
                    {
                        if (!first) y += spacing;
                        float ch = EditorGUI.GetPropertyHeight(child, true);
                        EditorGUI.PropertyField(
                            new Rect(position.x + PAD, y, position.width - PAD * 2, ch),
                            child, true);
                        y += ch;
                        first = false;
                        if (!child.NextVisible(false)) break;
                    }

                    EditorGUI.indentLevel = indent;
                }
            }
        }

        private int FindCurrentIndexReference(SerializedProperty property)
        {
            if (property.managedReferenceValue == null) return 0;

            var currentType = property.managedReferenceValue.GetType();
            for (int i = 0; i < _types.Length; i++)
            {
                if (_types[i] == currentType) return i + 1;
            }
            return 0;
        }

        #endregion

        #region 弹窗工具 [POPUP UTILITY]

        /// <summary>
        /// 显示带类型详情的自定义下拉弹窗。
        /// </summary>
        private void ShowDropdown(Rect activatorRect, int currentIndex, Action<int> onSelected)
        {
            PopupWindow.Show(activatorRect, new TypeDropdownPopup(
                _names, _types, currentIndex, onSelected));
        }

        #endregion

        #region 工具 [UTILITIES]

        /// <summary>
        /// 获取人性化的显示名称。
        /// 优先使用特性中指定的 Label；否则使用 Unity 默认 label（引用模式）
        /// 或从字段名自动推导并追加 " Helper"（string 模式）。
        /// </summary>
        private GUIContent GetDisplayName(GUIContent defaultLabel)
        {
            if (!string.IsNullOrEmpty(attribute.Label))
                return new GUIContent(attribute.Label);

            if (defaultLabel != null && !string.IsNullOrEmpty(defaultLabel.text))
                return defaultLabel;

            string name = fieldInfo.Name;
            name = Regex.Replace(name, @"^m_", string.Empty);
            name = Regex.Replace(name, @"HelperTypeName$", string.Empty);
            name = Regex.Replace(name, @"((?<=[a-z])[A-Z]|[A-Z](?=[a-z]))", " $1").Trim();
            return new GUIContent(name + " Helper");
        }

        private static bool HasVisibleChildren(SerializedProperty property)
        {
            var child = property.Copy();
            var end = child.GetEndProperty();
            return child.NextVisible(true) && !SerializedProperty.EqualContents(child, end);
        }

        #endregion
    }

    /// <summary>
    /// Odin 原生 Drawer，为 <see cref="HelperDropdownAttribute"/> 自动接管 Odin 绘制，
    /// 委托到 Unity <see cref="EditorGUI.PropertyField"/>（触发 <see cref="HelperDropdownAttributeDrawer"/>）。
    /// <para>无需在每个字段上手动添加 <c>[DrawWithUnity]</c>。</para>
    /// </summary>
    /// <remarks>
    /// 优先级设为 wrapper=10001，高于 Odin 默认 managed reference drawer 和 DrawWithUnity(10000)。
    /// 当 PropertyTree 背后有 SerializedObject 时（ScriptableObject 场景），获取 SerializedProperty 并委托绘制；
    /// 否则回退到 Odin 默认行为。
    /// </remarks>
    [DrawerPriority(0, 10001, 0)]
    internal sealed class HelperDropdownOdinDrawer : OdinAttributeDrawer<HelperDropdownAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            var prop = Property.Tree.GetUnityPropertyForPath(Property.UnityPropertyPath);
            if (prop == null)
            {
                CallNextDrawer(label);
                return;
            }

            float h = EditorGUI.GetPropertyHeight(prop, label, true);
            Rect rect = EditorGUILayout.GetControlRect(true, h, GUILayout.ExpandWidth(true));
            EditorGUI.PropertyField(rect, prop, label, true);
        }
    }
}
