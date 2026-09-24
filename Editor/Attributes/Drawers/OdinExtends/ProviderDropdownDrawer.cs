using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using PopupWindow = UnityEditor.PopupWindow;

namespace Moirai.Atropos
{
    /// <summary>
    /// <see cref="ProviderDropdownAttribute"/> 的 Drawer（Odin 为框架必备组件，本 Drawer 为唯一实现，
    /// 不再提供 Unity 原生 PropertyDrawer 路径）。
    /// 下拉行绘制与子属性展开由本 Drawer 全权负责：<b>子属性优先交由 Odin PropertyTree 绘制</b>——
    /// 实现类字段上的 Odin 特性（[ValueDropdown]、[LabelText]、[InfoBox] 等）由此正常生效。
    /// <para>无需在每个字段上手动添加 <c>[DrawWithUnity]</c>。</para>
    /// </summary>
    /// <remarks>
    /// 优先级设为 super=1，确保在 Odin 4.0.x 下优先于默认 managed reference drawer 与 DrawWithUnity(10000)，始终接管绘制。
    /// 优先获取 Unity SerializedProperty（SerializedObject 场景）走串行化属性路径绘制；
    /// 4.0.x 下 UnityPropertyPath 解析失败或纯 Odin 宿主（无 SerializedObject）时，
    /// 退化为 Odin 值条目驱动路径（<see cref="DrawValueEntryFallback"/>），不再回退到 Odin 默认 managed-reference 绘制，
    /// 从而避免其子内容渲染失效。
    /// Odin 未解析出子属性时（如未启用多态序列化后端），子属性区回退为 Unity 序列化绘制。
    /// </remarks>
    [DrawerPriority(1, 0, 0)]
    internal sealed class ProviderDropdownDrawer : OdinAttributeDrawer<ProviderDropdownAttribute>
    {
        internal const float PAD = 3f;
        private const float FOLDOUT_W = 16f;

        /// <summary>foldout 展开状态，键为对象实例 ID + 属性路径（值条目路径为 Odin 树哈希 + 属性路径）。</summary>
        private static readonly Dictionary<string, bool> s_Foldouts = new Dictionary<string, bool>();

        #region 类型菜单缓存 [TYPE MENU CACHE]

        /// <summary>
        /// 类型菜单缓存：按基类全局共享一份（TypeCache 查询、排序、选项数组、索引字典），
        /// 避免同一基类的每个属性每次绘制重复构建。
        /// </summary>
        internal sealed class TypeMenuCache
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

            /// <summary>缓存对应的基类/接口类型（字段声明类型或特性指定的 BaseType）。</summary>
            internal readonly Type BaseType;

            /// <summary>候选实现类型（按名称排序），不含 (None) 项。</summary>
            internal readonly Type[] Types;

            /// <summary>选项内容（含 "(None)" 前缀项）。</summary>
            internal readonly GUIContent[] Names;

            private readonly Dictionary<string, int> _nameToIndex;
            private readonly Dictionary<Type, int> _typeToIndex;

