using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 打包工具窗口。仿 Unity 6 Build Profiles：左侧预设列表 + 右侧配置详情，UI Toolkit 布局。
    /// <para>配置字段绘制由窗口内嵌的 Odin PropertyTree 负责（<see cref="BuildConfig"/> 上的 Odin 特性驱动；
    /// BuildConfig 位于编辑器程序集，Odin 默认编辑器不接管该类，必须显式建树绘制）。本窗口只负责预设管理与构建执行。</para>
    /// <para>注意：不要继承 OdinEditorWindow——本窗口自身无 Odin 成员可绘，OdinEditorWindow 会向根元素
    /// 注入一个不可见但参与布局的空属性树 IMGUIContainer（挤占窗口高度），且无法可靠隐藏（Odin 会复显）。</para>
    /// </summary>
    public class BuildPipelineWindow : EditorWindow
    {
        private const string DEFAULT_PRESET_FOLDER = "Assets/Settings/BuildPipeline";

        private class PresetEntry
        {
            public string Guid;
            public string Path;
            public string Name;
        }

        private readonly List<PresetEntry> _presets = new List<PresetEntry>();

        private BuildConfig _config;
        private string _selectedGUID;
        private SerializedObject _configSerializedObject;
        private PropertyTree _configTree;

        // 左侧
        private ListView _listView;

        // 右侧
        private VisualElement _detailRoot;
        private VisualElement _emptyHint;
        private VisualElement _configEditorContainer;
        private Label _presetNameLabel;

        // 日志
        private readonly List<string> _buildLogs = new List<string>();
        private Label _logTitleLabel;
        private ScrollView _logScroll;

        [MenuItem("Tools/Build/打包工具窗口", false, 30)]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildPipelineWindow>("打包工具");
            window.minSize = new Vector2(1080, 720);
        }

        #region 生命周期 [LIFECYCLE]

        private void OnEnable()
        {
            BuildUI();
            RefreshPresets();
            SyncSelectionAfterRefresh();
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            DisposeConfigTree();
        }

        private void OnProjectChanged()
        {
            RefreshPresets();
            SyncSelectionAfterRefresh();
        }

        #endregion

        #region UI 构建 [UI BUILD]

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();

            var split = new TwoPaneSplitView(0, 220, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            root.Add(split);

            split.Add(BuildLeftPane());
            split.Add(BuildRightPane());
        }

        private VisualElement BuildLeftPane()
        {
            var left = new VisualElement();

            var toolbar = new Toolbar();

            var createBtn = new ToolbarButton(CreatePreset) { tooltip = "新建预设", focusable = false };
            createBtn.Add(MakeToolbarIcon("CreateAddNew"));

            var refreshBtn = new ToolbarButton(() =>
            {
                RefreshPresets();
                SyncSelectionAfterRefresh();
                AddLog("已刷新预设列表");
            }) { tooltip = "刷新列表", focusable = false };
            refreshBtn.Add(MakeToolbarIcon("Refresh"));

            toolbar.Add(createBtn);
            toolbar.Add(refreshBtn);
            left.Add(toolbar);

            _listView = new ListView(_presets, 22,
                () => new Label
                {
                    style =
                    {
                        unityTextAlign = TextAnchor.MiddleLeft,
                        flexGrow = 1,
                        paddingLeft = 6,
                    }
                },
                (el, i) =>
                {
                    var label = (Label)el;
                    label.text = _presets[i].Name;
                    label.tooltip = _presets[i].Path;
                })
            {
                selectionType = SelectionType.Single,
            };
            // ListView 事件名在 Unity 2023.1 重命名（onSelectionChange→selectionChanged、onItemsChosen→itemsChosen）
#if UNITY_2023_1_OR_NEWER
            _listView.selectionChanged += OnListViewSelectionChanged;
            _listView.itemsChosen += OnListViewItemsChosen;
#else
            _listView.onSelectionChange += OnListViewSelectionChanged;
            _listView.onItemsChosen += OnListViewItemsChosen;
#endif
            _listView.style.flexGrow = 1;
            left.Add(_listView);

            var bottomBar = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var dupBtn = new Button(DuplicatePreset) { text = "复制" };
            var delBtn = new Button(DeletePreset) { text = "删除" };
            dupBtn.style.flexGrow = 1;
            delBtn.style.flexGrow = 1;
            bottomBar.Add(dupBtn);
            bottomBar.Add(delBtn);
            left.Add(bottomBar);

            return left;
        }

        private void OnListViewSelectionChanged(IEnumerable<object> objs)
        {
            if (objs.FirstOrDefault() is PresetEntry entry)
                SelectEntry(entry);
        }

        // 双击（或回车）在 Project 窗口中 Ping 该资产
        private void OnListViewItemsChosen(IEnumerable<object> items)
        {
            if (items.FirstOrDefault() is PresetEntry entry)
            {
                var asset = AssetDatabase.LoadAssetAtPath<BuildConfig>(entry.Path);
                if (asset != null)
                    EditorGUIUtility.PingObject(asset);
            }
        }

        private VisualElement BuildRightPane()
        {
            // 右侧面板 = 上部滚动区（预设详情） + 下部固定 Console 日志组
            var right = new VisualElement { style = { flexGrow = 1 } };

            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            right.Add(scroll);

            // 空提示
            _emptyHint = new Label("请在左侧选择或创建一个 BuildConfig 预设")
            {
                style =
                {
                    alignSelf = Align.Center,
                    unityFontStyleAndWeight = FontStyle.Italic,
                    height = Length.Percent(100),
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };
            scroll.Add(_emptyHint);

            _detailRoot = new VisualElement();
            scroll.Add(_detailRoot);

            BuildHeader(_detailRoot);

            // BuildConfig 字段绘制由窗口内嵌的 Odin PropertyTree 负责
            _configEditorContainer = new VisualElement();
            _detailRoot.Add(_configEditorContainer);

            BuildActionButtons(_detailRoot);

            BuildLogSection(right);

            return right;
        }

        private void BuildHeader(VisualElement parent)
        {
            var title = new Label("打包工具")
            {
                style =
                {
                    fontSize = 16,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    alignSelf = Align.Center,
                    marginTop = 5,
                    height = 30,
                }
            };
            parent.Add(title);
            parent.Add(MakeSeparator());

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _presetNameLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, alignSelf = Align.Center } };
            _presetNameLabel.style.flexGrow = 1;
            var refreshBtn = new Button(() =>
            {
                RefreshPresets();
                SyncSelectionAfterRefresh();
                AddLog("已刷新预设列表");
            }) { text = "刷新" };
            refreshBtn.style.width = 60;
            var resetBtn = new Button(() =>
            {
                if (_config == null) return;
                ResetCurrentToDefault();
                AddLog($"已重置 {_config.name} 为默认配置");
            }) { text = "重置默认" };
            resetBtn.style.width = 80;
            row.Add(_presetNameLabel);
            row.Add(refreshBtn);
            row.Add(resetBtn);
            parent.Add(row);
        }

        private void BuildActionButtons(VisualElement parent)
        {
            parent.Add(MakeSeparator());

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(MakeBuildButton("构建 AssetBundle", ExecuteBuildAB));
            row.Add(MakeBuildButton("构建 Player", ExecuteBuildPlayerOnly));
            parent.Add(row);

            var fullBtn = new Button(ExecuteBuildAll) { text = "一键构建 (AB + Player)" };
            fullBtn.style.height = 38;
            fullBtn.style.fontSize = 13;
            fullBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            fullBtn.style.unityBackgroundImageTintColor = new Color(0.2f, 0.6f, 1f);
            parent.Add(fullBtn);
        }

        /// <summary>
        /// 底部固定 Console 日志组：不参与右侧滚动区，始终可见。
        /// </summary>
        private void BuildLogSection(VisualElement parent)
        {
            var console = new VisualElement
            {
                style =
                {
                    height = 200,
                    flexShrink = 0,
                    borderTopWidth = 1,
                    borderTopColor = new Color(0f, 0f, 0f, 0.4f),
                    backgroundColor = new Color(0f, 0f, 0f, 0.15f),
                }
            };

            var titleRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginTop = 4,
                    marginBottom = 2,
                    paddingLeft = 4,
                    paddingRight = 4,
                }
            };
            _logTitleLabel = new Label("构建日志 (0)")
            {
                tooltip = "构建过程的日志输出",
                style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 }
            };
            var clearBtn = new Button(() =>
            {
                _buildLogs.Clear();
                RebuildLogUI();
            }) { text = "清空" };
            clearBtn.style.width = 50;
            clearBtn.style.height = 20;
            titleRow.Add(_logTitleLabel);
            titleRow.Add(clearBtn);
            console.Add(titleRow);

            _logScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                style = { flexGrow = 1, paddingLeft = 4, paddingRight = 4 }
            };
            console.Add(_logScroll);

            parent.Add(console);
        }

        #endregion

        #region UI 工具 [UI HELPERS]

        private static Image MakeToolbarIcon(string iconName)
        {
            var icon = EditorGUIUtility.IconContent(iconName).image as Texture2D;
            return new Image
            {
                image = icon,
                style =
                {
                    width = 16,
                    height = 16,
                }
            };
        }

        private static VisualElement MakeSeparator()
        {
            var sep = new VisualElement
            {
                style =
                {
                    height = 1,
                    backgroundColor = new Color(0f, 0f, 0f, 0.3f),
                    marginTop = 5,
                    marginBottom = 5,
                }
            };
            return sep;
        }

        private static Button MakeBuildButton(string text, Action clicked)
        {
            var btn = new Button(clicked) { text = text };
            btn.style.flexGrow = 1;
            btn.style.height = 35;
            btn.style.fontSize = 13;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            return btn;
        }

        #endregion

        #region 预设管理 [PRESET MANAGEMENT]

        private void RefreshPresets()
        {
            _presets.Clear();

            string[] guids = AssetDatabase.FindAssets("t:" + typeof(BuildConfig));
            var entries = new List<PresetEntry>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    continue;

                entries.Add(new PresetEntry
                {
                    Guid = guid,
                    Path = path,
                    Name = Path.GetFileNameWithoutExtension(path),
                });
            }

            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            _presets.AddRange(entries);

            _listView.Rebuild();
        }

        private void SyncSelectionAfterRefresh()
        {
            int idx = _presets.FindIndex(p => p.Guid == _selectedGUID);
            if (idx < 0) idx = _presets.Count > 0 ? 0 : -1;

            if (idx >= 0)
            {
                _listView.SetSelection(idx); // 触发 onSelectionChange -> SelectEntry
            }
            else
            {
                ClearSelection();
            }
        }

        private void SelectEntry(PresetEntry entry)
        {
            if (entry == null) return;

            _selectedGUID = entry.Guid;
            BindConfig(AssetDatabase.LoadAssetAtPath<BuildConfig>(entry.Path));
        }

        private void BindConfig(BuildConfig config)
        {
            _config = config;

            DisposeConfigTree();
            _configEditorContainer.Clear();

            _detailRoot.style.display = config != null ? DisplayStyle.Flex : DisplayStyle.None;
            _emptyHint.style.display = config != null ? DisplayStyle.None : DisplayStyle.Flex;

            if (config == null) return;

            _presetNameLabel.text = $"当前预设: {config.name}";

            // 显式创建 Odin PropertyTree（绑定 SerializedObject，撤销/脏标记由树处理），经 IMGUIContainer 托管嵌入 UITK 布局
            _configSerializedObject = new SerializedObject(config);
            _configTree = PropertyTree.Create(_configSerializedObject);
            _configEditorContainer.Add(new IMGUIContainer(DrawConfigTree));
        }

        /// <summary>
        /// IMGUIContainer 回调：绘制当前预设的 Odin 属性树。
        /// </summary>
        private void DrawConfigTree()
        {
            _configTree?.Draw();
        }

        private void DisposeConfigTree()
        {
            _configTree?.Dispose();
            _configTree = null;
            _configSerializedObject?.Dispose();
            _configSerializedObject = null;
        }

        private void ClearSelection()
        {
            _selectedGUID = null;
            BindConfig(null);
        }

        private void CreatePreset()
        {
            EnsureFolder(DEFAULT_PRESET_FOLDER);

            // 固定目录直接创建，自动编号（NewBuildConfig、NewBuildConfig 1 ...）
            string path = AssetDatabase.GenerateUniqueAssetPath($"{DEFAULT_PRESET_FOLDER}/NewBuildConfig.asset");

            var config = BuildConfig.CreateDefault();
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();

            _selectedGUID = AssetDatabase.AssetPathToGUID(path);
            RefreshPresets();
            SyncSelectionAfterRefresh();
            AddLog($"已创建预设: {Path.GetFileNameWithoutExtension(path)}");
        }

        private void DuplicatePreset()
        {
            var entry = _presets.FirstOrDefault(p => p.Guid == _selectedGUID);
            if (entry == null) return;

            string dstName = Path.GetFileNameWithoutExtension(entry.Path) + " Copy";
            string dstPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{Path.GetDirectoryName(entry.Path)}/{dstName}.asset".Replace('\\', '/'));

            if (AssetDatabase.CopyAsset(entry.Path, dstPath))
            {
                AssetDatabase.SaveAssets();
                _selectedGUID = AssetDatabase.AssetPathToGUID(dstPath);
                RefreshPresets();
                SyncSelectionAfterRefresh();
                AddLog($"已复制预设: {Path.GetFileNameWithoutExtension(dstPath)}");
            }
            else
            {
                AddLog("[错误] 复制预设失败");
            }
        }

        private void DeletePreset()
        {
            var entry = _presets.FirstOrDefault(p => p.Guid == _selectedGUID);
            if (entry == null) return;

            string name = Path.GetFileNameWithoutExtension(entry.Path);
            if (!EditorUtility.DisplayDialog("删除预设", $"确定删除预设 [{name}] ？\n{entry.Path}", "删除", "取消"))
                return;

            if (AssetDatabase.DeleteAsset(entry.Path))
            {
                AssetDatabase.SaveAssets();
                _selectedGUID = null;
                RefreshPresets();
                SyncSelectionAfterRefresh();
                AddLog($"已删除预设: {name}");
            }
            else
            {
                AddLog("[错误] 删除预设失败");
            }
        }

        private void ResetCurrentToDefault()
        {
            if (_config == null) return;

            var def = BuildConfig.CreateDefault();
            string assetName = _config.name;
            Undo.RecordObject(_config, "Reset BuildConfig to default");
            EditorUtility.CopySerialized(def, _config);
            _config.name = assetName; // CopySerialized 会覆盖 m_Name，需保留资产名
            DestroyImmediate(def);
            EditorUtility.SetDirty(_config);

            // 重建编辑器 UI 以反映重置后的值
            BindConfig(_config);
        }

        private static void EnsureFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets"))
                return;

            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        #endregion

        #region 构建执行 [BUILD EXECUTION]

        /// <summary>仅构建 AssetBundle（克隆配置执行，不污染预设资产）。</summary>
        private void ExecuteBuildAB()
        {
            if (_config == null) return;
            var copy = CloneConfig(_config);
            copy.m_BuildPlayer = false;
            ExecuteBuild(copy, buildPlayer: false);
        }

        /// <summary>一键构建 AB + Player。</summary>
        private void ExecuteBuildAll()
        {
            if (_config == null) return;
            var copy = CloneConfig(_config);
            copy.m_BuildPlayer = true;
            ExecuteBuild(copy, buildPlayer: true);
        }

        private void ExecuteBuild(BuildConfig config, bool buildPlayer)
        {
            if (config == null) return;

            _buildLogs.Clear();
            RebuildLogUI();
            AddLog("========== 开始构建 ==========");
            AddLog($"平台: {config.m_BuildTarget} | 管线: {config.m_BuildPipeline} | 最小包: {config.m_MinimalPackage}");

            if (string.IsNullOrWhiteSpace(config.PackageVersion))
            {
                config.PackageVersion = BuildConfig.GetDefaultPackageVersion();
                AddLog($"版本号为空，自动生成: {config.PackageVersion}");
            }

            try
            {
                Application.logMessageReceived += OnBuildLogReceived;

                if (buildPlayer)
                {
                    ReleaseTools.BuildWithConfig(config, buildPlayer: true);
                }
                else
                {
                    // 仅构建AB，不走Player
                    config.m_BuildPlayer = false;
                    ReleaseTools.BuildWithConfig(config, buildPlayer: false);
                }

                AddLog("========== 构建完成 ==========");
            }
            catch (Exception e)
            {
                AddLog($"[错误] {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                Application.logMessageReceived -= OnBuildLogReceived;
            }

        }

        private void ExecuteBuildPlayerOnly()
        {
            if (_config == null) return;

            _buildLogs.Clear();
            RebuildLogUI();
            AddLog("========== 仅构建 Player ==========");
            AddLog($"平台: {_config.m_PlayerPlatform} | 输出: {_config.m_PlayerOutputPath}");

            try
            {
                Application.logMessageReceived += OnBuildLogReceived;
                ReleaseTools.BuildImp(
                    BuildConfig.GetBuildTargetGroup(_config.m_PlayerPlatform),
                    _config.m_PlayerPlatform,
                    _config.m_PlayerOutputPath
                );
                AddLog("========== Player 构建完成 ==========");
            }
            catch (Exception e)
            {
                AddLog($"[错误] {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                Application.logMessageReceived -= OnBuildLogReceived;
            }

        }

        private void OnBuildLogReceived(string condition, string stackTrace, LogType type)
        {
            string prefix = type switch
            {
                LogType.Error => "[ERR]",
                LogType.Warning => "[WARN]",
                LogType.Assert => "[ASSERT]",
                _ => ""
            };

            if (!string.IsNullOrEmpty(prefix) || condition.StartsWith("[") || condition.Contains("构建") || condition.Contains("Build"))
            {
                AddLog($"{prefix}{condition}");
            }
        }

        /// <summary>追加一条日志：增量添加 Label（避免全量重建），并滚动到底部。</summary>
        private void AddLog(string message)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _buildLogs.Add(entry);

            var label = new Label(entry) { style = { whiteSpace = WhiteSpace.Normal } };
            _logScroll.Add(label);
            _logTitleLabel.text = $"构建日志 ({_buildLogs.Count})";
            _logScroll.schedule.Execute(() => _logScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        private void RebuildLogUI()
        {
            _logScroll.Clear();
            _logTitleLabel.text = $"构建日志 ({_buildLogs.Count})";
        }

        #endregion

        #region 工具方法 [UTILITY METHODS]

        private static BuildConfig CloneConfig(BuildConfig source)
        {
            var clone = CreateInstance<BuildConfig>();
            EditorUtility.CopySerialized(source, clone);
            return clone;
        }

        #endregion
    }
}
