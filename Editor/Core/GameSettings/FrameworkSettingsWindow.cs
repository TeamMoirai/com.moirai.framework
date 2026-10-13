using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 框架配置统一管理窗口（Odin 实现）：自动发现所有 <c>FrameworkSettings&lt;T&gt;</c> 子类，侧边栏为 Odin 菜单树，内容区由 Odin 原生管线绘制。
    /// </summary>
    /// <remarks>
    /// 侧边栏内置搜索 / 状态图标；内容区与独立 Inspector 同一宿主，managed reference 及其 Odin 特性完整生效；支持创建、Ping、重置和打开目录。
    /// </remarks>
    public class FrameworkSettingsWindow : FrameworkMenuWindowBase
    {
        #region 菜单 [MENU]

        /// <summary>
        /// 通过菜单打开窗口。
        /// </summary>
        [MenuItem("Tools/Framework Settings", false, -99999)]
        public static void Open()
        {
            GetWindow<FrameworkSettingsWindow>().Show();
        }

        /// <summary>
        /// 打开窗口并在侧边栏选中指定配置类型的条目（如 SaveServiceSettings）。
        /// </summary>
        public static void Open(Type settingsType)
        {
            var window = GetWindow<FrameworkSettingsWindow>();
            window.Show();
            window.SelectEntry(settingsType);
        }

        #endregion        
        
        #region 常量 [CONSTANTS]

        private const float MENU_WIDTH = 252f;

        private static readonly Color s_ExistsIconColor = new Color(0.30f, 0.80f, 0.42f);
        private static readonly Color s_MissingIconColor = new Color(0.95f, 0.58f, 0.18f);

        // Odin LabelText 的类型名以字符串引用，避免硬依赖
        private const string ODIN_LABEL_TEXT_TYPE = "Sirenix.OdinInspector.LabelTextAttribute";
        private const string ODIN_LABEL_TEXT_PROP = "Text";

        #endregion

        #region 条目元数据 [ENTRY METADATA]

        /// <summary>
        /// 单个框架配置条目的元数据与缓存反射信息。
        /// </summary>
        private sealed class SettingEntry
        {
            public Type type;
            // 反射缓存：避免每次操作遍历继承链
            public Type genericBase;
            public PropertyInfo instanceProp;
            public string title;
            public string description;
            public int order;
            public string saveFolder;
            public ScriptableObject instance;

            // 预缓存的字段级搜索文本（含字段名、Tooltip、Header、LabelText）
            public string fieldSearchText;

            /// <summary>资产是否已创建。</summary>
            public bool Exists => instance != null;

            /// <summary>配置资产期望路径。</summary>
            public string AssetPath => saveFolder + type.Name + ".asset";
        }

        #endregion

        #region 未创建页面 [MISSING PAGE]

        /// <summary>
        /// 未创建资产的配置条目在内容区展示的 Odin 页面（纯值对象，由 Odin PropertyTree 绘制）。
        /// </summary>
        private sealed class MissingSettingPage
        {
            private readonly FrameworkSettingsWindow _window;
            private readonly SettingEntry _entry;

            /// <summary>
            /// 构造未创建页面。
            /// </summary>
            public MissingSettingPage(FrameworkSettingsWindow window, SettingEntry entry)
            {
                _window = window;
                _entry = entry;
            }

            /// <summary>所属配置条目（供窗口反查）。</summary>
            public SettingEntry Entry => _entry;

            [ShowInInspector, LabelText("类型 [TYPE]"), ReadOnly]
            private string TypeLabel => _entry.type.FullName;

            [ShowInInspector, LabelText("期望路径 [PATH]"), ReadOnly]
            private string PathLabel => _entry.AssetPath;

            [InfoBox("$GetInfoText", InfoMessageType.None)]
            [Button("Create Asset"), GUIColor(0.36f, 0.68f, 1.00f)]
            private void CreateAsset() => _window.CreateOrPingAsset(_entry);

            [Button("Open Folder")]
            private void OpenFolder() => _window.OpenSaveFolder(_entry);

            private string GetInfoText() => "Setting asset not found. Click 'Create Asset' to generate one.";
        }

        #endregion

        #region 字段 [FIELDS]

        private List<SettingEntry> _entries;
        private readonly Dictionary<Type, SettingEntry> _entriesByType = new Dictionary<Type, SettingEntry>();

        // 工具栏条件按钮的选中条目：仅 Layout 遍刷新，保证同帧 Layout/Repaint 控件数一致
        private SettingEntry _toolbarEntry;

        // 工具栏计数标签缓存：仅发现期更新，绘制期零分配
        private string _toolbarCountLabel = "0/0";

        // 重建前捕获的选中条目类型：重建后按类型恢复（Create Asset 流程依赖——未创建页选中重建后定位到新实例）
        private Type _rebuildEntryType;

        #endregion
        
        #region 生命周期 [LIFECYCLE]

        protected override void OnEnable()
        {
            minSize = new Vector2(860f, 500f);
            titleContent = new GUIContent("Framework Settings");
            MenuWidth = MENU_WIDTH;
            base.OnEnable();
            EnsureMenuTreeBuilt();
        }

        private void OnFocus()
        {
            // 外部工具写入资产不会触发 projectChanged，聚焦时补加载缺失的实例
            if (_entries == null) return;
            bool changed = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.instance == null)
                {
                    entry.instance = TryLoadAsset(entry.AssetPath, entry.type);
                    changed |= entry.instance != null;
                }
            }
            if (changed) QueueTreeRebuild();
        }

        private void OnProjectChange()
        {
            // 资产创建/删除/移动后重建菜单树并恢复选中项
            QueueTreeRebuild();
        }

        /// <summary>
        /// Project 窗口选中配置资产时，同步高亮菜单树中对应条目。
        /// </summary>
        private void OnSelectionChange()
        {
            if (!(Selection.activeObject is ScriptableObject so)) return;
            if (_entriesByType.TryGetValue(so.GetType(), out var entry)
                && entry.Exists
                && MenuTree != null
                && !ReferenceEquals(MenuTree.Selection.SelectedValue, so))
            {
                TrySelectMenuItemWithObject(so);
            }
        }

        #endregion

        #region 菜单树构建 [MENU TREE]

        /// <summary>
        /// 构建菜单树：发现全部配置类型 → 按特性排序 → 为已创建条目挂资产、未创建条目挂创建页面。
        /// </summary>
        protected override OdinMenuTree BuildMenuTree()
        {
            Discover();

            var tree = new OdinMenuTree(false);

            ApplyMenuStyle(tree);

            tree.Config.DrawSearchToolbar = true;
            tree.Config.AutoScrollOnSelectionChanged = true;
            tree.Config.AutoHandleKeyboardNavigation = false;
            tree.Config.SearchFunction = MatchesSearch;

            foreach (var entry in _entries)
            {
                object target = entry.Exists ? (object)entry.instance : new MissingSettingPage(this, entry);
                var item = tree.Add(entry.title, target).Last();
                item.SearchString = entry.fieldSearchText;
                // 状态图标：√ = 资产已创建，× = 未创建
                item.SdfIcon = entry.Exists ? SdfIconType.CheckCircleFill : SdfIconType.XCircleFill;
                item.SdfIconColor = entry.Exists ? s_ExistsIconColor : s_MissingIconColor;
                item.OnRightClick = _ => ShowContextMenu(entry);
            }

            return tree;
        }

        /// <summary>
        /// 搜索匹配：条目标题、类型名、描述与字段级元数据（Tooltip/Header/LabelText/字段名）。
        /// </summary>
        private static bool MatchesSearch(OdinMenuItem item)
        {
            string term = item.MenuTree.Config.SearchTerm;
            if (string.IsNullOrEmpty(term)) return true;
            if (item.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (item.Value is ScriptableObject so
                && (so.GetType().Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            if (item.Value is MissingSettingPage page
                && !string.IsNullOrEmpty(page.Entry.fieldSearchText)
                && page.Entry.fieldSearchText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (item.SearchString != null
                && item.SearchString.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        #endregion

        #region 工具栏 [TOOLBAR]

        /// <summary>
        /// 顶部工具栏：刷新、计数与当前选中条目的快捷操作。
        /// </summary>
        protected override void OnImGUI()
        {
            HeaderStyles.Init();

            if (_entries == null)
            {
                QueueTreeRebuild();
                return;
            }

            // 仅 Layout 遍求值，避免帧内选中变化导致控件数不匹配
            if (Event.current.type == EventType.Layout)
                _toolbarEntry = GetSelectedEntry();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // Refresh 固定最左，计数随后自适应宽度，按钮位置不随条目数变化
                if (ToolbarButton("Refresh", 64f))
                    QueueTreeRebuild();

                GUILayout.Label(_toolbarCountLabel,
                    HeaderStyles.IsReady ? HeaderStyles.CountLabel : EditorStyles.miniLabel,
                    GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(true));

                GUILayout.FlexibleSpace();

                var entry = _toolbarEntry;
                if (entry != null)
                {
                    if (entry.Exists)
                    {
                        if (ToolbarButton("Ping", 48f))
                            EditorGUIUtility.PingObject(entry.instance);
                        if (ToolbarButton("Reset", 64f))
                            ResetSetting(entry);
                    }
                    else if (ToolbarButton("Create", 64f))
                    {
                        CreateOrPingAsset(entry);
                    }
                    if (ToolbarButton("Open Folder", 88f))
                        OpenSaveFolder(entry);
                }
            }

            // 此 Odin 版本必须调 base.OnImGUI 才会绘制菜单树与编辑器区
            base.OnImGUI();
        }

        /// <summary>
        /// 反查当前选中条目（已创建 → 资产本体，未创建 → 创建页面）。
        /// </summary>
        private SettingEntry GetSelectedEntry()
        {
            var tree = MenuTree;
            if (tree == null) return null;
            return tree.Selection.SelectedValue switch
            {
                MissingSettingPage page => page.Entry,
                ScriptableObject so => _entriesByType.TryGetValue(so.GetType(), out var entry) ? entry : null,
                _ => null,
            };
        }

        #endregion

        #region 信息头 [HEADER]

        /// <summary>
        /// 编辑器区绘制前的信息头（仅内容列）：配置名称、描述、Script 引用与类型/资产路径。
        /// </summary>
        protected override void OnBeginDrawEditors()
        {
            HeaderStyles.Init();

            var entry = GetSelectedEntry();
            if (entry == null) return;

            using (new EditorGUILayout.VerticalScope(HeaderStyles.IsReady ? HeaderStyles.HeaderBg : EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var titleContent = entry.title;
                    GUILayout.Label(titleContent, HeaderStyles.IsReady ? HeaderStyles.Title : EditorStyles.boldLabel,
                        GUILayout.ExpandWidth(true), GUILayout.MinWidth(0));

                    if (entry.instance != null)
                    {
                        var script = MonoScript.FromScriptableObject(entry.instance);
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.ObjectField(script, typeof(MonoScript), false, GUILayout.Width(260f));
                        }
                    }
                }

                if (!string.IsNullOrEmpty(entry.description))
                    GUILayout.Label(entry.description, HeaderStyles.IsReady ? HeaderStyles.Description : EditorStyles.wordWrappedLabel);

                GUILayout.Label($"{entry.type.FullName}  \u00B7  {entry.AssetPath}",
                    HeaderStyles.IsReady ? HeaderStyles.Meta : EditorStyles.miniLabel);
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space(4);
        }

        #endregion

        #region 发现 [DISCOVERY]

        /// <summary>
        /// 完整刷新：发现所有设置类型 → 读取元数据 → 缓存反射信息 → 加载已有资产 → 排序。
        /// </summary>
        private void Discover()
        {
            _entries = new List<SettingEntry>();
            _entriesByType.Clear();

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

                BuildFieldSearchText(entry);
                _entries.Add(entry);
                _entriesByType[t] = entry;
            }

            _entries.Sort((a, b) =>
            {
                int cmp = a.order.CompareTo(b.order);
                return cmp != 0 ? cmp : string.Compare(a.title, b.title, StringComparison.Ordinal);
            });

            // 工具栏计数：仅发现期更新，绘制期零分配
            int loadedCount = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Exists) loadedCount += 1;
            }

            _toolbarCountLabel = $"{loadedCount}/{_entries.Count}";
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
        /// 向上遍历继承链找 <c>FrameworkSettings&lt;T&gt;</c> 泛型基类；CRTP 下须比对泛型定义，<c>IsAssignableFrom</c> 不可用。
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
        /// 仅从磁盘加载已存在的 asset，不调用 Instance（避免意外创建缺失的配置文件）。
        /// </summary>
        private static ScriptableObject TryLoadAsset(string assetPath, Type type)
            => AssetDatabase.LoadAssetAtPath(assetPath, type) as ScriptableObject;

        #endregion

        #region 字段搜索文本构建器 [FIELD SEARCH BUILDER]

        /// <summary>
        /// 把字段名（Nicified + 原始名）、<c>[Tooltip]</c>、<c>[Header]</c>、Odin <c>[LabelText]</c> 拼成一段搜索文本，供一次性匹配。
        /// </summary>
        private static void BuildFieldSearchText(SettingEntry entry)
        {
            var sb = new StringBuilder(256);
            var visitedHeaders = new HashSet<string>();

            var chain = new List<FieldInfo>();
            CollectFieldsUpChain(entry.type, chain);

            foreach (var field in chain)
            {
                // 跳过不可序列化的私有字段与 static / readonly / const
                if (field.IsPrivate && field.GetCustomAttribute<SerializeField>() == null) continue;
                if (field.IsStatic || field.IsInitOnly || field.IsLiteral) continue;

                sb.AppendLine(NicifyFieldName(field.Name));
                sb.AppendLine(field.Name);

                var tooltip = field.GetCustomAttribute<TooltipAttribute>();
                if (tooltip != null && !string.IsNullOrEmpty(tooltip.tooltip)) sb.AppendLine(tooltip.tooltip);

                var header = field.GetCustomAttribute<HeaderAttribute>();
                if (header != null && !string.IsNullOrEmpty(header.header) && visitedHeaders.Add(header.header))
                    sb.AppendLine(header.header);

                TryAppendOdinLabelText(field, sb);
            }
            entry.fieldSearchText = sb.ToString();
        }

        /// <summary>
        /// 沿继承链向上收集字段，直到遇到 <c>FrameworkSettings&lt;T&gt;</c> 泛型基类或 <c>ScriptableObject</c>；同名字段只保留最子类的版本。
        /// </summary>
        private static void CollectFieldsUpChain(Type type, List<FieldInfo> result)
        {
            var seen = new HashSet<string>();
            var current = type;
            while (current != null && current != typeof(ScriptableObject) && current != typeof(UObject))
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(FrameworkSettings<>))
                    break;

                foreach (var f in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (seen.Add(f.Name)) result.Add(f);
                current = current.BaseType;
            }
        }

        /// <summary>
        /// 通过类型名检测 Odin 的 <c>LabelTextAttribute</c> 并取出 <c>Text</c> 值；项目未安装 Odin 时静默跳过。
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
        /// 去掉 <c>m_</c> / <c>_</c> 前缀后调用 <c>NicifyVariableName</c>，生成带空格的可读名称（<c>m_moveSpeed</c> → <c>Move Speed</c>）。
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

        #region 重建 [REBUILD]

        /// <summary>
        /// 重建前捕获选中条目类型，重建后按类型恢复选中（未创建页 → Create Asset 后定位到新实例）。
        /// </summary>
        protected override void OnBeforeTreeRebuild(object previousSelection)
        {
            _rebuildEntryType = GetSelectedEntry()?.type;
        }

        protected override void OnAfterTreeRebuild(object previousSelection)
        {
            if (_rebuildEntryType != null
                && _entriesByType.TryGetValue(_rebuildEntryType, out SettingEntry entry)
                && entry.Exists
                && MenuTree != null)
            {
                TrySelectMenuItemWithObject(entry.instance);
            }

            _rebuildEntryType = null;
        }

        /// <summary>
        /// 按配置类型选中菜单条目；树未就绪时延迟重试。编程式选中必须走 <c>OdinMenuItem.Select</c>。
        /// </summary>
        private void SelectEntry(Type type)
        {
            if (type == null) return;

            if (MenuTree == null)
            {
                EditorApplication.delayCall += () =>
                {
                    if (this != null) SelectEntry(type);
                };
                return;
            }

            foreach (var item in MenuTree.EnumerateTree())
            {
                bool match = item.Value switch
                {
                    ScriptableObject so => so.GetType() == type,
                    MissingSettingPage page => page.Entry.type == type,
                    _ => false,
                };
                if (!match) continue;

                item.Select(false);
                Repaint();
                return;
            }
        }

        #endregion

        #region 操作 [ACTIONS]

        /// <summary>
        /// 创建或 Ping 配置资产：创建经反射调 <c>Instance</c>（泛型参数 <c>T</c> 编译期未知）。
        /// </summary>
        private void CreateOrPingAsset(SettingEntry entry)
        {
            if (entry.genericBase == null || entry.instanceProp == null)
            {
                Debug.LogWarning($"[FrameworkSettings] Cannot invoke Instance for {entry.type.Name}.");
                return;
            }

            if (entry.Exists)
            {
                EditorGUIUtility.PingObject(entry.instance);
                if (MenuTree != null) TrySelectMenuItemWithObject(entry.instance);
                return;
            }

            if (!(entry.instanceProp.GetValue(null) is ScriptableObject instance))
            {
                Debug.LogWarning($"[FrameworkSettings] Instance returned null for {entry.type.Name}.");
                return;
            }

            EditorGUIUtility.PingObject(instance);
            // Instance 首次调用会在磁盘创建资产
            QueueTreeRebuild();
        }

        /// <summary>
        /// 二次确认后用临时实例 <c>CopySerialized</c> 重置全部序列化字段（等效 Inspector 的 Reset），保留 <c>m_Name</c>。
        /// </summary>
        private void ResetSetting(SettingEntry entry)
        {
            if (!entry.Exists) return;
            if (!EditorUtility.DisplayDialog("Reset Setting",
                $"Reset '{entry.title}' to default values?\n\nThis cannot be undone.", "Reset", "Cancel")) return;

            var temp = ScriptableObject.CreateInstance(entry.type);
            string originalName = entry.instance.name;
            EditorUtility.CopySerialized(temp, entry.instance);
            entry.instance.name = originalName;
            DestroyImmediate(temp);

            EditorUtility.SetDirty(entry.instance);
            AssetDatabase.SaveAssets();

            QueueTreeRebuild();
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
        /// 侧边栏条目右键菜单。已存在的配置显示 Select/Ping/Reset，不存在的显示 Create。
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
