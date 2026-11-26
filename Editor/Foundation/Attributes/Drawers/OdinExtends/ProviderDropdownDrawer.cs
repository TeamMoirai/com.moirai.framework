using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using PopupWindow = UnityEditor.PopupWindow;

namespace Moirai.Atropos
{
    /// <summary>
    /// <see cref="ProviderDropdownAttribute"/> 的 Drawer：接管下拉行绘制与子属性展开，是 Odin 下该特性的唯一实现。
    /// 子属性优先交由 Odin PropertyTree 绘制，实现类字段上的 Odin 特性据此生效；优先级 super=1，
    /// 优先于 Odin 默认 managed reference drawer 与 DrawWithUnity(10000)。
    /// 主路径走 Unity SerializedProperty，解析失败或纯 Odin 宿主时退化为值条目路径（<see cref="DrawValueEntryFallback"/>），
    /// 不回退 Odin 默认 managed-reference 绘制；Odin 未解析出子属性时子属性区回退 Unity 序列化绘制。
    /// </summary>
    /// <remarks>
    /// 集合字段（元素为 managed reference）整体接管为类型列表 UI：分组以仅描边的细线盒包裹字段行与全部子元素
    /// （对齐 Odin 原生列表盒）；行首箭头 + 整行点击收展，右侧 <c>+</c> 幽灵图标弹类型弹窗新增；子元素平铺绘制
    /// （对齐 Odin 原生列表元素的平铺观感）、右侧 ✕ 幽灵图标删除；结构变更经
    /// <see cref="ICollectionResolver"/> Queue API + <see cref="PropertyTree.DelayActionUntilRepaint"/> 延迟应用。
    /// 非 managed reference 元素与只读集合不接管，交回 Odin 原生列表 UI；集合元素行不显示标签，行内只呈现类型下拉。
    /// </remarks>
    [DrawerPriority(1, 0, 0)]
    internal sealed class ProviderDropdownDrawer : OdinAttributeDrawer<ProviderDropdownAttribute>
    {
        internal const float PAD = 3f;
        private const float FOLDOUT_W = 16f;
        private const float BUTTON_GAP = 2f;
        private const float COUNT_GAP = 8f;

        /// <summary>foldout 展开状态，键为对象实例 ID + 属性路径（值条目路径为 Odin 树哈希 + 属性路径）。</summary>
        private static readonly Dictionary<string, bool> s_Foldouts = new Dictionary<string, bool>();

        /// <summary>当前正在绘制子元素列表的 managed collection 根属性；元素行据此判定自己是否附 ✕ 删除竖条。</summary>
        private static InspectorProperty s_ManagedCollectionScope;

        /// <summary>实例内缓存的 foldout 键与计数标签，避免每帧字符串拼接与重复测量。</summary>
        private string _unityFoldKey;
        private string _odinFoldKey;
        private string _rootFoldKey;
        private int _countCache = -1;
        private GUIContent _countLabel;
        private float _countWidth = -1f;

        /// <summary>跨帧实测的元素盒高（删除竖条定高用；-1 为尚未测得）。</summary>
        private float _elementBoxHeight = -1f;

        #region 类型菜单缓存 [TYPE MENU CACHE]

        /// <summary>
        /// 类型菜单缓存：按基类全局共享一份（TypeCache 查询、排序、选项数组、索引字典），避免同一基类的每个属性每次绘制重复构建。
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

            /// <summary>选项内容（含 "(None)" 前缀项；<see cref="ProviderDisplayAttribute.Title"/> 非空时行名替换为 title）。</summary>
            internal readonly GUIContent[] Names;

            /// <summary>每候选类型的 <see cref="ProviderDisplayAttribute.Description"/>（未标注为 null），与 <see cref="Types"/> 对齐。</summary>
            internal readonly string[] Descriptions;

            private readonly Dictionary<string, int> _nameToIndex;
            private readonly Dictionary<Type, int> _typeToIndex;

