using System;
using System.Collections.Generic;
using Moirai.Atropos.Debugger;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// GameObject 池信息窗口（摘要常驻 + 池视图常驻：侧边栏同款 Foldout 折叠浏览、计数器告警、实例清单与冲刷操作）。
    /// <para>摘要卡与池视图构建一次常驻——轮询仅原地刷新数值，实例行仅在展开期间按轮询重建；池集合经签名哈希检测变化时才整体重建，交互不落在重建边界被吞掉。</para>
    /// </summary>
    public sealed class GameObjectPoolServiceDebuggerWindow : ScrollableDebuggerWindowBase
    {
        #region 常量 [CONSTANTS]

        private const float REFRESH_INTERVAL = 0.5f;
        private const int INITIAL_SNAPSHOT_BUFFER_SIZE = 32;
        private const int MAX_LISTED_INSTANCES = 32;

        /// <summary>池集合签名哈希种子（顺序敏感：新建/销毁均会改变签名）。</summary>
        private const int POOL_SET_HASH_SEED = 17;

        /// <summary>GameObject 池窗口是否显示实例清单</summary>
        private const string POOL_SHOW_INSTANCES = "Debugger.GameObjectPool.ShowInstances";

        #endregion

        #region 字段 [FIELDS]

        private readonly HashSet<string> _expandedPools = new HashSet<string>();
        private readonly Dictionary<string, PoolView> _poolViews = new Dictionary<string, PoolView>();
        private GameObjectPoolSnapshot[] _snapshotBuffer = new GameObjectPoolSnapshot[INITIAL_SNAPSHOT_BUFFER_SIZE];
        private int _snapshotCount;

        private VisualElement _listRoot;
        private Label _noMatchHint;
        private Button _readyButton;
        private Button _poolsButton;
        private Button _loadedPrefabsButton;
        private Button _instancesButton;
        private Button _activeButton;
        private Button _inactiveButton;
        private Button _pendingMaintenanceButton;

        private bool _showInstances = true;
        private bool _hideEmpty;
        private bool _pause;
        private string _searchText = string.Empty;

        private float _countdown;
        private int _poolCount;
        private int _poolSetHash;
        private bool _viewsBuilt;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            _showInstances = SettingUtility.GetBool(POOL_SHOW_INSTANCES, true);

            if (!GameObjectPoolService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("GameObject Pool Information"));
                root.Add(DebuggerUI.CreateHintLabel("GameObjectPoolService is not registered (opt-in service)."));
                return;
            }

            VisualElement summaryCard = AddSection(root, "GameObject Pool Information");
            AddRow(summaryCard, "Ready", "No", out _readyButton);
            AddRow(summaryCard, "Pools", "0", out _poolsButton);
            AddRow(summaryCard, "Loaded Prefabs", "0", out _loadedPrefabsButton);
            AddRow(summaryCard, "Instances", "0", out _instancesButton);
            AddRow(summaryCard, "Active Instances", "0", out _activeButton);
            AddRow(summaryCard, "Inactive Instances", "0", out _inactiveButton);
            AddRow(summaryCard, "Pending Maintenance", "0", out _pendingMaintenanceButton);
            BuildSummaryToolbar(summaryCard);

            _listRoot = new VisualElement();
            _listRoot.style.flexDirection = FlexDirection.Column;
            root.Add(_listRoot);

            _noMatchHint = DebuggerUI.CreateHintLabel("No pooled runtime instances exist yet.");
            _noMatchHint.style.display = DisplayStyle.None;

            Refresh();
        }

        /// <inheritdoc />
        public override void OnEnter()
        {
            // 重新进入窗口立即刷新一次，避免展示陈旧内容
            _countdown = 0f;
        }

        /// <inheritdoc />
        public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            // 摘要卡尚未构建（服务未注册或视图未挂载）或已暂停——跳过刷新
            if (_poolsButton == null || _pause)
            {
                return;
            }

            _countdown -= realElapseSeconds;
            if (_countdown > 0f)
            {
                return;
            }

            _countdown = REFRESH_INTERVAL;
            Refresh();
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            _poolViews.Clear();
            _expandedPools.Clear();
            _viewsBuilt = false;
            _listRoot = null;
            _noMatchHint = null;
            _readyButton = null;
            _poolsButton = null;
            _loadedPrefabsButton = null;
            _instancesButton = null;
            _activeButton = null;
            _inactiveButton = null;
            _pendingMaintenanceButton = null;
        }

        #endregion

        #region 构建摘要卡 [BUILD SUMMARY]

        private void BuildSummaryToolbar(VisualElement card)
        {
            VisualElement toolbar = DebuggerUI.CreateToolbarRow();

            TextField searchField = DebuggerUI.CreateSearchField("Filter pools", value =>
            {
                _searchText = value ?? string.Empty;
                ApplyFilter();
            });
            searchField.AddToClassList("dbg-search");
            VisualElement searchWrap = (VisualElement)searchField.userData;
            searchWrap.style.width = 180f;
            searchWrap.style.marginRight = 8f;
            toolbar.Add(searchWrap);

            toolbar.Add(DebuggerUI.CreateFilterChip("Instances", _showInstances, DebuggerUI.EChipStyle.Accent, value =>
            {
                _showInstances = value;
                SettingUtility.SetBool(POOL_SHOW_INSTANCES, value);
                RefreshInstanceRows();
            }));

            toolbar.Add(DebuggerUI.CreateFilterChip("Hide Empty", _hideEmpty, DebuggerUI.EChipStyle.Warning, value =>
            {
                _hideEmpty = value;
                ApplyFilter();
            }));

            toolbar.Add(DebuggerUI.CreateFilterChip("Pause", _pause, DebuggerUI.EChipStyle.Info, value =>
            {
                _pause = value;
                if (!_pause)
                {
                    _countdown = 0f;
                }
            }));

            toolbar.Add(DebuggerUI.CreateToolbarButton("Expand All", ExpandAllPools));
            toolbar.Add(DebuggerUI.CreateToolbarButton("Collapse All", CollapseAllPools));
            toolbar.Add(DebuggerUI.CreateToolbarButton("Refresh", Refresh));
            toolbar.Add(DebuggerUI.CreateToolbarButton("Flush All", () =>
            {
                GameObjectPoolService.FlushAll();
                Refresh();
            }, DebuggerUI.EButtonStyle.Danger));

            card.Add(toolbar);
        }

        private void RefreshSummary()
        {
            GameObjectPoolSummarySnapshot summary = GameObjectPoolService.GetDebugSummary();
            _readyButton.text = summary.IsReady ? "Yes" : "No";
            SetSeverity(_readyButton, false, !summary.IsReady);

            _poolsButton.text = summary.PoolCount.ToString();
            _loadedPrefabsButton.text = summary.LoadedPrefabCount.ToString();
            _instancesButton.text = summary.TotalInstanceCount.ToString();

            _activeButton.text = summary.ActiveInstanceCount.ToString();
            SetSeverity(_activeButton, summary.ActiveInstanceCount > 0, false);

            _inactiveButton.text = summary.InactiveInstanceCount.ToString();

            _pendingMaintenanceButton.text = summary.PendingMaintenanceCount.ToString();
            SetSeverity(_pendingMaintenanceButton, summary.PendingMaintenanceCount > 0, false);
        }

        #endregion

        #region 刷新 [REFRESH]

        private void Refresh()
        {
            if (_poolsButton == null)
            {
                return;
            }

            int poolCount = FetchSnapshots();
            RefreshSummary();

            if (IsPoolSetStale(poolCount))
            {
                RebuildPoolViews(poolCount);
            }

            for (int i = 0; i < poolCount; i++)
            {
                GameObjectPoolSnapshot snapshot = _snapshotBuffer[i];
                if (snapshot == null || !_poolViews.TryGetValue(GetPoolKey(snapshot), out PoolView view))
                {
                    continue;
                }

                UpdatePoolView(view, snapshot);
            }

            ApplyFilter();
        }

        private int FetchSnapshots()
        {
            // 缓冲不足时倍增重取——返回值等于缓冲长度意味着还有池被截断
            while (true)
            {
                int count = GameObjectPoolService.GetDebugSnapshots(_snapshotBuffer);
                if (count < _snapshotBuffer.Length)
                {
                    _snapshotCount = count;
                    return count;
                }

                _snapshotBuffer = new GameObjectPoolSnapshot[_snapshotBuffer.Length << 1];
            }
        }

        private bool IsPoolSetStale(int poolCount)
        {
            return !_viewsBuilt || _poolCount != poolCount || _poolSetHash != ComputePoolSetHash(poolCount);
        }

        private int ComputePoolSetHash(int poolCount)
        {
            int hash = POOL_SET_HASH_SEED;
            for (int i = 0; i < poolCount; i++)
            {
                GameObjectPoolSnapshot snapshot = _snapshotBuffer[i];
                hash = hash * 31 + (snapshot != null ? GetPoolKey(snapshot).GetHashCode() : 0);
            }

            return hash;
        }

        #endregion

        #region 池视图 [POOL VIEWS]

        private void RebuildPoolViews(int poolCount)
        {
            _listRoot.Clear();
            _poolViews.Clear();
            for (int i = 0; i < poolCount; i++)
            {
                GameObjectPoolSnapshot snapshot = _snapshotBuffer[i];
                string key = snapshot != null ? GetPoolKey(snapshot) : null;
                if (key == null || _poolViews.ContainsKey(key))
                {
                    continue;
                }

                _poolViews.Add(key, BuildPoolView(snapshot));
            }

            _listRoot.Add(_noMatchHint);

            _poolCount = poolCount;
            _poolSetHash = ComputePoolSetHash(poolCount);
            _viewsBuilt = true;
        }

        private PoolView BuildPoolView(GameObjectPoolSnapshot snapshot)
        {
            string key = GetPoolKey(snapshot);
            PoolView view = new PoolView(key, snapshot.location, snapshot.group);

            VisualElement section = DebuggerUI.CreateSection(null, out VisualElement card);
            view.Section = section;
            _listRoot.Add(section);

            // 侧边栏同款 Foldout——主题自绘折叠箭头；value 先于回调赋值（程序化赋值不触发回调）
            Foldout foldout = new Foldout { text = GetPoolTitle(snapshot) };
            foldout.AddToClassList("dbg-sidebar-foldout");
            foldout.value = _expandedPools.Contains(key);

            Toggle toggle = foldout.Q<Toggle>(null, "unity-foldout__toggle");
            if (toggle != null)
            {
                toggle.focusable = false;

                Label nameLabel = toggle.Q<Label>(className: "unity-label");
                if (nameLabel != null)
                {
                    // 长池名省略号截断，为右侧摘要让位
                    nameLabel.style.minWidth = 0f;
                    nameLabel.style.flexShrink = 1f;
                    nameLabel.style.overflow = Overflow.Hidden;
                    nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
                    nameLabel.style.textOverflow = TextOverflow.Ellipsis;
                }

                VisualElement spacer = new VisualElement { style = { flexGrow = 1f } };
                spacer.pickingMode = PickingMode.Ignore;

                // 策略徽标与摘要并排右侧——toggle 的文本标签非直接子级（IndexOf 为 -1），不可按索引插入
                Label policyLabel = new Label(snapshot.policy.ToString().ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                policyLabel.AddToClassList("dbg-text--accent");
                policyLabel.style.flexShrink = 0f;
                policyLabel.style.marginLeft = 8f;

                view.SummaryLabel = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
                view.SummaryLabel.AddToClassList("dbg-sidebar-row__title");
                view.SummaryLabel.style.flexGrow = 0f;
                view.SummaryLabel.style.flexShrink = 0f;
                view.SummaryLabel.style.marginLeft = 8f;

                toggle.Add(spacer);
                toggle.Add(policyLabel);
                toggle.Add(view.SummaryLabel);
            }

            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    _expandedPools.Add(view.Key);
                    UpdatePoolView(view, FindSnapshot(view.Key));   // 展开即取最近快照（详情+实例行），无需等下一轮轮询
                }
                else
                {
                    _expandedPools.Remove(view.Key);
                }
            });

            card.Add(foldout);
            view.Foldout = foldout;

            // 详情行（值按钮引用跨轮询存活——轮询只写 text，不重建行）
            AddRow(foldout, "Entry Name", string.Empty, out view.EntryNameButton);
            AddRow(foldout, "Group", string.Empty, out view.GroupButton);
            AddRow(foldout, "Location", string.Empty, out view.LocationButton);
            AddRow(foldout, "Min Idle / Retain Target", string.Empty, out view.MinIdleButton);
            AddRow(foldout, "Soft / Hard Capacity", string.Empty, out view.CapacityButton);
            AddRow(foldout, "Unload Prefab", string.Empty, out view.UnloadPrefabButton);
            AddRow(foldout, "Prefab Loaded", string.Empty, out view.PrefabLoadedButton);
            AddRow(foldout, "Active / Inactive / Total", string.Empty, out view.CountsButton);
            AddRow(foldout, "Next Maintenance In", string.Empty, out view.NextMaintenanceButton);

            AddRow(foldout, "Spawn", string.Empty, out view.SpawnButton);
            AddRow(foldout, "Despawn", string.Empty, out view.DespawnButton);
            AddRow(foldout, "Hit", string.Empty, out view.HitButton);
            AddRow(foldout, "Miss", string.Empty, out view.MissButton);
            AddRow(foldout, "Expand", string.Empty, out view.ExpandButton);
            AddRow(foldout, "Destroy", string.Empty, out view.DestroyButton);
            AddRow(foldout, "Peak Active", string.Empty, out view.PeakActiveButton);

            view.ActionsRow = BuildPoolActions(view);
            foldout.Add(view.ActionsRow);

            view.InstancesRoot = new VisualElement();
            foldout.Add(view.InstancesRoot);

            return view;
        }

        private void UpdatePoolView(PoolView view, GameObjectPoolSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            view.Foldout.text = GetPoolTitle(snapshot);
            if (view.SummaryLabel != null)
            {
                view.SummaryLabel.text = StringUtility.Format("A{0}/I{1}/T{2} | Hit {3}/{4}", snapshot.activeCount, snapshot.inactiveCount, snapshot.totalCount, snapshot.hitCount, snapshot.spawnCount);
            }

            view.EntryNameButton.text = string.IsNullOrWhiteSpace(snapshot.entryName) ? "<None>" : snapshot.entryName;
            view.GroupButton.text = string.IsNullOrWhiteSpace(view.Group) ? "<None>" : view.Group;
            view.LocationButton.text = view.Location;
            view.MinIdleButton.text = StringUtility.Format("{0} / {1}", snapshot.minIdle, snapshot.retainTarget);
            view.CapacityButton.text = StringUtility.Format("{0} / {1}", snapshot.softCapacity, snapshot.hardCapacity);
            view.UnloadPrefabButton.text = snapshot.unloadPrefab ? "Yes" : "No";
            view.PrefabLoadedButton.text = snapshot.prefabLoaded ? "Yes" : "No";
            view.CountsButton.text = StringUtility.Format("{0} / {1} / {2}", snapshot.activeCount, snapshot.inactiveCount, snapshot.totalCount);
            view.NextMaintenanceButton.text = snapshot.nextMaintenanceIn >= 0f ? StringUtility.Format("{0:F2}s", snapshot.nextMaintenanceIn) : "None";

            view.SpawnButton.text = snapshot.spawnCount.ToString();
            view.DespawnButton.text = snapshot.despawnCount.ToString();
            view.HitButton.text = snapshot.hitCount.ToString();

            // 容量 churn 计数（Miss/Expand/Destroy）非零时以警示色标注
            view.MissButton.text = snapshot.missCount.ToString();
            view.MissButton.EnableInClassList("dbg-text--warning", snapshot.missCount > 0);
            view.ExpandButton.text = snapshot.expandCount.ToString();
            view.ExpandButton.EnableInClassList("dbg-text--warning", snapshot.expandCount > 0);
            view.DestroyButton.text = snapshot.destroyCount.ToString();
            view.DestroyButton.EnableInClassList("dbg-text--warning", snapshot.destroyCount > 0);
            view.PeakActiveButton.text = snapshot.peakActive.ToString();

            // 收起的池跳过实例行重建（展开回调内即时补一次快照）
            if (_expandedPools.Contains(view.Key))
            {
                RebuildInstanceRows(view, snapshot);
            }
        }

        private void RebuildInstanceRows(PoolView view, GameObjectPoolSnapshot snapshot)
        {
            VisualElement instancesRoot = view.InstancesRoot;
            instancesRoot.Clear();

            if (!_showInstances || snapshot == null)
            {
                return;
            }

            GameObjectPoolService.FillDebugInstances(snapshot);
            if (snapshot.InstanceCount <= 0)
            {
                return;
            }

            int listedCount = Mathf.Min(snapshot.InstanceCount, MAX_LISTED_INSTANCES);
            instancesRoot.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("Instances ({0}) — State | Life | Idle", snapshot.InstanceCount)));
            for (int i = 0; i < listedCount; i++)
            {
                AddInstanceRow(instancesRoot, snapshot.GetInstance(i));
            }

            if (snapshot.InstanceCount > listedCount)
            {
                instancesRoot.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("... {0} more instances omitted.", snapshot.InstanceCount - listedCount)));
            }
        }

        private void AddInstanceRow(VisualElement container, GameObjectPoolInstanceSnapshot instance)
        {
            if (instance == null)
            {
                return;
            }

            string idle = instance.isActive ? "-" : StringUtility.Format("{0:F2}s", instance.idleDuration);
            VisualElement row = DebuggerUI.CreateRow(
                string.IsNullOrEmpty(instance.instanceName) ? "<Unknown>" : instance.instanceName,
                StringUtility.Format("{0} | Life {1:F2}s | Idle {2}", instance.isActive ? "Active" : "Inactive", instance.lifeDuration, idle));

            if (instance.isActive)
            {
                // 值按钮自身即文本元素（Button:TextElement）——活跃实例挂语义类着色
                row.Q<Button>()?.AddToClassList("dbg-text--warning");
            }

            container.Add(row);
        }

        private VisualElement BuildPoolActions(PoolView view)
        {
            // 冲刷回调以视图键触发——避免回调落在下一轮快照回收后读到失效引用
            VisualElement actions = DebuggerUI.CreateToolbarRow();
            actions.Add(DebuggerUI.CreateActionButton("Flush This", () =>
            {
                GameObjectPoolService.Flush(view.Location);
                Refresh();
            }));

            Button flushGroupButton = DebuggerUI.CreateActionButton("Flush Group", () =>
            {
                GameObjectPoolService.FlushGroup(view.Group);
                Refresh();
            });
            flushGroupButton.SetEnabled(!string.IsNullOrEmpty(view.Group));
            actions.Add(flushGroupButton);
            return actions;
        }

        private void ExpandAllPools()
        {
            // 只展开当前可见池——大池量下重建开销与渲染集合一致，且不往被过滤池堆积展开状态
            for (int i = 0; i < _snapshotCount; i++)
            {
                GameObjectPoolSnapshot snapshot = _snapshotBuffer[i];
                if (snapshot == null || !IsPoolVisible(snapshot))
                {
                    continue;
                }

                if (_poolViews.TryGetValue(GetPoolKey(snapshot), out PoolView view))
                {
                    SetPoolExpanded(view, true);
                }
            }
        }

        private void CollapseAllPools()
        {
            foreach (KeyValuePair<string, PoolView> pair in _poolViews)
            {
                SetPoolExpanded(pair.Value, false);
            }
        }

        private void SetPoolExpanded(PoolView view, bool expanded)
        {
            // 程序化赋值不触发回调（见项目约定）——状态与内容在此同步，与用户点击回调同效
            if (view.Foldout.value != expanded)
            {
                view.Foldout.value = expanded;
            }

            if (expanded)
            {
                _expandedPools.Add(view.Key);
                UpdatePoolView(view, FindSnapshot(view.Key));
            }
            else
            {
                _expandedPools.Remove(view.Key);
            }
        }

        private GameObjectPoolSnapshot FindSnapshot(string key)
        {
            // 最近一轮快照在下一轮取数前持续有效，可在轮询间隙直接使用
            for (int i = 0; i < _snapshotCount; i++)
            {
                GameObjectPoolSnapshot snapshot = _snapshotBuffer[i];
                if (snapshot != null && GetPoolKey(snapshot) == key)
                {
                    return snapshot;
                }
            }

            return null;
        }

        private void RefreshInstanceRows()
        {
            for (int i = 0; i < _snapshotCount; i++)
            {
                GameObjectPoolSnapshot snapshot = _snapshotBuffer[i];
                if (snapshot == null || !_poolViews.TryGetValue(GetPoolKey(snapshot), out PoolView view))
                {
                    continue;
                }

                if (_expandedPools.Contains(view.Key))
                {
                    RebuildInstanceRows(view, snapshot);
                }
            }
        }

        #endregion

        #region 过滤 [FILTER]

        private void ApplyFilter()
        {
            int visibleCount = 0;
            for (int i = 0; i < _poolCount; i++)
            {
                GameObjectPoolSnapshot snapshot = _snapshotBuffer[i];
                if (snapshot == null || !_poolViews.TryGetValue(GetPoolKey(snapshot), out PoolView view))
                {
                    continue;
                }

                bool visible = IsPoolVisible(snapshot);
                view.Section.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                {
                    visibleCount++;
                }
            }

            if (_noMatchHint != null)
            {
                _noMatchHint.text = _poolCount == 0
                    ? "No pooled runtime instances exist yet."
                    : StringUtility.Format("No pools match the current filter ({0} hidden).", _poolCount - visibleCount);
                _noMatchHint.style.display = visibleCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private bool IsPoolVisible(GameObjectPoolSnapshot snapshot)
        {
            if (_hideEmpty && snapshot.totalCount <= 0)
            {
                return false;
            }

            if (string.IsNullOrEmpty(_searchText))
            {
                return true;
            }

            return MatchesSearch(snapshot.location) || MatchesSearch(snapshot.entryName) || MatchesSearch(snapshot.group);
        }

        private bool MatchesSearch(string value)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region 私有 [PRIVATE]

        private static string GetPoolKey(GameObjectPoolSnapshot snapshot)
        {
            return string.Concat(snapshot.group, "|", snapshot.location);
        }

        private static string GetPoolTitle(GameObjectPoolSnapshot snapshot)
        {
            string name = string.IsNullOrWhiteSpace(snapshot.entryName) ? snapshot.location : snapshot.entryName;
            return StringUtility.Format("[{0}] {1}", string.IsNullOrEmpty(snapshot.group) ? "<None>" : snapshot.group, name);
        }

        private static void SetSeverity(Button valueButton, bool warning, bool danger)
        {
            valueButton.EnableInClassList("dbg-text--warning", warning);
            valueButton.EnableInClassList("dbg-text--danger", danger);
        }

        #endregion

        #region 视图数据 [VIEW DATA]

        /// <summary>
        /// 单个池的常驻视图元素（跨轮询存活——轮询只写 text，不重建结构；键为 group|location，池存活期间稳定）。
        /// </summary>
        private sealed class PoolView
        {
            /// <summary>池键（group|location，折叠状态与视图字典索引）。</summary>
            public readonly string Key;

            /// <summary>资源地址（建池即固定，冲刷回调捕获用）。</summary>
            public readonly string Location;

            /// <summary>分组名称（建池即固定）。</summary>
            public readonly string Group;

            /// <summary>分区根元素（过滤显隐作用于整体）。</summary>
            public VisualElement Section;

            /// <summary>折叠面板（标题为池显示名，内嵌策略徽标与摘要标签）。</summary>
            public Foldout Foldout;

            /// <summary>折叠钮右侧摘要（A/I/T | Hit）。</summary>
            public Label SummaryLabel;

            /// <summary>详情行值按钮引用（轮询原地刷新文本）。</summary>
            public Button EntryNameButton;
            public Button GroupButton;
            public Button LocationButton;
            public Button MinIdleButton;
            public Button CapacityButton;
            public Button UnloadPrefabButton;
            public Button PrefabLoadedButton;
            public Button CountsButton;
            public Button NextMaintenanceButton;

            /// <summary>计数行值按钮引用（Miss/Expand/Destroy 非零挂警示色）。</summary>
            public Button SpawnButton;
            public Button DespawnButton;
            public Button HitButton;
            public Button MissButton;
            public Button ExpandButton;
            public Button DestroyButton;
            public Button PeakActiveButton;

            /// <summary>操作按钮行（Flush This / Flush Group）。</summary>
            public VisualElement ActionsRow;

            /// <summary>实例行容器（仅展开期间按轮询重建）。</summary>
            public VisualElement InstancesRoot;

            /// <summary>
            /// 初始化池视图数据的新实例。
            /// </summary>
            /// <param name="key">池键。</param>
            /// <param name="location">资源地址。</param>
            /// <param name="group">分组名称。</param>
            public PoolView(string key, string location, string group)
            {
                Key = key;
                Location = location;
                Group = group;
            }
        }

        #endregion
    }
}
