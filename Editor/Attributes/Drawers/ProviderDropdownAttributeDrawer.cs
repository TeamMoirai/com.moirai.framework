using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using PopupWindow = UnityEditor.PopupWindow;

namespace Moirai.Atropos
{
    [CustomPropertyDrawer(typeof(ProviderDropdownAttribute), true)]
    internal sealed class ProviderDropdownAttributeDrawer : PropertyDrawer
    {
        private const float PAD = 3f;
        private const float FOLDOUT_W = 16f;

        /// <summary>foldout 展开状态（IMGUI 与 UITK 共享），键为 propertyPath。</summary>
        private static readonly Dictionary<string, bool> s_Foldouts = new Dictionary<string, bool>();

        private TypeMenuCache _cache;
        private bool _isStringMode;
        private GUIContent _labelGUI;
        private string _labelText;

        private new ProviderDropdownAttribute attribute => (ProviderDropdownAttribute)base.attribute;

        #region 类型菜单缓存 [TYPE MENU CACHE]

        /// <summary>
        /// 类型菜单缓存：按基类全局共享一份（TypeCache 查询、排序、选项数组、索引字典），
        /// 避免同一基类的每个字段 Drawer 实例重复构建。
        /// </summary>
        private sealed class TypeMenuCache
        {
            private static readonly Dictionary<Type, TypeMenuCache> s_Caches = new Dictionary<Type, TypeMenuCache>();

            static TypeMenuCache()
            {
                // 域重载被关闭时静态字段不会自动清理，脚本变更前手动失效
                AssemblyReloadEvents.beforeAssemblyReload += () => s_Caches.Clear();
            }

            internal static TypeMenuCache Get(Type baseType)
            {
                if (!s_Caches.TryGetValue(baseType, out var cache))
                    s_Caches[baseType] = cache = new TypeMenuCache(baseType);
                return cache;
            }

            /// <summary>候选实现类型（按名称排序），不含 (None) 项。</summary>
            internal readonly Type[] Types;

            /// <summary>IMGUI 选项内容（含 "(None)" 前缀项）。</summary>
            internal readonly GUIContent[] Names;

            /// <summary>UITK 选项文本（含 "(None)" 前缀项）。PopupField 要求 List&lt;T&gt;，全局共享一份避免分配。</summary>
            internal readonly List<string> DisplayNames;

            private readonly Dictionary<string, int> _nameToIndex;
            private readonly Dictionary<Type, int> _typeToIndex;

            private TypeMenuCache(Type baseType)
            {
                Types = TypeCache.GetTypesDerivedFrom(baseType)
                    .Where(t => !t.IsAbstract
                        && t.GetConstructor(Type.EmptyTypes) != null // 需无参构造（供 Activator.CreateInstance 实例化）
                        && !t.Assembly.GetName().Name.EndsWith(".Tests"))
                    .OrderBy(t => t.Name, StringComparer.Ordinal)
                    .ToArray();

                int n = Types.Length;
                Names = new GUIContent[n + 1];
                Names[0] = new GUIContent("(None)");
                DisplayNames = new List<string>(n + 1) { "(None)" };

                _nameToIndex = new Dictionary<string, int>(n * 2);
                _typeToIndex = new Dictionary<Type, int>(n);

                for (int i = 0; i < n; i++)
                {
                    Type t = Types[i];
                    int choice = i + 1;
                    Names[choice] = new GUIContent(t.Name);
                    DisplayNames.Add(t.Name);
                    _typeToIndex[t] = choice;
                    _nameToIndex[t.FullName] = choice; // 全名：唯一键，不同命名空间的同名类型不冲突
                    if (!_nameToIndex.ContainsKey(t.Name))
                        _nameToIndex[t.Name] = choice; // 简单名：仅无冲突时登记，兼容手输的简单类型名
                }
            }

            /// <summary>按类型全名或简单名查选项索引（0 = None），O(1)。</summary>
            internal int IndexOfName(string typeName) =>
                !string.IsNullOrEmpty(typeName) && _nameToIndex.TryGetValue(typeName, out int index) ? index : 0;

            /// <summary>按 Type 查选项索引（0 = None），O(1)。</summary>
            internal int IndexOfType(Type type) =>
                type != null && _typeToIndex.TryGetValue(type, out int index) ? index : 0;
        }

        private TypeMenuCache Cache => _cache ??= TypeMenuCache.Get(attribute.BaseType ?? fieldInfo.FieldType);

        #endregion