            private TypeMenuCache(Type baseType)
            {
                BaseType = baseType;
                // 候选筛选项走归行判据层的唯一真相，避免下拉与判据两边各写一遍后分叉
                Types = ProviderCatalogIndex.CompiledCandidates(baseType);

                int n = Types.Length;
                Names = new GUIContent[n + 1];
                Descriptions = new string[n];
                Names[0] = new GUIContent("(None)");

                _nameToIndex = new Dictionary<string, int>(n * 2);
                _typeToIndex = new Dictionary<Type, int>(n);

                for (int i = 0; i < n; i++)
                {
                    Type t = Types[i];
                    int choice = i + 1;
                    var display = t.GetCustomAttribute<ProviderDisplayAttribute>();
                    Descriptions[i] = display?.Description;
                    Names[choice] = new GUIContent(display?.Title ?? t.Name);
                    _typeToIndex[t] = choice;
                    _nameToIndex[t.FullName] = choice; // 全名：唯一键，不同命名空间的同名类型不冲突
                    if (!_nameToIndex.ContainsKey(t.Name))
                        _nameToIndex[t.Name] = choice; // 简单名：仅无冲突时登记，兼容手输的简单类型名
                }
            }

            /// <summary>
            /// 按类型全名或简单名查选项索引（0 = None），O(1)。
            /// </summary>
            internal int IndexOfName(string typeName) =>
                !string.IsNullOrEmpty(typeName) && _nameToIndex.TryGetValue(typeName, out int index) ? index : 0;

            /// <summary>
            /// 按 Type 查选项索引（0 = None），O(1)。
            /// </summary>
            internal int IndexOfType(Type type) =>
                type != null && _typeToIndex.TryGetValue(type, out int index) ? index : 0;
        }

        #endregion

        #region 通用与共享绘制 [SHARED]

        /// <summary>
        /// 每字段的下拉选项视图：集中 <c>(None)</c> 的显示决策与索引换算，供串行化属性路径与值条目路径共用同一套「本地选项」（索引从 0 连续递增）。
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

            /// <summary>
            /// 缓存索引（0=None，1..n=类型）→ 本地索引。
            /// </summary>
            internal readonly int CacheToLocal(int cacheIndex)
            {
                if (IncludeNone) return cacheIndex;
                return cacheIndex <= 0 ? 0 : Mathf.Min(cacheIndex - 1, Count - 1);
            }

