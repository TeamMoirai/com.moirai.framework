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
    /// 源设置住独立设置面（工具栏 <c>Settings</c> 进、<c>返回列表</c> 出），默认视图的树里没有它。<br />
    /// 开窗先用上次扫分支的缓存铺列表，随后自动在后台重扫：缓存上屏即视作数据就绪，冷装载遮罩只在首次开窗（无缓存）时出现。<br />
    /// 现场读数全部在 OnEnable 重取：装与卸都会以 <c>AssetDatabase.Refresh()</c> 收尾，内存态进度活不过域重载。<br />
    /// 装卸动作成功后不重扫——域重载完窗口会被重建，那时再取一次才是准的。
    /// </remarks>
    internal sealed class PackageManagerWindow : FrameworkMenuWindowBase
    {
        private const int MENU_PRIORITY = 2300;
        private const float MENU_WIDTH_PIXELS = 200f;
        private const string SOURCE_NODE_NAME = "分支包源";
        private const string UNGROUPED_NODE_NAME = "未分组";

        private PackageManagerStateData _state;
        private PluginInstaller _installer;
        private PackageManagerSourceStatus _status;
        private PluginCatalogResult _catalog;
        private SourceSettingsModel _settingsModel;
        private readonly List<PluginEntry> _entries = new List<PluginEntry>();
        private string _message = "开窗先用上次扫分支的缓存铺列表，随后自动在后台重扫。";
        private MessageType _messageType = MessageType.Info;
        private string _selectedEntryId;
        private bool _settingsFace;
        private List<string> _updatableIds = new List<string>();
        private string _filter = string.Empty;
        private int _visibleEntryCount;

        // 版本历史一问一次：值在下拉打开时才去取（每个候选一条 git）；扫分支换了头就整体作废
        private readonly Dictionary<string, List<PluginVersionRevision>> _versionHistory =
            new Dictionary<string, List<PluginVersionRevision>>(StringComparer.Ordinal);

        // 后台扫分支的产出：后台步只写这三个，主线程步只读
        private PluginCatalogResult _scanCatalog;
        private string _scanHeadCommit;
        private string _scanFailureReason;

        // 上次扫描的缓存已铺上屏：装载遮罩据此免显（数据在屏就不该遮）
        private bool _cacheOnScreen;

        [MenuItem("Tools/Moirai/Package Manager", false, MENU_PRIORITY)]
        private static void Open()
        {
            PackageManagerWindow window = GetWindow<PackageManagerWindow>();
            window.titleContent = new GUIContent("Package Manager");
            window.minSize = new Vector2(960f, 520f);
            window.Show();
        }

        protected override void OnEnable()
        {
            MenuWidth = MENU_WIDTH_PIXELS;
            base.OnEnable();
            // 先读账本再建树：设置节点要装活的设置模型，否则工具栏 Settings 按对象匹配找不到它
            ReloadFromDisk();
            ApplyCachedCatalog();
            EnsureMenuTreeBuilt();

            StartDataLoad();
        }

        /// <summary>缓存已铺上屏即视作数据就绪：基类的冷装载遮罩不该盖住已经在看的列表。</summary>
        protected override bool IsDataReady => base.IsDataReady || _cacheOnScreen;

        /// <summary>后台刷新在途：提示框先让位，左下角那句状态顶上；刷新一结束提示自己回来。</summary>
        internal bool IsRefreshing => IsDataLoading;

        /// <summary>后台扫一遍分支，主线程把结果铺上树。</summary>
        protected override void BuildDataLoadSteps(EditorLoadPipeline pipeline)
        {
            pipeline.AddBackgroundStep("扫分支", token =>
            {
                _scanCatalog = null;
                _scanHeadCommit = null;
                _scanFailureReason = null;

                var report = new PluginInstallReport();
                if (_installer.TryBrowse(report, out PluginCatalogResult catalog, out string headCommit))
                {
                    _scanCatalog = catalog;
                    _scanHeadCommit = headCommit;
                    return;
                }

                _scanFailureReason = report.FailureReason ?? "扫分支没走通";
            });

            pipeline.AddStep("铺列表", ApplyScanResult);
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
            _versionHistory.Clear();
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree(false);
            ApplyMenuStyle(tree);
            tree.DefaultMenuStyle.SetHeight(24);

            // 源设置只在设置面里出现：默认视图的树只列插件
            if (_settingsFace)
            {
                tree.Add(SOURCE_NODE_NAME, _settingsModel);
                return tree;
            }

            int visible = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                PluginEntry entry = _entries[i];
                if (!string.IsNullOrEmpty(_filter) && !entry.Matches(_filter)) continue;

                tree.Add(entry.GroupName + "/" + entry.DisplayName, entry);
                visible++;
            }

            _visibleEntryCount = visible;

            return tree;
        }

        protected override void OnBeforeTreeRebuild(object previousSelection)
        {
            // 只记插件条目：设置面里选中项是源设置本身，别让它把待恢复的条目覆盖掉
            if (previousSelection is PluginEntry entry) _selectedEntryId = entry.EntryId;
        }

        protected override void OnAfterTreeRebuild(object previousSelection)
        {
            // 同步选中：设置面/原条目当场落在新树上，不等 Odin 的下一拍延迟匹配
            if (_settingsFace)
            {
                SelectMenuItemWithValue(_settingsModel);
                return;
            }

            if (string.IsNullOrEmpty(_selectedEntryId)) return;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (!string.Equals(_entries[i].EntryId, _selectedEntryId, StringComparison.Ordinal)) continue;
                SelectMenuItemWithValue(_entries[i]);
                return;
            }
        }

        protected override void OnImGUI()
        {
            DrawToolbar();
            DrawSourceStrip();
            base.OnImGUI();
            DrawMessage();
            DrawRefreshStatus();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (ToolbarButton("Refresh", 80f)) Refresh();

            GUILayout.Space(8f);
            string typed = GUILayout.TextField(_filter, EditorStyles.toolbarSearchField, GUILayout.Width(160f));
            if (!string.Equals(typed, _filter, StringComparison.Ordinal))
            {
                _filter = typed;
                QueueTreeRebuild();
            }

            if (!string.IsNullOrEmpty(_filter) && ToolbarButton("✕", 24f))
            {
                _filter = string.Empty;
                QueueTreeRebuild();
            }

            string updateLabel = _updatableIds.Count > 0 ? $"Update All ({_updatableIds.Count})" : "Update All";
            if (ToolbarButton(updateLabel, 110f)) UpdateAll();

            if (_settingsFace)
            {
                if (ToolbarButton("返回列表", 80f)) SwitchFace(false);
            }
            else if (ToolbarButton("Settings", 80f)) SwitchFace(true);

            GUILayout.FlexibleSpace();
            string countLabel = string.IsNullOrEmpty(_filter)
                ? $"分支包：{_entries.Count} 个"
                : $"分支包：{_visibleEntryCount}/{_entries.Count} 个";
            GUILayout.Label(countLabel, HeaderStyles.IsReady ? HeaderStyles.Meta : EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>切换设置面：<c>true</c> 只列「分支包源」，<c>false</c> 回到插件列表。</summary>
        private void SwitchFace(bool settingsFace)
        {
            _settingsFace = settingsFace;
            QueueTreeRebuild();
        }

        private void DrawSourceStrip()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 现场细节（源/HEAD/工作树/落点）只在设置面报；常规面报上次刷新这类常用读数
            if (_settingsFace)
            {
                GUILayout.Label($"源：{Text(_status.RepoUrl)}#{_status.Branch}　· HEAD：{ShortSha(_status.HeadCommit)}　· " +
                                $"工作树：{_status.WorktreeShape}　· 落点：{Text(_status.PackageRootPath)}",
                    HeaderStyles.IsReady ? HeaderStyles.Meta : EditorStyles.miniLabel);
                GUILayout.Label($"上次刷新：{FetchText()}", EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.Label($"上次刷新：{FetchText()}　· 分支：{Text(_status.Branch)}　· 已装：{(_state?.installed?.Count ?? 0)} 个",
                    HeaderStyles.IsReady ? HeaderStyles.Meta : EditorStyles.miniLabel);
            }

            if (!string.IsNullOrEmpty(_status.FailureReason))
            {
                EditorGUILayout.HelpBox(_status.FailureReason, MessageType.Error);
                if (GUILayout.Button("重试", GUILayout.Width(60f))) Refresh();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>上次刷新时刻的可读文案。</summary>
        private string FetchText() => string.IsNullOrEmpty(_status.LastFetchAt) ? "从未" : _status.LastFetchAt;

        private void DrawMessage()
        {
            // 刷新在途时这条也收起来：它讲的是上一趟的读数，左下角已有"正在刷新"那句话
            if (IsDataLoading || string.IsNullOrEmpty(_message)) return;
            EditorGUILayout.HelpBox(_message, _messageType);
        }

        /// <summary>后台刷新在途时在左下角报一句状态（有缓存的暖开窗不画装载遮罩，就靠它给反馈）。</summary>
        private void DrawRefreshStatus()
        {
            if (!IsDataLoading) return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(DescribeRefreshStatus(), HeaderStyles.IsReady ? HeaderStyles.Meta : EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private string DescribeRefreshStatus()
        {
            string stage = DataLoad?.StageLabel;
            return string.IsNullOrEmpty(stage) ? "后台刷新中…" : $"后台刷新中…（{stage}）";
        }

        /// <summary>重扫一遍分支（开窗自动走这条，工具栏 Refresh 也走它；在途重扫会被取消重来）。</summary>
        private void Refresh()
        {
            StartDataLoad();
        }

        /// <summary>用上次扫分支的缓存先把列表铺上，免得开窗一片空白。</summary>
        private void ApplyCachedCatalog()
        {
            if (!PackageManagerStateRegistry.TryGetCachedCatalog(_state, out List<PluginManifest> manifests, out string headCommit))
            {
                return;
            }

            var catalog = new PluginCatalogResult();
            for (int i = 0; i < manifests.Count; i++) catalog.AddManifest(manifests[i]);

            _catalog = catalog;

            // 可更新判定只比本地对象（账本记的修订 vs 远端分支引用），不联网：缓存面也直接标出 ↑
            if (_installer.TryCollectUpdatable(_state, out List<string> updatable, out _)) _updatableIds = updatable;

            RebuildEntries(catalog, headCommit);
            _cacheOnScreen = true;
        }

        /// <summary>把后台扫出的结果铺上树，并连同"上次刷新"与缓存一起落盘（主线程步）。</summary>
        private void ApplyScanResult()
        {
            if (_scanCatalog == null)
            {
                SetMessage(_scanFailureReason ?? "扫分支没走通", MessageType.Error);
                Repaint();
                return;
            }

            PluginCatalogResult catalog = _scanCatalog;
            PackageManagerStateRegistry.StampFetch(_state);

            // 落盘前重读一遍账本再写：别拿本窗口的旧快照覆盖别处（并行会话/其他路径）的记账
            PackageManagerStateData latest = PackageManagerStateRegistry.Load();
            if (latest?.settings != null) latest.settings.lastFetchAt = _state.settings.lastFetchAt;
            PackageManagerStateRegistry.CacheCatalog(latest, catalog, _scanHeadCommit);
            PackageManagerStateRegistry.Save(latest);

            _status = PackageManagerSourceStatus.Capture(_state.settings, _installer.Source);
            _catalog = catalog;
            _versionHistory.Clear();

            if (!_installer.TryCollectUpdatable(_state, out List<string> updatable, out string updatableReason))
            {
                _updatableIds = new List<string>();
                SetMessage("可更新判定没读通：" + updatableReason, MessageType.Warning);
            }
            else
            {
                _updatableIds = updatable;
                SetMessage($"分支上有 {catalog.Manifests.Count} 个包，可更新 {updatable.Count} 个。", MessageType.Info);
            }

            // 条目要吃到刚算出的可更新集，故排在判定之后铺
            RebuildEntries(catalog, _scanHeadCommit);
            _cacheOnScreen = true;

            QueueTreeRebuild();
            Repaint();
        }

        private void RebuildEntries(PluginCatalogResult catalog, string headCommit)
        {
            _entries.Clear();

            for (int i = 0; i < catalog.Manifests.Count; i++)
            {
                PluginManifest manifest = catalog.Manifests[i];
                var entry = new PluginEntry(this, manifest, InstalledVersionOf(manifest.id),
                    _updatableIds.Contains(manifest.id), headCommit);
                _entries.Add(entry);
            }
        }

        /// <summary>账本里该插件的已装版本；没装过为空。</summary>
        private string InstalledVersionOf(string pluginId)
        {
            List<InstalledPluginData> installed = _state?.installed;
            for (int i = 0; installed != null && i < installed.Count; i++)
            {
                if (string.Equals(installed[i].id, pluginId, StringComparison.Ordinal)) return installed[i].version;
            }

            return null;
        }

        /// <summary>依赖行用：某插件的已装状态一句话。</summary>
        private string DescribeInstalled(string pluginId)
        {
            string version = InstalledVersionOf(pluginId);
            return string.IsNullOrEmpty(version) ? "未装" : $"已装 {version}";
        }

        /// <summary>分支目录里该插件的版本；不在目录里为空。</summary>
        private string BranchVersionOf(string pluginId)
        {
            return _catalog != null && _catalog.ById.TryGetValue(pluginId, out PluginManifest manifest)
                ? manifest.version
                : null;
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
                SetMessage("没有可更新的插件。", MessageType.Info);
                return;
            }

            if (!ConfirmUpdateAll()) return;

            PluginInstallReport report = _installer.Install(_updatableIds, true);
            AfterMutation(report, $"更新了 {report.TouchedPluginIds.Count} 个");
        }

        /// <summary>单包更新：强制重装一遍，可选先清目录（干净更新）。</summary>
        private void UpdateRequested(string pluginId, bool removeFirst)
        {
            PluginInstallReport report = _installer.Install(pluginId, true, removeFirst);
            AfterMutation(report, $"更新了 {pluginId}");
        }

        /// <summary>装所选的历史版本：先清目录再按该修订物化，账本记该修订。</summary>
        private void InstallVersionRequested(string pluginId, string revision, string version)
        {
            PluginInstallReport report = _installer.InstallAtRevision(pluginId, revision, version);
            AfterMutation(report, $"装了 {pluginId} 的历史版本 {version}");
        }

        /// <summary>取某插件的版本历史（按 id 缓存一次）；列不出来时交出 false，下拉里就只剩分支头那一条。</summary>
        internal bool TryGetVersionHistory(string pluginId, out List<PluginVersionRevision> history)
        {
            if (_versionHistory.TryGetValue(pluginId, out history)) return history != null;

            if (_installer == null || !_installer.Source.TryListManifestHistory(pluginId, out history, out _)) history = null;

            _versionHistory[pluginId] = history;
            return history != null;
        }

        /// <summary>更新前把「已装 → 分支」逐条摆出来问一次。</summary>
        private bool ConfirmUpdateAll()
        {
            var lines = new List<string>();
            for (int i = 0; i < _updatableIds.Count; i++)
            {
                string id = _updatableIds[i];
                lines.Add($"{id}　{InstalledVersionOf(id) ?? "?"} → {BranchVersionOf(id) ?? "?"}");
            }

            lines.Add(string.Empty);
            lines.Add("更新的外部依赖按归属台账回收/写入，装完刷新一次。");
            return EditorUtility.DisplayDialog($"更新 {_updatableIds.Count} 个插件", string.Join("\n", lines), "继续", "取消");
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
        /// <remarks>清单里的说明走类级 <c>[InfoBox]</c> 展示：只读、折行，且没写说明时不占地方。</remarks>
        [InfoBox("$Description", InfoMessageType.None, nameof(HasDescription))]
        [Serializable]
        private sealed class PluginEntry
        {
            [NonSerialized] private readonly PackageManagerWindow _window;
            [NonSerialized] private readonly PluginManifest _manifest;
            [NonSerialized] private readonly string _installedVersion;
            [NonSerialized] private readonly bool _updateAvailable;
            [NonSerialized] private readonly string _headCommit;

            internal PluginEntry(PackageManagerWindow window, PluginManifest manifest, string installedVersion,
                bool updateAvailable, string headCommit)
            {
                _window = window;
                _manifest = manifest;
                _installedVersion = installedVersion;
                _updateAvailable = updateAvailable;
                _headCommit = headCommit;
                SelectedVersion = manifest.version;
            }

            [BoxGroup("概况"), ShowInInspector, LabelText("插件")]
            internal string DisplayName => string.IsNullOrEmpty(_manifest.name) ? _manifest.id : _manifest.name;

            [BoxGroup("概况"), ShowInInspector, LabelText("分组")]
            internal string GroupName => string.IsNullOrEmpty(_manifest.function) ? UNGROUPED_NODE_NAME : _manifest.function;

            [BoxGroup("概况"), ShowInInspector, LabelText("状态")]
            internal string StatusText => DescribeStatus();

            [BoxGroup("概况"), ShowInInspector, LabelText("分支版本")]
            internal string Version => _manifest.version;

            [BoxGroup("概况"), ShowInInspector, LabelText("最低 Unity")]
            internal string MinUnityVersion => "Unity " + _manifest.minUnityVersion + "+";

            [BoxGroup("概况"), ShowInInspector, LabelText("最低框架")]
            internal string MinFrameworkVersion => "框架 " + _manifest.minFrameworkVersion + "+";

            [BoxGroup("概况"), ShowInInspector, LabelText("分支头")]
            internal string HeadCommit => ShortSha(_headCommit);

            [BoxGroup("标签"), ShowInInspector, LabelText("标签")]
            internal string TagsText => Join(_manifest.tags);

            [BoxGroup("依赖"), ShowInInspector, LabelText("包间依赖")]
            internal string DependenciesText => DescribeDependencies(_manifest);

            [BoxGroup("依赖"), ShowInInspector, LabelText("外部依赖")]
            internal string UpmText => DescribeUpm(_manifest);

            [BoxGroup("操作"), ShowInInspector, LabelText("安装版本"), ValueDropdown(nameof(VersionOptions))]
            [Tooltip("默认分支头那一条；选历史版本后用「安装所选版本」——先清目录再按那次修订物化，因此可以降级。")]
            internal string SelectedVersion { get; set; }

            [BoxGroup("操作"), ShowInInspector, LabelText("更新前先清目录")]
            [Tooltip("勾上则先删掉插件目录再按分支头重新物化：清掉新版本不再产出的文件。")]
            internal bool RemoveBeforeUpdate { get; set; }

            internal string EntryId => _manifest.id;

            /// <summary>清单里的说明，给类级说明框取用。</summary>
            internal string Description => _manifest.description;

            /// <summary>已装与否：动作按它取舍。</summary>
            private bool IsInstalled => !string.IsNullOrEmpty(_installedVersion);

            /// <summary>分支版本与已装版本不一致：给「更新到分支版本」。</summary>
            private bool UpdateAvailable => _updateAvailable;

            /// <summary>清单里写了说明才画那个框。</summary>
            private bool HasDescription =>
                !string.IsNullOrEmpty(_manifest.description) && !(_window?.IsRefreshing ?? false);

            [BoxGroup("操作"), Button("安装")]
            [HideIf(nameof(IsInstalled))]
            private void Install() => _window?.InstallRequested(_manifest.id, false);

            [BoxGroup("操作"), Button("更新到分支版本")]
            [ShowIf(nameof(UpdateAvailable))]
            private void UpdateToBranch() => _window?.UpdateRequested(_manifest.id, RemoveBeforeUpdate);

            [BoxGroup("操作"), Button("重装（忽略已装判定）")]
            [ShowIf(nameof(IsInstalled))]
            private void Reinstall() => _window?.InstallRequested(_manifest.id, true);

            [BoxGroup("操作"), Button("卸载")]
            [ShowIf(nameof(IsInstalled))]
            private void Uninstall() => _window?.UninstallRequested(_manifest.id);

            [BoxGroup("操作"), Button("安装所选版本")]
            [ShowIf(nameof(SelectedVersionDiffersFromHead))]
            private void InstallSelectedVersion()
            {
                string revision = RevisionOf(SelectedVersion);
                if (string.IsNullOrEmpty(revision))
                {
                    _window?.SetMessage($"找不到 {SelectedVersion} 那次修订，先点一次 Refresh 再试。", MessageType.Warning);
                    return;
                }

                _window?.InstallVersionRequested(_manifest.id, revision, SelectedVersion);
            }

            private string DescribeStatus()
            {
                if (!IsInstalled) return "未装";

                return _updateAvailable
                    ? $"已装 {_installedVersion}　↑ 可更新到 {_manifest.version}"
                    : $"已装 {_installedVersion}";
            }

            /// <summary>版本候选：分支头那一条，加上清单历史上出现过的版本（打开下拉时才去问 git）。</summary>
            private IEnumerable<ValueDropdownItem<string>> VersionOptions()
            {
                string headMarker = InstalledMarker(_manifest.version);
                yield return new ValueDropdownItem<string>(_manifest.version + "（分支头）" + headMarker, _manifest.version);

                if (_window == null || !_window.TryGetVersionHistory(_manifest.id, out List<PluginVersionRevision> history)) yield break;

                for (int i = 0; i < history.Count; i++)
                {
                    PluginVersionRevision item = history[i];
                    if (item.IsHead || string.Equals(item.Version, _manifest.version, StringComparison.Ordinal)) continue;

                    yield return new ValueDropdownItem<string>(item.Version + InstalledMarker(item.Version), item.Version);
                }
            }

            /// <summary>所选版本那次修订：分支头版本用分支头提交，其余去历史里找。</summary>
            private string RevisionOf(string version)
            {
                if (string.Equals(version, _manifest.version, StringComparison.Ordinal)) return _headCommit;
                if (_window == null || !_window.TryGetVersionHistory(_manifest.id, out List<PluginVersionRevision> history)) return null;

                for (int i = 0; i < history.Count; i++)
                {
                    if (string.Equals(history[i].Version, version, StringComparison.Ordinal)) return history[i].Revision;
                }

                return null;
            }

            /// <summary>选中的不是分支头那条：给「安装所选版本」。</summary>
            private bool SelectedVersionDiffersFromHead =>
                !string.IsNullOrEmpty(SelectedVersion) && !string.Equals(SelectedVersion, _manifest.version, StringComparison.Ordinal);

            private string InstalledMarker(string version)
            {
                return string.Equals(_installedVersion, version, StringComparison.Ordinal) ? "（已装）" : string.Empty;
            }

            /// <summary>筛选命中：名字/标识/分组/说明/标签，任一处不区分大小写包含即可。</summary>
            internal bool Matches(string filter)
            {
                return Contains(_manifest.name, filter)
                       || Contains(_manifest.id, filter)
                       || Contains(_manifest.function, filter)
                       || Contains(_manifest.description, filter)
                       || ContainsTag(_manifest.tags, filter);
            }

            private static bool Contains(string value, string needle)
            {
                return !string.IsNullOrEmpty(value) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static bool ContainsTag(string[] tags, string needle)
            {
                for (int i = 0; tags != null && i < tags.Length; i++)
                {
                    if (Contains(tags[i], needle)) return true;
                }

                return false;
            }

            private static string Join(string[] values)
            {
                return values == null || values.Length == 0 ? "—" : string.Join("、", values);
            }

            private string DescribeDependencies(PluginManifest manifest)
            {
                PluginDependency[] dependencies = manifest.dependencies;
                if (dependencies == null || dependencies.Length == 0) return "—";

                var parts = new List<string>();
                for (int i = 0; i < dependencies.Length; i++)
                {
                    PluginDependency dependency = dependencies[i];
                    parts.Add($"{dependency.id} {dependency.versionOperator}{dependency.version}" +
                              $"（{_window?.DescribeInstalled(dependency.id)}）");
                }

                return string.Join("；", parts);
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