        #region 标签 [LABEL]

        /// <summary>字段名推导用的正则（编译并复用，避免每次绘制重建）。</summary>
        private static class LabelPatterns
        {
            internal static readonly Regex MPrefix = new Regex(@"^m_", RegexOptions.Compiled);
            internal static readonly Regex HelperSuffix = new Regex(@"HelperTypeName$", RegexOptions.Compiled);
            internal static readonly Regex CamelSplit = new Regex(@"((?<=[a-z])[A-Z]|[A-Z](?=[a-z]))", RegexOptions.Compiled);

            /// <summary>从字段名推导 string 模式标签：去 m_ 前缀与 HelperTypeName 后缀、驼峰分词、追加 " Helper"。</summary>
            internal static string DeriveHelperLabel(string fieldName)
            {
                string name = HelperSuffix.Replace(MPrefix.Replace(fieldName, string.Empty), string.Empty);
                return CamelSplit.Replace(name, " $1").Trim() + " Helper";
            }
        }

        /// <summary>IMGUI 标签（懒加载，单次分配）。</summary>
        private GUIContent LabelGUI => _labelGUI ??= new GUIContent(LabelText);

        /// <summary>标签文本（懒加载）。优先特性 Label；string 模式从字段名推导，引用模式 Nicify 变量名。</summary>
        private string LabelText => _labelText ??=
            !string.IsNullOrEmpty(attribute.Label) ? attribute.Label
            : _isStringMode ? LabelPatterns.DeriveHelperLabel(fieldInfo.Name)
            : ObjectNames.NicifyVariableName(fieldInfo.Name);

        #endregion

        #region 通用 [SHARED]

        /// <summary>写入选项（IMGUI / UITK 共用）：string 模式存类型全名，引用模式存实例，0 = None。</summary>
        private void ApplySelection(SerializedProperty property, int index)
        {
            if (_isStringMode)
            {
                property.stringValue = index >= 1 && index <= Cache.Types.Length
                    ? Cache.Types[index - 1].FullName
                    : string.Empty;
            }
            else
            {
                property.managedReferenceValue = index == 0
                    ? null
                    : Activator.CreateInstance(Cache.Types[index - 1]);
            }
        }

        /// <summary>
        /// 写入选项并注册撤销（IMGUI / UITK 共用）：
        /// Update → Undo.RecordObject → 写值 → ApplyModifiedProperties，保证 Ctrl+Z 可回退。
        /// </summary>
        private void ApplySelectionWithUndo(SerializedProperty property, int index)
        {
            property.serializedObject.Update();
            Undo.RecordObject(property.serializedObject.targetObject, "Change Provider");
            ApplySelection(property, index);
            property.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>读取当前选项索引（两种模式共用，字典 O(1) 查询）。</summary>
        private int FindCurrentIndex(SerializedProperty property) => _isStringMode
            ? Cache.IndexOfName(property.stringValue)
            : property.managedReferenceValue == null
                ? 0
                : Cache.IndexOfType(property.managedReferenceValue.GetType());

        /// <summary>foldout 键：对象实例 ID + 属性路径，避免不同对象的相同属性路径互相干扰。</summary>
        private static string FoldoutKey(SerializedProperty property) =>
            property.serializedObject.targetObject.GetInstanceID() + property.propertyPath;

        private static bool GetFoldout(string key) =>
            s_Foldouts.TryGetValue(key, out bool value) ? value : true;

        private static void SetFoldout(string key, bool value) => s_Foldouts[key] = value;

        /// <summary>遍历直接可见子属性（高度计算 / IMGUI 绘制 / UITK 构建共用）。visitor 需跨迭代持有时应自行 Copy。</summary>
        private static void ForEachVisibleChild(SerializedProperty property, Action<SerializedProperty> visit)
        {
            var child = property.Copy();
            var end = child.GetEndProperty();
            if (!child.NextVisible(true)) return;

            while (!SerializedProperty.EqualContents(child, end))
            {
                visit(child);
                if (!child.NextVisible(false)) break;
            }
        }

        private static bool HasVisibleChildren(SerializedProperty property)
        {
            var child = property.Copy();
            var end = child.GetEndProperty();
            return child.NextVisible(true) && !SerializedProperty.EqualContents(child, end);
        }

        #endregion

        #region IMGUI

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // string 模式：单行 popup，无 foldout
            if (property.propertyType == SerializedPropertyType.String)
                return EditorGUIUtility.singleLineHeight;

            if (property.managedReferenceValue == null || !HasVisibleChildren(property))
                return EditorGUIUtility.singleLineHeight;
            if (!GetFoldout(FoldoutKey(property)))
                return EditorGUIUtility.singleLineHeight;

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float h = EditorGUIUtility.singleLineHeight + spacing + PAD * 2;

            bool first = true;
            ForEachVisibleChild(property, child =>
            {
                if (!first) h += spacing;
                h += EditorGUI.GetPropertyHeight(child, true);
                first = false;
            });
            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            _isStringMode = property.propertyType == SerializedPropertyType.String;

            if (_isStringMode)
                DrawStringMode(position, property);
            else
                DrawReferenceMode(position, property);
        }

