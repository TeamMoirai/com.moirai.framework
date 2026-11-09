using System;
using System.Collections.Generic;
using System.IO;
using Moirai.Atropos.MenuEditorWindow;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 包管理器窗口骨架：分支作包源、清单目录、依赖闭包规划与装卸编排的统一交互面。
    /// </summary>
    /// <remarks>
    /// 派生窗只提供一份 <see cref="PackageManagerProfile"/>：窗口标题、状态账本、检出落点、源缺省与界面文案。
    /// 菜单项、工具栏、详情面、版本历史下拉与装卸动作全部留在本类，派生方不重复实现。<br />
    /// 菜单按清单的 <c>function</c> 分组，叶子绑一个条目；详情面就是这个条目本身。<br />
    /// 源设置住独立设置面（工具栏 <c>Settings</c> 进、<c>返回列表</c> 出），默认视图的树里没有它。<br />
    /// 开窗先用上次扫分支的缓存铺列表，随后自动在后台重扫：缓存上屏即视作数据就绪，冷装载遮罩只在首次开窗（无缓存）时出现。<br />
    /// 现场读数全部在 OnEnable 重取：装与卸都会以 <c>AssetDatabase.Refresh()</c> 收尾，内存态进度活不过域重载。<br />
    /// 装卸动作成功后不重扫，只按本机账本重算：账本落盘后、刷新触发重编译之前先把列表与详情就地换到新状态。
    /// </remarks>
    public abstract partial class PackageManagerWindowBase : FrameworkMenuWindowBase
    {
        private const float MENU_WIDTH_PIXELS = 200f;

        // 编排层在"刷新资源"这一步回报 0.95：账本已落盘、AssetDatabase.Refresh 还没开始
        private const float LOCAL_STATE_REFRESH_PROGRESS = 0.95f;

        // 进度弹窗收起后，动作期间点下的鼠标事件往往这时才被派发：留一小段输入冷却再恢复
        private const double MUTATION_INPUT_GRACE_SECONDS = 0.5d;

        // 反向同步前只问一次"是不是 git 工作树"，读不通就停手
        private const int SYNC_GIT_QUERY_TIMEOUT_MILLISECONDS = 30000;

        private static readonly string[] FILTER_LABELS = { "全部", "未安装", "可升级" };

        // 状态图标颜色：已装绿、可升级蓝、有拦路红、超出支持范围黄
        private static readonly Color INSTALLED_ICON_COLOR = new Color(0.30f, 0.80f, 0.42f);
        private static readonly Color UPGRADABLE_ICON_COLOR = new Color(0.35f, 0.65f, 0.98f);
        private static readonly Color BLOCKED_ICON_COLOR = new Color(0.95f, 0.35f, 0.32f);
        private static readonly Color OUT_OF_SUPPORT_ICON_COLOR = new Color(0.98f, 0.78f, 0.20f);

        /// <summary>菜单树的列表筛选口径；与文本筛选叠加。</summary>
        private enum EntryFilterMode
        {
            All = 0,
            NotInstalled = 1,
            Upgradable = 2
        }

        /// <summary>设置面左树里的页签。</summary>
        private enum SettingsNode
        {
            Source = 0,
            DevTools = 1
        }

        private PackageManagerProfile _profile;
        private PackageManagerStateRegistry _stateRegistry;
        private PackageManagerStateData _state;
        private PackageManagerCacheData _cache;
        private PluginInstaller _installer;
        private PackageManagerSourceStatus _status;
        private PluginCatalogResult _catalog;
        private SourceSettingsModel _settingsModel;
        private DevToolsModel _devToolsModel;
        private SettingsNode _settingsNode;
        private readonly List<PluginEntry> _entries = new List<PluginEntry>();
        private string _message = "开窗先用上次扫分支的缓存铺列表，随后自动在后台重扫。";
        private MessageType _messageType = MessageType.Info;
        private string _selectedEntryId;
        private bool _settingsFace;
        private List<string> _updatableIds = new List<string>();
        private string _filter = string.Empty;
        private EntryFilterMode _filterMode;
        private int _visibleEntryCount;

        // 装卸在途：进度弹窗会继续派发输入，不锁整窗就可能在动作里再点一次（并叠加第二趟安装）
        private bool _mutating;

        // 动作结束后再锁一小段时钟：动作期间点下的那些点击事件要等弹窗收起才被派发
        private double _inputLockedUntil;

        // 版本历史一问一次：值在下拉打开时才去取（每个候选一条 git）；扫分支换了头就整体作废
        private readonly Dictionary<string, List<PluginVersionRevision>> _versionHistory = new Dictionary<string, List<PluginVersionRevision>>(StringComparer.Ordinal);

        // 后台扫分支的产出：后台步只写这三个，主线程步只读
        private PluginCatalogResult _scanCatalog;
        private string _scanHeadCommit;
        private string _scanFailureReason;

        // 上次扫描的缓存已铺上屏：装载遮罩据此免显（数据在屏就不该遮）
        private bool _cacheOnScreen;

        /// <summary>
        /// 派生窗提供这一套管理器的身份、落点与文案；首次取用时缓存。
        /// </summary>
        protected abstract PackageManagerProfile CreateProfile();

        private PackageManagerProfile Profile => _profile ?? (_profile = CreateProfile());

        private string EntryNoun => Profile.EntryNoun;

        private string BranchEntryNoun => Profile.BranchEntryNoun;

        private string HostLabel => Profile.HostLabel;

        private string UngroupedNodeName => Profile.UngroupedNodeName;

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

        /// <summary>
        /// 后台扫一遍分支，主线程把结果铺上树。
        /// </summary>
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

        /// <summary>
        /// 重读账本与现场，不联网。
        /// </summary>
        private void ReloadFromDisk()
        {
            _stateRegistry = PackageManagerStateRegistry.Create(Profile.StateFileName, Profile.WorkspaceFolderName,
                Profile.CreateDefaultSettings);
            _state = _stateRegistry.Load();
            _cache = _stateRegistry.LoadCache();
            _installer = PluginInstaller.Create(_state.settings, Profile, _stateRegistry);
            _status = PackageManagerSourceStatus.Capture(_state.settings, _cache, _installer.Source);
            _settingsModel = new SourceSettingsModel(this, _state.settings);
            _devToolsModel = new DevToolsModel(this);
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

            // 源设置与开发工具只在设置面里出现：默认视图的树只列可装包
            if (_settingsFace)
            {
                tree.Add(Profile.SourceNodeName, _settingsModel);
                tree.Add(Profile.DevToolsNodeName, _devToolsModel);
                return tree;
            }

            int visible = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                PluginEntry entry = _entries[i];
                if (!entry.MatchesMode(_filterMode)) continue;
                if (!string.IsNullOrEmpty(_filter) && !entry.Matches(_filter)) continue;

                // 图标挂在叶子上：Add 返回的可能是（新建的）分组节点加叶子，取最后一个
                OdinMenuItem item = null;
                foreach (OdinMenuItem added in tree.Add(entry.GroupName + "/" + entry.DisplayName, entry))
                {
                    item = added;
                }

                if (item != null && entry.StatusIcon.HasValue)
                {
                    item.SdfIcon = entry.StatusIcon.Value;
                    item.SdfIconColor = entry.StatusIconColor;
                }

                visible++;
            }

            _visibleEntryCount = visible;

            return tree;
        }

        protected override void OnBeforeTreeRebuild(object previousSelection)
        {
            // 只记条目：设置面里选中项是设置模型本身，别让它把待恢复的条目覆盖掉
            if (previousSelection is PluginEntry entry)
            {
                _selectedEntryId = entry.EntryId;
            }
            else if (previousSelection is DevToolsModel)
            {
                _settingsNode = SettingsNode.DevTools;
            }
            else if (previousSelection is SourceSettingsModel)
            {
                _settingsNode = SettingsNode.Source;
            }
        }

        protected override void OnAfterTreeRebuild(object previousSelection)
        {
            // 同步选中：设置面/原条目当场落在新树上，不等 Odin 的下一拍延迟匹配
            if (_settingsFace)
            {
                SelectMenuItemWithValue(_settingsNode == SettingsNode.DevTools ? (object)_devToolsModel : _settingsModel);
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

        /// <summary>装卸在途或收尾冷却中：窗口整体锁输入。</summary>
        private bool IsInputLocked => _mutating || EditorApplication.timeSinceStartup < _inputLockedUntil;

        protected override void OnImGUI()
        {
            bool locked = IsInputLocked;

            // 冷却期把点击事件直接吃掉：GUI.enabled 只管按钮，树上的选中也要一并挡住
            if (locked && Event.current != null)
            {
                EventType type = Event.current.type;
                if (type == EventType.MouseDown || type == EventType.MouseUp || type == EventType.MouseDrag)
                    Event.current.Use();
            }

            // 装卸是同步长动作：整窗（工具栏、树、详情、设置面）一起禁输入，避免在动作里再点一次
            using (new EditorGUI.DisabledScope(locked))
            {
                DrawToolbar();
                DrawSourceStrip();
                base.OnImGUI();
                DrawMessage();
                DrawRefreshStatus();
            }
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

            GUILayout.Space(8f);
            var mode = (EntryFilterMode)EditorGUILayout.Popup((int)_filterMode, FILTER_LABELS,
                EditorStyles.toolbarPopup, GUILayout.Width(80f));
            if (mode != _filterMode)
            {
                _filterMode = mode;
                QueueTreeRebuild();
            }

            // 没有可更新项就不占这一格：按钮本身也点不出动作
            if (_updatableIds.Count > 0 && ToolbarButton($"Update All ({_updatableIds.Count})", 110f)) UpdateAll();

            if (_settingsFace)
            {
                if (ToolbarButton("返回列表", 80f)) SwitchFace(false);
            }
            else if (ToolbarButton("Settings", 80f)) SwitchFace(true);

            GUILayout.FlexibleSpace();
            bool filtered = !string.IsNullOrEmpty(_filter) || _filterMode != EntryFilterMode.All;
            string countLabel = filtered
                ? $"分支{BranchEntryNoun}：{_visibleEntryCount}/{_entries.Count} 个"
                : $"分支{BranchEntryNoun}：{_entries.Count} 个";
            GUILayout.Label(countLabel, HeaderStyles.IsReady ? HeaderStyles.Meta : EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 切换设置面：<c>true</c> 只列源设置，<c>false</c> 回到可装包列表。
        /// </summary>
        private void SwitchFace(bool settingsFace)
        {
            _settingsFace = settingsFace;
            QueueTreeRebuild();
        }

        private void DrawSourceStrip()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 现场细节（源/HEAD/检出目录）只在设置面报；常规面报上次刷新这类常用读数
            if (_settingsFace)
            {
                GUILayout.Label($"源：{Text(_status.RepoUrl)}#{_status.Branch}　· HEAD：{ShortSha(_status.HeadCommit)}　· " +
                                $"检出：{Text(_status.WorkspacePath)}",
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

        /// <summary>
        /// 后台刷新在途时在左下角报一句状态（有缓存的暖开窗不画装载遮罩，就靠它给反馈）。
        /// </summary>
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

        /// <summary>
        /// 重扫一遍分支（开窗自动走这条，工具栏 Refresh 也走它；在途重扫会被取消重来）。
        /// </summary>
        private void Refresh()
        {
            StartDataLoad();
        }

        /// <summary>
        /// 用上次扫分支的缓存先把列表铺上，免得开窗一片空白。
        /// </summary>
        private void ApplyCachedCatalog()
        {
            if (!_stateRegistry.TryGetCachedCatalog(_cache, _state.settings, out List<PluginManifest> manifests, out string headCommit))
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

        /// <summary>
        /// 把后台扫出的结果铺上树，并连同"上次刷新"写进本机缓存（主线程步）。
        /// </summary>
        private void ApplyScanResult()
        {
            if (_scanCatalog == null)
            {
                SetMessage(_scanFailureReason ?? "扫分支没走通", MessageType.Error);
                Repaint();
                return;
            }

            PluginCatalogResult catalog = _scanCatalog;

            // 解析判非的那几份要照面：静默少一个包会被读成"分支上没有它"
            string brokenNote = catalog.Errors.Count > 0
                ? $"　{catalog.Errors.Count} 份清单判非：" + string.Join("；", catalog.Errors)
                : string.Empty;

            if (_installer.LastFetchReachedRemote) _stateRegistry.StampFetch(_cache);

            // 缓存会被并行会话改写，落盘前重读一遍再写；账本这次不碰
            PackageManagerCacheData latestCache = _stateRegistry.LoadCache();
            latestCache.lastFetchAt = _cache.lastFetchAt;
            latestCache.reverseSyncFolder = _cache.reverseSyncFolder;
            _stateRegistry.CacheCatalog(latestCache, _state.settings, catalog, _scanHeadCommit);
            _stateRegistry.SaveCache(latestCache);
            _cache = latestCache;

            _status = PackageManagerSourceStatus.Capture(_state.settings, _cache, _installer.Source);
            _catalog = catalog;
            _versionHistory.Clear();

            if (!_installer.TryCollectUpdatable(_state, out List<string> updatable, out string updatableReason))
            {
                _updatableIds = new List<string>();
                SetMessage("可更新判定没读通：" + updatableReason + brokenNote, MessageType.Warning);
            }
            else
            {
                _updatableIds = updatable;
                string offline = _installer.LastFetchReachedRemote
                    ? string.Empty
                    : "　本轮没到远端，按本地已有的分支头列的。";
                MessageType type = catalog.Errors.Count > 0 || !string.IsNullOrEmpty(offline)
                    ? MessageType.Warning
                    : MessageType.Info;
                SetMessage($"分支上有 {catalog.Manifests.Count} 个{BranchEntryNoun}，可更新 {updatable.Count} 个。{offline}{brokenNote}", type);
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

            // 徽标要用与安装同一条判据：拿规划层试装一次，缺依赖/版本不符/地板不符才报得出来
            PluginInstaller.TryGetEngineVersion(out PluginVersion unityVersion, out _);
            PluginInstaller.TryGetHostPackageVersion(Profile.HostPackageType, out PluginVersion hostVersion, out _);
            HashSet<string> installedIds = InstalledIdSet();

            for (int i = 0; i < catalog.Manifests.Count; i++)
            {
                PluginManifest manifest = catalog.Manifests[i];
                InstallPlan probe = InstallPlanner.Plan(catalog.Manifests, installedIds, new[] { manifest.id },
                    Array.Empty<string>(), unityVersion, hostVersion);

                var entry = new PluginEntry(this, manifest, InstalledVersionOf(manifest.id),
                    _updatableIds.Contains(manifest.id), headCommit, DescribeBlocker(probe),
                    IsOutOfSupportOnly(probe));
                _entries.Add(entry);
            }
        }

        /// <summary>
        /// 拦路项是否全是"超出支持范围"：只有这一种时才用黄色警告，其余拦路仍按错误报红。
        /// </summary>
        private static bool IsOutOfSupportOnly(InstallPlan plan)
        {
            if (plan.Conflicts.Count == 0) return false;

            for (int i = 0; i < plan.Conflicts.Count; i++)
            {
                PlanConflictKind kind = plan.Conflicts[i].Kind;
                if (kind != PlanConflictKind.UnityCeilingExceeded && kind != PlanConflictKind.FrameworkCeilingExceeded)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 把规划判出的拦路项拼成一行；能装则空。
        /// </summary>
        /// <remarks>全部冲突都要报：只取第一条会把"还缺哪几个依赖"截掉，修一条才露下一条。</remarks>
        private string DescribeBlocker(InstallPlan plan)
        {
            if (plan.Conflicts.Count == 0) return null;

            var parts = new List<string>();
            for (int i = 0; i < plan.Conflicts.Count; i++)
            {
                PlanConflict conflict = plan.Conflicts[i];
                parts.Add($"{KindLabel(conflict.Kind)}：{conflict.Reason}");
            }

            return string.Join("；", parts);
        }

        /// <summary>
        /// 按台账标出每条外部依赖的归属：谁写入的、要求到的版本、当前有哪几个包在用。
        /// </summary>
        private string DescribeUpmOwnership(PluginManifest manifest)
        {
            PluginUpmDependency[] entries = manifest.upmDependencies;
            if (entries == null || entries.Length == 0) return "这个包没有外部依赖";

            var lines = new List<string>();
            for (int i = 0; i < entries.Length; i++)
            {
                UpmOwnershipData record = _stateRegistry.FindOwnership(_state, entries[i].id);
                if (record == null)
                {
                    lines.Add($"{entries[i].id}：台账里还没有（没人装过时不占位）");
                    continue;
                }

                string owners = record.owners == null || record.owners.Count == 0
                    ? "无"
                    : string.Join("、", record.owners);
                string writer = record.preexisting ? "用户手写" : "工具写入";
                lines.Add($"{entries[i].id}：{writer}，要求 {record.requiredVersion}，在用 {owners}");
            }

            return string.Join("\n", lines);
        }

        private string KindLabel(PlanConflictKind kind)
        {
            switch (kind)
            {
                case PlanConflictKind.MissingDependency: return "缺依赖";
                case PlanConflictKind.Cycle: return "依赖成环";
                case PlanConflictKind.VersionUnsatisfied: return "版本不符";
                case PlanConflictKind.UnityFloorUnmet: return "Unity 版本不符";
                case PlanConflictKind.FrameworkFloorUnmet: return HostLabel + "版本不符";
                case PlanConflictKind.UnityCeilingExceeded: return "超出 Unity 支持范围";
                case PlanConflictKind.FrameworkCeilingExceeded: return HostLabel + "侧停止提供";
                default: return kind.ToString();
            }
        }

        /// <summary>
        /// 账本里该包的已装版本；没装过为空。
        /// </summary>
        private string InstalledVersionOf(string pluginId)
        {
            List<InstalledPluginData> installed = _state?.installed;
            for (int i = 0; installed != null && i < installed.Count; i++)
            {
                if (string.Equals(installed[i].id, pluginId, StringComparison.Ordinal)) return installed[i].version;
            }

            return null;
        }

        /// <summary>
        /// 依赖行用：某个包的已装状态一句话。
        /// </summary>
        private string DescribeInstalled(string pluginId)
        {
            string version = InstalledVersionOf(pluginId);
            return string.IsNullOrEmpty(version) ? "未装" : $"已装 {version}";
        }

        /// <summary>
        /// 分支目录里该包的版本；不在目录里为空。
        /// </summary>
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

        /// <summary>
        /// 装卸是同步的，编辑器会被占住一段时间；进度条把当前阶段摆出来，至少能看清卡在哪一步。
        /// </summary>
        /// <remarks>动作在途时再来一次直接判空：进度弹窗会继续派发输入，不禁用就可能叠第二趟。</remarks>
        private PluginInstallReport RunWithProgress(string title, Func<PluginInstallReport> action)
        {
            if (IsInputLocked) return null;

            PluginInstaller installer = _installer;
            bool localStateRefreshed = false;
            _mutating = true;
            try
            {
                installer.Progress = (value, stage) =>
                {
                    // 账本已落盘、刷新还没开始：先把列表与详情摆到新状态，
                    // 否则刷新触发的重编译期间窗口会一直停在"未安装"的样子
                    if (!localStateRefreshed && value >= LOCAL_STATE_REFRESH_PROGRESS)
                    {
                        localStateRefreshed = true;
                        RefreshLocalState();
                    }

                    EditorUtility.DisplayProgressBar(title, stage, value);
                };
                return action();
            }
            finally
            {
                installer.Progress = null;
                EditorUtility.ClearProgressBar();
                _mutating = false;

                // 动作期间点下的点击事件要等进度弹窗收起才派发：再锁半秒，别让它们变成第二趟
                _inputLockedUntil = EditorApplication.timeSinceStartup + MUTATION_INPUT_GRACE_SECONDS;
                Repaint();
            }
        }

        /// <summary>
        /// 装单个包，依赖闭包由规划层带出。
        /// </summary>
        private void InstallRequested(string pluginId, bool force)
        {
            PluginInstallReport report = RunWithProgress($"安装 {pluginId}", () => _installer.Install(pluginId, force));
            if (report == null) return;

            AfterMutation(report, $"装了 {pluginId}");
        }

        /// <summary>
        /// 卸单个包，依赖方在场时先问。
        /// </summary>
        private void UninstallRequested(string pluginId)
        {
            if (IsInputLocked) return;

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
                int choice = EditorUtility.DisplayDialogComplex("还有包依赖它",
                    $"卸载 {pluginId} 后，这些在场包会缺依赖：{string.Join("、", dependents)}",
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

            if (report.Ok && !cascade && report.Dependents.Count > 0)
            {
                SetMessage($"只卸了 {pluginId}，这些在场包仍缺它：{string.Join("、", report.Dependents)}", MessageType.Warning);
                Repaint();
            }
        }

        private void UpdateAll()
        {
            if (IsInputLocked) return;

            if (_updatableIds.Count == 0)
            {
                SetMessage("没有可更新的包。", MessageType.Info);
                return;
            }

            // 与详情面同一判据：版本过档的包不参与批量更新，且逐条点名为什么被跳，不静默少一个
            var allowed = new List<string>();
            var skipped = new List<string>();
            for (int i = 0; i < _updatableIds.Count; i++)
            {
                string id = _updatableIds[i];
                PluginEntry entry = FindEntry(id);
                if (entry == null || entry.CanInstall) allowed.Add(id);
                else skipped.Add($"{id}：{entry.BlockerText}");
            }

            if (allowed.Count == 0)
            {
                SetMessage("可更新的包都装不了：" + string.Join("；", skipped), MessageType.Warning);
                return;
            }

            if (!ConfirmUpdateAll(allowed)) return;

            string skippedNote = skipped.Count == 0
                ? string.Empty
                : $"，跳过 {skipped.Count} 个装不了的：{string.Join("；", skipped)}";
            PluginInstallReport report = RunWithProgress($"更新 {allowed.Count} 个{BranchEntryNoun}", () => _installer.Install(allowed, true));
            if (report == null) return;

            AfterMutation(report, $"更新了 {report.TouchedPluginIds.Count} 个" + skippedNote);
        }

        /// <summary>按 id 取当前目录里那条包的详情模型；不在场返回 null。</summary>
        private PluginEntry FindEntry(string pluginId)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].EntryId, pluginId, StringComparison.Ordinal)) return _entries[i];
            }

            return null;
        }

        /// <summary>
        /// 更新前把「已装 → 分支」逐条摆出来问一次。
        /// </summary>
        private bool ConfirmUpdateAll(List<string> ids)
        {
            var lines = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                lines.Add($"{id}　{InstalledVersionOf(id) ?? "?"} → {BranchVersionOf(id) ?? "?"}");
            }

            lines.Add(string.Empty);
            lines.Add("更新的外部依赖按归属台账回收/写入，装完刷新一次。");
            return EditorUtility.DisplayDialog($"更新 {ids.Count} 个{BranchEntryNoun}", string.Join("\n", lines), "继续", "取消");
        }

        /// <summary>
        /// 装所选的历史版本：先清目录再按该修订物化，账本记该修订。
        /// </summary>
        private void InstallVersionRequested(string pluginId, string revision, string version)
        {
            PluginInstallReport report = RunWithProgress($"安装 {pluginId} {version}", () => _installer.InstallAtRevision(pluginId, revision, version));
            if (report == null) return;

            AfterMutation(report, $"装了 {pluginId} 的历史版本 {version}");
        }

        /// <summary>
        /// 取某个包的版本历史（按 id 缓存一次）；列不出来时交出 false，下拉里就只剩分支头那一条。
        /// </summary>
        internal bool TryGetVersionHistory(string pluginId, out List<PluginVersionRevision> history)
        {
            if (_versionHistory.TryGetValue(pluginId, out history)) return history != null;

            if (_installer == null || !_installer.Source.TryListManifestHistory(pluginId, out history, out _)) history = null;

            _versionHistory[pluginId] = history;
            return history != null;
        }

        /// <summary>
        /// 单包更新：强制重装一遍，可选先清目录（干净更新）。
        /// </summary>
        private void UpdateRequested(string pluginId, bool removeFirst)
        {
            PluginInstallReport report = RunWithProgress($"更新 {pluginId}", () => _installer.Install(pluginId, true, removeFirst));
            if (report == null) return;

            AfterMutation(report, $"更新了 {pluginId}");
        }

        /// <summary>
        /// 装卸后的统一收尾：报告失败就停在原状；成功则按账本立即重算（域重载后还会再取一次现场读数）。
        /// </summary>
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
                for (int i = 0; i < report.Notes.Count; i++) Debug.Log(Profile.LogPrefix + " " + report.Notes[i]);
            }

            RefreshLocalState();
            SetMessage(successText + "　" + report.Summary(), MessageType.Info);
            Repaint();
        }

        /// <summary>
        /// 把已装插件的改动反向写回本地 git 工作树：每个插件落到目标目录下的 <c>&lt;id&gt;/</c>。
        /// </summary>
        /// <remarks>目标必须是 git 工作树；同步按镜像走，目标里多出的文件会删掉，安装生成的根 <c>package.json</c> 不参与。</remarks>
        private void SyncRequested()
        {
            if (IsInputLocked) return;

            string targetRoot = _cache?.reverseSyncFolder;
            if (string.IsNullOrEmpty(targetRoot))
            {
                SetMessage($"先在「{Profile.DevToolsNodeName}」里设置反向同步文件夹。", MessageType.Warning);
                return;
            }

            if (!Directory.Exists(targetRoot))
            {
                SetMessage($"反向同步文件夹不存在：{targetRoot}", MessageType.Error);
                return;
            }

            if (!TryValidateSyncWorktree(targetRoot, out string gitReason))
            {
                SetMessage(gitReason, MessageType.Error);
                return;
            }

            var ids = new List<string>();
            List<InstalledPluginData> installed = _state?.installed;
            for (int i = 0; installed != null && i < installed.Count; i++)
            {
                if (!string.IsNullOrEmpty(installed[i]?.id)) ids.Add(installed[i].id);
            }

            if (ids.Count == 0)
            {
                SetMessage($"账本里还没有已装{EntryNoun}，没有可同步的内容。", MessageType.Info);
                return;
            }

            string body = $"目标：{targetRoot}\n\n" +
                          $"把 {ids.Count} 个已装{EntryNoun}的目录写回目标下的 <id>/：覆盖同名文件、删除多出的文件；" +
                          "安装时生成的根 package.json 及其 .meta 不参与。";
            if (!EditorUtility.DisplayDialog($"同步 {ids.Count} 个{EntryNoun}到本地 git", body, "继续", "取消")) return;

            var reports = new List<PluginSyncReport>(ids.Count);
            _mutating = true;
            try
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    EditorUtility.DisplayProgressBar($"同步{EntryNoun}",
                        $"{ids[i]}（{i + 1}/{ids.Count}）", (i + 1f) / ids.Count);
                    reports.Add(PluginFolderSync.Sync(ids[i], _installer.InstallFolderPath(ids[i]), targetRoot));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _mutating = false;
                _inputLockedUntil = EditorApplication.timeSinceStartup + MUTATION_INPUT_GRACE_SECONDS;
                Repaint();
            }

            int succeeded = 0;
            string firstFailure = null;
            for (int i = 0; i < reports.Count; i++)
            {
                PluginSyncReport report = reports[i];
                for (int j = 0; j < report.Notes.Count; j++) Debug.Log(Profile.LogPrefix + " " + report.Notes[j]);

                if (report.Ok)
                {
                    succeeded++;
                    Debug.Log(Profile.LogPrefix + $" 同步 {report.PluginId}：{report.Summary()}");
                    continue;
                }

                if (firstFailure == null) firstFailure = $"{report.PluginId}：{report.FailureReason}";
            }

            if (succeeded == reports.Count)
            {
                SetMessage($"已同步 {succeeded} 个{EntryNoun}到 {targetRoot}", MessageType.Info);
            }
            else
            {
                SetMessage($"同步完成：成功 {succeeded}，失败 {reports.Count - succeeded}。{firstFailure}", MessageType.Error);
            }

            Repaint();
        }

        /// <summary>目标目录是不是本地 git 工作树；读不通时把 git 的原因一并交回。</summary>
        private bool TryValidateSyncWorktree(string folder, out string failureReason)
        {
            GitRunResult result = GitProcessUtility.Run(_state.settings.gitPath, folder,
                "rev-parse --is-inside-work-tree", SYNC_GIT_QUERY_TIMEOUT_MILLISECONDS);
            if (result.Ok && string.Equals(result.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                failureReason = null;
                return true;
            }

            failureReason = result.Ok
                ? $"反向同步文件夹不是 git 工作树：{folder}"
                : $"反向同步文件夹读不成 git 工作树：{folder}（{result.FailureReason()}）";
            return false;
        }

        /// <summary>
        /// 装卸成功后按本机账本重算一遍：重读状态、重算可更新集、用手里的目录重建行。
        /// </summary>
        /// <remarks>不走网络，也不等域重载：否则列表会停在旧读数上继续显示"可升级"。</remarks>
        private void RefreshLocalState()
        {
            _state = _stateRegistry.Load();
            _installer = PluginInstaller.Create(_state.settings, Profile, _stateRegistry);
            _status = PackageManagerSourceStatus.Capture(_state.settings, _cache, _installer.Source);
            _settingsModel = new SourceSettingsModel(this, _state.settings);
            _devToolsModel = new DevToolsModel(this);

            _updatableIds = new List<string>();
            if (_installer.TryCollectUpdatable(_state, out List<string> updatable, out _)) _updatableIds = updatable;

            if (_catalog != null) RefreshEntryStates(_catalog);
            else ApplyCachedCatalog();

            QueueTreeRebuild();
            Repaint();
        }

        /// <summary>
        /// 目录没变时只就地换每行的本机读数与图标：Odin 树持有这些条目对象，
        /// 整表换新要等一次重建才上屏，而刷新触发的重编译可能把那次重建一直压到域重载。
        /// </summary>
        private void RefreshEntryStates(PluginCatalogResult catalog)
        {
            PluginInstaller.TryGetEngineVersion(out PluginVersion unityVersion, out _);
            PluginInstaller.TryGetHostPackageVersion(Profile.HostPackageType, out PluginVersion hostVersion, out _);
            HashSet<string> installedIds = InstalledIdSet();

            bool missing = _entries.Count != catalog.Manifests.Count;
            for (int i = 0; i < catalog.Manifests.Count; i++)
            {
                PluginManifest manifest = catalog.Manifests[i];
                InstallPlan probe = InstallPlanner.Plan(catalog.Manifests, installedIds, new[] { manifest.id },
                    Array.Empty<string>(), unityVersion, hostVersion);

                PluginEntry entry = FindEntry(manifest.id);
                if (entry == null)
                {
                    missing = true;
                    continue;
                }

                entry.ApplyLocalState(InstalledVersionOf(manifest.id), _updatableIds.Contains(manifest.id),
                    DescribeBlocker(probe), IsOutOfSupportOnly(probe));
            }

            // 目录与条目对不上（例如扫描刚换了头）就退回整表重建
            if (missing)
            {
                RebuildEntries(catalog, _status.HeadCommit);
                return;
            }

            RefreshEntryIcons();
        }

        /// <summary>把就地更新后的状态图标写回已有叶子：列表结构不动，只换图标与颜色。</summary>
        private void RefreshEntryIcons()
        {
            OdinMenuTree tree = MenuTree;
            if (tree == null) return;

            foreach (OdinMenuItem item in tree.EnumerateTree())
            {
                if (!(item.Value is PluginEntry entry)) continue;

                item.SdfIcon = entry.StatusIcon ?? SdfIconType.None;
                if (entry.StatusIcon.HasValue) item.SdfIconColor = entry.StatusIconColor;
            }
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
        /// 一个分支上的包在菜单里的那一行，详情面就是它本身。
        /// </summary>
        /// <remarks>清单里的说明走类级 <c>[InfoBox]</c> 展示：只读、折行，且没写说明时不占地方。</remarks>
        [InfoBox("$Description", InfoMessageType.None, nameof(HasDescription))]
        [Serializable]
        private sealed class PluginEntry
        {
            [NonSerialized] private readonly PackageManagerWindowBase _window;
            [NonSerialized] private readonly PluginManifest _manifest;
            [NonSerialized] private readonly string _headCommit;

            // 本机读数可在原位刷新：Odin 树持有条目对象，整表换新要等一次重建才上屏
            [NonSerialized] private string _installedVersion;
            [NonSerialized] private bool _updateAvailable;
            [NonSerialized] private string _blocker;
            [NonSerialized] private bool _outOfSupport;

            internal PluginEntry(PackageManagerWindowBase window, PluginManifest manifest, string installedVersion,
                bool updateAvailable, string headCommit, string blocker, bool outOfSupport)
            {
                _window = window;
                _manifest = manifest;
                _installedVersion = installedVersion;
                _updateAvailable = updateAvailable;
                _headCommit = headCommit;
                _blocker = blocker;
                _outOfSupport = outOfSupport;
                SelectedVersion = manifest.version;
            }

            /// <summary>
            /// 就地换本机读数：目录没变时树里的对象不换，改完就能在下一帧看到新图标与动作。
            /// </summary>
            internal void ApplyLocalState(string installedVersion, bool updateAvailable, string blocker, bool outOfSupport)
            {
                _installedVersion = installedVersion;
                _updateAvailable = updateAvailable;
                _blocker = blocker;
                _outOfSupport = outOfSupport;
            }

            [BoxGroup("概况"), ShowInInspector, LabelText("$EntryLabel")]
            internal string DisplayName => string.IsNullOrEmpty(_manifest.name) ? _manifest.id : _manifest.name;

            [BoxGroup("概况"), ShowInInspector, LabelText("分组")]
            internal string GroupName => string.IsNullOrEmpty(_manifest.function)
                ? (_window != null ? _window.UngroupedNodeName : "未分组")
                : _manifest.function;

            [BoxGroup("概况"), ShowInInspector, LabelText("状态")]
            internal string StatusText => DescribeStatus();

            [BoxGroup("概况"), ShowInInspector, LabelText("分支版本")]
            internal string Version => _manifest.version;

            [BoxGroup("概况"), ShowInInspector, LabelText("Unity 区间")]
            internal string UnityRange => DescribeRange("Unity", _manifest.minUnityVersion, _manifest.maxUnityVersion);

            [BoxGroup("概况"), ShowInInspector, LabelText("$HostRangeLabel")]
            internal string HostRange => DescribeRange(_window != null ? _window.HostLabel : "框架",
                _manifest.minFrameworkVersion, _manifest.maxFrameworkVersion);

            /// <summary>条目行的标签：随管理器称法变化（插件/模块）。</summary>
            internal string EntryLabel => _window != null ? _window.EntryNoun : "插件";

            /// <summary>宿主版本区间的标签：随管理器称法变化（框架区间/本体区间）。</summary>
            internal string HostRangeLabel => (_window != null ? _window.HostLabel : "框架") + "区间";

            /// <summary>
            /// 版本区间的一行读数：<c>[min, max)</c>，上限写作"起不再提供"以免读成"含这一档还能用"。
            /// </summary>
            private static string DescribeRange(string label, string floor, string ceiling)
            {
                string text = label + " " + floor + "+";
                return string.IsNullOrEmpty(ceiling) ? text : $"{text}，{ceiling} 起不再提供";
            }

            [BoxGroup("概况"), ShowInInspector, LabelText("分支头")]
            internal string HeadCommit => ShortSha(_headCommit);

            [BoxGroup("概况"), ShowInInspector, LabelText("来源路径")]
            internal string SourcePath => string.IsNullOrEmpty(_manifest.SourceTreePath)
                ? _manifest.id + "/" + _manifest.id + PluginManifest.MANIFEST_SUFFIX
                : _manifest.SourceTreePath;

            [BoxGroup("标签"), ShowInInspector, LabelText("标签")]
            internal string TagsText => Join(_manifest.tags);

            // 依赖三条都画成多行只读文本：一条一行，宽度不够自动折行（DisplayAsString 的无参重载是裁切溢出）
            [BoxGroup("依赖"), ShowInInspector, LabelText("包间依赖"), DisplayAsString(false)]
            internal string DependenciesText => DescribeDependencies(_manifest);

            [BoxGroup("依赖"), ShowInInspector, LabelText("外部依赖"), DisplayAsString(false)]
            internal string UpmText => DescribeUpm(_manifest);

            [BoxGroup("依赖"), ShowInInspector, LabelText("归属台账"), DisplayAsString(false)]
            internal string UpmOwnershipText => _window.DescribeUpmOwnership(_manifest);

            [BoxGroup("操作"), ShowInInspector, LabelText("安装版本"), ValueDropdown(nameof(VersionOptions))]
            [Tooltip("默认分支头那一条；选历史版本后用「安装所选版本」——先清目录再按那次修订物化，因此可以降级。")]
            internal string SelectedVersion { get; set; }

            [BoxGroup("操作"), ShowInInspector, LabelText("更新前先清目录")]
            [Tooltip("勾上则先删掉包目录再按分支头重新物化：清掉新版本不再产出的文件。")]
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

            /// <summary>版本区间与依赖都过得了才允许安装类动作；详情面与 Update All 共用这一条读数。</summary>
            /// <remarks>判据来源就是徽标那条 _blocker——它由规划层试装得出，与真正拒绝安装的是同一条，
            /// 所以置灰的理由与拒绝的理由不可能各说各话。</remarks>
            internal bool CanInstall => string.IsNullOrEmpty(_blocker);

            /// <summary>拦路的原文（能装为空），给 Update All 的跳过点名取用。</summary>
            internal string BlockerText => _blocker;

            /// <summary>
            /// 菜单行前的状态图标：超出支持范围黄警告 &gt; 其他拦路红叉 &gt; 可升级 &gt; 已装；未装且无拦路不画图标。
            /// </summary>
            internal SdfIconType? StatusIcon
            {
                get
                {
                    if (!CanInstall) return _outOfSupport ? SdfIconType.ExclamationCircleFill : SdfIconType.XCircleFill;
                    if (_updateAvailable) return SdfIconType.ArrowUpCircleFill;
                    return IsInstalled ? SdfIconType.CheckCircleFill : (SdfIconType?)null;
                }
            }

            /// <summary>状态图标的前景色，与图标一一对应。</summary>
            internal Color StatusIconColor
            {
                get
                {
                    if (!CanInstall) return _outOfSupport ? OUT_OF_SUPPORT_ICON_COLOR : BLOCKED_ICON_COLOR;
                    if (_updateAvailable) return UPGRADABLE_ICON_COLOR;
                    return INSTALLED_ICON_COLOR;
                }
            }

            /// <summary>列表筛选口径的命中判据；文本筛选另由 <see cref="Matches"/> 叠加。</summary>
            internal bool MatchesMode(EntryFilterMode mode)
            {
                switch (mode)
                {
                    case EntryFilterMode.NotInstalled: return !IsInstalled;
                    case EntryFilterMode.Upgradable: return _updateAvailable;
                    default: return true;
                }
            }

            /// <summary>不能装时给操作区那一行的正文；能装时为空、整行不画。</summary>
            private string VersionBlockerBody => CanInstall ? string.Empty : _blocker;

            [BoxGroup("操作"), Button("安装", ButtonSizes.Large)]
            [HideIf(nameof(IsInstalled))]
            [EnableIf(nameof(CanInstall))]
            private void Install() => _window?.InstallRequested(_manifest.id, false);

            [BoxGroup("操作"), Button("更新到分支版本", ButtonSizes.Large)]
            [ShowIf(nameof(UpdateAvailable))]
            [EnableIf(nameof(CanInstall))]
            private void UpdateToBranch() => _window?.UpdateRequested(_manifest.id, RemoveBeforeUpdate);

            [BoxGroup("操作"), Button("重装（忽略已装判定）", ButtonSizes.Large)]
            [ShowIf(nameof(IsInstalled))]
            [EnableIf(nameof(CanInstall))]
            private void Reinstall() => _window?.InstallRequested(_manifest.id, true);

            [BoxGroup("操作"), Button("卸载")]
            [ShowIf(nameof(IsInstalled))]
            private void Uninstall() => _window?.UninstallRequested(_manifest.id);

            [BoxGroup("操作"), Button("安装所选版本", ButtonSizes.Large)]
            [ShowIf(nameof(SelectedVersionDiffersFromHead))]
            [EnableIf(nameof(CanInstall))]
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

            /// <summary>
            /// 装不了的成因：✕ 图标加原因，排在动作按钮之后；能装时整行不画。
            /// </summary>
            /// <remarks>卸载不在拦的范围里——版本过档了也得让人把已装的拆掉，所以那一颗按钮不接 <see cref="CanInstall"/>。</remarks>
            [BoxGroup("操作"), ShowInInspector, LabelText("不能安装的原因"), PropertyOrder(100)]
            [HideIf(nameof(CanInstall))]
            internal string InstallBlocker => "✕ " + VersionBlockerBody;

            private string DescribeStatus()
            {
                if (!string.IsNullOrEmpty(_blocker)) return _blocker;
                if (!IsInstalled) return "未装";

                return _updateAvailable
                    ? $"已装 {_installedVersion}　↑ 可更新到 {_manifest.version}"
                    : $"已装 {_installedVersion}";
            }

            /// <summary>
            /// 版本候选：分支头那一条，加上清单历史上出现过的版本（打开下拉时才去问 git）。
            /// </summary>
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

            /// <summary>
            /// 所选版本那次修订：分支头版本用分支头提交，其余去历史里找。
            /// </summary>
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

            /// <summary>
            /// 筛选命中：名字/标识/分组/说明/标签，任一处不区分大小写包含即可。
            /// </summary>
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

                return string.Join("\n", parts);
            }

            private static string DescribeUpm(PluginManifest manifest)
            {
                PluginUpmDependency[] dependencies = manifest.upmDependencies;
                if (dependencies == null || dependencies.Length == 0) return "—";

                var parts = new List<string>();
                for (int i = 0; i < dependencies.Length; i++) parts.Add($"{dependencies[i].id} {dependencies[i].version}");
                return string.Join("\n", parts);
            }
        }

        /// <summary>
        /// 开发工具页：反向同步落点，以及把已装插件写回本地 git 的动作。
        /// </summary>
        [Serializable]
        private sealed class DevToolsModel
        {
            [NonSerialized] private readonly PackageManagerWindowBase _window;

            internal DevToolsModel(PackageManagerWindowBase window)
            {
                _window = window;
            }

            [ShowInInspector, LabelText("说明"), DisplayAsString(false)]
            internal string Description =>
                "把 Packages 下已装插件的改动反向写回本地 git 工作树：目标目录下的 <id>/ 等于分支里的插件目录。" +
                "同步按镜像走，覆盖同名文件并删除多出的文件；安装时生成的根 package.json 及其 .meta 不参与。" +
                "同步落点里的 ~ 表示省略的工程绝对路径前缀，箭头后是相对项目 Assets 的路径。";

            [ShowInInspector, LabelText("反向同步文件夹")]
            [FolderPath(AbsolutePath = true)]
            [Tooltip("本地 git 工作树根目录；同步时把已装插件的改动写到它下面的 <id>/。")]
            internal string ReverseSyncFolder
            {
                get => _window?._cache?.reverseSyncFolder;
                set { if (_window?._cache != null) _window._cache.reverseSyncFolder = value; }
            }

            [ShowInInspector, LabelText("已装插件"), DisplayAsString(false)]
            internal string InstalledSummary => DescribeInstalled();

            [ShowInInspector, LabelText("同步落点"), DisplayAsString(false)]
            internal string SyncTargets => DescribeTargets();

            [Button("保存设置")]
            private void Save()
            {
                PackageManagerWindowBase window = _window;
                if (window == null) return;

                window._stateRegistry.SaveCache(window._cache);
                window.QueueTreeRebuild();
                window.SetMessage("同步设置已写入本机缓存。", MessageType.Info);
            }

            [Button("同步全部已装插件", ButtonSizes.Large)]
            private void SyncAll() => _window?.SyncRequested();

            private string DescribeInstalled()
            {
                List<InstalledPluginData> installed = _window?._state?.installed;
                if (installed == null || installed.Count == 0) return "账本里还没有已装插件。";

                var parts = new List<string>(installed.Count);
                for (int i = 0; i < installed.Count; i++)
                {
                    InstalledPluginData entry = installed[i];
                    if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

                    parts.Add(string.IsNullOrEmpty(entry.version) ? entry.id : entry.id + " " + entry.version);
                }

                return parts.Count == 0 ? "账本里还没有已装插件。" : string.Join("\n", parts);
            }

            private string DescribeTargets()
            {
                string root = _window?._cache?.reverseSyncFolder;
                if (string.IsNullOrEmpty(root)) return "先设置反向同步文件夹。";

                List<InstalledPluginData> installed = _window?._state?.installed;
                if (installed == null || installed.Count == 0) return "—";

                var parts = new List<string>(installed.Count);
                for (int i = 0; i < installed.Count; i++)
                {
                    InstalledPluginData entry = installed[i];
                    if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

                    string source = _window?._installer?.InstallFolderPath(entry.id) ?? "<未就绪>";
                    parts.Add($"{entry.id}：{CollapseToPackages(source)} → {DescribeTargetPath(root, entry.id)}");
                }

                return parts.Count == 0 ? "—" : string.Join("\n", parts);
            }

            /// <summary>把安装目录收敛成从 <c>Packages/</c> 起的形状：前面的工程绝对路径用 ~ 省略。</summary>
            private static string CollapseToPackages(string path)
            {
                string normalized = (path ?? string.Empty).Replace('\\', '/');
                int index = normalized.LastIndexOf("/Packages/", StringComparison.OrdinalIgnoreCase);
                return index < 0 ? normalized : "~" + normalized.Substring(index);
            }

            /// <summary>同步落点相对项目 <c>Assets/</c> 的路径；路径形状不合法时退回插件目录名。</summary>
            private static string DescribeTargetPath(string root, string pluginId)
            {
                try
                {
                    return RelativeToAssets(Path.Combine(root, pluginId));
                }
                catch (Exception)
                {
                    return pluginId + "/";
                }
            }

            /// <summary>从项目 <c>Assets/</c> 出发表示目标路径；跨盘符时交出绝对路径。</summary>
            private static string RelativeToAssets(string path)
            {
                string assetsRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Assets"))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullPath = Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                string[] baseParts = assetsRoot.Replace('\\', '/').Split('/');
                string[] targetParts = fullPath.Replace('\\', '/').Split('/');

                // 不同盘符算不出相对路径，原样交出绝对路径
                if (!string.Equals(baseParts[0], targetParts[0], StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath.Replace('\\', '/');
                }

                int common = 0;
                while (common < baseParts.Length && common < targetParts.Length
                       && string.Equals(baseParts[common], targetParts[common], StringComparison.OrdinalIgnoreCase))
                {
                    common++;
                }

                var parts = new List<string>(baseParts.Length + targetParts.Length);
                for (int i = common; i < baseParts.Length; i++)
                {
                    if (baseParts[i].Length > 0) parts.Add("..");
                }

                for (int i = common; i < targetParts.Length; i++)
                {
                    if (targetParts[i].Length > 0) parts.Add(targetParts[i]);
                }

                return parts.Count == 0 ? "." : string.Join("/", parts);
            }
        }

        /// <summary>
        /// 源地址、分支与 git 可执行文件的可编辑设置，就地写在窗口里而不另开设置页。
        /// </summary>
        [Serializable]
        private sealed class SourceSettingsModel
        {
            [NonSerialized] private readonly PackageManagerWindowBase _window;
            [NonSerialized] private readonly PackageManagerSettingsData _settings;

            internal SourceSettingsModel(PackageManagerWindowBase window, PackageManagerSettingsData settings)
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
            internal string StatePath => _window?._stateRegistry.StatePath;

            [ShowInInspector, LabelText("本机缓存")]
            internal string CachePath => _window?._stateRegistry.CachePath;

            [Button("保存设置")]
            private void Save()
            {
                _window?._stateRegistry.Save(_settings == null ? new PackageManagerStateData() : _window._state);
                _window?.ReloadFromDisk();
                _window?.QueueTreeRebuild();
                _window?.SetMessage("设置已写入状态文件。", MessageType.Info);
            }
        }
    }
}
