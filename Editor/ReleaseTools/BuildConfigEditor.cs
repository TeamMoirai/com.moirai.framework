using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YooAsset;
using YooAsset.Editor;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// <see cref="BuildConfig"/> 的自定义 Inspector（UI Toolkit）。
    /// <para>同时服务于 Project Inspector 与 <see cref="BuildPipelineWindow"/> 内嵌面板，
    /// 是 BuildConfig 字段绘制的唯一实现（单一职责）。</para>
    /// <para>YooAsset 枚举无本地化标签，各分节的中文显示名在此集中维护。</para>
    /// </summary>
    [CustomEditor(typeof(BuildConfig))]
    internal sealed class BuildConfigEditor : UnityEditor.Editor
    {
        #region 显示名映射 [DISPLAY NAMES]

        private static readonly string[] s_PlatformNames =
        {
            "Windows 64-bit",
            "macOS",
            "Linux",
            "Android",
            "iOS",
            "WebGL",
        };

        private static readonly BuildTarget[] s_PlatformTargets =
        {
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneOSX,
            BuildTarget.StandaloneLinux64,
            BuildTarget.Android,
            BuildTarget.iOS,
            BuildTarget.WebGL,
        };

        private static readonly string[] s_PipelineNames =
        {
            "ScriptableBuildPipeline (SBP)",
            "LegacyBuildPipeline (内置)",
        };

        private static readonly string[] s_CompressNames =
        {
            "Uncompressed (不压缩)",
            "LZMA (高压缩)",
            "LZ4 (快速压缩)",
        };

        private static readonly string[] s_CopyOptionNames =
        {
            "None (不拷贝)",
            "ClearAndCopyAll (清空后拷贝全部)",
            "ClearAndCopyByTags (清空后按Tag拷贝)",
            "OnlyCopyAll (仅拷贝全部)",
            "OnlyCopyByTags (仅按Tag拷贝)",
        };

        private static readonly string[] s_FileNameStyleNames =
        {
            "HashName (哈希名)",
            "BundleName (资源包名称)",
            "BundleName_HashName (资源包名称 + 哈希值名称)",
        };

        #endregion

        private BuildConfig cfg => (BuildConfig)target;

        // 手动同步值的控件：popup/字段回调直写 SO，配置重建或重置后需 RefreshControlsFromConfig 刷新显示
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

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            BuildBasicSection(root);
            BuildMinimalSection(root);
            BuildAdvancedSection(root);
            BuildDllSection(root);
            BuildPlayerSection(root);

            RefreshControlsFromConfig();

            // Inspector 上下文由 InspectorElement 自动绑定；窗口内嵌上下文无人绑定，
            // 此处显式绑定（Bind 幂等，两种上下文均安全）——否则 PropertyField(加密方式)空白。
            root.Bind(serializedObject);
            return root;
        }

        #region UI 构建 [UI BUILD]

        private void BuildBasicSection(VisualElement parent)
        {
            var section = MakeFoldout("basic", "基础设置", "目标平台、构建管线、加密等核心参数", true);

            _platformPopup = MakePopup("目标平台", s_PlatformNames, name =>
            {
                int idx = Array.IndexOf(s_PlatformNames, name);
                if (idx >= 0) Apply("Set Build Target", () => cfg.BuildTarget = s_PlatformTargets[idx]);
            });

            _pipelinePopup = MakePopup("构建管线", s_PipelineNames, name =>
            {
                int idx = Array.IndexOf(s_PipelineNames, name);
                if (idx >= 0)
                    Apply("Set Build Pipeline", () => cfg.BuildPipeline = idx == 1
                        ? EBuildPipeline.LegacyBuildPipeline
                        : EBuildPipeline.ScriptableBuildPipeline);
            });

            _compressPopup = MakePopup("压缩方式", s_CompressNames, name =>
            {
                int idx = Array.IndexOf(s_CompressNames, name);
                if (idx >= 0) Apply("Set Compress Option", () => cfg.CompressOption = (ECompressOption)idx);
            });

            // 加密方式：抽象类型 [SerializeReference]，经 ProviderDropdown drawer 绘制，走 UITK 绑定
            var encryptorField = new PropertyField(
                serializedObject.FindProperty("m_EncryptorHandler"), "加密方式");

            _versionField = MakeTextField("资源版本号", v => Apply("Set Package Version", () => cfg.PackageVersion = v));
            var versionRow = MakeRow(_versionField);
            versionRow.Add(MakeSmallButton("自动", () =>
            {
                Apply("Set Package Version", () => cfg.PackageVersion = BuildConfig.GetDefaultPackageVersion());
                _versionField.SetValueWithoutNotify(cfg.PackageVersion);
            }));

            _outputRootField = MakeTextField("AB输出目录", v => Apply("Set Output Root", () => cfg.OutputRoot = v));
            var outputRow = MakeRow(_outputRootField);
            outputRow.Add(MakeSmallButton("浏览", () =>
            {
                string selected = EditorUtility.OpenFolderPanel("选择输出目录", cfg.OutputRoot, "");
                if (string.IsNullOrEmpty(selected)) return;

                string rel = PathGetRelative(Application.dataPath + "/../", selected);
                Apply("Set Output Root", () => cfg.OutputRoot = string.IsNullOrEmpty(rel) ? selected : rel);
                _outputRootField.SetValueWithoutNotify(cfg.OutputRoot);
            }));

            section.Add(_platformPopup);
            section.Add(_pipelinePopup);
            section.Add(_compressPopup);
            section.Add(encryptorField);
            section.Add(versionRow);
            section.Add(outputRow);
            section.Add(new HelpBox("选择构建目标平台和基础参数。AB输出目录支持相对路径（相对于项目根目录）。", HelpBoxMessageType.Info));

            parent.Add(section);
        }

        private void BuildMinimalSection(VisualElement parent)
        {
            var section = MakeFoldout("minimal", "最小包设置", "删除 StreamingAssets 中的 .bundle 文件以减小首包体积", true);

            _minimalToggle = MakeToggle("启用最小包模式", "构建后删除 StreamingAssets 中的 .bundle 文件", v =>
            {
                Apply("Toggle Minimal Package", () => cfg.MinimalPackage = v);
                UpdateExtrasVisibility();
            });

            _retainTagsField = MakeTextField("保留Tag(逗号分隔)", v =>
            {
                Apply("Set Retain Tags", () => cfg.RetainTags = v);
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
            var section = MakeFoldout("advanced", "高级设置", "共享打包、依赖数据库、增量构建等", false);

            _sharePackToggle = MakeToggle("启用共享资源打包", "自动提取共享资源到独立bundle", v =>
                Apply("Toggle Share Pack Rule", () => cfg.EnableSharePackRule = v));
            _depDbToggle = MakeToggle("使用资源依赖数据库", "提高打包速度", v =>
                Apply("Toggle Asset Dependency DB", () => cfg.UseAssetDependencyDB = v));
            _clearCacheToggle = MakeToggle("清理构建缓存(禁用增量构建)", "全量重新构建", v =>
                Apply("Toggle Clear Build Cache", () => cfg.ClearBuildCache = v));
            _verifyToggle = MakeToggle("验证构建结果", "构建后验证资源完整性", v =>
                Apply("Toggle Verify Building Result", () => cfg.VerifyBuildingResult = v));

            _copyOptionPopup = MakePopup("内置文件拷贝", s_CopyOptionNames, name =>
            {
                int idx = Array.IndexOf(s_CopyOptionNames, name);
                if (idx >= 0) Apply("Set Bundled Copy Option", () => cfg.BundledCopyOption = (EBundledCopyOption)idx);
            });
            _fileNameStylePopup = MakePopup("文件名风格", s_FileNameStyleNames, name =>
            {
                int idx = Array.IndexOf(s_FileNameStyleNames, name);
                if (idx >= 0) Apply("Set File Name Style", () => cfg.FileNameStyle = (EFileNameStyle)idx);
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
            var section = MakeFoldout("dll", "热更DLL设置", "HybridCLR 热更程序集编译", true);

            _dllToggle = MakeToggle("构建前编译热更DLL", "执行 BuildDLLCommand.BuildAndCopyDlls", v =>
                Apply("Toggle Build HotFix DLL", () => cfg.BuildHotFixDll = v));

            section.Add(_dllToggle);
            parent.Add(section);
        }

        private void BuildPlayerSection(VisualElement parent)
        {
            var section = MakeFoldout("player", "打包Player设置", "构建可执行程序", false);

            _buildPlayerToggle = MakeToggle("构建Player", "构建可执行程序(exe/apk/ipa)", v =>
            {
                Apply("Toggle Build Player", () => cfg.BuildPlayer = v);
                UpdateExtrasVisibility();
            });

            _playerPlatformPopup = MakePopup("Player平台", s_PlatformNames, name =>
            {
                int idx = Array.IndexOf(s_PlatformNames, name);
                if (idx >= 0) Apply("Set Player Platform", () => cfg.PlayerPlatform = s_PlatformTargets[idx]);
            });

            _playerOutputField = MakeTextField("输出路径", v =>
                Apply("Set Player Output Path", () => cfg.PlayerOutputPath = v));
            var outputRow = MakeRow(_playerOutputField);
            outputRow.Add(MakeSmallButton("浏览", () =>
            {
                string selected = EditorUtility.SaveFilePanel("选择输出路径",
                    Path.GetDirectoryName(cfg.PlayerOutputPath),
                    Path.GetFileName(cfg.PlayerOutputPath), "");
                if (string.IsNullOrEmpty(selected)) return;

                Apply("Set Player Output Path", () => cfg.PlayerOutputPath = selected);
                _playerOutputField.SetValueWithoutNotify(selected);
            }));

            _playerExtras = new VisualElement();
            _playerExtras.Add(_playerPlatformPopup);
            _playerExtras.Add(outputRow);

            section.Add(_buildPlayerToggle);
            section.Add(_playerExtras);
            parent.Add(section);
        }

        #endregion

        #region UI 工具 [UI HELPERS]

        /// <summary>记录 Undo → 应用修改 → 标记脏。所有直写 SO 的控件修改统一走此入口，保证可撤销、可落盘。</summary>
        private void Apply(string undoName, Action assign)
        {
            Undo.RecordObject(cfg, undoName);
            assign();
            EditorUtility.SetDirty(cfg);
        }

        /// <summary>分节折叠框，展开状态经 SessionState 持久化（切换预设/重置后保持用户展开习惯）。</summary>
        private static Foldout MakeFoldout(string key, string title, string tooltip, bool defaultExpanded)
        {
            string fullKey = $"BuildConfigEditor.Foldout.{key}";
            var foldout = new Foldout
            {
                text = title,
                tooltip = tooltip,
                value = SessionState.GetBool(fullKey, defaultExpanded),
            };
            foldout.RegisterValueChangedCallback(evt => SessionState.SetBool(fullKey, evt.newValue));
            return foldout;
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
            btn.style.width = 50;
            btn.style.marginLeft = 4;
            return btn;
        }

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

        private void UpdateExtrasVisibility()
        {
            _minimalExtras.style.display = cfg.MinimalPackage ? DisplayStyle.Flex : DisplayStyle.None;
            _playerExtras.style.display = cfg.BuildPlayer ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateMinimalHelpBox()
        {
            string tagInfo = string.IsNullOrWhiteSpace(cfg.RetainTags)
                ? "所有 .bundle 文件将被删除（仅保留清单）"
                : $"保留带 [{cfg.RetainTags}] Tag 的 bundle，其余删除";

            _minimalHelpBox.text =
                $"最小包模式：删除 StreamingAssets 中所有 .bundle 文件，仅保留清单文件（.bytes/.hash/.version）。\n" +
                $"当前: {tagInfo}\n\n" +
                $"适用于 HostPlayMode 在线下载资源的场景，可大幅减小首包体积。";
        }

        /// <summary>将 SO 当前值同步到手动控件。枚举值经 Clamp 防御（资产数据损坏时不越界崩溃）。</summary>
        private void RefreshControlsFromConfig()
        {
            _platformPopup.SetValueWithoutNotify(PlatformNameFromTarget(cfg.BuildTarget));
            _pipelinePopup.SetValueWithoutNotify(s_PipelineNames[cfg.BuildPipeline == EBuildPipeline.LegacyBuildPipeline ? 1 : 0]);
            _compressPopup.SetValueWithoutNotify(s_CompressNames[ClampIndex((int)cfg.CompressOption, s_CompressNames.Length)]);
            _versionField.SetValueWithoutNotify(cfg.PackageVersion);
            _outputRootField.SetValueWithoutNotify(cfg.OutputRoot);

            _minimalToggle.SetValueWithoutNotify(cfg.MinimalPackage);
            _retainTagsField.SetValueWithoutNotify(cfg.RetainTags);

            _sharePackToggle.SetValueWithoutNotify(cfg.EnableSharePackRule);
            _depDbToggle.SetValueWithoutNotify(cfg.UseAssetDependencyDB);
            _clearCacheToggle.SetValueWithoutNotify(cfg.ClearBuildCache);
            _verifyToggle.SetValueWithoutNotify(cfg.VerifyBuildingResult);
            _copyOptionPopup.SetValueWithoutNotify(s_CopyOptionNames[ClampIndex((int)cfg.BundledCopyOption, s_CopyOptionNames.Length)]);
            _fileNameStylePopup.SetValueWithoutNotify(s_FileNameStyleNames[ClampIndex((int)cfg.FileNameStyle, s_FileNameStyleNames.Length)]);

            _dllToggle.SetValueWithoutNotify(cfg.BuildHotFixDll);

            _buildPlayerToggle.SetValueWithoutNotify(cfg.BuildPlayer);
            _playerPlatformPopup.SetValueWithoutNotify(PlatformNameFromTarget(cfg.PlayerPlatform));
            _playerOutputField.SetValueWithoutNotify(cfg.PlayerOutputPath);

            UpdateMinimalHelpBox();
            UpdateExtrasVisibility();
        }

        private static int ClampIndex(int value, int count) => Mathf.Clamp(value, 0, count - 1);

        #endregion

        #region 工具方法 [UTILITY METHODS]

        private static string PlatformNameFromTarget(BuildTarget target)
        {
            for (int i = 0; i < s_PlatformTargets.Length; i++)
                if (s_PlatformTargets[i] == target)
                    return s_PlatformNames[i];

            // 未知平台（如当前构建目标不在支持列表）回退到激活平台
            BuildTarget active = EditorUserBuildSettings.activeBuildTarget;
            for (int i = 0; i < s_PlatformTargets.Length; i++)
                if (s_PlatformTargets[i] == active)
                    return s_PlatformNames[i];

            return s_PlatformNames[0];
        }

        /// <summary>将绝对路径转为项目相对路径；失败（跨盘符等）返回空串。</summary>
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

        #endregion
    }
}