        private void DrawStringMode(Rect position, SerializedProperty property)
        {
            var fieldRect = EditorGUI.PrefixLabel(
                new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight), LabelGUI);

            int index = FindCurrentIndex(property);
            GUIContent current = index < Cache.Names.Length ? Cache.Names[index] : GUIContent.none;

            if (EditorGUI.DropdownButton(fieldRect, current, FocusType.Keyboard, EditorStyles.popup))
            {
                ShowDropdown(fieldRect, index, newIndex => ApplySelectionWithUndo(property, newIndex));
            }
        }

        private void DrawReferenceMode(Rect position, SerializedProperty property)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float lineH = EditorGUIUtility.singleLineHeight;

            int index = FindCurrentIndex(property);
            bool hasChildren = property.managedReferenceValue != null && HasVisibleChildren(property);

            var fieldRect = EditorGUI.PrefixLabel(new Rect(position.x, position.y, position.width, lineH), LabelGUI);

            // 有子属性时需要为 foldout 预留空间
            Rect popupRect = hasChildren
                ? new Rect(fieldRect.x, fieldRect.y, fieldRect.width - FOLDOUT_W, lineH)
                : fieldRect;

            GUIContent current = index < Cache.Names.Length ? Cache.Names[index] : GUIContent.none;
            if (EditorGUI.DropdownButton(popupRect, current, FocusType.Keyboard, EditorStyles.popup))
            {
                ShowDropdown(popupRect, index, newIndex =>
                {
                    ApplySelectionWithUndo(property, newIndex);
                    GUI.changed = true;
                });
            }

            if (!hasChildren) return;

            // ── foldout ──
            string foldKey = FoldoutKey(property);
            bool open = EditorGUI.Foldout(
                new Rect(fieldRect.xMax - FOLDOUT_W, fieldRect.y, FOLDOUT_W, lineH),
                GetFoldout(foldKey), GUIContent.none, true);
            SetFoldout(foldKey, open);
            if (!open) return;

            // ── 子属性绘制 ──
            float childStartY = position.y + lineH + spacing;
            GUI.Box(new Rect(position.x, childStartY, position.width, position.yMax - childStartY), GUIContent.none);

            float y = childStartY + PAD;
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;

            bool first = true;
            ForEachVisibleChild(property, child =>
            {
                if (!first) y += spacing;
                float childH = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(
                    new Rect(position.x + PAD, y, position.width - PAD * 2, childH), child, true);
                y += childH;
                first = false;
            });