            private TypeMenuCache(Type baseType)
            {
                BaseType = baseType;
                Types = TypeCache.GetTypesDerivedFrom(baseType)
                    .Where(t => !t.IsAbstract
                        && !t.Assembly.GetName().Name.EndsWith(".Tests")
                        && !t.Assembly.GetName().Name.Contains(".Tests."))
                    .OrderBy(t => t.Name, StringComparer.Ordinal)
                    .ToArray();

                int n = Types.Length;
                Names = new GUIContent[n + 1];
                Names[0] = new GUIContent("(None)");

                _nameToIndex = new Dictionary<string, int>(n * 2);
                _typeToIndex = new Dictionary<Type, int>(n);

                for (int i = 0; i < n; i++)
                {
                    Type t = Types[i];
                    int choice = i + 1;
                    Names[choice] = new GUIContent(t.Name);
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

        #endregion

        #region 通用与共享绘制 [SHARED]

        /// <summary>
        /// 每字段的下拉选项视图：把 <c>(None)</c> 的显示决策与索引换算集中到这里，
        /// 供串行化属性路径与值条目路径共用同一套"本地选项"（其索引从 0 连续递增）。
        /// </summary>
        internal readonly struct ProviderOptions
        {
            /// <summary>底层共享类型菜单缓存（其索引约定：0 = None，1..n = 候选类型）。</summary>
            internal readonly TypeMenuCache Cache;

            /// <summary>本字段是否包含 "(None)" 项（ShowNone 为 true，或候选类型为空时强制为 true）。</summary>
            internal readonly bool IncludeNone;

            /// <summary>显示数组（本地索引）。IncludeNone 时与 <see cref="TypeMenuCache.Names"/> 同序。</summary>
            internal readonly GUIContent[] NameOptions;

            internal ProviderOptions(TypeMenuCache cache, bool showNone)
            {
                Cache = cache;
                IncludeNone = showNone || cache.Types.Length == 0;

                if (IncludeNone)
                {
                    NameOptions = cache.Names;          // [0]=(None)，[i+1]=类型名
                }
                else
                {
                    NameOptions = new GUIContent[cache.Types.Length];
                    for (int i = 0; i < cache.Types.Length; i++)
                    {
                        // 缓存 Names 的 [i+1] 对应第 i 个候选类型
                        NameOptions[i] = cache.Names[i + 1];
                    }
                }
            }

            /// <summary>本地选项数量（恒 ≥ 1：候选为空时仍强制包含 (None)）。</summary>
            internal readonly int Count => NameOptions.Length;

            /// <summary>缓存索引（0=None，1..n=类型）→ 本地索引。</summary>
            internal readonly int CacheToLocal(int cacheIndex)
            {
                if (IncludeNone) return cacheIndex;
                return cacheIndex <= 0 ? 0 : Mathf.Min(cacheIndex - 1, Count - 1);
            }

            /// <summary>本地索引 → 缓存索引（0=None，1..n=类型）。</summary>
            internal readonly int LocalToCache(int localIndex)
            {
                if (IncludeNone) return localIndex;
                return localIndex + 1;
            }
        }

        /// <summary>写入选项：string 模式存类型全名，引用模式存实例，0 = None。</summary>
        private static void ApplySelection(SerializedProperty property, int index, TypeMenuCache cache)
        {
            if (property.propertyType == SerializedPropertyType.String)
            {
                property.stringValue = index >= 1 && index <= cache.Types.Length
                    ? cache.Types[index - 1].FullName
                    : string.Empty;
            }
            else
            {
                property.managedReferenceValue = index == 0
                    ? null
                    : Activator.CreateInstance(cache.Types[index - 1]);
            }
        }

        /// <summary>
        /// 写入选项并注册撤销：
        /// Update → Undo.RecordObject → 写值 → ApplyModifiedProperties，保证 Ctrl+Z 可回退。
        /// </summary>
        private static void ApplySelectionWithUndo(SerializedProperty property, int index, TypeMenuCache cache)
        {
            property.serializedObject.Update();
            Undo.RecordObject(property.serializedObject.targetObject, "Change Provider");
            ApplySelection(property, index, cache);
            property.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>读取当前选项索引（按属性类型自动分派，字典 O(1) 查询）。</summary>
        private static int FindCurrentIndex(TypeMenuCache cache, SerializedProperty property) =>
            property.propertyType == SerializedPropertyType.String
                ? cache.IndexOfName(property.stringValue)
                : property.managedReferenceValue == null
                    ? 0
                    : cache.IndexOfType(property.managedReferenceValue.GetType());

        /// <summary>foldout 键：对象实例 ID + 属性路径，避免不同对象的相同属性路径互相干扰。</summary>
        private static string FoldoutKey(SerializedProperty property) =>
            property.serializedObject.targetObject.GetInstanceID() + property.propertyPath;

        private static bool GetFoldout(string key) =>
            s_Foldouts.TryGetValue(key, out bool value) ? value : true;

        private static void SetFoldout(string key, bool value) => s_Foldouts[key] = value;

        /// <summary>遍历直接可见子属性（高度计算与 Unity 序列化回退绘制共用）。visitor 需跨迭代持有时应自行 Copy。</summary>
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

        /// <summary>子属性区高度：内边距 ×2 + 子属性高度与间距（Unity 序列化回退路径用）。</summary>
        private static float GetChildrenHeight(SerializedProperty property)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float h = PAD * 2;

            bool first = true;
            ForEachVisibleChild(property, child =>
            {
                if (!first) h += spacing;
                h += EditorGUI.GetPropertyHeight(child, true);
                first = false;
            });
            return h;
        }

        /// <summary>
        /// 绘制下拉行：标签 + popup 按钮（引用模式且需展开子属性时右侧并排 foldout 箭头）。<br />
        /// 串行化属性路径与值条目路径共用，保证两种驱动下行内交互完全一致。<br />
        /// 返回 foldout 展开状态（string 模式恒为 true）。<br />
        /// <paramref name="applySelection"/> 收到的是<b>缓存索引</b>（0=None，1..n=类型）。
        /// </summary>
        private static bool DrawRow(Rect position, SerializedProperty property, GUIContent label,
            ProviderOptions options, bool reserveFoldout, Action<SerializedProperty, int> applySelection)
        {
            int cacheIndex = FindCurrentIndex(options.Cache, property);
            return DrawRowCore(position, label, options, reserveFoldout, FoldoutKey(property),
                options.CacheToLocal(cacheIndex), i => applySelection(property, options.LocalToCache(i)));
        }

        /// <summary>
        /// 下拉行核心绘制（不依赖 SerializedProperty）。<br />
        /// 供串行化属性路径与值条目回退路径共用，保证两种驱动下行内交互完全一致。<br />
        /// <paramref name="currentLocalIndex"/> 与 <paramref name="onSelectedLocal"/> 均为<b>本地索引</b>。
        /// </summary>
        private static bool DrawRowCore(Rect position, GUIContent label, ProviderOptions options, bool reserveFoldout,
            string foldKey, int currentLocalIndex, Action<int> onSelectedLocal)
        {
            float lineH = EditorGUIUtility.singleLineHeight;
            Rect fieldRect = EditorGUI.PrefixLabel(new Rect(position.x, position.y, position.width, lineH), label);

            // 需要为 foldout 预留空间
            Rect popupRect = reserveFoldout
                ? new Rect(fieldRect.x, fieldRect.y, fieldRect.width - FOLDOUT_W, lineH)
                : fieldRect;

            GUIContent current = currentLocalIndex < options.NameOptions.Length
                ? options.NameOptions[currentLocalIndex] : GUIContent.none;
            if (EditorGUI.DropdownButton(popupRect, current, FocusType.Keyboard, EditorStyles.popup))
                ShowDropdown(popupRect, options, currentLocalIndex, onSelectedLocal);

            if (!reserveFoldout) return true;

            bool open = EditorGUI.Foldout(
                new Rect(fieldRect.xMax - FOLDOUT_W, fieldRect.y, FOLDOUT_W, lineH),
                GetFoldout(foldKey), GUIContent.none, true);
            SetFoldout(foldKey, open);
            return open;
        }

        /// <summary>
        /// 绘制子属性盒（Unity 序列化回退路径）：unity-box 背景 + PAD 内边距内逐个绘制子属性。
        /// </summary>
        private static void DrawChildren(Rect boxRect, SerializedProperty property)
        {
            GUI.Box(boxRect, GUIContent.none);

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float y = boxRect.y + PAD;
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;

            bool first = true;
            ForEachVisibleChild(property, child =>
            {
                if (!first) y += spacing;
                float childH = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(
                    new Rect(boxRect.x + PAD, y, boxRect.width - PAD * 2, childH), child, true);
                y += childH;
                first = false;
            });

            EditorGUI.indentLevel = indent;
        }

        /// <summary>显示带类型详情的自定义下拉弹窗（两条路径共用）。</summary>
        private static void ShowDropdown(Rect activatorRect, ProviderOptions options, int currentLocalIndex, Action<int> onSelectedLocal)
        {
            PopupWindow.Show(activatorRect, new TypeDropdownPopup(options, currentLocalIndex, onSelectedLocal));
        }

        #endregion

        #region Odin 绘制 [ODIN DRAWING]

        /// <summary>子属性容器样式：unity-box 背景 + PAD 内边距。</summary>
        private static GUIStyle s_ChildrenBoxStyle;

        private static GUIStyle ChildrenBoxStyle => s_ChildrenBoxStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset((int)PAD, (int)PAD, (int)PAD, (int)PAD)
        };

        protected override void DrawPropertyLayout(GUIContent label)
        {
            // 4.0.x 下 managed reference 的 UnityPropertyPath 解析可能失败（返回 null 或抛异常）。
            // 此时不再回退到 Odin 默认绘制（其 managed-reference 子内容易渲染失效），而是走值条目驱动路径。
            SerializedProperty prop;
            try { prop = Property.Tree.GetUnityPropertyForPath(Property.UnityPropertyPath); }
            catch { prop = null; }

            if (prop == null)
            {
                DrawValueEntryFallback(label);
                return;
            }

            TypeMenuCache cache = TypeMenuCache.Get(Attribute.BaseType ?? Property.BaseValueEntry.BaseValueType);
            var opts = new ProviderOptions(cache, Attribute.ShowNone);

            // 行标签：特性 Label 覆写优先，否则沿用 Odin 标签（已含 [LabelText]/[Tooltip] 等处理）
            GUIContent rowLabel = !string.IsNullOrEmpty(Attribute.Label) ? new GUIContent(Attribute.Label) : label;

            // string 模式：单行 popup，无 foldout
            if (prop.propertyType == SerializedPropertyType.String)
            {
                Rect rowRect = EditorGUILayout.GetControlRect(
                    true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                DrawRow(rowRect, prop, rowLabel ?? GUIContent.none, opts, false,
                    (p, i) =>
                    {
                        ApplySelectionWithUndo(p, i, opts.Cache);
                        GUI.changed = true;
                    });
                return;
            }

            // 引用模式：foldout 可见性取 Odin 子属性（State.Visible 已处理 [HideInInspector]/[Hidden] 等），
            // 与 Unity 可见性取并集，任一存在可显示子属性即展示箭头
            bool hasOdinChildren = HasVisibleOdinChildren();
            bool hasChildren = prop.managedReferenceValue != null
                && (hasOdinChildren || HasVisibleChildren(prop));

            Rect row = EditorGUILayout.GetControlRect(
                true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            bool open = DrawRow(row, prop, rowLabel ?? GUIContent.none, opts,
                hasChildren, (p, i) =>
                {
                    ApplySelectionWithUndo(p, i, opts.Cache);
                    Property.Update(true); // 类型切换后强制重解析值与子属性
                    GUI.changed = true;
                });

            if (!hasChildren || !open) return;

            if (hasOdinChildren)
            {
                DrawChildrenWithOdin();
            }
            else
            {
                // Odin 未解析出子属性：回退 Unity 序列化绘制
                Rect boxRect = EditorGUILayout.GetControlRect(
                    true, GetChildrenHeight(prop), GUILayout.ExpandWidth(true));
                DrawChildren(boxRect, prop);
            }
        }

        /// <summary>
        /// 值条目驱动回退路径：当无法取得 Unity SerializedProperty（4.0.x 路径解析失败 / 纯 Odin 宿主）时，
        /// 行读取/写入改由 Odin <see cref="InspectorProperty.ValueEntry"/> 完成，子内容仍交由 Odin PropertyTree 绘制，
        /// 保证自定义下拉与序列化内容在任何宿主下都能正常显示。
        /// </summary>
        private void DrawValueEntryFallback(GUIContent label)
        {
            var valueEntry = Property.ValueEntry;
            if (valueEntry == null)
            {
                CallNextDrawer(label);
                return;
            }

            TypeMenuCache cache = TypeMenuCache.Get(Attribute.BaseType ?? Property.BaseValueEntry.BaseValueType);
            var opts = new ProviderOptions(cache, Attribute.ShowNone);

            GUIContent rowLabel = !string.IsNullOrEmpty(Attribute.Label)
                ? new GUIContent(Attribute.Label) : (label ?? GUIContent.none);

            // foldout 键：无 Unity ID 可用，改用 Odin 树 + 属性路径（保持稳定且与其他路径互不干扰）
            string foldKey = "odin|" + Property.Tree.GetHashCode() + "|" + Property.Path;

            if (valueEntry.TypeOfValue == typeof(string))
            {
                Rect rowRect = EditorGUILayout.GetControlRect(
                    true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                int currentLocal = opts.CacheToLocal(cache.IndexOfName(valueEntry.WeakSmartValue as string));
                DrawRowCore(rowRect, rowLabel, opts, false, foldKey,
                    currentLocal, i => ApplyValue(opts, opts.LocalToCache(i)));
                return;
            }

            // 引用模式：子内容仅走 Odin 子属性
            bool hasOdinChildren = HasVisibleOdinChildren();
            bool hasChildren = valueEntry.WeakSmartValue != null && hasOdinChildren;

            Rect row = EditorGUILayout.GetControlRect(
                true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            int currentCacheIndex = valueEntry.WeakSmartValue == null
                ? 0 : cache.IndexOfType(valueEntry.WeakSmartValue.GetType());
            bool open = DrawRowCore(row, rowLabel, opts, hasChildren, foldKey,
                opts.CacheToLocal(currentCacheIndex), i => ApplyValue(opts, opts.LocalToCache(i)));

            if (!hasChildren || !open) return;
            DrawChildrenWithOdin();
        }

        /// <summary>值条目模式写入当前选中项（string 模式存类型全名，引用模式存实例，缓存索引 0 = None）。</summary>
        private void ApplyValue(ProviderOptions opts, int index)
        {
            var valueEntry = Property.ValueEntry;
            if (valueEntry == null) return;

            if (valueEntry.TypeOfValue == typeof(string))
            {
                valueEntry.WeakSmartValue = index >= 1 && index <= opts.Cache.Types.Length
                    ? opts.Cache.Types[index - 1].FullName : string.Empty;
            }
            else
            {
                valueEntry.WeakSmartValue = index == 0
                    ? null : Activator.CreateInstance(opts.Cache.Types[index - 1]);
            }

            Property.Update(true); // 类型切换后强制重解析值与子属性
            GUI.changed = true;
        }

        /// <summary>子属性是否在 Odin 侧存在可见项（State.Visible 由 Odin 处理隐藏特性后写入）。</summary>
        private bool HasVisibleOdinChildren()
        {
            var children = Property.Children;
            for (int i = 0; i < children.Count; i++)
                if (children[i].State.Visible) return true;
            return false;
        }

        /// <summary>子属性交由 Odin PropertyTree 绘制，子字段上的 Odin 特性（ValueDropdown 等）正常生效。</summary>
        private void DrawChildrenWithOdin()
        {
            using (new EditorGUILayout.VerticalScope(ChildrenBoxStyle))
            {
                GUIHelper.PushIndentLevel(1);
                try
                {
                    var children = Property.Children;
                    for (int i = 0; i < children.Count; i++)
                    {
                        InspectorProperty child = children[i];
                        if (!child.State.Visible) continue;
                        child.Draw();
                    }
                }
                finally
                {
                    GUIHelper.PopIndentLevel();
                }
            }
        }

        #endregion

        #region 下拉弹窗 [DROPDOWN POPUP]

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

            private readonly ProviderOptions _options;
            private readonly int _currentLocal;
            private readonly Action<int> _onSelectedLocal;
            private int _hoverIndex;
            private Vector2 _scroll;

            internal TypeDropdownPopup(ProviderOptions options, int currentLocalIndex, Action<int> onSelectedLocal)
            {
                _options = options;
                _currentLocal = currentLocalIndex;
                _onSelectedLocal = onSelectedLocal;
                _hoverIndex = currentLocalIndex;
            }

            public override void OnOpen()
            {
                // 不开启 wantsMouseMove 收不到 MouseMove 事件，悬停高亮与详情面板将失效
                editorWindow.wantsMouseMove = true;
            }

            public override Vector2 GetWindowSize()
            {
                float listH = _options.NameOptions.Length * ITEM_H;
                return new Vector2(MIN_WIDTH, Mathf.Min(listH + GetInfoHeight(), MAX_HEIGHT));
            }

            public override void OnGUI(Rect rect)
            {
                GUIContent[] names = _options.NameOptions;
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
                        i == _currentLocal ? EditorStyles.boldLabel : EditorStyles.label);

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
                        _onSelectedLocal(i);
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
                int cacheIndex = _options.LocalToCache(_hoverIndex);
                if (cacheIndex < 1 || cacheIndex > _options.Cache.Types.Length)
                {
                    EditorGUI.LabelField(
                        new Rect(infoRect.x + INFO_PAD, infoRect.y + 4, infoRect.width - INFO_PAD * 2, LINE_H),
                        "(None)", EditorStyles.miniLabel);
                    return;
                }

                Type type = _options.Cache.Types[cacheIndex - 1];
                float y = infoRect.y + INFO_PAD;

                DrawInfoLine(infoRect, ref y, "Type", type.FullName);
                DrawInfoLine(infoRect, ref y, "Base", _options.Cache.BaseType.FullName);
                DrawInfoLine(infoRect, ref y, "Assembly", type.Assembly.GetName().Name);

                // 点击详情区也可选中当前悬停项（回调需本地索引）
                if (Event.current.type == EventType.MouseDown && infoRect.Contains(Event.current.mousePosition))
                {
                    _onSelectedLocal(_hoverIndex);
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
}
