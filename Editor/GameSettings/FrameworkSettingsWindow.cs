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
            public Type Type;
            public string Title;
            public string Description;
            public int Order;
            public string SaveFolder;
            public ScriptableObject Instance;

            public bool Exists => Instance != null;
            public string AssetPath => SaveFolder + Type.Name + ".asset";
        }

        #endregion

        #region Fields

        private List<SettingEntry> m_Entries;
        private int m_SelectedIndex = -1;
        private string m_Search = "";
        private Vector2 m_SidebarScroll;
        private Vector2 m_ContentScroll;
        private UnityEditor.Editor m_CachedEditor;
        private readonly List<int> m_Filtered = new();

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
                for (int i = 0; i < (m_Entries?.Count ?? 0); i++)
                {
                    if (m_Entries[i].Type == so.GetType())
                    {
                        m_SelectedIndex = i;
                        RecreateEditor();
                        m_SidebarScroll = CalculateScrollToEntry(i);
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
            m_Entries = new List<SettingEntry>();
            m_SelectedIndex = -1;
            m_Search = "";

            foreach (var t in FindSettingsTypes())
            {
                var attr = t.GetCustomAttribute<FrameworkSettingAttribute>();
                string folder = attr?.SaveFolder ?? FrameworkSettingAttribute.DEFAULT_SAVE_FOLDER;

                m_Entries.Add(new SettingEntry
                {
                    Type        = t,
                    Title       = attr?.Title ?? ObjectNames.NicifyVariableName(t.Name),
                    Description = attr?.Description,
                    Order       = attr?.Order ?? 0,
                    SaveFolder  = folder,
                    Instance    = TryLoadAsset(folder + t.Name + ".asset", t)
                });
            }

            m_Entries.Sort((a, b) =>
            {
                int cmp = a.Order.CompareTo(b.Order);
                return cmp != 0 ? cmp : string.Compare(a.Title, b.Title, StringComparison.Ordinal);
            });

            ApplyFilter();
            if (m_Entries.Count > 0)
                m_SelectedIndex = m_Filtered.Count > 0 ? m_Filtered[0] : -1;
        }

        /// <summary>
        /// 只补加载 Instance 为空的条目，绝不调用 Instance 属性。
        /// </summary>
        private void RefreshInstances()
        {
            if (m_Entries == null) return;
            for (int i = 0; i < m_Entries.Count; i++)
            {
                var e = m_Entries[i];
                if (e.Instance == null)
                    e.Instance = TryLoadAsset(e.AssetPath, e.Type);
            }
        }

        /// <summary>
        /// 直接通过 AssetDatabase 从磁盘加载已存在的 asset。
        /// 文件不存在 → 返回 null，不做任何创建。
        /// </summary>
        private static ScriptableObject TryLoadAsset(string assetPath, Type type)
        {
            return AssetDatabase.LoadAssetAtPath(assetPath, type) as ScriptableObject;
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
            m_Filtered.Clear();
            if (m_Entries == null) return;

            for (int i = 0; i < m_Entries.Count; i++)
            {
                if (MatchesSearch(m_Entries[i]))
                    m_Filtered.Add(i);
            }
        }

        private bool MatchesSearch(SettingEntry e)
        {
            if (string.IsNullOrEmpty(m_Search)) return true;
            return e.Title.IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0
                || e.Type.Name.IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0
                || (e.Description != null && e.Description.IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        #endregion

        #region Editor Caching

        private void SelectEntry(int absoluteIndex)
        {
            if (m_SelectedIndex == absoluteIndex) return;
            m_SelectedIndex = absoluteIndex;
            RecreateEditor();
        }

        private void RecreateEditor()
        {
            DestroyEditor();
            if (m_SelectedIndex >= 0 && m_SelectedIndex < m_Entries.Count && m_Entries[m_SelectedIndex].Exists)
                m_CachedEditor = UnityEditor.Editor.CreateEditor(m_Entries[m_SelectedIndex].Instance);
        }

        private void DestroyEditor()
        {
            if (m_CachedEditor != null)
            {
                DestroyImmediate(m_CachedEditor);
                m_CachedEditor = null;
            }
        }

        private Vector2 CalculateScrollToEntry(int absoluteIndex)
        {
            int filteredPos = m_Filtered.IndexOf(absoluteIndex);
            if (filteredPos < 0) return m_SidebarScroll;
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

                m_Search = EditorGUILayout.TextField(m_Search, "Search", GUILayout.Width(240));

                if (GUILayout.Button("", "ToolbarSearchCancelButton"))
                {
                    m_Search = "";
                    GUI.FocusControl(null);
                }

                if (EditorGUI.EndChangeCheck())
                    ApplyFilter();

                GUILayout.FlexibleSpace();

                int total = m_Entries?.Count ?? 0;
                int loaded = m_Entries?.Count(e => e.Exists) ?? 0;
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

                m_SidebarScroll = EditorGUILayout.BeginScrollView(
                    m_SidebarScroll,
                    GUIStyle.none,
                    GUI.skin.verticalScrollbar);

                if (m_Entries == null || m_Entries.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        "No FrameworkSettings types found.",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                    GUILayout.FlexibleSpace();
                }
                else if (m_Filtered.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        "No matching results.",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                    GUILayout.FlexibleSpace();
                }
                else
                {
                    for (int i = 0; i < m_Filtered.Count; i++)
                    {
                        int idx = m_Filtered[i];
                        DrawSidebarEntry(m_Entries[idx], idx);
                    }

                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSidebarEntry(SettingEntry entry, int absoluteIndex)
        {
            Rect rect = GUILayoutUtility.GetRect(SIDEBAR_WIDTH, ENTRY_HEIGHT, GUILayout.ExpandWidth(true));

            bool isSelected = absoluteIndex == m_SelectedIndex;
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
            GUI.Label(titleRect, entry.Title,
                new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = titleColor },
                    fontSize = 12,
                    clipping = TextClipping.Overflow
                });

            if (!string.Equals(entry.Title, entry.Type.Name, StringComparison.Ordinal))
            {
                var subRect = new Rect(textX, rect.y + 22, textW, 14);
                GUI.Label(subRect, entry.Type.Name,
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
            if (m_SelectedIndex < 0 || m_SelectedIndex >= (m_Entries?.Count ?? 0))
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Select a setting from the sidebar",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 13 });
                GUILayout.FlexibleSpace();
                return;
            }

            var entry = m_Entries[m_SelectedIndex];

            // 只在 Instance 为 null 时尝试加载，不调用 Instance 属性
            if (entry.Instance == null)
            {
                entry.Instance = TryLoadAsset(entry.AssetPath, entry.Type);
                if (entry.Instance != null)
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
                        GUILayout.Label(entry.Title,
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

                    if (!string.IsNullOrEmpty(entry.Description))
                    {
                        GUILayout.Label(entry.Description,
                            new GUIStyle(EditorStyles.wordWrappedLabel)
                            {
                                normal = { textColor = new Color(0.62f, 0.62f, 0.62f) },
                                richText = true
                            });
                    }

                    GUILayout.Label(
                        $"{entry.Type.FullName}  ·  {entry.AssetPath}",
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
                                Selection.activeObject = entry.Instance;
                            if (GUILayout.Button("Ping", GUILayout.Width(52)))
                                EditorGUIUtility.PingObject(entry.Instance);
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
                if (m_CachedEditor == null || m_CachedEditor.target != entry.Instance)
                    RecreateEditor();

                if (m_CachedEditor != null)
                {
                    m_ContentScroll = EditorGUILayout.BeginScrollView(m_ContentScroll);
                    EditorGUI.BeginChangeCheck();
                    m_CachedEditor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck())
                        EditorUtility.SetDirty(entry.Instance);
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
                    () => Selection.activeObject = entry.Instance);
                menu.AddItem(new GUIContent("Ping"), false,
                    () => EditorGUIUtility.PingObject(entry.Instance));
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
        /// 绝不调用 Instance 属性。
        /// </summary>
        private void CreateAsset(SettingEntry entry)
        {
            // 确保目录存在
            if (!string.IsNullOrEmpty(entry.SaveFolder) && !Directory.Exists(entry.SaveFolder))
            {
                Directory.CreateDirectory(entry.SaveFolder);
                AssetDatabase.Refresh();
            }

            string path = entry.AssetPath;

            // 直接创建，绕开 Instance 属性
            var asset = ScriptableObject.CreateInstance(entry.Type);
            AssetDatabase.CreateAsset(asset, path);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            // 直接把刚创建的实例赋给 entry，不再重新加载
            entry.Instance = asset;
            RecreateEditor();
            EditorGUIUtility.PingObject(asset);
        }

        private void ResetSetting(SettingEntry entry)
        {
            if (!entry.Exists) return;

            if (!EditorUtility.DisplayDialog(
                "Reset Setting",
                $"Reset '{entry.Title}' to default values?\n\nThis cannot be undone.",
                "Reset",
                "Cancel"))
                return;

            var resetMethod = entry.Type.GetMethod(
                "Reset",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            resetMethod?.Invoke(entry.Instance, null);

            EditorUtility.SetDirty(entry.Instance);
            AssetDatabase.SaveAssets();

            RecreateEditor();
        }

        private void OpenSaveFolder(SettingEntry entry)
        {
            string folder = entry.SaveFolder;

            while (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                folder = Path.GetDirectoryName(folder);
            }

            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                EditorUtility.RevealInFinder(folder);
            else
                Debug.Log($"[FrameworkSettings] No existing folder found for: {entry.SaveFolder}");
        }

        #endregion
    }
}