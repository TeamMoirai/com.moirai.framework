using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 框架配置统一管理窗口。自动发现所有 FrameworkSettings&lt;T&gt; 子类，
    /// 侧边栏展示所有配置条目及其存在状态，支持创建、查看、Ping 和重置操作。
    /// </summary>
    public class FrameworkSettingsWindow : EditorWindow
    {
        #region Types

        private sealed class SettingEntry
        {
            public Type type;
            // 缓存 FrameworkSettings<T> 泛型基类及其 Instance 属性，避免每次操作都遍历继承链
            public Type genericBase;
            public PropertyInfo instanceProp;
            public string title;
            public string description;
            public int order;
            public string saveFolder;
            public FrameworkSettings instance;

            // 预缓存的字段级搜索文本（含字段名、Tooltip、Header、LabelText）
            public string fieldSearchText;

            public bool Exists => instance != null;
            public string AssetPath => saveFolder + type.Name + ".asset";
        }

        #endregion

        #region Constants

        private const float SIDEBAR_WIDTH = 252f;
        private const float ENTRY_HEIGHT = 44f;
        private const float ACCENT_WIDTH = 3f;
        private const float SEARCH_HEIGHT = 28f;

        private const float CLEAR_BTN_SIZE = 24f;
        private const float CLEAR_CIRCLE_RADIUS = 11f;

        #endregion

        #region Cached Styles

        /// <summary>
        /// 所有 GUIStyle 仅创建一次，后续每帧复用，避免 new GUIStyle 带来的 GC 开销。
        /// </summary>
        private static class Styles
        {
            // ── Colors ──
            public static readonly Color Accent      = new(0.36f, 0.68f, 1.00f);
            public static readonly Color ExistsDot   = new(0.30f, 0.80f, 0.42f);
            public static readonly Color MissingDot  = new(0.95f, 0.58f, 0.18f);
            public static readonly Color SelectedBg  = new(0.24f, 0.38f, 0.58f, 0.30f);
            public static readonly Color HoverBg     = new(1f, 1f, 1f, 0.04f);
            public static readonly Color Divider     = new(0.11f, 0.11f, 0.11f);
            public static readonly Color MutedText   = new(0.48f, 0.48f, 0.48f);
            public static readonly Color HeaderBgCol = new(0.158f, 0.158f, 0.158f);
            public static readonly Color MetaText    = new(0.55f, 0.55f, 0.55f);

            public static readonly Color SearchBgCol      = new(0.094f, 0.094f, 0.094f);
            public static readonly Color SearchBgFocusCol = new(0.115f, 0.115f, 0.115f);
            public static readonly Color SearchBorderCol  = new(0.19f, 0.19f, 0.19f);
            public static readonly Color PlaceholderCol   = new(0.32f, 0.32f, 0.32f);

            public static readonly Color ClearCircleNormal = new(0.28f, 0.28f, 0.28f);
            public static readonly Color ClearCircleHover  = new(0.42f, 0.42f, 0.42f);
            public static readonly Color ClearCircleActive = new(0.52f, 0.52f, 0.52f);
            public static readonly Color ClearXNormal      = new(0.58f, 0.58f, 0.58f);
            public static readonly Color ClearXHover       = new(0.95f, 0.95f, 0.95f);

            // ── GUIStyle fields ──
            public static GUIStyle EntryTitle;
            public static GUIStyle EntryTitleSel;
            public static GUIStyle EntrySub;
            public static GUIStyle ContentTitle;
            public static GUIStyle ContentDesc;
            public static GUIStyle ContentMeta;
            public static GUIStyle EmptyHint;
            public static GUIStyle CountLabel;
            public static GUIStyle HeaderBg;

            public static GUIStyle SearchInput;
            public static GUIStyle SearchPlaceholder;

            // ── Textures ──
            private static Texture2D s_HeaderBgTex;
            private static Texture2D s_SearchBgTex;
            private static Texture2D s_SearchBgFocusTex;
            private static bool s_Initialized;

            /// <summary>
            /// 外部可通过此属性判断 Styles 是否已就绪，避免在域重载早期阶段绘制 GUI。
            /// </summary>
            public static bool IsReady => s_Initialized;

            public static void Init()
            {
                if (s_Initialized) return;
                // 域重载后 EditorStyles 可能尚未初始化，此时 boldLabel 等属性返回 null
                // 直接返回，等下一帧 OnGUI 重试
                if (EditorStyles.boldLabel == null) return;

                s_Initialized = true;

                s_HeaderBgTex = MakeTex(HeaderBgCol);

                HeaderBg = new GUIStyle
                {
                    normal = { background = s_HeaderBgTex },
                    padding = new RectOffset(20, 20, 14, 10)
                };

                EntryTitle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    normal = { textColor = new Color(0.88f, 0.88f, 0.88f) },
                    clipping = TextClipping.Overflow
                };

                EntryTitleSel = new GUIStyle(EntryTitle)
                {
                    normal = { textColor = Color.white }
                };

                EntrySub = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = MutedText }
                };

                ContentTitle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 17,
                    normal = { textColor = new Color(0.95f, 0.95f, 0.95f) }
                };

                ContentDesc = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    normal = { textColor = new Color(0.60f, 0.60f, 0.60f) },
                    fontSize = 12
                };

                ContentMeta = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = MetaText },
                    wordWrap = true
                };

                EmptyHint = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true,
                    wordWrap = true
                };

                CountLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = MetaText },
                    alignment = TextAnchor.MiddleRight
                };

                s_SearchBgTex      = MakeTex(SearchBgCol);
                s_SearchBgFocusTex = MakeTex(SearchBgFocusCol);

                SearchInput = new GUIStyle
                {
                    fontSize  = 12,
                    alignment = TextAnchor.MiddleLeft,
                    clipping  = TextClipping.Clip,
                    padding   = new RectOffset(8, 4, 0, 0),
                    normal =
                    {
                        background = s_SearchBgTex,
                        textColor  = new Color(0.85f, 0.85f, 0.85f)
                    },
                    focused =
                    {
                        background = s_SearchBgFocusTex,
                        textColor  = Color.white
                    }
                };

                SearchPlaceholder = new GUIStyle
                {
                    fontSize  = 12,
                    alignment = TextAnchor.MiddleLeft,
                    padding   = new RectOffset(8, 4, 0, 0),
                    normal    = { textColor = PlaceholderCol }
                };
            }

            private static Texture2D MakeTex(Color color)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                tex.SetPixel(0, 0, color);
                tex.Apply();
                return tex;
            }

            public static void Cleanup()
            {
                if (s_HeaderBgTex != null)
                {
                    UnityEngine.Object.DestroyImmediate(s_HeaderBgTex);
                    s_HeaderBgTex = null;
                }
                if (s_SearchBgTex != null)
                {
                    UnityEngine.Object.DestroyImmediate(s_SearchBgTex);
                    s_SearchBgTex = null;
                }
                if (s_SearchBgFocusTex != null)
                {
                    UnityEngine.Object.DestroyImmediate(s_SearchBgFocusTex);
                    s_SearchBgFocusTex = null;
                }
                s_Initialized = false;
            }
        }

        #endregion

        #region Fields

        private List<SettingEntry> _entries;
        private int _selectedIndex = -1;
        private string _search = "";
        private Vector2 _sidebarScroll;
        private Vector2 _contentScroll;

        /// <summary>
        /// 核心优化：按条目索引缓存 Editor 实例。
        /// Editor.CreateEditor 涉及反射和序列化，开销大。
        /// 缓存后切换页签变为 O(1) 查表，而非每次重建。
        /// </summary>
        private readonly Dictionary<int, UnityEditor.Editor> _editorCache = new();
        private readonly List<int> _filtered = new();
        private int _loadedCount;

        // 复用 GUIContent 避免每帧分配
        private static readonly GUIContent s_Temp = new();

        // ── [新增] Odin LabelTextAttribute 的完整类型名，避免硬依赖 ──
        private const string ODIN_LABEL_TEXT_TYPE = "Sirenix.OdinInspector.LabelTextAttribute";
        private const string ODIN_LABEL_TEXT_PROP = "Text";

        #endregion

        #region Menu

        /// <summary>
        /// 通过菜单 Tools > Framework Settings 打开窗口。
        /// </summary>
        [MenuItem("Tools/Framework Settings", false, -99999)]
        public static void Open()
        {
            var w = GetWindow<FrameworkSettingsWindow>();
            w.titleContent = new GUIContent("Framework Settings");
            w.minSize = new Vector2(860, 500);
            w.Show();
        }

        #endregion

        #region Lifecycle

		// 不要在这里调用 Styles.Init()
		// 域重载后 EditorStyles 尚未就绪，此时访问会抛出 NRE
		// GUI 样式的初始化延迟到第一帧 OnGUI 中完成
        private void OnEnable()  => Discover();
        private void OnDisable() { ClearEditorCache(); Styles.Cleanup(); }
        private void OnProjectChange() { RefreshInstances(); Repaint(); }
        private void OnFocus()         { RefreshInstances(); Repaint(); }


        /// <summary>
        /// Project 窗口选中 ScriptableObject 时，同步高亮侧边栏中对应的配置条目。
        /// </summary>
        private void OnSelectionChange()
        {
            if (Selection.activeObject is ScriptableObject so)
            {
                for (int i = 0; i < (_entries?.Count ?? 0); i++)
                {
                    if (_entries[i].type == so.GetType())
                    {
                        _selectedIndex = i;
                        _contentScroll = Vector2.zero;
                        _sidebarScroll = CalculateScrollToEntry(i);
                        Repaint();
                        return;
                    }
                }
            }
        }

        #endregion

        #region Discovery

        /// <summary>
        /// 完整刷新：发现所有设置类型 → 读取元数据 → 缓存反射信息 → 加载已有资产 → 排序。
        /// </summary>
        private void Discover()
        {
            ClearEditorCache();
            _entries = new List<SettingEntry>();
            _selectedIndex = -1;
            _search = "";

            foreach (var t in FindSettingsTypes())
            {
                var attr = t.GetCustomAttribute<FrameworkSettingAttribute>();
                string folder = attr?.SaveFolder ?? FrameworkSettingAttribute.DEFAULT_SAVE_FOLDER;

                var genericBase = FindGenericBaseType(t);
                var instanceProp = genericBase?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

                var entry = new SettingEntry
                {
                    type = t, genericBase = genericBase, instanceProp = instanceProp,
                    title = attr?.Title ?? ObjectNames.NicifyVariableName(t.Name),
                    description = attr?.Description, order = attr?.Order ?? 0,
                    saveFolder = folder,
                    instance = TryLoadAsset(folder + t.Name + ".asset", t)
                };

                // 构建字段级搜索文本
                BuildFieldSearchText(entry);
                _entries.Add(entry);
            }

            _entries.Sort((a, b) =>
            {
                int cmp = a.order.CompareTo(b.order);
                return cmp != 0 ? cmp : string.Compare(a.title, b.title, StringComparison.Ordinal);
            });

            UpdateCounts(); ApplyFilter();
            if (_entries.Count > 0)
                _selectedIndex = _filtered.Count > 0 ? _filtered[0] : -1;
        }

        /// <summary>
        /// 使用 TypeCache 发现所有 FrameworkSettings&lt;T&gt; 的具体子类（编辑器专用 API，支持域重载）。
        /// </summary>
        private static IEnumerable<Type> FindSettingsTypes()
        {
            return TypeCache.GetTypesDerivedFrom(typeof(FrameworkSettings<>))
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && !t.IsInterface);
        }

        /// <summary>
        /// 向上遍历继承链，找到具体的 FrameworkSettings&lt;T&gt; 泛型基类。
        /// CRTP 模式下无法用 IsAssignableFrom 直接判断，必须检查泛型定义。
        /// </summary>
        private static Type FindGenericBaseType(Type type)
        {
            var c = type.BaseType;
            while (c != null && c != typeof(ScriptableObject))
            {
                if (c.IsGenericType && c.GetGenericTypeDefinition() == typeof(FrameworkSettings<>))
                    return c;
                c = c.BaseType;
            }
            return null;
        }

        /// <summary>
        /// 仅补加载 instance 为 null 的条目，绝不调用 Instance 属性（避免意外创建）。
        /// </summary>
        private void RefreshInstances()
        {
            if (_entries == null) return;
            bool changed = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.instance == null)
                {
                    e.instance = TryLoadAsset(e.AssetPath, e.type);
                    if (e.instance != null) { changed = true; InvalidateEditor(i); }
                }
            }
            if (changed) UpdateCounts();
        }

        /// <summary>
        /// 仅从磁盘加载已存在的 asset，不调用 Instance（避免意外创建缺失的配置文件）。
        /// </summary>
        private static FrameworkSettings TryLoadAsset(string assetPath, Type type)
            => AssetDatabase.LoadAssetAtPath(assetPath, type) as FrameworkSettings;

        private void UpdateCounts()
            => _loadedCount = _entries?.Count(e => e.Exists) ?? 0;

        #endregion

        #region Field Search Text Builder

        /// <summary>
        /// 扫描 entry.type 的所有序列化字段，将字段名（Nicified）、
        /// [Tooltip]、[Header]、[LabelText]（Odin）的文本拼接为一个字符串，
        /// 供搜索时一次性匹配，避免逐帧反射。
        /// </summary>
        private static void BuildFieldSearchText(SettingEntry entry)
        {
            var sb = new StringBuilder(256);
            var visitedHeaders = new HashSet<string>();

            // 同时扫描基类链上的字段（直到 FrameworkSettings / ScriptableObject）
            var chain = new List<FieldInfo>();
            CollectFieldsUpChain(entry.type, chain);

            foreach (var field in chain)
            {
				// 跳过不可序列化的私有字段
                if (field.IsPrivate && field.GetCustomAttribute<SerializeField>() == null) continue;

                // 跳过 static / readonly / const / volatile
                if (field.IsStatic || field.IsInitOnly || field.IsLiteral) continue;

				// 1. 字段名（Nicified
                string niceName = NicifyFieldName(field.Name);
                sb.AppendLine(niceName);

       			// 同时追加原始字段名，方便搜索 m_moveSpeed → "moveSpeed"
                sb.AppendLine(field.Name);

				// 2. [Tooltip("...")]
                var tooltip = field.GetCustomAttribute<TooltipAttribute>();
                if (tooltip != null && !string.IsNullOrEmpty(tooltip.tooltip)) sb.AppendLine(tooltip.tooltip);

                // 3. [Header("...")]
                var header = field.GetCustomAttribute<HeaderAttribute>();
                if (header != null && !string.IsNullOrEmpty(header.header) && visitedHeaders.Add(header.header))
                    sb.AppendLine(header.header);

				// 4. [LabelText("...")] — Odin Inspector，通过类型名反射避免硬依赖
                TryAppendOdinLabelText(field, sb);
            }
            entry.fieldSearchText = sb.ToString();
        }

        /// <summary>
        /// 沿继承链向上收集字段，直到遇到 FrameworkSettings（非泛型基类）或 ScriptableObject 为止。
        /// 同一字段名只保留最子类的版本（Unity 序列化行为）。
        /// </summary>
        private static void CollectFieldsUpChain(Type type, List<FieldInfo> result)
        {
            var seen = new HashSet<string>();
            var current = type;
            while (current != null && current != typeof(FrameworkSettings) && current != typeof(ScriptableObject) && current != typeof(UnityEngine.Object))
            {
                foreach (var f in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (seen.Add(f.Name)) result.Add(f);
                current = current.BaseType;
            }
        }

        /// <summary>
        /// 通过类型名检测 Odin 的 LabelTextAttribute，取出 Text 值。
        /// 若项目未安装 Odin 则静默跳过，零开销。
        /// </summary>
        private static void TryAppendOdinLabelText(FieldInfo field, StringBuilder sb)
        {
            foreach (var attr in field.GetCustomAttributes(false))
            {
                if (attr.GetType().FullName != ODIN_LABEL_TEXT_TYPE) continue;
                var prop = attr.GetType().GetProperty(ODIN_LABEL_TEXT_PROP, BindingFlags.Public | BindingFlags.Instance);
                if (prop?.GetValue(attr) is string value && !string.IsNullOrEmpty(value)) sb.AppendLine(value);
                break;
            }
        }

        /// <summary>
        /// 去掉 m_ / _ 前缀后调用 NicifyVariableName，生成带空格的可读名称。
        /// m_moveSpeed → "Move Speed"
        /// </summary>
        private static string NicifyFieldName(string raw)
        {
            ReadOnlySpan<char> span = raw.AsSpan();
            if (span.Length > 2 && span[0] == 'm' && span[1] == '_') span = span.Slice(2);
            else if (span.Length > 1 && span[0] == '_') span = span.Slice(1);

			// 保留首字母大写以便 Nicify 正确切词
            string trimmed = span.ToString();
            if (trimmed.Length > 0) trimmed = char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
            return ObjectNames.NicifyVariableName(trimmed);
        }

        #endregion

        #region Filter

        private void ApplyFilter()
        {
            _filtered.Clear();
            if (_entries == null) return;
            for (int i = 0; i < _entries.Count; i++)
                if (MatchesSearch(_entries[i])) _filtered.Add(i);
        }

        /// <summary>
        /// 搜索匹配：标题、类型名、描述、字段名、Tooltip、Header、LabelText。
        /// </summary>
        private bool MatchesSearch(SettingEntry e)
        {
            if (string.IsNullOrEmpty(_search)) return true;
			// 原有匹配：标题 / 类型名 / 描述
            return e.title.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || e.type.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || (e.description != null && e.description.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
				// 字段级元数据匹配
                || (!string.IsNullOrEmpty(e.fieldSearchText) && e.fieldSearchText.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        #endregion

        #region Editor Cache

        private void SelectEntry(int absoluteIndex)
        {
            if (_selectedIndex == absoluteIndex) return;
            _selectedIndex = absoluteIndex;
            _contentScroll = Vector2.zero;
        }

        /// <summary>
        /// 获取或创建指定条目的缓存 Editor。切换页签时直接命中缓存，无需重建。
        /// 仅在首次访问或缓存失效（如域重载、资产重建）时调用 CreateEditor。
        /// </summary>
        private UnityEditor.Editor GetCachedEditor(int absoluteIndex)
        {
            if (absoluteIndex < 0 || absoluteIndex >= _entries.Count) return null;
            if (_editorCache.TryGetValue(absoluteIndex, out var cached) && cached != null && cached.target != null)
                return cached;
            _editorCache.Remove(absoluteIndex);
            var editor = UnityEditor.Editor.CreateEditor(_entries[absoluteIndex].instance);
            _editorCache[absoluteIndex] = editor;
            return editor;
        }

        private void InvalidateEditor(int absoluteIndex)
        {
            if (_editorCache.TryGetValue(absoluteIndex, out var editor))
            {
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                _editorCache.Remove(absoluteIndex);
            }
        }

        private void ClearEditorCache()
        {
            foreach (var kvp in _editorCache)
                if (kvp.Value != null) UnityEngine.Object.DestroyImmediate(kvp.Value);
            _editorCache.Clear();
        }

        /// <summary>
        /// 计算滚动偏移使指定条目在侧边栏中可见。
        /// </summary>
        private Vector2 CalculateScrollToEntry(int absoluteIndex)
        {
            int filteredPos = _filtered.IndexOf(absoluteIndex);
            if (filteredPos < 0) return _sidebarScroll;
            float targetY = filteredPos * ENTRY_HEIGHT;
            return new Vector2(0, Mathf.Max(0, targetY - ENTRY_HEIGHT));
        }

        #endregion

        #region GUI - Main

        private void OnGUI()
        {
            Styles.Init();

            // 样式未就绪时直接跳过本帧绘制，等 EditorStyles 初始化完毕
            // 这同时避免了 Layout/Repaint 控件数不匹配导致的 ArgumentException
            if (!Styles.IsReady) { Repaint(); return; }

            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                DrawSidebar();
                DrawVerticalDivider();
                DrawContentArea();
            }
        }

        #endregion

        #region GUI - Toolbar

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.FlexibleSpace();
                s_Temp.text = $"{_loadedCount}/{_entries?.Count ?? 0}";
                GUILayout.Label(s_Temp, Styles.CountLabel, GUILayout.Width(44));
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    Discover();
            }
        }

        #endregion

        #region GUI - Sidebar

        private void DrawSidebar()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(SIDEBAR_WIDTH), GUILayout.ExpandHeight(true)))
            {
                DrawSidebarSearch();

                EditorGUI.DrawRect(GUILayoutUtility.GetRect(SIDEBAR_WIDTH, 1, GUILayout.ExpandWidth(true)), Styles.Divider);

                _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

                if (_entries == null || _entries.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("No FrameworkSettings types found.", Styles.EmptyHint);
                    GUILayout.FlexibleSpace();
                }
                else if (_filtered.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("No matching results.", Styles.EmptyHint);
                    GUILayout.FlexibleSpace();
                }
                else
                {
                    for (int i = 0; i < _filtered.Count; i++)
                        DrawSidebarEntry(_entries[_filtered[i]], _filtered[i]);
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSidebarEntry(SettingEntry entry, int absoluteIndex)
        {
            bool isSelected = (_selectedIndex == absoluteIndex);
            Rect rect = GUILayoutUtility.GetRect(SIDEBAR_WIDTH, ENTRY_HEIGHT, GUILayout.ExpandWidth(true));

            if (isSelected) EditorGUI.DrawRect(rect, Styles.SelectedBg);
            else if (rect.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(rect, Styles.HoverBg);

            if (isSelected) EditorGUI.DrawRect(new Rect(rect.x, rect.y, ACCENT_WIDTH, rect.height), Styles.Accent);

            // 状态圆点
            float dotSize = 7f, dotMargin = 14f;
            Rect dotRect = new Rect(rect.xMax - dotMargin - dotSize, rect.y + (rect.height - dotSize) * 0.5f, dotSize, dotSize);
            DrawFilledCircle(dotRect.center, dotSize * 0.5f, entry.Exists ? Styles.ExistsDot : Styles.MissingDot);

            // 文本
            Rect textRect = new Rect(rect.x + ACCENT_WIDTH + 10, rect.y + 4,
                rect.width - ACCENT_WIDTH - 10 - dotSize - dotMargin - 8, rect.height - 8);

            s_Temp.text = entry.title;
            GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 18), s_Temp, isSelected ? Styles.EntryTitleSel : Styles.EntryTitle);

            s_Temp.text = string.IsNullOrEmpty(entry.description) ? entry.type.Name : entry.description;
            GUI.Label(new Rect(textRect.x, textRect.y + 18, textRect.width, 16), s_Temp, Styles.EntrySub);

            if (Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition))
            { ShowContextMenu(entry); Event.current.Use(); }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            { SelectEntry(absoluteIndex); Event.current.Use(); Repaint(); }
        }

        private static void DrawFilledCircle(Vector2 center, float radius, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            var prev = Handles.color;
            Handles.color = color;
            const int seg = 20;
            var pts = new Vector3[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2f;
                pts[i] = new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius, 0);
            }
            Handles.DrawAAConvexPolygon(pts);
            Handles.color = prev;
        }

        #endregion

        #region GUI - Search

        private void DrawSidebarSearch()
        {
            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);

                bool hasText = !string.IsNullOrEmpty(_search);

                // 关键：搜索框占满剩余空间，清除按钮紧跟其后，由 GUILayout 自动排列
                // 清除按钮区域完全不参与 GUILayout 分配，用 fieldRect 绝对坐标在紧邻右侧绘制

                float btnTotal = hasText ? CLEAR_BTN_SIZE + 4 : 0; // 紧邻留 4px 间距
                Rect fieldRect = GUILayoutUtility.GetRect(0, SEARCH_HEIGHT,
                    GUILayout.ExpandWidth(true),
                    GUILayout.MaxWidth(SIDEBAR_WIDTH - 10 - 10 - btnTotal));

                // 1px 边框
                EditorGUI.DrawRect(fieldRect, Styles.SearchBorderCol);
                Rect inner = new Rect(fieldRect.x + 1, fieldRect.y + 1,
                                      fieldRect.width - 2, fieldRect.height - 2);

                bool isFocused = GUI.GetNameOfFocusedControl() == "_sidebarSearch";

                EditorGUI.BeginChangeCheck();
                GUI.SetNextControlName("_sidebarSearch");
                _search = GUI.TextField(inner, _search ?? "", Styles.SearchInput);
                if (EditorGUI.EndChangeCheck()) ApplyFilter();

                if (!hasText && !isFocused)
                    GUI.Label(inner, "Search settings...", Styles.SearchPlaceholder);

                // 清除按钮：紧贴在 fieldRect 右侧（已预留空间）
                if (hasText)
                {
                    Rect btnRect = new Rect(
                        fieldRect.xMax + 4,
                        fieldRect.y + (SEARCH_HEIGHT - CLEAR_BTN_SIZE) * 0.5f,
                        CLEAR_BTN_SIZE, CLEAR_BTN_SIZE);

                    bool isHover  = btnRect.Contains(Event.current.mousePosition);
                    bool isActive = isHover && Event.current.type == EventType.MouseDown && Event.current.button == 0;

                    Color bgColor = isActive  ? Styles.ClearCircleActive
                                  : isHover   ? Styles.ClearCircleHover
                                              : Styles.ClearCircleNormal;
                    DrawFilledCircle(btnRect.center, CLEAR_CIRCLE_RADIUS, bgColor);

                    s_Temp.text = "\u00D7";
                    GUI.Label(btnRect, s_Temp, new GUIStyle
                    {
                        fontSize = 15, fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = isHover ? Styles.ClearXHover : Styles.ClearXNormal }
                    });

                    var clearBtnStyle = new GUIStyle { normal = { background = null } };
                    if (GUI.Button(btnRect, GUIContent.none, clearBtnStyle))
                    {
                        _search = ""; GUI.FocusControl(null); ApplyFilter(); GUIUtility.ExitGUI();
                    }

                    if (isHover) EditorGUIUtility.AddCursorRect(btnRect, MouseCursor.Arrow);
                }

                GUILayout.Space(10);
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape
                && GUI.GetNameOfFocusedControl() == "_sidebarSearch")
            {
                _search = ""; GUI.FocusControl(null); ApplyFilter(); Event.current.Use(); GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space(4);
        }

        #endregion

        #region GUI - Content Area

        private void DrawVerticalDivider()
        {
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true)), Styles.Divider);
        }

        private void DrawContentArea()
        {
            if (_selectedIndex < 0 || _selectedIndex >= (_entries?.Count ?? 0))
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Select a setting from the sidebar.", Styles.EmptyHint);
                GUILayout.FlexibleSpace();
                return;
            }

            var entry = _entries[_selectedIndex];
            if (entry.instance == null)
            {
                entry.instance = TryLoadAsset(entry.AssetPath, entry.type);
                if (entry.instance != null) InvalidateEditor(_selectedIndex);
            }

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true)))
            {
                DrawContentHeader(entry);
                EditorGUILayout.Space(2);
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), Styles.Divider);
                EditorGUILayout.Space(4);
                DrawContentBody(entry);
            }
        }

        private void DrawContentHeader(SettingEntry entry)
        {
            using (new EditorGUILayout.VerticalScope(Styles.HeaderBg))
            {
                s_Temp.text = entry.title; GUILayout.Label(s_Temp, Styles.ContentTitle);
                if (!string.IsNullOrEmpty(entry.description))
                { s_Temp.text = entry.description; GUILayout.Label(s_Temp, Styles.ContentDesc); }

                s_Temp.text = $"{entry.type.FullName}  \u00B7  {entry.AssetPath}";
                GUILayout.Label(s_Temp, Styles.ContentMeta);
                EditorGUILayout.Space(6);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!entry.Exists)
                    {
                        if (GUILayout.Button("Create", GUILayout.Width(68), GUILayout.Height(24)))
                            CreateOrPingAsset(entry);
                    }
                    else
                    {
                        if (GUILayout.Button("Ping", GUILayout.Width(54), GUILayout.Height(24)))
                            CreateOrPingAsset(entry);
                        if (GUILayout.Button("Reset", GUILayout.Width(58), GUILayout.Height(24)))
                            ResetSetting(entry);
                    }
                    if (GUILayout.Button("Open Folder", GUILayout.Width(92), GUILayout.Height(24)))
                        OpenSaveFolder(entry);
                }
            }
        }

        /// <summary>
        /// 内容区主体：已存在时显示 Inspector，不存在时显示创建提示。
        /// 使用缓存的 Editor，切换页签时无需重建。
        /// </summary>
        private void DrawContentBody(SettingEntry entry)
        {
            if (entry.Exists)
            {
                var editor = GetCachedEditor(_selectedIndex);
                if (editor != null)
                {
                    _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(6); // 左侧留白，与右侧自然间距对称
                        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                        {
                            EditorGUI.BeginChangeCheck();
                            editor.OnInspectorGUI();
                            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(entry.instance);
                        }

                        GUILayout.Space(3); // 右侧微调，避免贴边
                    }
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Failed to create editor.", Styles.EmptyHint);
                    GUILayout.FlexibleSpace();
                }
            }
            else
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Setting asset not found.\nClick <b>Create</b> above to generate one.", Styles.EmptyHint);
                GUILayout.FlexibleSpace();
            }
        }

        #endregion

        #region Actions

        /// <summary>
        /// 创建或 Ping 配置资产。通过反射调用 FrameworkSettings&lt;T&gt;.Instance，
        /// 因为具体泛型参数 T 在编译期未知。
        /// </summary>
        private void CreateOrPingAsset(SettingEntry entry)
        {
            if (entry.genericBase == null || entry.instanceProp == null)
            { Debug.LogWarning($"[FrameworkSettings] Cannot invoke Instance for {entry.type.Name}."); return; }

            var instance = entry.instanceProp.GetValue(null) as FrameworkSettings;
            if (instance == null)
            { Debug.LogWarning($"[FrameworkSettings] Instance returned null for {entry.type.Name}."); return; }

            entry.instance = instance;
            int index = _entries.IndexOf(entry);
            if (index >= 0) InvalidateEditor(index);
            UpdateCounts();
            EditorGUIUtility.PingObject(instance);
            Repaint();
        }

        /// <summary>
        /// 二次确认后重置配置到默认值，调用非泛型基类的 ResetToDefaults()。
        /// </summary>
        private void ResetSetting(SettingEntry entry)
        {
            if (!entry.Exists) return;
            if (!EditorUtility.DisplayDialog("Reset Setting",
                $"Reset '{entry.title}' to default values?\n\nThis cannot be undone.", "Reset", "Cancel")) return;

            entry.instance.ResetToDefaults();
            EditorUtility.SetDirty(entry.instance);
            AssetDatabase.SaveAssets();

            int index = _entries.IndexOf(entry);
            if (index >= 0) InvalidateEditor(index);
        }

        /// <summary>
        /// 在资源管理器中打开配置资产所在目录。优先定位资产文件，资产不存在时回退到 saveFolder。
        /// </summary>
        private void OpenSaveFolder(SettingEntry entry)
        {
            if (entry.instance != null)
            {
                var path = AssetDatabase.GetAssetPath(entry.instance);
                if (!string.IsNullOrEmpty(path)) { EditorUtility.RevealInFinder(path); return; }
            }

            // saveFolder 可能不存在（如 Editor-only 配置），向上回退到最近的已有目录
            string folder = entry.saveFolder;
            while (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                folder = Path.GetDirectoryName(folder);

            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                EditorUtility.RevealInFinder(folder);
            else
                Debug.Log($"[FrameworkSettings] No existing folder found for: {entry.saveFolder}");
        }

        /// <summary>
        /// 侧边栏右键菜单。已存在的配置显示 Select/Ping/Reset，不存在的显示 Create。
        /// </summary>
        private void ShowContextMenu(SettingEntry entry)
        {
            var menu = new GenericMenu();
            if (entry.Exists)
            {
                menu.AddItem(new GUIContent("Select"), false, () => Selection.activeObject = entry.instance);
                menu.AddItem(new GUIContent("Ping"), false, () => CreateOrPingAsset(entry));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Reset"), false, () => ResetSetting(entry));
                menu.AddSeparator("");
            }
            else menu.AddItem(new GUIContent("Create"), false, () => CreateOrPingAsset(entry));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open Folder"), false, () => OpenSaveFolder(entry));
            menu.ShowAsContext();
        }

        #endregion
    }
}