            /// <summary>
            /// 本地索引 → 缓存索引（0=None，1..n=类型）。
            /// </summary>
            internal readonly int LocalToCache(int localIndex)
            {
                if (IncludeNone) return localIndex;
                return localIndex + 1;
            }
        }

        /// <summary>
        /// 写入选项：string 模式存类型全名，引用模式存实例，0 = None。
        /// </summary>
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
        /// 写入选项并注册撤销：<c>Update</c> → <c>Undo.RecordObject</c> → 写值 → <c>ApplyModifiedProperties</c>，保证 Ctrl+Z 可回退。
        /// </summary>
        private static void ApplySelectionWithUndo(SerializedProperty property, int index, TypeMenuCache cache)
        {
            property.serializedObject.Update();
            Undo.RecordObject(property.serializedObject.targetObject, "Change Provider");
            ApplySelection(property, index, cache);
            property.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 读取当前选项索引（按属性类型自动分派，字典 O(1) 查询）。
        /// </summary>
        private static int FindCurrentIndex(TypeMenuCache cache, SerializedProperty property) =>
            property.propertyType == SerializedPropertyType.String
                ? cache.IndexOfName(property.stringValue)
                : property.managedReferenceValue == null
                    ? 0
                    : cache.IndexOfType(property.managedReferenceValue.GetType());

        /// <summary>
        /// 类型名模式下配置值不在候选内（手输错、类改名）时的原样显示文字；只改显示不改选中。
        /// 未知名解析为 0，ShowNone=false 时 0 映射到本地首项，不覆写会把配错显示成已选中首项。
        /// </summary>
        private static GUIContent UnknownNameLabel(TypeMenuCache cache, string configuredName) =>
            !string.IsNullOrEmpty(configuredName) && cache.IndexOfName(configuredName) == 0
                ? new GUIContent($"{configuredName}（未识别）")
                : null;

        /// <summary>
        /// foldout 键：对象实例 ID + 属性路径，避免不同对象的相同属性路径互相干扰。
        /// </summary>
        private static string FoldoutKey(SerializedProperty property) =>
            property.serializedObject.targetObject.GetInstanceID() + property.propertyPath;

        /// <summary>
        /// 集合根的 foldout 键：有 Unity 属性时与单元素同款（实例 ID + 路径），否则退化为 Odin 树哈希 + 路径。
        /// </summary>
        private static string RootFoldoutKey(SerializedProperty prop, InspectorProperty property) =>
            prop != null
                ? FoldoutKey(prop)
                : "odin|" + property.Tree.GetHashCode() + "|" + property.Path;

        private static bool GetFoldout(string key) => GetFoldout(key, true);

        private static bool GetFoldout(string key, bool defaultValue) =>
            s_Foldouts.TryGetValue(key, out bool value) ? value : defaultValue;

        private static void SetFoldout(string key, bool value) => s_Foldouts[key] = value;

        /// <summary>内置 inspector 折叠三角图标（按需取一次）。</summary>
        private static Texture s_ArrowCollapsed;
        private static Texture s_ArrowExpanded;

        /// <summary>
        /// 行首展开箭头：点击切换并绘制内置 inspector 折叠三角。不用 <c>EditorGUI.Foldout</c>——
        /// hierarchyMode 下其绘制区与点击区几何错位，窄 rect 时可见三角落在点击区外（嵌套行收起失效的根因）。
        /// </summary>
        private static void DrawFoldoutArrow(Rect arrowRect, string foldKey, bool defaultExpanded)
        {
            bool open = GetFoldout(foldKey, defaultExpanded);
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && arrowRect.Contains(evt.mousePosition))
            {
                SetFoldout(foldKey, !open);
                open = !open;
                evt.Use();
                GUIHelper.RequestRepaint();
            }

            if (evt.type != EventType.Repaint) return;
            if (s_ArrowCollapsed == null) s_ArrowCollapsed = EditorGUIUtility.IconContent("IN foldout").image;
            if (s_ArrowExpanded == null) s_ArrowExpanded = EditorGUIUtility.IconContent("IN foldout on").image;
            Texture tex = open ? s_ArrowExpanded : s_ArrowCollapsed;
            if (tex != null) GUI.DrawTexture(arrowRect, tex, ScaleMode.ScaleToFit);
        }

        /// <summary>
        /// 遍历直接可见子属性（高度计算与 Unity 序列化回退绘制共用）。visitor 需跨迭代持有时应自行 Copy。
        /// </summary>
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

        /// <summary>
        /// 子属性区高度：子属性高度与间距（Unity 序列化回退路径用，平铺无内边距）。
        /// </summary>
        private static float GetChildrenHeight(SerializedProperty property)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float h = 0f;

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
        /// 绘制下拉行（SerializedProperty 驱动）：<paramref name="applySelection"/> 收到缓存索引（0=None），
        /// <paramref name="currentOverride"/> 只覆写显示文字。
        /// </summary>
        /// <returns>foldout 展开状态（string 模式恒为 true）。</returns>
        private bool DrawRow(Rect position, SerializedProperty property, GUIContent label,
            ProviderOptions options, bool reserveFoldout, Action<SerializedProperty, int> applySelection,
            GUIContent currentOverride = null)
        {
            int cacheIndex = FindCurrentIndex(options.Cache, property);
            _unityFoldKey ??= FoldoutKey(property);
            return DrawRowCore(position, label, options, reserveFoldout, _unityFoldKey,
                options.CacheToLocal(cacheIndex), i => applySelection(property, options.LocalToCache(i)),
                currentOverride);
        }

        /// <summary>
        /// 下拉行核心绘制（不依赖 SerializedProperty），两条驱动路径共用；索引均为本地索引。
        /// 展开箭头行首左置并跟随字段缩进；箭头与标签区点击收展（同 Unity foldout），popup 区点击归类型切换；
        /// 标签为空时跳过 <c>PrefixLabel</c>，字段区占满整行。
        /// </summary>
        private static bool DrawRowCore(Rect position, GUIContent label, ProviderOptions options, bool reserveFoldout,
            string foldKey, int currentLocalIndex, Action<int> onSelectedLocal, GUIContent currentOverride = null)
        {
            float lineH = EditorGUIUtility.singleLineHeight;
            bool hasLabelText = label != null && !string.IsNullOrEmpty(label.text);

            Rect content = position;
            Rect arrowRect = default;
            bool open = true;
            if (reserveFoldout)
            {
                // 箭头偏移与 PrefixLabel 的标签偏移同源（indentLevel × 18），右缘正好贴住标签左缘
                arrowRect = new Rect(position.x + EditorGUI.indentLevel * 18f, position.y, FOLDOUT_W, lineH);
                DrawFoldoutArrow(arrowRect, foldKey, true);
                open = GetFoldout(foldKey);
                content = new Rect(position.x + FOLDOUT_W, position.y, position.width - FOLDOUT_W, lineH);
            }

            Rect fieldRect = hasLabelText
                ? EditorGUI.PrefixLabel(new Rect(content.x, content.y, content.width, lineH), label)
                : content;

            if (reserveFoldout)
            {
                // 行首到字段控件之前点击收展；箭头区已被 DrawFoldoutArrow 消费，popup 区归类型切换
                Rect labelClickRect = new Rect(position.x, position.y,
                    Mathf.Max(0f, fieldRect.x - position.x), lineH);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                    && labelClickRect.Contains(Event.current.mousePosition))
                {
                    SetFoldout(foldKey, !open);
                    open = !open;
                    Event.current.Use();
                    GUIHelper.RequestRepaint();
                }
            }

            GUIContent current = currentOverride
                ?? (currentLocalIndex >= 0 && currentLocalIndex < options.NameOptions.Length
                    ? options.NameOptions[currentLocalIndex] : GUIContent.none);
            if (EditorGUI.DropdownButton(fieldRect, current, FocusType.Keyboard, EditorStyles.popup))
                ShowDropdown(fieldRect, options, currentLocalIndex, onSelectedLocal);

            return open;
        }

        /// <summary>
        /// 平铺绘制子属性（Unity 序列化回退路径）：缩进表达从属，不叠盒。
        /// </summary>
        private static void DrawChildren(Rect rect, SerializedProperty property)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float top = rect.y;
            float x = rect.x;
            float width = rect.width;
            float y = top;
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;

            bool first = true;
            ForEachVisibleChild(property, child =>
            {
                if (!first) y += spacing;
                float childH = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(
                    new Rect(x, y, width, childH), child, true);
                y += childH;
                first = false;
            });

            EditorGUI.indentLevel = indent;
        }

        /// <summary>
        /// 显示带类型详情的自定义下拉弹窗（两条路径共用）。
        /// </summary>
        private static void ShowDropdown(Rect activatorRect, ProviderOptions options, int currentLocalIndex, Action<int> onSelectedLocal)
        {
            PopupWindow.Show(activatorRect, new TypeDropdownPopup(options, currentLocalIndex, onSelectedLocal));
        }

        #endregion

        #region Odin 绘制 [ODIN DRAWING]

        /// <summary>
        /// 元素盒样式：无背景无描边——平铺绘制对齐 Odin 原生列表元素，仅承担 PAD 内边距与上下 3px 元素间距。
        /// </summary>
        private static GUIStyle s_ElementBoxStyle;

        private static GUIStyle ElementBoxStyle => s_ElementBoxStyle ??= new GUIStyle
        {
            padding = new RectOffset((int)PAD, (int)PAD, (int)PAD, (int)PAD),
            margin = new RectOffset(0, 0, 3, 3)
        };

        /// <summary>
        /// 集合分组盒样式：无背景——1px 细描边由 <see cref="DrawCollectionBoxFrame"/> 对齐 Odin 原生列表盒绘制，
        /// 仅承担 4px 内边距与上下 3px 分组间距。
        /// </summary>
        private static GUIStyle s_CollectionBoxStyle;

        private static GUIStyle CollectionBoxStyle => s_CollectionBoxStyle ??= new GUIStyle
        {
            padding = new RectOffset(4, 4, 4, 4),
            margin = new RectOffset(0, 0, 3, 3)
        };

        protected override void DrawPropertyLayout(GUIContent label)
        {
            if (Property.ChildResolver.IsCollection)
            {
                DrawCollectionProperty(label);
                return;
            }

            SerializedProperty prop = Property.Tree.UnitySerializedObject?.FindProperty(Property.UnityPropertyPath);

            if (prop == null)
            {
                DrawValueEntryFallback(label);
                return;
            }

            TypeMenuCache cache = TypeMenuCache.Get(Attribute.BaseType ?? Property.BaseValueEntry.BaseValueType);
            var opts = new ProviderOptions(cache, Attribute.ShowNone);

            // 行标签：特性 Label 覆写优先，否则沿用 Odin 标签
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
                    },
                    UnknownNameLabel(opts.Cache, prop.stringValue));
                return;
            }

            // 引用模式：子属性可见性取 Odin 与 Unity 的并集，任一侧存在即展示箭头
            bool hasOdinChildren = HasVisibleOdinChildren();
            bool hasChildren = prop.managedReferenceValue != null
                && (hasOdinChildren || HasVisibleChildren(prop));

            // 集合宿主下的元素行：box 包裹 + 行尾 ✕ 竖条
            if (s_ManagedCollectionScope == Property.Parent)
            {
                // 元素行不显示标签（Odin 下发的「字段 [n]」无信息量），类型下拉占满整行
                DrawManagedElement(prop, opts, hasChildren, hasOdinChildren);
                return;
            }

            // 非集合宿主（单元素字段）：整行 popup + 行首左置展开箭头
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
        /// 集合宿主下的元素行（Unity 属性路径）：头行与子属性绘制委托给 <see cref="DrawElementBox"/>；元素行不显示标签，类型下拉占满整行。
        /// </summary>
        private void DrawManagedElement(SerializedProperty prop, ProviderOptions opts,
            bool hasChildren, bool hasOdinChildren)
        {
            DrawElementBox(
                () =>
                {
                    Rect row = EditorGUILayout.GetControlRect(
                        true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                    return DrawRow(row, prop, GUIContent.none, opts, hasChildren,
                        (p, i) =>
                        {
                            ApplySelectionWithUndo(p, i, opts.Cache);
                            Property.Update(true); // 类型切换后重解析值与子属性
                            GUI.changed = true;
                        });
                },
                hasChildren ? (Action)(() =>
                {
                    if (hasOdinChildren) DrawChildrenWithOdin();
                    else
                    {
                        // Odin 未解析出子属性：回退 Unity 序列化平铺绘制
                        Rect boxRect = EditorGUILayout.GetControlRect(
                            true, GetChildrenHeight(prop), GUILayout.ExpandWidth(true));
                        DrawChildren(boxRect, prop);
                    }
                }) : null);
        }

        /// <summary>
        /// 子元素盒骨架：左列元素盒（头行 + 展开的子属性），右列 ✕ 竖条撑满同行高度；
        /// <paramref name="drawHeader"/> 返回展开状态，为真且 <paramref name="drawChildren"/> 非空时绘制子属性。
        /// </summary>
        /// <remarks>
        /// 删除竖条高度用「固定值 + 跨帧实测元素盒 rect」校准，不用 <c>GUILayout.ExpandHeight</c>——
        /// OdinMenuEditorWindow 系窗口（FrameworkSettings 等）的属性根容器被拉伸到整窗高，
        /// ExpandHeight 占位会吸满剩余高度，把分组框连同整页配置撑到占满窗口。
        /// </remarks>
        private void DrawElementBox(Func<bool> drawHeader, Action drawChildren)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var boxScope = new EditorGUILayout.VerticalScope(ElementBoxStyle);
                using (boxScope)
                {
                    if (drawHeader() && drawChildren != null) drawChildren();
                }

                // 实测元素盒高，供删除竖条定高：rect 随 Layout 事件解析，首帧未测得时竖条走单行兜底
                if (boxScope.rect.height > 0f) _elementBoxHeight = boxScope.rect.height;

                if (!CanRemoveElement()) return;
                GUILayout.Space(BUTTON_GAP);
                Rect strip = _elementBoxHeight > 0f
                    ? EditorGUILayout.GetControlRect(
                        GUILayout.Width(EditorGUIUtility.singleLineHeight),
                        GUILayout.Height(_elementBoxHeight))
                    : EditorGUILayout.GetControlRect(GUILayout.Width(EditorGUIUtility.singleLineHeight));
                if (DrawRemoveStripButton(strip))
                {
                    QueueRemoveSelf();
                    GUI.changed = true;
                }
            }
        }

        /// <summary>
        /// 值条目驱动回退路径：无 Unity SerializedProperty 时行读写改走 <see cref="InspectorProperty.ValueEntry"/>，子内容仍由 Odin 绘制。
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

            // foldout 键：无 Unity ID 可用，改用 Odin 树哈希 + 属性路径
            _odinFoldKey ??= "odin|" + Property.Tree.GetHashCode() + "|" + Property.Path;
            string foldKey = _odinFoldKey;

            if (valueEntry.TypeOfValue == typeof(string))
            {
                Rect rowRect = EditorGUILayout.GetControlRect(
                    true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                int currentLocal = opts.CacheToLocal(cache.IndexOfName(valueEntry.WeakSmartValue as string));
                DrawRowCore(rowRect, rowLabel, opts, false, foldKey,
                    currentLocal, i => ApplyValue(opts, opts.LocalToCache(i)),
                    UnknownNameLabel(cache, valueEntry.WeakSmartValue as string));
                return;
            }

            // 引用模式：子内容仅走 Odin 子属性
            bool hasOdinChildren = HasVisibleOdinChildren();
            bool hasChildren = valueEntry.WeakSmartValue != null && hasOdinChildren;

            // 集合宿主下的元素行：与 Unity 路径同款元素盒结构
            if (s_ManagedCollectionScope == Property.Parent)
            {
                DrawElementBox(
                    () =>
                    {
                        Rect row = EditorGUILayout.GetControlRect(
                            true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                        int currentCacheIndex = valueEntry.WeakSmartValue == null
                            ? 0 : cache.IndexOfType(valueEntry.WeakSmartValue.GetType());
                        return DrawRowCore(row, GUIContent.none, opts, hasChildren, foldKey,
                            opts.CacheToLocal(currentCacheIndex), i => ApplyValue(opts, opts.LocalToCache(i)));
                    },
                    hasChildren ? (Action)(() => DrawChildrenWithOdin()) : null);
                return;
            }

            // 非集合宿主（单元素字段）：整行 popup + 行首左置展开箭头
            Rect fallbackRow = EditorGUILayout.GetControlRect(
                true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            int fallbackCacheIndex = valueEntry.WeakSmartValue == null
                ? 0 : cache.IndexOfType(valueEntry.WeakSmartValue.GetType());
            bool fallbackOpen = DrawRowCore(fallbackRow, rowLabel, opts, hasChildren, foldKey,
                opts.CacheToLocal(fallbackCacheIndex), i => ApplyValue(opts, opts.LocalToCache(i)));

            if (!hasChildren || !fallbackOpen) return;
            DrawChildrenWithOdin();
        }

        /// <summary>
        /// 值条目模式写入当前选中项（string 模式存类型全名，引用模式存实例，缓存索引 0 = None）。
        /// </summary>
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

        /// <summary>
        /// 子属性是否在 Odin 侧存在可见项（State.Visible 由 Odin 处理隐藏特性后写入）。
        /// </summary>
        private bool HasVisibleOdinChildren()
        {
            var children = Property.Children;
            for (int i = 0; i < children.Count; i++)
                if (children[i].State.Visible) return true;
            return false;
        }

        /// <summary>
        /// 子属性交由 Odin PropertyTree 平铺绘制（缩进表达从属），子字段上的 Odin 特性正常生效。
        /// </summary>
        private void DrawChildrenWithOdin()
        {
            using (new EditorGUILayout.VerticalScope())
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

        #region 集合绘制 [COLLECTION DRAWING]

        /// <summary>
        /// 集合根整体接管：根行 = 箭头 + 标签 + 计数 + 「+」，整行点击收展；子元素盒 + ✕ 删除。
        /// 候选基类取 <see cref="ICollectionResolver.ElementType"/>（集合根的 managedReferenceValue 不可读，读即抛异常）；
        /// 增删经 Queue API + <see cref="PropertyTree.DelayActionUntilRepaint"/> 延迟应用，避免布局中途改树。
        /// </summary>
        private void DrawCollectionProperty(GUIContent label)
        {
            var resolver = Property.ChildResolver as ICollectionResolver;
            if (resolver == null || resolver.ElementType == null || resolver.ElementType == typeof(string))
            {
                CallNextDrawer(label);
                return;
            }

            SerializedProperty prop = TryGetUnityProperty();
            if (prop != null && prop.arraySize > 0
                && prop.GetArrayElementAtIndex(0).propertyType != SerializedPropertyType.ManagedReference)
            {
                CallNextDrawer(label); // 非 SerializeReference 集合（普通 PPtr 等），特性语义不适用
                return;
            }

            TypeMenuCache cache = TypeMenuCache.Get(Attribute.BaseType ?? resolver.ElementType);
            var opts = new ProviderOptions(cache, Attribute.ShowNone);
            GUIContent rowLabel = !string.IsNullOrEmpty(Attribute.Label) ? new GUIContent(Attribute.Label) : label;

            bool canAdd = !resolver.IsReadOnly && cache.Types.Length > 0;

            float lineH = EditorGUIUtility.singleLineHeight;

            // 计数标签缓存：元素数变化时重建
            int count = prop != null ? prop.arraySize : Property.Children.Count;
            if (_countLabel == null || _countCache != count)
            {
                _countCache = count;
                _countLabel = new GUIContent(FormatCount(count));
                _countWidth = EditorStyles.miniLabel != null ? EditorStyles.miniLabel.CalcSize(_countLabel).x : 0f;
            }
            GUIStyle countStyle = EditorStyles.miniLabel; // 域重载早期可能为 null，为空本帧跳过
            float countW = countStyle != null ? _countWidth : 0f;

            // 分组盒包裹字段行与全部子元素（对齐 Odin 原生列表盒：仅 1px 描边、无填充）；
            // scope 变量须声明在 using 外，作用域结束后才读得到 rect（块外引用 using 变量会 CS0103）
            var groupBox = new EditorGUILayout.VerticalScope(CollectionBoxStyle);
            using (groupBox)
            {
                Rect row = EditorGUILayout.GetControlRect(false, lineH, GUILayout.ExpandWidth(true));
                Rect plusRect = new Rect(row.xMax - lineH, row.y, lineH, lineH); // 行高正方形
                Rect countRect = new Rect(plusRect.x - COUNT_GAP - countW, row.y, countW, lineH);
                Rect arrowRect = new Rect(row.x, row.y, FOLDOUT_W, lineH);
                Rect labelRect = new Rect(arrowRect.xMax + 6, row.y,
                    row.width - FOLDOUT_W - 6 - lineH - COUNT_GAP - countW, row.height);

                _rootFoldKey ??= RootFoldoutKey(prop, Property);
                DrawFoldoutArrow(arrowRect, _rootFoldKey, false);
                bool open = GetFoldout(_rootFoldKey, false);

                bool hasLabelText = rowLabel != null && !string.IsNullOrEmpty(rowLabel.text);
                if (hasLabelText) GUI.Label(labelRect, rowLabel, EditorStyles.label);
                if (countW > 0) GUI.Label(countRect, _countLabel, countStyle);

                bool prevEnabled = GUI.enabled;
                GUI.enabled &= canAdd;
                if (SirenixEditorGUI.SDFIconButton(plusRect, SdfIconType.Plus, SirenixGUIStyles.IconButton))
                {
                    // 同一类型弹窗，选中即新增（(None) = 取消）
                    ShowDropdown(plusRect, opts, -1, localIndex =>
                    {
                        int cacheIndex = opts.LocalToCache(localIndex);
                        if (cacheIndex >= 1) QueueAddElement(cache.Types[cacheIndex - 1], resolver);
                        GUI.changed = true;
                    });
                }
                GUI.enabled = prevEnabled;

                // 整行点击收展（＋ 区域的 MouseDown 已被控件消费）
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                    && row.Contains(Event.current.mousePosition) && !plusRect.Contains(Event.current.mousePosition))
                {
                    SetFoldout(_rootFoldKey, !open);
                    Event.current.Use();
                    GUIHelper.RequestRepaint();
                }

                if (GetFoldout(_rootFoldKey, false))
                {
                    EditorGUILayout.Space(5); // 字段行与首个子元素的间距

                    // 元素经 resolver 特性透传仍由本类单元素模式绘制，按 scope 附元素盒与 ✕
                    var children = Property.Children;
                    var prevScope = s_ManagedCollectionScope;
                    s_ManagedCollectionScope = Property;
                    GUIHelper.PushIndentLevel(1);
                    try
                    {
                        for (int i = 0; i < children.Count; i++)
                        {
                            children[i].Draw();
                        }
                    }
                    finally
                    {
                        s_ManagedCollectionScope = prevScope;
                        GUIHelper.PopIndentLevel();
                    }
                }
            }

            // 描边须在作用域结束后绘制（rect 已定稿）；边线只落在内边距带内，不遮内容
            DrawCollectionBoxFrame(groupBox.rect);
        }

        /// <summary>
        /// 分组盒描边：对齐 Odin 原生列表的细描边盒——仅 1px 边线、无填充；须在分组作用域结束后的 Repaint 帧绘制。
        /// </summary>
        private static void DrawCollectionBoxFrame(Rect boxRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color line = EditorGUIUtility.isProSkin
                ? new Color(0f, 0f, 0f, 0.47f)
                : new Color(0f, 0f, 0f, 0.2f);
            EditorGUI.DrawRect(new Rect(boxRect.x, boxRect.y, boxRect.width, 1f), line);
            EditorGUI.DrawRect(new Rect(boxRect.x, boxRect.yMax - 1f, boxRect.width, 1f), line);
            EditorGUI.DrawRect(new Rect(boxRect.x, boxRect.y, 1f, boxRect.height), line);
            EditorGUI.DrawRect(new Rect(boxRect.xMax - 1f, boxRect.y, 1f, boxRect.height), line);
        }

        private static string FormatCount(int count) => count == 1 ? "1 item" : count + " items";

        /// <summary>
        /// 新增元素：逐选中父对象各建独立实例，Queue → Apply 延迟到本帧绘制结束后执行。
        /// </summary>
        private void QueueAddElement(Type elementType, ICollectionResolver resolver)
        {
            int parentCount = Property.ValueEntry.ValueCount;
            var values = new object[parentCount];
            for (int i = 0; i < parentCount; i++)
            {
                values[i] = Activator.CreateInstance(elementType); // 逐父对象独立实例，避免 managed reference 跨对象共享
            }

            Property.Tree.DelayActionUntilRepaint(() =>
            {
                resolver.QueueAdd(values);
                ((IApplyableResolver)resolver).ApplyChanges();
                Property.Update(true); // 结构变更后强制重解析子属性
                GUIHelper.RequestRepaint();
            });
        }

        /// <summary>
        /// 元素行 ✕ 删除：按位删除所有选中对象的同位元素，延迟到本帧绘制结束后应用。
        /// </summary>
        private void QueueRemoveSelf()
        {
            InspectorProperty parent = Property.Parent;
            var resolver = parent?.ChildResolver as IOrderedCollectionResolver;
            if (resolver == null || Property.Index < 0) return;

            int index = Property.Index;
            parent.Tree.DelayActionUntilRepaint(() =>
            {
                resolver.QueueRemoveAt(index);
                ((IApplyableResolver)resolver).ApplyChanges();
                parent.Update(true); // 结构变更后强制重解析子属性
                GUIHelper.RequestRepaint();
            });
        }

        /// <summary>
        /// 元素行是否具备 ✕ 删除条件：宿主集合可写、可按位删除且本行处于有效元素位。
        /// </summary>
        private bool CanRemoveElement()
        {
            if (!(Property.Parent?.ChildResolver is IOrderedCollectionResolver resolver)) return false;
            return !resolver.IsReadOnly && Property.Index >= 0;
        }

        /// <summary>
        /// ✕ 竖条按钮：行高正方形、纵向居中（SDFIconButton 会把图标缩放到 rect 尺寸，不能传整条竖条）；
        /// 幽灵图标不叠自绘边框，对齐 Odin 原生元素 ✕ 的观感。
        /// </summary>
        private static bool DrawRemoveStripButton(Rect strip)
        {
            float size = EditorGUIUtility.singleLineHeight;
            Rect iconRect = new Rect(strip.x, strip.center.y - size * 0.5f, size, size);
            return SirenixEditorGUI.SDFIconButton(iconRect, SdfIconType.X, SirenixGUIStyles.IconButton);
        }

        /// <summary>
        /// 取集合根的 Unity SerializedProperty；解析失败（4.0.x 偶发）返回 null，退化值条目路径。
        /// </summary>
        private SerializedProperty TryGetUnityProperty()
        {
            try
            {
                return Property.Tree.UnitySerializedObject?.FindProperty(Property.UnityPropertyPath);
            }
            catch (Exception)
            {
                return null;
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

                // 描述优先：置顶显示，其余 meta 随后；无描述时面板只有 Type / Base / Assembly
                string desc = _options.Cache.Descriptions[cacheIndex - 1];
                if (!string.IsNullOrEmpty(desc))
                    DrawDescriptionLine(infoRect, ref y, desc);

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

            /// <summary>
            /// 描述行：置顶整行绘制 <see cref="ProviderDisplayAttribute.Description"/>（不带前缀标签），按内容折行、高度随文本伸缩。
            /// </summary>
            private void DrawDescriptionLine(Rect infoRect, ref float y, string desc)
            {
                float descH = DescriptionHeight(desc);

                EditorGUI.LabelField(
                    new Rect(infoRect.x + INFO_PAD, y, infoRect.width - INFO_PAD * 2, descH),
                    desc, EditorStyles.wordWrappedMiniLabel);
                y += descH + 2f;
            }

            private static float DescriptionHeight(string desc) =>
                EditorStyles.wordWrappedMiniLabel.CalcHeight(new GUIContent(desc), MIN_WIDTH - INFO_PAD * 2) + 2f;

            private float GetInfoHeight() =>
                INFO_PAD * 2 + LINE_H * 3 + 4 + (HoverDescription() is string desc ? DescriptionHeight(desc) : 0f);

            /// <summary>
            /// 当前悬停项的描述（(None) 档与未标注项为 null）。
            /// </summary>
            private string HoverDescription()
            {
                int cacheIndex = _options.LocalToCache(_hoverIndex);
                return cacheIndex >= 1 && cacheIndex <= _options.Cache.Types.Length
                    ? _options.Cache.Descriptions[cacheIndex - 1]
                    : null;
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
        }

        #endregion
    }
}
