using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 分支作包源的浏览与装卸窗口。
    /// </summary>
    /// <remarks>
    /// 菜单按清单的 <c>function</c> 分组，叶子绑一个 <see cref="PluginEntry"/>；详情面就是这个条目本身。<br />
    /// 现场读数全部在 OnEnable 重取：装与卸都会以 <c>AssetDatabase.Refresh()</c> 收尾，内存态进度活不过域重载。<br />
    /// 装卸动作成功后不重扫——域重载完窗口会被重建，那时再取一次才是准的。
    /// </remarks>
    internal sealed class PackageManagerWindow : FrameworkMenuWindowBase
    {
        private const int MenuPriority = 2300;
        private const float MenuWidthPixels = 300f;
        private const string SourceNodeName = "分支包源";
        private const string UngroupedNodeName = "未分组";

        private PackageManagerStateData _state;
        private PluginInstaller _installer;
        private PackageManagerSourceStatus _status;
        private PluginCatalogResult _catalog;
        private SourceSettingsModel _settingsModel;
        private readonly List<PluginEntry> _entries = new List<PluginEntry>();
        private string _message = "打开窗口只读现场，不联网：点 Refresh 才扫分支。";
        private MessageType _messageType = MessageType.Info;
        private string _selectedEntryId;
        private List<string> _updatableIds = new List<string>();

        [MenuItem("Tools/Moirai/Package Manager", false, MenuPriority)]
        private static void Open()
        {
            PackageManagerWindow window = GetWindow<PackageManagerWindow>();
            window.titleContent = new GUIContent("Package Manager");
            window.minSize = new Vector2(960f, 520f);
            window.Show();
        }

        protected override void OnEnable()
        {
            MenuWidth = MenuWidthPixels;
            base.OnEnable();
            // 先读账本再建树：设置节点要装活的设置模型，否则工具栏 Settings 按对象匹配找不到它
            ReloadFromDisk();
            EnsureMenuTreeBuilt();
        }

        /// <summary>重读账本与现场，不联网。</summary>
        private void ReloadFromDisk()
        {
            _state = PackageManagerStateRegistry.Load();
            _installer = PluginInstaller.Create(_state.settings);
            _status = PackageManagerSourceStatus.Capture(_state.settings, _installer.Source);
            _settingsModel = new SourceSettingsModel(this, _state.settings);
            _entries.Clear();
            _catalog = null;
            _updatableIds = new List<string>();
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree(false);
            ApplyMenuStyle(tree);
            tree.DefaultMenuStyle.SetHeight(24);

            tree.Add(SourceNodeName, _settingsModel);

            for (int i = 0; i < _entries.Count; i++)
            {
                PluginEntry entry = _entries[i];
                tree.Add(entry.GroupName + "/" + entry.DisplayName, entry);
            }

            return tree;
        }

        protected override void OnBeforeTreeRebuild(object previousSelection)
        {
            _selectedEntryId = (previousSelection as PluginEntry)?.EntryId;
        }

        protected override void OnAfterTreeRebuild(object previousSelection)
        {
            if (string.IsNullOrEmpty(_selectedEntryId)) return;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (!string.Equals(_entries[i].EntryId, _selectedEntryId, StringComparison.Ordinal)) continue;
                TrySelectMenuItemWithObject(_entries[i]);
                return;
            }
        }

        protected override void OnImGUI()
        {
            DrawToolbar();
            DrawSourceStrip();
            base.OnImGUI();
            DrawMessage();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (ToolbarButton("Refresh", 80f)) RefreshFromBranch();

            string updateLabel = _updatableIds.Count > 0 ? $"Update All ({_updatableIds.Count})" : "Update All";
            if (ToolbarButton(updateLabel, 110f)) UpdateAll();

            if (ToolbarButton("Settings", 80f)) TrySelectMenuItemWithObject(_settingsModel);

            GUILayout.FlexibleSpace();
            GUILayout.Label($"分支包：{_entries.Count} 个", HeaderStyles.IsReady ? HeaderStyles.Meta : EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSourceStrip()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label($"源：{Text(_status.RepoUrl)}# {_status.Branch}　· HEAD：{ShortSha(_status.HeadCommit)}　· " +
                            $"工作树：{_status.WorktreeShape}　· 落点：{Text(_status.PackageRootPath)}",
                HeaderStyles.IsReady ? HeaderStyles.Meta : EditorStyles.miniLabel);

            if (!string.IsNullOrEmpty(_status.LastFetchAt))
            {
                GUILayout.Label($"上次 fetch：{_status.LastFetchAt}", EditorStyles.miniLabel);
            }

            if (!string.IsNullOrEmpty(_status.FailureReason))
            {
                EditorGUILayout.HelpBox(_status.FailureReason, MessageType.Error);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMessage()
        {
            if (string.IsNullOrEmpty(_message)) return;
            EditorGUILayout.HelpBox(_message, _messageType);
        }

        /// <summary>扫一遍分支：建检出、fetch、列树、建目录。</summary>
        private void RefreshFromBranch()
        {
            var report = new PluginInstallReport();
            if (!_installer.TryBrowse(report, out PluginCatalogResult catalog, out string headCommit))
            {
                SetMessage(report.FailureReason ?? "扫分支没走通", MessageType.Error);
                Repaint();
                return;
            }

            _catalog = catalog;
            RebuildEntries(catalog, headCommit);

            if (!_installer.TryCollectUpdatable(_state, out List<string> updatable, out string updatableReason))
            {
                SetMessage("可更新判定没读通：" + updatableReason, MessageType.Warning);
            }
            else
            {
                _updatableIds = updatable;
                SetMessage($"分支上有 {catalog.Manifests.Count} 个包，可更新 {updatable.Count} 个。", MessageType.Info);
            }

            QueueTreeRebuild();
            Repaint();
        }

        private void RebuildEntries(PluginCatalogResult catalog, string headCommit)
        {
            _entries.Clear();
            HashSet<string> installedIds = InstalledIdSet();

            for (int i = 0; i < catalog.Manifests.Count; i++)
            {
                PluginManifest manifest = catalog.Manifests[i];
                var entry = new PluginEntry(this, manifest, installedIds.Contains(manifest.id), headCommit);
                _entries.Add(entry);
            }
        }

        private HashSet<string> InstalledIdSet()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            List<InstalledPluginData> installed = _state?.installed;
            for (int i = 0; installed != null && i < installed.Count; i++)
            {
                if (!string.IsNullOrEmpty(installed[i].id)) ids.Add(installed[i].id);
            }

            return ids;
        }

        /// <summary>装单个包，依赖闭包由规划层带出。</summary>
        private void InstallRequested(string pluginId, bool force)
        {
            PluginInstallReport report = _installer.Install(pluginId, force);
            AfterMutation(report, $"装了 {pluginId}");
        }

        /// <summary>卸单个包，依赖方在场时先问。</summary>
        private void UninstallRequested(string pluginId)
        {
            if (!InstalledIdSet().Contains(pluginId))
            {
                SetMessage($"{pluginId} 不在账本里", MessageType.Warning);
                return;
            }

            var dependents = new List<string>();
            if (_catalog != null)
            {
                dependents.AddRange(InstallPlanner.DependentsOf(_catalog.Manifests, pluginId, InstalledIdSet()));
            }

            dependents.Remove(pluginId);
            bool cascade = false;
            if (dependents.Count > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex("还有插件依赖它",
                    $"卸载 {pluginId} 后，这些在场插件会缺依赖：{string.Join("、", dependents)}",
                    "连带卸载", "中止", "只卸它");
                if (choice == 1)
                {
                    SetMessage("已中止，什么都没改。", MessageType.Info);
                    return;
                }

                cascade = choice == 0;
            }

            PluginInstallReport report = _installer.Uninstall(pluginId, cascade);
            AfterMutation(report, $"卸了 {pluginId}");
        }

        private void UpdateAll()
        {
            if (_updatableIds.Count == 0)
            {
                SetMessage("没有可更新的插件（先 Refresh）。", MessageType.Info);
                return;
            }

            PluginInstallReport report = _installer.Install(_updatableIds, true);
            AfterMutation(report, $"更新了 {report.TouchedPluginIds.Count} 个");
        }

        /// <summary>装卸后的统一收尾：报告失败就停在原状，成功则等域重载重建。</summary>
        private void AfterMutation(PluginInstallReport report, string successText)
        {
            if (!report.Ok)
            {
                string detail = report.HasConflicts ? "　冲突：" + string.Join("；", ReportConflicts(report)) : string.Empty;
                SetMessage(report.FailureReason + detail, MessageType.Error);
                Repaint();
                return;
            }

            if (report.Notes.Count > 0)
            {
                for (int i = 0; i < report.Notes.Count; i++) Debug.Log("[PackageManager] " + report.Notes[i]);
            }

            SetMessage(successText + "　" + report.Summary(), MessageType.Info);
            Repaint();
        }

        private static List<string> ReportConflicts(PluginInstallReport report)
        {
            var reasons = new List<string>();
            for (int i = 0; i < report.Conflicts.Count; i++)
            {
                reasons.Add(report.Conflicts[i].PluginId + "：" + report.Conflicts[i].Reason);
            }

            return reasons;
        }

        private void SetMessage(string message, MessageType type)
        {
            _message = message;
            _messageType = type;
        }

        private static string Text(string value) => string.IsNullOrEmpty(value) ? "<未设置>" : value;

        private static string ShortSha(string sha)
        {
            if (string.IsNullOrEmpty(sha)) return "<无>";
            return sha.Length <= 8 ? sha : sha.Substring(0, 8);
        }

        /// <summary>
        /// 一个分支上的插件在菜单里的那一行，详情面就是它本身。
        /// </summary>
        [Serializable]
        private sealed class PluginEntry
        {
            [NonSerialized] private readonly PackageManagerWindow _window;
            [NonSerialized] private readonly PluginManifest _manifest;
            [NonSerialized] private readonly bool _isInstalled;
            [NonSerialized] private readonly string _headCommit;

            internal PluginEntry(PackageManagerWindow window, PluginManifest manifest, bool isInstalled, string headCommit)
            {
                _window = window;
                _manifest = manifest;
                _isInstalled = isInstalled;
                _headCommit = headCommit;
            }

            [ShowInInspector, LabelText("插件")]
            internal string DisplayName => string.IsNullOrEmpty(_manifest.name) ? _manifest.id : _manifest.name;

            [ShowInInspector, LabelText("分组")]
            internal string GroupName => string.IsNullOrEmpty(_manifest.function) ? UngroupedNodeName : _manifest.function;

            [ShowInInspector, LabelText("状态")]
            internal string StatusText => _isInstalled ? "已装" : "未装";

            [ShowInInspector, LabelText("分支版本")]
            internal string Version => _manifest.version;

            [ShowInInspector, LabelText("最低 Unity")]
            internal string MinUnityVersion => _manifest.minUnityVersion;

            [ShowInInspector, LabelText("标签")]
            internal string TagsText => Join(_manifest.tags);

            [ShowInInspector, LabelText("包间依赖")]
            internal string DependenciesText => DescribeDependencies(_manifest);

            [ShowInInspector, LabelText("外部依赖")]
            internal string UpmText => DescribeUpm(_manifest);

            [ShowInInspector, LabelText("分支头")]
            internal string HeadCommit => ShortSha(_headCommit);

            internal string EntryId => _manifest.id;

            [Button("安装")]
            private void Install() => _window?.InstallRequested(_manifest.id, false);

            [Button("重装（忽略已装判定）")]
            private void Reinstall() => _window?.InstallRequested(_manifest.id, true);

            [Button("卸载")]
            private void Uninstall() => _window?.UninstallRequested(_manifest.id);

            private static string Join(string[] values)
            {
                return values == null || values.Length == 0 ? "—" : string.Join("、", values);
            }

            private static string DescribeDependencies(PluginManifest manifest)
            {
                PluginDependency[] dependencies = manifest.dependencies;
                if (dependencies == null || dependencies.Length == 0) return "—";

                var parts = new List<string>();
                for (int i = 0; i < dependencies.Length; i++)
                {
                    parts.Add($"{dependencies[i].id} {dependencies[i].versionOperator}{dependencies[i].version}");
                }

                return string.Join("，", parts);
            }

            private static string DescribeUpm(PluginManifest manifest)
            {
                PluginUpmDependency[] dependencies = manifest.upmDependencies;
                if (dependencies == null || dependencies.Length == 0) return "—";

                var parts = new List<string>();
                for (int i = 0; i < dependencies.Length; i++) parts.Add($"{dependencies[i].id} {dependencies[i].version}");
                return string.Join("，", parts);
            }
        }

        /// <summary>
        /// 源地址、分支与 git 可执行文件的可编辑设置，就地写在窗口里而不另开设置页。
        /// </summary>
        [Serializable]
        private sealed class SourceSettingsModel
        {
            [NonSerialized] private readonly PackageManagerWindow _window;
            [NonSerialized] private readonly PackageManagerSettingsData _settings;

            internal SourceSettingsModel(PackageManagerWindow window, PackageManagerSettingsData settings)
            {
                _window = window;
                _settings = settings;
            }

            [ShowInInspector, LabelText("远端地址")]
            internal string RepoUrl { get => _settings.repoUrl; set => _settings.repoUrl = value; }

            [ShowInInspector, LabelText("分支")]
            internal string Branch { get => _settings.branch; set => _settings.branch = value; }

            [ShowInInspector, LabelText("git 可执行文件")]
            internal string GitPath { get => _settings.gitPath; set => _settings.gitPath = value; }

            [ShowInInspector, LabelText("状态文件")]
            internal string StatePath => PackageManagerStateRegistry.Internal_PeekStatePath();

            [Button("保存设置")]
            private void Save()
            {
                PackageManagerStateRegistry.Save(_settings == null ? new PackageManagerStateData() : _window._state);
                _window?.ReloadFromDisk();
                _window?.QueueTreeRebuild();
                _window?.SetMessage("设置已写入状态文件。", MessageType.Info);
            }
        }
    }
}
