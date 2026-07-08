using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

            public bool Exists => instance != null;
            public string AssetPath => saveFolder + type.Name + ".asset";
        }

        #endregion

        #region Constants

        private const float SIDEBAR_WIDTH = 244f;
        private const float ENTRY_HEIGHT = 40f;

        private static readonly Color s_ExistsColor = new(0.24f, 0.82f, 0.34f);
        private static readonly Color s_MissingColor = new(0.96f, 0.56f, 0.14f);
        private static readonly Color s_HoverBg = new(1f, 1f, 1f, 0.04f);
        private static readonly Color s_SelectedBg = new(0.17f, 0.33f, 0.56f);
        private static readonly Color s_SidebarBg = new(0.196f, 0.196f, 0.196f);
        private static readonly Color s_Divider = new(0.12f, 0.12f, 0.12f);
        private static readonly Color s_MutedText = new(0.45f, 0.45f, 0.45f);

        #endregion

        #region Fields

        private List<SettingEntry> _entries;
        private int _selectedIndex = -1;
        private string _search = "";
        private Vector2 _sidebarScroll;
        private Vector2 _contentScroll;
        private UnityEditor.Editor _cachedEditor;
        private readonly List<int> _filtered = new();

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

        private void OnEnable()
        {
            Discover();
        }

        private void OnDisable()
        {
            DestroyEditor();
        }

        private void OnProjectChange()
        {
            RefreshInstances();
            Repaint();
        }

        private void OnFocus()
        {
            RefreshInstances();
            Repaint();
        }

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
                        RecreateEditor();
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
            DestroyEditor();
            _entries = new List<SettingEntry>();
            _selectedIndex = -1;
            _search = "";

            foreach (var t in FindSettingsTypes())
            {
                var attr = t.GetCustomAttribute<FrameworkSettingAttribute>();
                string folder = attr?.SaveFolder ?? FrameworkSettingAttribute.DEFAULT_SAVE_FOLDER;

                var genericBase = FindGenericBaseType(t);
                var instanceProp = genericBase?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

                _entries.Add(new SettingEntry
                {
                    type = t,
                    genericBase = genericBase,
                    instanceProp = instanceProp,
                    title = attr?.Title ?? ObjectNames.NicifyVariableName(t.Name),
                    description = attr?.Description,
                    order = attr?.Order ?? 0,
                    saveFolder = folder,
                    instance = TryLoadAsset(folder + t.Name + ".asset", t)
                });
            }

            _entries.Sort((a, b) =>
            {
                int cmp = a.order.CompareTo(b.order);
                return cmp != 0 ? cmp : string.Compare(a.title, b.title, StringComparison.Ordinal);
            });

            ApplyFilter();
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
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.instance == null)
                    e.instance = TryLoadAsset(e.AssetPath, e.type);
            }
        }

        /// <summary>
        /// 仅从磁盘加载已存在的 asset，不调用 Instance（避免意外创建缺失的配置文件）。
        /// </summary>
        private static FrameworkSettings TryLoadAsset(string assetPath, Type type)
        {
            return AssetDatabase.LoadAssetAtPath(assetPath, type) as FrameworkSettings;
        }

        #endregion

        #region Filter

        private void ApplyFilter()
        {
            _filtered.Clear();
            if (_entries == null) return;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (MatchesSearch(_entries[i]))
                    _filtered.Add(i);
            }
        }

        /// <summary>
        /// 搜索匹配：标题、类型名、描述（不区分大小写）。
        /// </summary>
        private bool MatchesSearch(SettingEntry e)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return e.title.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || e.type.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || (e.description != null && e.description.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        #endregion

        #region Editor Cache

        private void SelectEntry(int absoluteIndex)
        {
            if (_selectedIndex == absoluteIndex) return;
            _selectedIndex = absoluteIndex;
            RecreateEditor();
        }

        private void RecreateEditor()
        {
            DestroyEditor();
            if (_selectedIndex >= 0 && _selectedIndex < _entries.Count && _entries[_selectedIndex].Exists)
                _cachedEditor = UnityEditor.Editor.CreateEditor(_entries[_selectedIndex].instance);
        }

        private void DestroyEditor()
        {
            if (_cachedEditor != null)
            {
                DestroyImmediate(_cachedEditor);
                _cachedEditor = null;
            }
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
                EditorGUI.BeginChangeCheck();

                _search = EditorGUILayout.TextField(_search, "Search", GUILayout.Width(240));

                if (GUILayout.Button("", "ToolbarSearchCancelButton"))
                {
                    _search = "";
                    GUI.FocusControl(null);
                }

                if (EditorGUI.EndChangeCheck())
                    ApplyFilter();

                GUILayout.FlexibleSpace();

                int total = _entries?.Count ?? 0;
                int loaded = _entries?.Count(e => e.Exists) ?? 0;
                GUILayout.Label($"{loaded}/{total}", EditorStyles.miniLabel, GUILayout.Width(40));

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(58)))
                    Discover();
            }
        }

        #endregion

        #region GUI - Sidebar

        private void DrawSidebar()
        {
            using (new EditorGUILayout.VerticalScope(
                       GUILayout.Width(SIDEBAR_WIDTH),
                       GUILayout.ExpandHeight(true)))
            {
                _sidebarScroll = EditorGUILayout.BeginScrollView(
                    _sidebarScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

                if (_entries == null || _entries.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("No FrameworkSettings types found.",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                    GUILayout.FlexibleSpace();
                }
                else if (_filtered.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("No matching results.",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                    GUILayout.FlexibleSpace();
                }
                else
                {
                    for (int i = 0; i < _filtered.Count; i++)
                    {
                        int idx = _filtered[i];
                        DrawSidebarEntry(_entries[idx], idx);
                    }

                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSidebarEntry(SettingEntry entry, int absoluteIndex)
        {
            Rect rect = GUILayoutUtility.GetRect(SIDEBAR_WIDTH, ENTRY_HEIGHT, GUILayout.ExpandWidth(true));

            bool isSelected = absoluteIndex == _selectedIndex;
            bool isHover = rect.Contains(Event.current.mousePosition);

            if (isSelected)
                EditorGUI.DrawRect(rect, s_SelectedBg);
            else if (isHover)
                EditorGUI.DrawRect(rect, s_HoverBg);

            float dotSize = 7f;
            var dotRect = new Rect(
                rect.x + 12,
                rect.y + (ENTRY_HEIGHT - dotSize) * 0.5f,
                dotSize, dotSize);

            EditorGUI.DrawRect(dotRect, entry.Exists ? s_ExistsColor : s_MissingColor);

            float textX = rect.x + 26;
            float textW = rect.width - 36;
            Color titleColor = isSelected ? Color.white : new Color(0.88f, 0.88f, 0.88f);

            var titleRect = new Rect(textX, rect.y + 4, textW, 18);
            GUI.Label(titleRect, entry.title,
                new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = titleColor },
                    fontSize = 12,
                    clipping = TextClipping.Overflow
                });

            if (!string.Equals(entry.title, entry.type.Name, StringComparison.Ordinal))
            {
                var subRect = new Rect(textX, rect.y + 22, textW, 14);
                GUI.Label(subRect, entry.type.Name,
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = s_MutedText } });
            }

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0)
                {
                    SelectEntry(absoluteIndex);
                    GUI.FocusControl(null);
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.button == 1)
                {
                    ShowContextMenu(entry);
                    Event.current.Use();
                }
            }
        }

        #endregion

        #region GUI - Content Area

        private void DrawVerticalDivider()
        {
            var r = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(r, s_Divider);
        }

        private void DrawContentArea()
        {
            if (_selectedIndex < 0 || _selectedIndex >= (_entries?.Count ?? 0))
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Select a setting from the sidebar",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 13 });
                GUILayout.FlexibleSpace();
                return;
            }

            var entry = _entries[_selectedIndex];

            if (entry.instance == null)
            {
                entry.instance = TryLoadAsset(entry.AssetPath, entry.type);
                if (entry.instance != null)
                    RecreateEditor();
            }

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true)))
            {
                DrawContentHeader(entry);

                EditorGUILayout.Space(4);
                var divRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(divRect, s_Divider);
                EditorGUILayout.Space(2);

                DrawContentBody(entry);
            }
        }

        private void DrawContentHeader(SettingEntry entry)
        {
            EditorGUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(16);

                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(entry.title,
                            new GUIStyle(EditorStyles.boldLabel)
                            {
                                fontSize = 16,
                                normal = { textColor = Color.white }
                            });

                        GUILayout.Space(10);
                    }

                    if (!string.IsNullOrEmpty(entry.description))
                    {
                        GUILayout.Label(entry.description,
                            new GUIStyle(EditorStyles.wordWrappedLabel)
                            {
                                normal = { textColor = new Color(0.62f, 0.62f, 0.62f) },
                                richText = true
                            });
                    }

                    GUILayout.Label($"{entry.type.FullName}  ·  {entry.AssetPath}",
                        new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal = { textColor = s_MutedText },
                            wordWrap = true
                        });

                    GUILayout.Space(6);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // 配置不存在时显示 Create；已存在时显示 Ping（调用 Instance 返回缓存）+ Reset
                        if (!entry.Exists)
                        {
                            if (GUILayout.Button("Create", GUILayout.Width(65)))
                                CreateOrPingAsset(entry);
                        }
                        else
                        {
                            if (GUILayout.Button("Ping", GUILayout.Width(52)))
                                CreateOrPingAsset(entry);
                            if (GUILayout.Button("Reset", GUILayout.Width(56)))
                                ResetSetting(entry);
                        }

                        if (GUILayout.Button("Open Folder", GUILayout.Width(92)))
                            OpenSaveFolder(entry);
                    }
                }

                GUILayout.Space(16);
            }
        }

        /// <summary>
        /// 内容区主体：已存在时显示 Inspector，不存在时显示创建提示。
        /// </summary>
        private void DrawContentBody(SettingEntry entry)
        {
            if (entry.Exists)
            {
                if (_cachedEditor == null || _cachedEditor.target != entry.instance)
                    RecreateEditor();

                if (_cachedEditor != null)
                {
                    _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);
                    EditorGUI.BeginChangeCheck();
                    _cachedEditor.OnInspectorGUI();
                    // Inspector 修改后标记 dirty，确保变更可被保存
                    if (EditorGUI.EndChangeCheck())
                        EditorUtility.SetDirty(entry.instance);
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Failed to create editor for this asset.",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                    GUILayout.FlexibleSpace();
                }
            }
            else
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    "Setting asset not found.\nClick 'Create' above to generate one.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    {
                        fontSize = 12,
                        alignment = TextAnchor.MiddleCenter,
                        richText = true
                    });
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
            {
                Debug.LogWarning($"[FrameworkSettings] Cannot invoke Instance for {entry.type.Name}: reflection failed.");
                return;
            }

            var instance = entry.instanceProp.GetValue(null) as FrameworkSettings;
            if (instance == null)
            {
                Debug.LogWarning($"[FrameworkSettings] Instance returned null for {entry.type.Name}.");
                return;
            }

            entry.instance = instance;
            RecreateEditor();
            EditorGUIUtility.PingObject(instance);
            Repaint();
        }

        /// <summary>
        /// 二次确认后重置配置到默认值，调用非泛型基类的 ResetToDefaults()。
        /// </summary>
        private void ResetSetting(SettingEntry entry)
        {
            if (!entry.Exists) return;

            if (!EditorUtility.DisplayDialog(
                "Reset Setting",
                $"Reset '{entry.title}' to default values?\n\nThis cannot be undone.",
                "Reset",
                "Cancel"))
                return;

            entry.instance.ResetToDefaults();
            EditorUtility.SetDirty(entry.instance);
            AssetDatabase.SaveAssets();
            RecreateEditor();
        }

        /// <summary>
        /// 在资源管理器中打开配置资产所在目录。优先定位资产文件，资产不存在时回退到 saveFolder。
        /// </summary>
        private void OpenSaveFolder(SettingEntry entry)
        {
            if (entry.instance != null)
            {
                var path = AssetDatabase.GetAssetPath(entry.instance);
                if (!string.IsNullOrEmpty(path))
                {
                    EditorUtility.RevealInFinder(path);
                    return;
                }
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
                menu.AddItem(new GUIContent("Select"), false,
                    () => Selection.activeObject = entry.instance);
                menu.AddItem(new GUIContent("Ping"), false,
                    () => CreateOrPingAsset(entry));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Reset"), false,
                    () => ResetSetting(entry));
                menu.AddSeparator("");
            }
            else
            {
                menu.AddItem(new GUIContent("Create"), false,
                    () => CreateOrPingAsset(entry));
                menu.AddSeparator("");
            }

            menu.AddItem(new GUIContent("Open Folder"), false,
                () => OpenSaveFolder(entry));

            menu.ShowAsContext();
        }

        #endregion
    }
}
