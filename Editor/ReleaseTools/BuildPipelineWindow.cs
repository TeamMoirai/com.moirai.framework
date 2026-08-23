using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YooAsset;
using YooAsset.Editor;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 打包工具窗口。仿 Unity 6 Build Profiles：左侧预设列表 + 右侧配置详情，UI Toolkit 实现。
    /// </summary>
    public class BuildPipelineWindow : EditorWindow
    {
        private static readonly string[] s_PlatformNames = new string[]
        {
            "Windows 64-bit",
            "macOS",
            "Linux",
            "Android",
            "iOS",
            "WebGL",
        };

        private static readonly BuildTarget[] s_PlatformTargets = new BuildTarget[]
        {
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneOSX,
            BuildTarget.StandaloneLinux64,
            BuildTarget.Android,
            BuildTarget.iOS,
            BuildTarget.WebGL,
        };

        private static readonly string[] s_PipelineNames = new string[]
        {
            "ScriptableBuildPipeline (SBP)",
            "LegacyBuildPipeline (内置)",
        };

        private static readonly string[] s_CompressNames = new string[]
        {
            "Uncompressed (不压缩)",
            "LZMA (高压缩)",
            "LZ4 (快速压缩)",
        };

        private static readonly string[] s_CopyOptionNames = new string[]
        {
            "None (不拷贝)",
            "ClearAndCopyAll (清空后拷贝全部)",
            "ClearAndCopyByTags (清空后按Tag拷贝)",
            "OnlyCopyAll (仅拷贝全部)",
            "OnlyCopyByTags (仅按Tag拷贝)",
        };

        private static readonly string[] s_FileNameStyleNames = new string[]
        {
            "HashName (哈希名)",
            "BundleName (资源包名称)",
            "BundleName_HashName (资源包名称 + 哈希值名称)",
        };

        private const string DEFAULT_PRESET_FOLDER = "Assets/Settings/BuildPipeline";

        private class PresetEntry
        {
            public string Guid;
            public string Path;
            public string Name;
        }

        private readonly List<PresetEntry> _presets = new List<PresetEntry>();

        private BuildConfig _config;
        private SerializedObject _serializedConfig;
        private string _selectedGUID;

        // 左侧
        private ListView _listView;

        // 右侧
        private VisualElement _detailRoot;
        private VisualElement _emptyHint;
        private Label _presetNameLabel;

        // 绑定控件
        private PropertyField _encryptorField;
        private PopupField<string> _platformPopup;
        private PopupField<string> _pipelinePopup;
        private PopupField<string> _compressPopup;
        private PopupField<string> _copyOptionPopup;
        private PopupField<string> _fileNameStylePopup;
        private PopupField<string> _playerPlatformPopup;
        private TextField _versionField;
        private TextField _outputRootField;
        private TextField _retainTagsField;
        private TextField _playerOutputField;
        private Toggle _minimalToggle;
        private Toggle _sharePackToggle;
        private Toggle _depDbToggle;
        private Toggle _clearCacheToggle;
        private Toggle _verifyToggle;
        private Toggle _dllToggle;
        private Toggle _buildPlayerToggle;
        private VisualElement _minimalExtras;
        private VisualElement _playerExtras;
        private HelpBox _minimalHelpBox;

        // 日志
        private readonly List<string> _buildLogs = new List<string>();
        private Foldout _logFoldout;
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
            // 恢复上次选中的预设
            string lastGuid = BuildPipelineWindowState.instance.LastSelectedPresetGUID;
            if (!string.IsNullOrEmpty(lastGuid))
                _selectedGUID = lastGuid;

            BuildUI();
            RefreshPresets();
            SyncSelectionAfterRefresh();
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
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
            root.Add(split);

            split.Add(BuildLeftPane());
            split.Add(BuildRightPane());
        }

        private VisualElement BuildLeftPane()
        {
            var left = new VisualElement();

            var toolbar = new Toolbar();
            var createBtn = new ToolbarButton(CreatePreset) { text = "+", tooltip = "新建预设" };
            var refreshBtn = new ToolbarButton(() =>
            {
                RefreshPresets();
                SyncSelectionAfterRefresh();
                AddLog("已刷新预设列表");
            }) { text = "⟳", tooltip = "刷新列表" };
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
            _listView.onSelectionChange += objs =>
            {
                if (objs.FirstOrDefault() is PresetEntry entry)
                    SelectEntry(entry);
            };
            // 双击（或回车）在 Project 窗口中 Ping 该资产
            _listView.onItemsChosen += items =>
            {
                if (items.FirstOrDefault() is PresetEntry entry)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<BuildConfig>(entry.Path);
                    if (asset != null)
                        EditorGUIUtility.PingObject(asset);
                }
            };
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

        private VisualElement BuildRightPane()
        {
            var right = new ScrollView(ScrollViewMode.Vertical);
            right.style.flexGrow = 1;

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
            right.Add(_emptyHint);

            _detailRoot = new VisualElement();
            right.Add(_detailRoot);

            BuildHeader(_detailRoot);
            BuildBasicSection(_detailRoot);
            BuildMinimalSection(_detailRoot);
            BuildAdvancedSection(_detailRoot);
            BuildDllSection(_detailRoot);
            BuildPlayerSection(_detailRoot);
            BuildActionButtons(_detailRoot);
            BuildLogSection(_detailRoot);

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
            SetFixedWidth(refreshBtn, 60);
            var resetBtn = new Button(() =>
            {
                if (_config == null) return;
                ResetCurrentToDefault();
                AddLog($"已重置 {_config.name} 为默认配置");
            }) { text = "重置默认" };
            SetFixedWidth(resetBtn, 80);
            row.Add(_presetNameLabel);
            row.Add(refreshBtn);
            row.Add(resetBtn);
            parent.Add(row);
        }

        private void BuildBasicSection(VisualElement parent)
        {
            var section = MakeFoldout("基础设置", "目标平台、构建管线、加密等核心参数", true);

            _platformPopup = MakePopup("目标平台", s_PlatformNames, name =>
            {
                int idx = Array.IndexOf(s_PlatformNames, name);
                if (idx >= 0 && _config != null) { _config.BuildTarget = s_PlatformTargets[idx]; Dirty(); }
            });

            _pipelinePopup = MakePopup("构建管线", s_PipelineNames, name =>
            {
                int idx = Array.IndexOf(s_PipelineNames, name);
                if (idx >= 0 && _config != null)
                {
                    _config.BuildPipeline = idx == 1
                        ? EBuildPipeline.LegacyBuildPipeline
                        : EBuildPipeline.ScriptableBuildPipeline;
                    Dirty();
                }
            });

            _compressPopup = MakePopup("压缩方式", s_CompressNames, name =>
            {
                int idx = Array.IndexOf(s_CompressNames, name);
                if (idx >= 0 && _config != null) { _config.CompressOption = (ECompressOption)idx; Dirty(); }
            });

            _encryptorField = new PropertyField { label = "加密方式" };

            _versionField = MakeTextField("资源版本号", v => { if (_config != null) { _config.PackageVersion = v; Dirty(); } });
            var versionRow = MakeRow(_versionField);
            var autoBtn = MakeSmallButton("自动", () =>
            {
                if (_config == null) return;
                _config.PackageVersion = BuildConfig.GetDefaultPackageVersion();
                Dirty();
                _versionField.SetValueWithoutNotify(_config.PackageVersion);
            });
            versionRow.Add(autoBtn);

            _outputRootField = MakeTextField("AB输出目录", v => { if (_config != null) { _config.OutputRoot = v; Dirty(); } });
            var outputRow = MakeRow(_outputRootField);
            var browseBtn = MakeSmallButton("浏览", () =>
            {
                if (_config == null) return;
                string selected = EditorUtility.OpenFolderPanel("选择输出目录", _config.OutputRoot, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    string projectPath = PathGetRelative(Application.dataPath + "/../", selected);
                    _config.OutputRoot = string.IsNullOrEmpty(projectPath) ? selected : projectPath;
                    Dirty();
                    _outputRootField.SetValueWithoutNotify(_config.OutputRoot);
                }
            });
            outputRow.Add(browseBtn);

            section.Add(_platformPopup);
            section.Add(_pipelinePopup);
            section.Add(_compressPopup);
            section.Add(_encryptorField);
            section.Add(versionRow);
            section.Add(outputRow);
            section.Add(new HelpBox("选择构建目标平台和基础参数。AB输出目录支持相对路径（相对于项目根目录）。", HelpBoxMessageType.Info));

            parent.Add(section);
        }

        private void BuildMinimalSection(VisualElement parent)
        {
            var section = MakeFoldout("最小包设置", "删除 StreamingAssets 中的 .bundle 文件以减小首包体积", true);

            _minimalToggle = MakeToggle("启用最小包模式", "构建后删除 StreamingAssets 中的 .bundle 文件", v =>
            {
                if (_config == null) return;
                _config.MinimalPackage = v;
                Dirty();
                UpdateExtrasVisibility();
            });

            _retainTagsField = MakeTextField("保留Tag(逗号分隔)", v =>
            {
                if (_config == null) return;
                _config.RetainTags = v;
                Dirty();
                UpdateMinimalHelpBox();
            });
            _retainTagsField.tooltip = "带这些Tag的bundle不会被删除";

            _minimalHelpBox = new HelpBox("", HelpBoxMessageType.Info);

            _minimalExtras = new VisualElement();
            _minimalExtras.Add(_retainTagsField);
            _minimalExtras.Add(_minimalHelpBox);

            section.Add(_minimalToggle);
            section.Add(_minimalExtras);
            parent.Add(section);
        }

        private void BuildAdvancedSection(VisualElement parent)
        {
            var section = MakeFoldout("高级设置", "共享打包、依赖数据库、增量构建等", false);

            _sharePackToggle = MakeToggle("启用共享资源打包", "自动提取共享资源到独立bundle", v =>
            {
                if (_config == null) return;
                _config.EnableSharePackRule = v;
                Dirty();
            });
            _depDbToggle = MakeToggle("使用资源依赖数据库", "提高打包速度", v =>
            {
                if (_config == null) return;
                _config.UseAssetDependencyDB = v;
                Dirty();
            });
            _clearCacheToggle = MakeToggle("清理构建缓存(禁用增量构建)", "全量重新构建", v =>
            {
                if (_config == null) return;
                _config.ClearBuildCache = v;
                Dirty();
            });
            _verifyToggle = MakeToggle("验证构建结果", "构建后验证资源完整性", v =>
            {
                if (_config == null) return;
                _config.VerifyBuildingResult = v;
                Dirty();
            });

            _copyOptionPopup = MakePopup("内置文件拷贝", s_CopyOptionNames, name =>
            {
                int idx = Array.IndexOf(s_CopyOptionNames, name);
                if (idx >= 0 && _config != null) { _config.BundledCopyOption = (EBundledCopyOption)idx; Dirty(); }
            });
            _fileNameStylePopup = MakePopup("文件名风格", s_FileNameStyleNames, name =>
            {
                int idx = Array.IndexOf(s_FileNameStyleNames, name);
                if (idx >= 0 && _config != null) { _config.FileNameStyle = (EFileNameStyle)idx; Dirty(); }
            });

            section.Add(_sharePackToggle);
            section.Add(_depDbToggle);
            section.Add(_clearCacheToggle);
            section.Add(_verifyToggle);
            section.Add(_copyOptionPopup);
            section.Add(_fileNameStylePopup);
            parent.Add(section);
        }

        private void BuildDllSection(VisualElement parent)
        {
            var section = MakeFoldout("热更DLL设置", "HybridCLR 热更程序集编译", true);

            _dllToggle = MakeToggle("构建前编译热更DLL", "执行 BuildDLLCommand.BuildAndCopyDlls", v =>
            {
                if (_config == null) return;
                _config.BuildHotFixDll = v;
                Dirty();
            });

            section.Add(_dllToggle);
            parent.Add(section);
        }

        private void BuildPlayerSection(VisualElement parent)
        {
            var section = MakeFoldout("打包Player设置", "构建可执行程序", false);

            _buildPlayerToggle = MakeToggle("构建Player", "构建可执行程序(exe/apk/ipa)", v =>
            {
                if (_config == null) return;
                _config.BuildPlayer = v;
                Dirty();
                UpdateExtrasVisibility();
            });

            _playerPlatformPopup = MakePopup("Player平台", s_PlatformNames, name =>
            {
                int idx = Array.IndexOf(s_PlatformNames, name);
                if (idx >= 0 && _config != null) { _config.PlayerPlatform = s_PlatformTargets[idx]; Dirty(); }
            });

            _playerOutputField = MakeTextField("输出路径", v =>
            {
                if (_config != null) { _config.PlayerOutputPath = v; Dirty(); }
            });
            var outputRow = MakeRow(_playerOutputField);
            var browseBtn = MakeSmallButton("浏览", () =>
            {
                if (_config == null) return;
                string selected = EditorUtility.SaveFilePanel("选择输出路径",
                    Path.GetDirectoryName(_config.PlayerOutputPath),
                    Path.GetFileName(_config.PlayerOutputPath), "");
                if (!string.IsNullOrEmpty(selected))
                {
                    _config.PlayerOutputPath = selected;
                    Dirty();
                    _playerOutputField.SetValueWithoutNotify(selected);
                }
            });
            outputRow.Add(browseBtn);

            _playerExtras = new VisualElement();
            _playerExtras.Add(_playerPlatformPopup);
            _playerExtras.Add(outputRow);

            section.Add(_buildPlayerToggle);
            section.Add(_playerExtras);
            parent.Add(section);
        }

        private void BuildActionButtons(VisualElement parent)
        {
            parent.Add(MakeSeparator());

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };

            var abBtn = MakeBuildButton("构建 AssetBundle", () => ExecuteBuild(CloneConfig(_config), buildPlayer: false));
            var playerBtn = MakeBuildButton("构建 Player", ExecuteBuildPlayerOnly);
            row.Add(abBtn);
            row.Add(playerBtn);
            parent.Add(row);

            var fullBtn = new Button(() =>
            {
                var copy = CloneConfig(_config);
                copy.BuildPlayer = true;
                ExecuteBuild(copy, buildPlayer: true);
            }) { text = "一键构建 (AB + Player)" };
            fullBtn.style.height = 38;
            fullBtn.style.fontSize = 13;
            fullBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            fullBtn.style.unityBackgroundImageTintColor = new Color(0.2f, 0.6f, 1f);
            parent.Add(fullBtn);
        }

        private void BuildLogSection(VisualElement parent)
        {
            _logFoldout = new Foldout { text = "构建日志 (0)", tooltip = "构建过程的日志输出", value = false };

            var clearBtn = new Button(() =>
            {
                _buildLogs.Clear();
                RebuildLogUI();
            }) { text = "清空日志" };
            SetFixedHeight(clearBtn, 22);

            _logScroll = new ScrollView(ScrollViewMode.Vertical) { style = { height = 150 } };

            _logFoldout.Add(clearBtn);
            _logFoldout.Add(_logScroll);
            parent.Add(_logFoldout);
        }

        #endregion

        #region UI 工具 [UI HELPERS]

        private static Foldout MakeFoldout(string title, string tooltip, bool expanded)
        {
            return new Foldout { text = title, tooltip = tooltip, value = expanded };
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

        private static VisualElement MakeRow(VisualElement content)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            content.style.flexGrow = 1;
            row.Add(content);
            return row;
        }

        private static Button MakeSmallButton(string text, Action clicked)
        {
            var btn = new Button(clicked) { text = text };
            SetFixedWidth(btn, 50);
            btn.style.marginLeft = 4;
            return btn;
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

        private static void SetFixedWidth(VisualElement el, float width) => el.style.width = width;
        private static void SetFixedHeight(VisualElement el, float height) => el.style.height = height;

        private static PopupField<string> MakePopup(string label, string[] choices, Action<string> onSet)
        {
            var popup = new PopupField<string>(label, new List<string>(choices), 0);
            popup.RegisterValueChangedCallback(evt => onSet(evt.newValue));
            return popup;
        }

        private static TextField MakeTextField(string label, Action<string> onSet)
        {
            var field = new TextField(label);
            field.RegisterValueChangedCallback(evt => onSet(evt.newValue));
            return field;
        }

        private static Toggle MakeToggle(string label, string tooltip, Action<bool> onSet)
        {
            var toggle = new Toggle(label) { tooltip = tooltip };
            toggle.RegisterValueChangedCallback(evt => onSet(evt.newValue));
            return toggle;
        }

        private void Dirty()
        {
            if (_config != null)
                EditorUtility.SetDirty(_config);
        }

        private void UpdateExtrasVisibility()
        {
            bool minimal = _config != null && _config.MinimalPackage;
            bool player = _config != null && _config.BuildPlayer;
            _minimalExtras.style.display = minimal ? DisplayStyle.Flex : DisplayStyle.None;
            _playerExtras.style.display = player ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateMinimalHelpBox()
        {
            if (_config == null) return;

            string tagInfo = string.IsNullOrWhiteSpace(_config.RetainTags)
                ? "所有 .bundle 文件将被删除（仅保留清单）"
                : $"保留带 [{_config.RetainTags}] Tag 的 bundle，其余删除";

            _minimalHelpBox.text =
                $"最小包模式：删除 StreamingAssets 中所有 .bundle 文件，仅保留清单文件（.bytes/.hash/.version）。\n" +
                $"当前: {tagInfo}\n\n" +
                $"适用于 HostPlayMode 在线下载资源的场景，可大幅减小首包体积。";
        }

        private void RefreshControlsFromConfig()
        {
            _presetNameLabel.text = $"当前预设: {_config.name}";

            _platformPopup.SetValueWithoutNotify(PlatformNameFromTarget(_config.BuildTarget));
            _pipelinePopup.SetValueWithoutNotify(s_PipelineNames[_config.BuildPipeline == EBuildPipeline.LegacyBuildPipeline ? 1 : 0]);
            _compressPopup.SetValueWithoutNotify(s_CompressNames[(int)_config.CompressOption]);
            _versionField.SetValueWithoutNotify(_config.PackageVersion);
            _outputRootField.SetValueWithoutNotify(_config.OutputRoot);

            _minimalToggle.SetValueWithoutNotify(_config.MinimalPackage);
            _retainTagsField.SetValueWithoutNotify(_config.RetainTags);

            _sharePackToggle.SetValueWithoutNotify(_config.EnableSharePackRule);
            _depDbToggle.SetValueWithoutNotify(_config.UseAssetDependencyDB);
            _clearCacheToggle.SetValueWithoutNotify(_config.ClearBuildCache);
            _verifyToggle.SetValueWithoutNotify(_config.VerifyBuildingResult);
            _copyOptionPopup.SetValueWithoutNotify(s_CopyOptionNames[(int)_config.BundledCopyOption]);
            _fileNameStylePopup.SetValueWithoutNotify(s_FileNameStyleNames[(int)_config.FileNameStyle]);

            _dllToggle.SetValueWithoutNotify(_config.BuildHotFixDll);

            _buildPlayerToggle.SetValueWithoutNotify(_config.BuildPlayer);
            _playerPlatformPopup.SetValueWithoutNotify(PlatformNameFromTarget(_config.PlayerPlatform));
            _playerOutputField.SetValueWithoutNotify(_config.PlayerOutputPath);

            UpdateMinimalHelpBox();
            UpdateExtrasVisibility();
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
            var config = AssetDatabase.LoadAssetAtPath<BuildConfig>(entry.Path);
            BindConfig(config);

            BuildPipelineWindowState.instance.SetLastSelectedPresetGUID(_selectedGUID);
        }

        private void BindConfig(BuildConfig config)
        {
            _config = config;
            _serializedConfig = config != null ? new SerializedObject(config) : null;

            var handlerProp = _serializedConfig?.FindProperty("m_EncryptorHandler");
            if (handlerProp != null)
                _encryptorField.BindProperty(handlerProp);
            else
                _encryptorField.Unbind();

            _detailRoot.style.display = config != null ? DisplayStyle.Flex : DisplayStyle.None;
            _emptyHint.style.display = config != null ? DisplayStyle.None : DisplayStyle.Flex;

            if (config != null)
                RefreshControlsFromConfig();
        }

        private void ClearSelection()
        {
            _selectedGUID = null;
            BindConfig(null);
            BuildPipelineWindowState.instance.SetLastSelectedPresetGUID("");
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

            _serializedConfig = new SerializedObject(_config);
            var handlerProp = _serializedConfig.FindProperty("m_EncryptorHandler");
            if (handlerProp != null)
                _encryptorField.BindProperty(handlerProp);
            RefreshControlsFromConfig();
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

        private void ExecuteBuild(BuildConfig config, bool buildPlayer)
        {
            _buildLogs.Clear();
            RebuildLogUI();
            AddLog("========== 开始构建 ==========");
            AddLog($"平台: {config.BuildTarget} | 管线: {config.BuildPipeline} | 最小包: {config.MinimalPackage}");

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
                    config.BuildPlayer = false;
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

            _logFoldout.value = true;
        }

        private void ExecuteBuildPlayerOnly()
        {
            if (_config == null) return;

            _buildLogs.Clear();
            RebuildLogUI();
            AddLog("========== 仅构建 Player ==========");
            AddLog($"平台: {_config.PlayerPlatform} | 输出: {_config.PlayerOutputPath}");

            try
            {
                Application.logMessageReceived += OnBuildLogReceived;
                ReleaseTools.BuildImp(
                    BuildConfig.GetBuildTargetGroup(_config.PlayerPlatform),
                    _config.PlayerPlatform,
                    _config.PlayerOutputPath
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

            _logFoldout.value = true;
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

        private void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _buildLogs.Add($"[{timestamp}] {message}");
            RebuildLogUI();
        }

        private void RebuildLogUI()
        {
            _logScroll.Clear();
            foreach (var log in _buildLogs)
            {
                var label = new Label(log);
                label.style.whiteSpace = WhiteSpace.Normal;
                _logScroll.Add(label);
            }

            _logFoldout.text = $"构建日志 ({_buildLogs.Count})";
            _logScroll.schedule.Execute(() => _logScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        #endregion

        #region 工具方法 [UTILITY METHODS]

        private static string PlatformNameFromTarget(BuildTarget target)
        {
            for (int i = 0; i < s_PlatformTargets.Length; i++)
                if (s_PlatformTargets[i] == target)
                    return s_PlatformNames[i];

            // 未知平台回退到当前激活平台
            BuildTarget active = EditorUserBuildSettings.activeBuildTarget;
            for (int i = 0; i < s_PlatformTargets.Length; i++)
                if (s_PlatformTargets[i] == active)
                    return s_PlatformNames[i];

            return s_PlatformNames[0];
        }

        private static string PathGetRelative(string relativeTo, string path)
        {
            try
            {
                var uri = new Uri(relativeTo + "/");
                var rel = Uri.UnescapeDataString(uri.MakeRelativeUri(new Uri(path)).ToString());
                return rel.Replace('/', '\\');
            }
            catch
            {
                return "";
            }
        }

        private static BuildConfig CloneConfig(BuildConfig source)
        {
            var clone = CreateInstance<BuildConfig>();
            EditorUtility.CopySerialized(source, clone);
            return clone;
        }

        #endregion

        #region 窗口状态持久化 [WINDOW STATE]

        [FilePath("ProjectSettings/BuildPipelineWindow.asset", FilePathAttribute.Location.ProjectFolder)]
        private sealed class BuildPipelineWindowState : ScriptableSingleton<BuildPipelineWindowState>
        {
            [SerializeField] private string m_LastSelectedPresetGUID = "";

            public string LastSelectedPresetGUID => m_LastSelectedPresetGUID;

            public void SetLastSelectedPresetGUID(string guid)
            {
                m_LastSelectedPresetGUID = guid;
                Save(true);
            }
        }

        #endregion
    }
}
