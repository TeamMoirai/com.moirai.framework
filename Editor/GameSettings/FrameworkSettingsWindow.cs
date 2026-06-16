using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    public class FrameworkSettingsWindow : EditorWindow
    {
        #region Types

        private sealed class SettingEntry
        {
            public Type type;
            public string title;
            public string description;
            public int order;
            public string saveFolder;
            public FrameworkSettings instance;

            public bool Exists => instance != null;
            public string AssetPath => saveFolder + type.Name + ".asset";
        }

        #endregion

        #region Fields

        private List<SettingEntry> _entries;
        private int _selectedIndex = -1;
        private string _search = "";
        private Vector2 _sidebarScroll;
        private Vector2 _contentScroll;
        private UnityEditor.Editor _cachedEditor;
        private readonly List<int> _filtered = new();

        private const float SIDEBAR_WIDTH = 244f;
        private const float ENTRY_HEIGHT = 40f;

        private static readonly Color s_ExistsColor  = new(0.24f, 0.82f, 0.34f);
        private static readonly Color s_MissingColor = new(0.96f, 0.56f, 0.14f);
        private static readonly Color s_HoverBg      = new(1f, 1f, 1f, 0.04f);
        private static readonly Color s_SelectedBg   = new(0.17f, 0.33f, 0.56f);
        private static readonly Color s_SidebarBg    = new(0.196f, 0.196f, 0.196f);
        private static readonly Color s_Divider      = new(0.12f, 0.12f, 0.12f);
        private static readonly Color s_MutedText    = new(0.45f, 0.45f, 0.45f);

        #endregion

        #region Menu

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
            // 只补加载还没有 Instance 的条目，不会触发 Instance 属性
            RefreshInstances();
            Repaint();
        }

        private void OnFocus()
        {
            RefreshInstances();
            Repaint();
        }

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

                _entries.Add(new SettingEntry
                {
                    type        = t,
                    title       = attr?.Title ?? ObjectNames.NicifyVariableName(t.Name),
                    description = attr?.Description,
                    order       = attr?.Order ?? 0,
                    saveFolder  = folder,
                    instance    = TryLoadAsset(folder + t.Name + ".asset", t)
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
        /// 只补加载 Instance 为空的条目，绝不调用 Instance 属性。
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
        /// 直接通过 AssetDatabase 从磁盘加载已存在的 asset。
        /// 文件不存在 → 返回 null，不做任何创建。
        /// </summary>
        private static FrameworkSettings TryLoadAsset(string assetPath, Type type)
        {
            return AssetDatabase.LoadAssetAtPath(assetPath, type) as FrameworkSettings;
        }

        private static IEnumerable<Type> FindSettingsTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                .Where(t => !t.IsAbstract
                         && !t.IsGenericTypeDefinition
                         && !t.IsInterface
                         && InheritsFrameworkSettings(t));
        }

        private static bool InheritsFrameworkSettings(Type type)
        {
            var c = type.BaseType;
            while (c != null && c != typeof(ScriptableObject))
            {
                if (c.IsGenericType && c.GetGenericTypeDefinition() == typeof(FrameworkSettings<>))
                    return true;
                c = c.BaseType;
            }
            return false;
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

        private bool MatchesSearch(SettingEntry e)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return e.title.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || e.type.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || (e.description != null && e.description.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        #endregion

        #region Editor Caching

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

        private Vector2 CalculateScrollToEntry(int absoluteIndex)
        {
            int filteredPos = _filtered.IndexOf(absoluteIndex);
            if (filteredPos < 0) return _sidebarScroll;
            float targetY = filteredPos * ENTRY_HEIGHT;
            return new Vector2(0, Mathf.Max(0, targetY - ENTRY_HEIGHT));
        }

        #endregion

        #region GUI — Main

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

        #region GUI — Toolbar

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
                GUILayout.Label(
                    $"{loaded} / {total} loaded",
                    EditorStyles.miniLabel,
                    GUILayout.Width(80)
                );

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(58)))
                    Discover();
            }
        }

        #endregion

        #region GUI — Sidebar

        private void DrawSidebar()
        {
            using (new EditorGUILayout.VerticalScope(
                GUILayout.Width(SIDEBAR_WIDTH),
                GUILayout.ExpandHeight(true)))
            {
                var bgRect = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

                _sidebarScroll = EditorGUILayout.BeginScrollView(
                    _sidebarScroll,
                    GUIStyle.none,
                    GUI.skin.verticalScrollbar);

                if (_entries == null || _entries.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        "No FrameworkSettings types found.",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                    GUILayout.FlexibleSpace();
                }
                else if (_filtered.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        "No matching results.",
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

            DrawRoundedRect(dotRect, entry.Exists ? s_ExistsColor : s_MissingColor);

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

        #region GUI — Content Area

        private void DrawVerticalDivider()
        {
            var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandHeight(true));
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

            // 只在 Instance 为 null 时尝试加载，不调用 Instance 属性
            if (entry.instance == null)
            {
                entry.instance = TryLoadAsset(entry.AssetPath, entry.type);
                if (entry.instance != null)
                    RecreateEditor();
            }

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
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

                        string badgeText = entry.Exists ? "● Loaded" : "● Missing";
                        Color badgeColor = entry.Exists ? s_ExistsColor : s_MissingColor;

                        GUILayout.Label(badgeText,
                            new GUIStyle(EditorStyles.miniLabel)
                            {
                                normal = { textColor = badgeColor },
                                fontStyle = FontStyle.Bold,
                                padding = new RectOffset(0, 0, 3, 0)
                            });
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

                    GUILayout.Label(
                        $"{entry.type.FullName}  ·  {entry.AssetPath}",
                        new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal = { textColor = s_MutedText },
                            wordWrap = true
                        });

                    GUILayout.Space(6);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (!entry.Exists)
                        {
                            if (GUILayout.Button("Create Setting Asset", GUILayout.Width(155)))
                                CreateAsset(entry);
                        }
                        else
                        {
                            if (GUILayout.Button("Select", GUILayout.Width(62)))
                                Selection.activeObject = entry.instance;
                            if (GUILayout.Button("Ping", GUILayout.Width(52)))
                                EditorGUIUtility.PingObject(entry.instance);
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
                    if (EditorGUI.EndChangeCheck())
                        EditorUtility.SetDirty(entry.instance);
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        "Failed to create editor for this asset.",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                    GUILayout.FlexibleSpace();
                }
            }
            else
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    "Setting asset not found.\nClick 'Create Setting Asset' above to generate one.",
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

        #region GUI — Helpers

        private static void DrawRoundedRect(Rect rect, Color color)
        {
            EditorGUI.DrawRect(rect, color);
        }

        #endregion

        #region Actions

        private void ShowContextMenu(SettingEntry entry)
        {
            var menu = new GenericMenu();

            if (entry.Exists)
            {
                menu.AddItem(new GUIContent("Select"), false,
                    () => Selection.activeObject = entry.instance);
                menu.AddItem(new GUIContent("Ping"), false,
                    () => EditorGUIUtility.PingObject(entry.instance));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Reset"), false,
                    () => ResetSetting(entry));
                menu.AddSeparator("");
            }
            else
            {
                menu.AddItem(new GUIContent("Create Setting Asset"), false,
                    () => CreateAsset(entry));
                menu.AddSeparator("");
            }

            menu.AddItem(new GUIContent("Open Folder"), false,
                () => OpenSaveFolder(entry));

            menu.ShowAsContext();
        }

        /// <summary>
        /// 纯 Editor 侧创建：ScriptableObject.CreateInstance + AssetDatabase.CreateAsset。
        /// </summary>
        private void CreateAsset(SettingEntry entry)
        {
            // 确保目录存在
            if (!string.IsNullOrEmpty(entry.saveFolder) && !Directory.Exists(entry.saveFolder))
            {
                Directory.CreateDirectory(entry.saveFolder);
                AssetDatabase.Refresh();
            }

            string path = entry.AssetPath;

            // 直接创建，绕开 Instance 属性
            var asset = (FrameworkSettings)ScriptableObject.CreateInstance(entry.type);
            AssetDatabase.CreateAsset(asset, path);
            entry.instance.Reset();

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            // 直接把刚创建的实例赋给 entry，不再重新加载
            entry.instance = asset;
            RecreateEditor();
            EditorGUIUtility.PingObject(asset);
        }

        private void ResetSetting(SettingEntry entry)
        {
            if (!entry.Exists) return;

            if (!EditorUtility.DisplayDialog(
                "Reset Setting",
                $"Reset '{entry.title}' to default values?\n\nThis cannot be undone.",
                "Reset",
                "Cancel"))
                return;

            entry.instance.Reset();

            EditorUtility.SetDirty(entry.instance);
            AssetDatabase.SaveAssets();

            RecreateEditor();
        }

        private void OpenSaveFolder(SettingEntry entry)
        {
            string folder = entry.saveFolder;

            while (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                folder = Path.GetDirectoryName(folder);
            }

            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                EditorUtility.RevealInFinder(folder);
            else
                Debug.Log($"[FrameworkSettings] No existing folder found for: {entry.saveFolder}");
        }

        #endregion
    }
}