            EditorGUI.indentLevel = indent;
        }

        #endregion

        #region UITK 支持 [UITK SUPPORT]

        /// <summary>
        /// UI Toolkit 入口：返回原生 <see cref="PopupField{T}"/>，
        /// 使标签与 UITK 窗口中的其他字段对齐（IMGUI 回退绘制会导致样式错位）。
        /// </summary>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            _isStringMode = property.propertyType == SerializedPropertyType.String;

            string propPath = property.propertyPath;
            SerializedObject so = property.serializedObject;

            // 按钮拉满剩余宽度，右缘与 IMGUI popup 对齐
            var popup = new PopupField<string>(LabelText, Cache.DisplayNames, FindCurrentIndex(property));
            popup.style.flexGrow = 1f;

            // string 模式：单行 popup，无 foldout
            if (_isStringMode)
            {
                popup.RegisterValueChangedCallback(_ => WriteSelectionUITK(so, propPath, popup));
                return popup;
            }

            // 引用模式（仿 IMGUI 布局）：popup 与 foldout 箭头同一行，箭头在最右
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            string foldKey = FoldoutKey(property);
            var arrow = new Foldout { text = string.Empty, value = GetFoldout(foldKey) };
            arrow.style.flexShrink = 0f;
            arrow.style.marginTop = 0f;
            arrow.style.marginBottom = 0f;

            // 子属性容器（独立于 arrow，避免进入行布局），unity-box 为编辑器内置盒样式（对应 IMGUI 的 GUI.Box）
            var children = new VisualElement();
            children.AddToClassList("unity-box");
            children.style.paddingTop = PAD;
            children.style.paddingBottom = PAD;
            children.style.paddingLeft = PAD;
            children.style.paddingRight = PAD;
            children.style.marginTop = 2f;

            arrow.RegisterValueChangedCallback(evt =>
            {
                SetFoldout(foldKey, evt.newValue);
                children.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
            });

            popup.RegisterValueChangedCallback(_ =>
            {
                WriteSelectionUITK(so, propPath, popup);
                RefreshChildrenUITK(so, propPath, popup, arrow, children);
            });

            row.Add(popup);
            row.Add(arrow);

            var root = new VisualElement();
            root.Add(row);
            root.Add(children);

            RefreshChildrenUITK(so, propPath, popup, arrow, children);
            return root;
        }

        /// <summary>UITK 选中写入：重新 FindProperty 后写值（含撤销注册）并应用。</summary>
        private void WriteSelectionUITK(SerializedObject so, string propPath, PopupField<string> popup)
        {
            so.Update();
            SerializedProperty fresh = so.FindProperty(propPath);
            if (fresh == null) return;

            Undo.RecordObject(so.targetObject, "Change Provider");
            ApplySelection(fresh, popup.index);
            so.ApplyModifiedProperties();
        }

        /// <summary>
        /// 类型切换后刷新下拉显示与子属性区（重新 FindProperty，避免使用失效的 SerializedProperty）。
        /// </summary>
        private void RefreshChildrenUITK(SerializedObject so, string propPath,
            PopupField<string> popup, Foldout arrow, VisualElement children)
        {
            so.Update();
            SerializedProperty fresh = so.FindProperty(propPath);
            if (fresh == null) return;

            bool hasInstance = fresh.managedReferenceValue != null;
            popup.SetValueWithoutNotify(Cache.DisplayNames[FindCurrentIndex(fresh)]);

            // 无实例或无可见子属性时不显示箭头与子属性区（与 IMGUI 一致）
            bool showChildren = hasInstance && HasVisibleChildren(fresh);
            arrow.style.display = showChildren ? DisplayStyle.Flex : DisplayStyle.None;
            children.style.display = showChildren && arrow.value ? DisplayStyle.Flex : DisplayStyle.None;

            children.Clear();
            if (!showChildren) return;

            ForEachVisibleChild(fresh, child =>
            {
                var childProp = child.Copy(); // PropertyField 需跨迭代持有，须复制
                var field = new PropertyField(childProp);
                field.BindProperty(childProp);
                children.Add(field);
            });
        }

        #endregion

        #region 下拉弹窗 [DROPDOWN POPUP]

        /// <summary>
        /// 显示带类型详情的自定义下拉弹窗（IMGUI 专用；UITK 使用原生 PopupField）。
        /// </summary>
        private void ShowDropdown(Rect activatorRect, int currentIndex, Action<int> onSelected)
        {
            PopupWindow.Show(activatorRect, new TypeDropdownPopup(Cache, currentIndex, onSelected));
        }

        /// <summary>
        /// 自定义下拉弹窗内容：选项列表 + 下方显示悬停项的类型详情。
        /// </summary>
        private sealed class TypeDropdownPopup : PopupWindowContent
        {
            private const float ITEM_H = 20f;
            private const float LINE_H = 18f;
            private const float INFO_PAD = 8f;
            private const float INFO_LABEL_W = 70f;
            private const float MIN_WIDTH = 300f;
            private const float MAX_HEIGHT = 400f;
            private const float SCROLLBAR_W = 16f;

            private readonly TypeMenuCache _cache;
            private readonly int _currentIndex;
            private readonly Action<int> _onSelected;
            private int _hoverIndex;
            private Vector2 _scroll;

            internal TypeDropdownPopup(TypeMenuCache cache, int currentIndex, Action<int> onSelected)
            {
                _cache = cache;
                _currentIndex = currentIndex;
                _onSelected = onSelected;
                _hoverIndex = currentIndex;
            }

            public override void OnOpen()
            {
                // 不开启 wantsMouseMove 收不到 MouseMove 事件，悬停高亮与详情面板将失效
                editorWindow.wantsMouseMove = true;
            }

            public override Vector2 GetWindowSize()
            {
                float listH = _cache.Names.Length * ITEM_H;
                return new Vector2(MIN_WIDTH, Mathf.Min(listH + GetInfoHeight(), MAX_HEIGHT));
            }

            public override void OnGUI(Rect rect)
            {
                GUIContent[] names = _cache.Names;

                // ── 选项列表 ──
                float infoH = GetInfoHeight();
                float listH = rect.height - infoH;

                bool needsScroll = names.Length * ITEM_H > listH;
                float viewW = needsScroll ? rect.width - SCROLLBAR_W : rect.width;

                _scroll = GUI.BeginScrollView(
                    new Rect(rect.x, rect.y, rect.width, listH),
                    _scroll,
                    new Rect(0, 0, viewW, names.Length * ITEM_H));

                // 视口裁剪：仅绘制可见项，长列表避免整表重绘
                int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / ITEM_H));
                int last = Mathf.Min(names.Length, Mathf.CeilToInt((_scroll.y + listH) / ITEM_H) + 1);

                for (int i = first; i < last; i++)
                {
                    var itemRect = new Rect(0, i * ITEM_H, viewW, ITEM_H);

                    if (i == _hoverIndex)
                        EditorGUI.DrawRect(itemRect, new Color(0.24f, 0.38f, 0.58f, 0.3f));

                    var contentRect = new Rect(itemRect.x + 4, itemRect.y, itemRect.width - 8, itemRect.height);
                    EditorGUI.LabelField(contentRect, names[i],
                        i == _currentIndex ? EditorStyles.boldLabel : EditorStyles.label);

                    // 内容坐标 = 视图坐标 + 滚动偏移
                    var mouseRect = new Rect(itemRect.x + _scroll.x, itemRect.y + _scroll.y, itemRect.width, itemRect.height);

                    if (Event.current.type == EventType.MouseMove
                        && mouseRect.Contains(Event.current.mousePosition))
                    {
                        if (_hoverIndex != i)
                        {
                            _hoverIndex = i;
                            editorWindow.Repaint();
                        }
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
                if (_hoverIndex < 1 || _hoverIndex > _cache.Types.Length)
                {
                    EditorGUI.LabelField(
                        new Rect(infoRect.x + INFO_PAD, infoRect.y + 4, infoRect.width - INFO_PAD * 2, LINE_H),
                        "(None)", EditorStyles.miniLabel);
                    return;
                }

                Type type = _cache.Types[_hoverIndex - 1];
                float y = infoRect.y + INFO_PAD;

                DrawInfoLine(infoRect, ref y, "Type", type.FullName);
                DrawInfoLine(infoRect, ref y, "Base", type.BaseType?.FullName ?? "(none)");
                DrawInfoLine(infoRect, ref y, "Assembly", type.Assembly.GetName().Name);

                // 点击详情区也可选中当前悬停项
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
                var valueRect = new Rect(infoRect.x + INFO_PAD + INFO_LABEL_W, y,
                    infoRect.width - INFO_PAD * 2 - INFO_LABEL_W, LINE_H);

                var prevColor = GUI.color;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                EditorGUI.LabelField(labelRect, label, EditorStyles.miniLabel);
                GUI.color = prevColor;

                EditorGUI.LabelField(valueRect, value, EditorStyles.miniLabel);
                y += LINE_H;
            }

            private float GetInfoHeight() => INFO_PAD * 2 + LINE_H * 3 + 4;
        }

        #endregion
    }

    /// <summary>
    /// Odin 原生 Drawer，为 <see cref="ProviderDropdownAttribute"/> 自动接管 Odin 绘制，
    /// 委托到 Unity <see cref="EditorGUI.PropertyField(Rect, SerializedProperty, GUIContent, bool)"/>（触发 <see cref="ProviderDropdownAttributeDrawer"/>）。
    /// <para>无需在每个字段上手动添加 <c>[DrawWithUnity]</c>。</para>
    /// </summary>
    /// <remarks>
    /// 优先级设为 wrapper=10001，高于 Odin 默认 managed reference drawer 和 DrawWithUnity(10000)。
    /// 当 PropertyTree 背后有 SerializedObject 时（ScriptableObject 场景），获取 SerializedProperty 并委托绘制；
    /// 否则回退到 Odin 默认行为。
    /// </remarks>
    [DrawerPriority(0, 10001, 0)]
    internal sealed class ProviderDropdownOdinDrawer : OdinAttributeDrawer<ProviderDropdownAttribute>
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
