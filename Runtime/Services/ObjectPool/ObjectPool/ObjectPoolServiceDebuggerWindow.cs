using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Moirai.Atropos.Debugger;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 通用对象池信息窗口（池折叠浏览、检索过滤、释放操作与 CSV 导出）。
    /// <para>摘要卡（计数/检索框/开关/全局操作）构建一次常驻，轮询仅原地刷新数值——折叠钮与操作按钮不随轮询重建，点击不会落在重建边界被吞掉。</para>
    /// <para>仅池集合变化（新建/销毁/优先级排序变化）时整体重建池视图；对象行仅折叠面板展开期间按节流重建（纯展示无交互）。</para>
    /// </summary>
    public sealed class ObjectPoolServiceDebuggerWindow : ScrollableDebuggerWindowBase
    {
        #region 常量 [CONSTANTS]

        private const float REFRESH_INTERVAL = 0.5f;

        /// <summary>单个池内最多绘制的对象信息行数（超出部分折叠提示，避免巨型池拖垮重建）。</summary>
        private const int MAX_DRAWN_OBJECT_INFOS = 128;

        /// <summary>池集合签名哈希种子（顺序敏感：新建/销毁/优先级排序变化均会改变签名）。</summary>
        private const int POOL_SET_HASH_SEED = 17;

        /// <summary>CSV 转义触发字符（逗号/引号/回车/换行——字段命中任一则整字段加引号包裹）。</summary>
        private static readonly char[] CSV_SPECIAL_CHARS = { ',', '"', '\r', '\n' };

        #endregion

        #region 字段 [FIELDS]

        private ObjectPoolBase[] _poolBuffer = Array.Empty<ObjectPoolBase>();
        private readonly ObjectInfo[] _objectInfoBuffer = new ObjectInfo[MAX_DRAWN_OBJECT_INFOS];
        private ObjectInfo[] _exportBuffer = Array.Empty<ObjectInfo>();

        private readonly HashSet<string> _expandedPools = new HashSet<string>();
        private readonly Dictionary<string, PoolView> _poolViews = new Dictionary<string, PoolView>();

        private VisualElement _listRoot;
        private Label _noMatchHint;
        private Button _poolCountButton;
        private Button _objectCountButton;
        private string _searchText = string.Empty;
        private bool _showFullTypeName;
        private bool _hideEmptyPools;

        private float _countdown;
        private int _poolCount;
        private int _poolSetHash;
        private bool _viewsBuilt;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化通用对象池信息窗口的新实例。
        /// </summary>
        public ObjectPoolServiceDebuggerWindow()
        {
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            if (!ObjectPoolService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("Object Pool Information"));
                root.Add(DebuggerUI.CreateHintLabel("ObjectPoolService is not registered (opt-in service)."));
                return;
            }

            BuildSummary(root);

            _listRoot = new VisualElement();
            _listRoot.style.flexDirection = FlexDirection.Column;
            root.Add(_listRoot);

            _noMatchHint = DebuggerUI.CreateHintLabel("No object pools.");
            _noMatchHint.style.display = DisplayStyle.None;

            Refresh();
        }

        /// <inheritdoc />
        public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            // 摘要卡尚未构建（服务未就绪或视图未挂载）——跳过刷新
            if (_poolCountButton == null)
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
            _poolCountButton = null;
            _objectCountButton = null;
        }

        #endregion

        #region 构建摘要卡 [BUILD SUMMARY]

        private void BuildSummary(VisualElement root)
        {
            VisualElement summaryCard = AddSection(root, "Object Pool Information");
            AddRow(summaryCard, "Pool Count", "0", out _poolCountButton);
            AddRow(summaryCard, "Total Object Count", "0", out _objectCountButton);

            VisualElement filterRow = DebuggerUI.CreateToolbarRow();
            TextField searchField = DebuggerUI.CreateSearchField("Search pools", value =>
            {
                _searchText = value ?? string.Empty;
                ApplyFilter();
            });
            searchField.AddToClassList("dbg-search");
            VisualElement searchWrap = (VisualElement)searchField.userData;
            searchWrap.style.flexGrow = 1f;
            filterRow.Add(searchWrap);
            filterRow.Add(DebuggerUI.CreateToggle("Full Type Name", _showFullTypeName, value =>
            {
                _showFullTypeName = value;
                Refresh();
            }));
            filterRow.Add(DebuggerUI.CreateToggle("Hide Empty Pools", _hideEmptyPools, value =>
            {
                _hideEmptyPools = value;
                Refresh();
            }));
            summaryCard.Add(filterRow);

            VisualElement actionRow = DebuggerUI.CreateToolbarRow();
            actionRow.Add(DebuggerUI.CreateActionButton("Release All Pools", () =>
            {
                ObjectPoolService.Release();
                Refresh();
            }));
            actionRow.Add(DebuggerUI.CreateActionButton("Flush All Pools", () =>
            {
                ObjectPoolService.FlushAll();
                Refresh();
            }, DebuggerUI.EButtonStyle.Warning));
            summaryCard.Add(actionRow);
        }

        #endregion

        #region 刷新 [REFRESH]

        /// <summary>
        /// 立即刷新窗口（摘要数值、池视图与过滤可见性）。
        /// </summary>
        private void Refresh()
        {
            if (_poolCountButton == null)
            {
                return;
            }

            int poolCount = FetchPools();
            int totalObjectCount = 0;
            for (int i = 0; i < poolCount; i++)
            {
                if (_poolBuffer[i] != null)
                {
                    totalObjectCount += _poolBuffer[i].Count;
                }
            }

            _poolCountButton.text = poolCount.ToString();
            _objectCountButton.text = totalObjectCount.ToString();

            if (IsPoolSetStale(poolCount))
            {
                RebuildPoolViews(poolCount);
            }

            for (int i = 0; i < poolCount; i++)
            {
                ObjectPoolBase pool = _poolBuffer[i];
                if (pool == null || !_poolViews.TryGetValue(pool.FullName, out PoolView view))
                {
                    continue;
                }

                UpdatePoolView(view, pool);
            }

            ApplyFilter();
        }

        /// <summary>
        /// 拉取全部池（按优先级降序）；缓冲区不足时扩容后重取，保证不截断。
        /// </summary>
        /// <returns>池总数。</returns>
        private int FetchPools()
        {
            int count = ObjectPoolService.GetAllObjectPools(true, _poolBuffer);
            if (count > _poolBuffer.Length)
            {
                _poolBuffer = new ObjectPoolBase[count];
                count = ObjectPoolService.GetAllObjectPools(true, _poolBuffer);
            }

            return count;
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
                ObjectPoolBase pool = _poolBuffer[i];
                hash = hash * 31 + (pool != null ? pool.FullName.GetHashCode() : 0);
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
                ObjectPoolBase pool = _poolBuffer[i];
                if (pool == null || _poolViews.ContainsKey(pool.FullName))
                {
                    continue;
                }

                _poolViews.Add(pool.FullName, BuildPoolView(pool));
            }

            _listRoot.Add(_noMatchHint);

            _poolCount = poolCount;
            _poolSetHash = ComputePoolSetHash(poolCount);
            _viewsBuilt = true;
        }

        private PoolView BuildPoolView(ObjectPoolBase pool)
        {
            PoolView view = new PoolView(pool);

            VisualElement section = DebuggerUI.CreateSection(null, out VisualElement card);
            view.Section = section;
            _listRoot.Add(section);

            Foldout foldout = new Foldout { text = GetDisplayName(pool) };
            foldout.AddToClassList("dbg-sidebar-foldout");
            foldout.value = _expandedPools.Contains(pool.FullName);

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

                view.SummaryLabel = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
                view.SummaryLabel.AddToClassList("dbg-sidebar-row__title");
                view.SummaryLabel.style.flexGrow = 0f;
                view.SummaryLabel.style.flexShrink = 0f;
                view.SummaryLabel.style.marginLeft = 8f;

                toggle.Add(spacer);
                toggle.Add(view.SummaryLabel);
            }

            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    _expandedPools.Add(view.Pool.FullName);
                    UpdatePoolView(view, view.Pool);   // 展开即取最新快照（详情+对象行），无需等下一轮轮询
                }
                else
                {
                    _expandedPools.Remove(view.Pool.FullName);
                }
            });

            card.Add(foldout);
            view.Foldout = foldout;

            // 详情行（值按钮引用跨轮询存活——轮询只写 text，不重建行）
            AddRow(foldout, "Object Type", pool.ObjectType.FullName, out view.TypeButton);
            AddRow(foldout, "Auto Release Interval", string.Empty, out view.AutoReleaseIntervalButton);
            AddRow(foldout, "Capacity", string.Empty, out view.CapacityButton);
            AddRow(foldout, "Used Count", string.Empty, out view.UsedCountButton);
            AddRow(foldout, "Expire Time", string.Empty, out view.ExpireTimeButton);
            AddRow(foldout, "Priority", string.Empty, out view.PriorityButton);

            view.ObjectsRoot = new VisualElement();
            foldout.Add(view.ObjectsRoot);

            view.ActionsRow = BuildActionsRow(view);
            foldout.Add(view.ActionsRow);

            return view;
        }

        private void UpdatePoolView(PoolView view, ObjectPoolBase pool)
        {
            view.Foldout.text = GetDisplayName(pool);
            if (view.SummaryLabel != null)
            {
                view.SummaryLabel.text = StringUtility.Format("Used {0} | Capacity {1}", pool.Count, pool.Capacity);
            }

            view.TypeButton.text = pool.ObjectType.FullName;
            view.AutoReleaseIntervalButton.text = pool.AutoReleaseInterval >= float.MaxValue
                ? "Disabled"
                : StringUtility.Format("{0:F1}s", pool.AutoReleaseInterval);
            view.CapacityButton.text = pool.Capacity == int.MaxValue ? "Unlimited" : pool.Capacity.ToString();
            view.UsedCountButton.text = pool.Count.ToString();
            view.UsedCountButton.EnableInClassList("dbg-text--warning", pool.Count > 0);
            view.ExpireTimeButton.text = pool.ExpireTime >= float.MaxValue
                ? "Never"
                : StringUtility.Format("{0:F1}s", pool.ExpireTime);
            view.PriorityButton.text = pool.Priority.ToString();

            // 收起的池跳过对象行重建（展开回调内即时补一次快照）
            if (_expandedPools.Contains(view.Pool.FullName))
            {
                RebuildObjectRows(view, pool);
            }
        }

        private string GetDisplayName(ObjectPoolBase pool)
        {
            if (_showFullTypeName)
            {
                return pool.FullName;
            }

            return string.IsNullOrEmpty(pool.Name)
                ? pool.ObjectType.Name
                : StringUtility.Concat(pool.ObjectType.Name, ".", pool.Name);
        }

        private void RebuildObjectRows(PoolView view, ObjectPoolBase pool)
        {
            VisualElement objectsRoot = view.ObjectsRoot;
            objectsRoot.Clear();

            int objectCount = pool.GetAllObjectInfos(_objectInfoBuffer);
            view.ActionsRow.style.display = objectCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (objectCount <= 0)
            {
                objectsRoot.Add(DebuggerUI.CreateHintLabel("Object Pool is empty."));
                return;
            }

            int drawCount = Mathf.Min(objectCount, _objectInfoBuffer.Length);
            for (int i = 0; i < drawCount; i++)
            {
                AddObjectInfoRow(objectsRoot, _objectInfoBuffer[i]);
            }

            if (objectCount > drawCount)
            {
                objectsRoot.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("... {0} more objects omitted.", objectCount - drawCount)));
            }
        }

        private static void AddObjectInfoRow(VisualElement container, ObjectInfo info)
        {
            string objectName = string.IsNullOrEmpty(info.Name) ? "<None>" : info.Name;
            string content = StringUtility.Format("InUse {0} | Spawn {1} | Locked {2} | CanRelease {3} | LastUse {4:F1}s ago",
                info.IsInUse ? "Yes" : "No", info.SpawnCount, info.Locked ? "Yes" : "No",
                info.CustomCanReleaseFlag ? "Yes" : "No", Time.realtimeSinceStartup - info.LastUseTime);

            AddRow(container, objectName, content, out Button valueButton);
            if (info.Locked || info.CustomCanReleaseFlag)
            {
                valueButton.AddToClassList("dbg-text--warning");
            }
        }

        private VisualElement BuildActionsRow(PoolView view)
        {
            VisualElement actionRow = DebuggerUI.CreateToolbarRow();

            actionRow.Add(DebuggerUI.CreateActionButton("Release", () =>
            {
                view.Pool.Release();
                Refresh();
            }));

            actionRow.Add(DebuggerUI.CreateActionButton("Release All Unused", () =>
            {
                view.Pool.ReleaseAllUnused();
                Refresh();
            }, DebuggerUI.EButtonStyle.Warning));

            actionRow.Add(DebuggerUI.CreateActionButton("Export CSV", () => ExportCsv(view.Pool)));

            return actionRow;
        }

        #endregion

        #region 过滤 [FILTER]

        private void ApplyFilter()
        {
            int visibleCount = 0;
            for (int i = 0; i < _poolCount; i++)
            {
                ObjectPoolBase pool = _poolBuffer[i];
                if (pool == null || !_poolViews.TryGetValue(pool.FullName, out PoolView view))
                {
                    continue;
                }

                bool visible = MatchesFilter(pool);
                view.Section.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                {
                    visibleCount++;
                }
            }

            if (_noMatchHint != null)
            {
                _noMatchHint.text = _poolCount == 0
                    ? "No object pools."
                    : StringUtility.Format("No pools match the current filter ({0} hidden).", _poolCount - visibleCount);
                _noMatchHint.style.display = visibleCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private bool MatchesFilter(ObjectPoolBase pool)
        {
            if (_hideEmptyPools && pool.Count <= 0)
            {
                return false;
            }

            return string.IsNullOrEmpty(_searchText) ||
                   pool.FullName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region 导出 [EXPORT]

        /// <summary>
        /// 导出池内全部对象信息为 CSV（写入 <see cref="Application.persistentDataPath"/>）。
        /// </summary>
        private void ExportCsv(ObjectPoolBase pool)
        {
            int count = pool.GetAllObjectInfos(GetExportBuffer(pool.Count));
            if (count > _exportBuffer.Length)
            {
                count = _exportBuffer.Length;
            }

            string fileName = SanitizeFileName(StringUtility.Format("ObjectPoolData-{0}-{1:yyyy-MM-dd-HH-mm-ss}.csv", pool.FullName, DateTime.Now));
            string exportPath = Path.Combine(Application.persistentDataPath, fileName);
            try
            {
                string[] lines = new string[count + 1];
                lines[0] = StringUtility.Format("Name,Locked,{0},Custom Can Release Flag,Last Use Time", pool.AllowMultiSpawn ? "Count" : "In Use");
                float now = Time.realtimeSinceStartup;
                for (int i = 0; i < count; i++)
                {
                    ObjectInfo info = _exportBuffer[i];
                    lines[i + 1] = StringUtility.Format("{0},{1},{2},{3},{4:F1}s ago", EscapeCsvField(info.Name), info.Locked,
                        pool.AllowMultiSpawn ? info.SpawnCount.ToString() : info.IsInUse.ToString(),
                        info.CustomCanReleaseFlag, now - info.LastUseTime);
                }

                File.WriteAllLines(exportPath, lines, Encoding.UTF8);
                LogUtility.Info("Export object pool CSV data to '{0}' success.", exportPath);
            }
            catch (Exception exception)
            {
                LogUtility.Error("Export object pool CSV data to '{0}' failure, exception is '{1}'.", exportPath, exception);
            }
        }

        private ObjectInfo[] GetExportBuffer(int count)
        {
            if (_exportBuffer.Length < count)
            {
                _exportBuffer = new ObjectInfo[count];
            }

            return _exportBuffer;
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return fileName;
        }

        /// <summary>
        /// 转义 CSV 字段（RFC 4180）：含逗号/引号/回车/换行的字段以引号包裹，内部引号双写。
        /// </summary>
        /// <param name="field">原始字段值（null 视为空）。</param>
        /// <returns>可直接拼入 CSV 行的字段值。</returns>
        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field) || field.IndexOfAny(CSV_SPECIAL_CHARS) < 0)
            {
                return field ?? string.Empty;
            }

            return StringUtility.Concat("\"", field.Replace("\"", "\"\""), "\"");
        }

        #endregion

        #region 视图数据 [VIEW DATA]

        /// <summary>
        /// 单个池的常驻视图元素（跨轮询存活——轮询只写 text，不重建结构）。
        /// </summary>
        private sealed class PoolView
        {
            /// <summary>池实例（池集合变化时整体重建视图，引用始终有效）。</summary>
            public readonly ObjectPoolBase Pool;

            /// <summary>分区根元素（过滤显隐作用于整体）。</summary>
            public VisualElement Section;

            /// <summary>折叠面板（标题为池显示名，内嵌摘要标签）。</summary>
            public Foldout Foldout;

            /// <summary>折叠钮右侧摘要（Used | Capacity）。</summary>
            public Label SummaryLabel;

            /// <summary>详情行值按钮引用（轮询原地刷新文本）。</summary>
            public Button TypeButton;
            public Button AutoReleaseIntervalButton;
            public Button CapacityButton;
            public Button UsedCountButton;
            public Button ExpireTimeButton;
            public Button PriorityButton;

            /// <summary>对象信息行容器（仅展开期间按节流重建）。</summary>
            public VisualElement ObjectsRoot;

            /// <summary>操作按钮行（Release / Release All Unused / Export CSV）。</summary>
            public VisualElement ActionsRow;

            /// <summary>
            /// 初始化池视图数据的新实例。
            /// </summary>
            /// <param name="pool">池实例。</param>
            public PoolView(ObjectPoolBase pool)
            {
                Pool = pool;
            }
        }

        #endregion
    }
}
