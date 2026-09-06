using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 控制台窗口（日志流虚拟化列表 + 分级过滤 + 搜索 + 锁滚动 + 详情与复制）。
    /// <para><see cref="ListView"/> makeItem/bindItem 虚拟化渲染——仅可视行分配元素；计数经 <see cref="DebuggerLogCapture"/> 增量维护，窗口零遍历刷新。</para>
    /// </summary>
    public sealed class ConsoleWindow : IDebuggerWindow
    {
        #region 常量 [CONSTANTS]

        private const string CHIP_INFO_TEXT = "Info";
        private const string CHIP_WARNING_TEXT = "Warning";
        private const string CHIP_ERROR_TEXT = "Error";
        private const string CHIP_FATAL_TEXT = "Fatal";

        #endregion

        #region 字段 [FIELDS]

        private readonly List<LogNode> _filteredLogs = new List<LogNode>(128);

        private DebuggerLogCapture _logCapture;

        private ListView _listView;
        private Label _hintLabel;
        private TextField _detailField;
        private TextField _searchField;
        private Toggle _lockScrollToggle;
        private Toggle _infoChip;
        private Toggle _warningChip;
        private Toggle _errorChip;
        private Toggle _fatalChip;
        private LogNode _selectedNode;
        private int _lastLogVersion = -1;
        private bool _infoFilter = true;
        private bool _warningFilter = true;
        private bool _errorFilter = true;
        private bool _fatalFilter = true;
        private bool _lockScroll = true;
        private string _searchText = string.Empty;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化控制台窗口（首参注入 <see cref="DebuggerLogCapture"/>）。
        /// </summary>
        /// <param name="args">初始化参数（args[0] 为日志捕获器）。</param>
        public void Initialize(params object[] args)
        {
            if (args == null || args.Length < 1 || !(args[0] is DebuggerLogCapture logCapture))
            {
                LogUtility.Error("ConsoleWindow requires a DebuggerLogCapture argument.");
                return;
            }

            _logCapture = logCapture;
            _lockScroll = SettingUtility.GetBool(Constant.Debug.LOCK_SCROLL, true);
            _infoFilter = SettingUtility.GetBool(Constant.Debug.INFO_FILTER, true);
            _warningFilter = SettingUtility.GetBool(Constant.Debug.WARNING_FILTER, true);
            _errorFilter = SettingUtility.GetBool(Constant.Debug.ERROR_FILTER, true);
            _fatalFilter = SettingUtility.GetBool(Constant.Debug.FATAL_FILTER, true);
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            _logCapture = null;
        }

        /// <inheritdoc />
        public void OnEnter()
        {
        }

        /// <inheritdoc />
        public void OnLeave()
        {
        }

        /// <inheritdoc />
        public void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            if (_logCapture == null || _listView == null)
            {
                return;
            }

            if (_lastLogVersion == _logCapture.Version)
            {
                return;
            }

            _lastLogVersion = _logCapture.Version;
            RefreshFilterChips();
            RebuildFilteredLogs();

            if (_lockScroll && _filteredLogs.Count > 0)
            {
                _listView.ScrollToItem(-1);
            }
        }

        /// <inheritdoc />
        public VisualElement CreateView()
        {
            VisualElement root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            root.Add(BuildToolbar());
            root.Add(BuildLogList());
            root.Add(BuildDetailPanel());

            if (_logCapture != null)
            {
                _lastLogVersion = -1;
                RefreshFilterChips();
                RebuildFilteredLogs();
            }

            return root;
        }

        #endregion

        #region 工具栏 [TOOLBAR]

        private VisualElement BuildToolbar()
        {
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("dbg-console-toolbar");

            Button clearButton = DebuggerUI.CreateToolbarButton("Clear", () =>
            {
                _logCapture.Clear();
                _selectedNode = null;
                _lastLogVersion = -1;
            }, DebuggerUI.EButtonStyle.Danger);
            toolbar.Add(clearButton);

            _lockScrollToggle = DebuggerUI.CreateFilterChip("Lock", _lockScroll, DebuggerUI.EChipStyle.Accent, value =>
            {
                _lockScroll = value;
                SettingUtility.SetBool(Constant.Debug.LOCK_SCROLL, value);
            });
            _lockScrollToggle.style.marginRight = 10f;
            toolbar.Add(_lockScrollToggle);

            _searchField = DebuggerUI.CreateSearchField("Search logs", value =>
            {
                _searchText = value;
                RebuildFilteredLogs();
            });
            _searchField.AddToClassList("dbg-search");
            _searchField.AddToClassList("dbg-console-search");
            VisualElement searchWrap = (VisualElement)_searchField.userData;
            toolbar.Add(searchWrap);

            toolbar.Add(new VisualElement { style = { flexGrow = 1f } });

            _infoChip = DebuggerUI.CreateFilterChip(CHIP_INFO_TEXT, _infoFilter, DebuggerUI.EChipStyle.Info, value =>
            {
                _infoFilter = value;
                SettingUtility.SetBool(Constant.Debug.INFO_FILTER, value);
                RebuildFilteredLogs();
            });
            _warningChip = DebuggerUI.CreateFilterChip(CHIP_WARNING_TEXT, _warningFilter, DebuggerUI.EChipStyle.Warning, value =>
            {
                _warningFilter = value;
                SettingUtility.SetBool(Constant.Debug.WARNING_FILTER, value);
                RebuildFilteredLogs();
            });
            _errorChip = DebuggerUI.CreateFilterChip(CHIP_ERROR_TEXT, _errorFilter, DebuggerUI.EChipStyle.Error, value =>
            {
                _errorFilter = value;
                SettingUtility.SetBool(Constant.Debug.ERROR_FILTER, value);
                RebuildFilteredLogs();
            });
            _fatalChip = DebuggerUI.CreateFilterChip(CHIP_FATAL_TEXT, _fatalFilter, DebuggerUI.EChipStyle.Fatal, value =>
            {
                _fatalFilter = value;
                SettingUtility.SetBool(Constant.Debug.FATAL_FILTER, value);
                RebuildFilteredLogs();
            });

            toolbar.Add(_infoChip);
            toolbar.Add(_warningChip);
            toolbar.Add(_errorChip);
            toolbar.Add(_fatalChip);

            return toolbar;
        }

        #endregion

        #region 日志列表 [LOG LIST]

        private VisualElement BuildLogList()
        {
            _listView = new ListView
            {
                itemsSource = _filteredLogs,
                makeItem = MakeLogRow,
                bindItem = BindLogRow,
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight
            };
            _listView.AddToClassList("dbg-console-list");
            _listView.showBorder = false;
            _listView.reorderable = false;
            _listView.showAlternatingRowBackgrounds = AlternatingRowBackground.None;
            ScrollView listViewScrollView = _listView.Q<ScrollView>();
            if (listViewScrollView != null)
            {
                DebuggerUI.StyleScrollView(listViewScrollView);
            }

#if UNITY_6000_0_OR_NEWER
            _listView.selectionChanged += OnSelectionChanged;
#else
            // 兼容低版本 UITK（2022.3 LTS）：selectionChanged 事件缺失，回退 onSelectionChange（全版本可用）
            _listView.onSelectionChange += OnSelectionChanged;
#endif

            _hintLabel = new Label("No logs match the current filters.");
            _hintLabel.AddToClassList("dbg-console-list__hint");
            _hintLabel.pickingMode = PickingMode.Ignore;
            _hintLabel.style.display = DisplayStyle.None;

            VisualElement listContainer = new VisualElement();
            listContainer.style.flexGrow = 1f;
            listContainer.style.minHeight = 0f;
            listContainer.style.position = Position.Relative;
            listContainer.Add(_listView);
            listContainer.Add(_hintLabel);
            return listContainer;
        }

        private static Label MakeLogRow()
        {
            Label label = new Label();
            label.AddToClassList("dbg-console-row");
            return label;
        }

        private void BindLogRow(VisualElement element, int index)
        {
            Label label = (Label)element;
            LogNode node = _filteredLogs[index];
            label.text = GetLogRowText(node);
            ApplyLogRowClass(label, node.LogType);
        }

        private static string GetLogRowText(LogNode node)
        {
            return StringUtility.Format("[{0:HH:mm:ss.fff}][F{1}] {2}", node.LogTime.ToLocalTime(), node.LogFrameCount, node.LogMessage);
        }

        private static void ApplyLogRowClass(Label label, LogType logType)
        {
            label.EnableInClassList("dbg-console-row--warning", logType == LogType.Warning);
            label.EnableInClassList("dbg-console-row--error", logType == LogType.Error);
            label.EnableInClassList("dbg-console-row--fatal", logType == LogType.Exception);
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            foreach (object item in selection)
            {
                _selectedNode = item as LogNode;
                RefreshDetailPanel();
                return;
            }

            _selectedNode = null;
            RefreshDetailPanel();
        }

        private void RefreshFilterChips()
        {
            if (_logCapture == null)
            {
                return;
            }

            DebuggerUI.UpdateFilterChipText(_infoChip, CHIP_INFO_TEXT, _logCapture.InfoCount);
            DebuggerUI.UpdateFilterChipText(_warningChip, CHIP_WARNING_TEXT, _logCapture.WarningCount);
            DebuggerUI.UpdateFilterChipText(_errorChip, CHIP_ERROR_TEXT, _logCapture.ErrorCount);
            DebuggerUI.UpdateFilterChipText(_fatalChip, CHIP_FATAL_TEXT, _logCapture.FatalCount);
        }

        private void RebuildFilteredLogs()
        {
            _filteredLogs.Clear();
            if (_logCapture != null)
            {
                foreach (LogNode node in _logCapture.GetLogNodes())
                {
                    if (!PassesLevelFilter(node) || !PassesSearchFilter(node))
                    {
                        continue;
                    }

                    _filteredLogs.Add(node);
                }
            }

            _listView.RefreshItems();
            _hintLabel.style.display = _filteredLogs.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool PassesLevelFilter(LogNode node)
        {
            switch (node.LogType)
            {
                case LogType.Log:
                    return _infoFilter;

                case LogType.Warning:
                    return _warningFilter;

                case LogType.Error:
                    return _errorFilter;

                case LogType.Exception:
                    return _fatalFilter;

                default:
                    return true;
            }
        }

        private bool PassesSearchFilter(LogNode node)
        {
            return string.IsNullOrEmpty(_searchText) || node.LogMessage != null && node.LogMessage.IndexOf(_searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region 详情面板 [DETAIL PANEL]

        private VisualElement BuildDetailPanel()
        {
            VisualElement panel = new VisualElement();
            panel.AddToClassList("dbg-console-detail");

            VisualElement header = new VisualElement();
            header.AddToClassList("dbg-console-detail__header");

            Label titleLabel = new Label("Detail");
            titleLabel.AddToClassList("dbg-console-detail__title");

            Button copyButton = DebuggerUI.CreateToolbarButton("Copy", () =>
            {
                if (_selectedNode != null)
                {
                    DebuggerUI.CopyToClipboard(StringUtility.Format("{0}\n\n{1}", _selectedNode.LogMessage, _selectedNode.StackTrack));
                }
            });
            copyButton.style.paddingLeft = 14f;
            copyButton.style.paddingRight = 14f;

            header.Add(titleLabel);
            header.Add(copyButton);
            panel.Add(header);

            _detailField = DebuggerUI.CreateReadOnlyMultilineText(string.Empty);
            _detailField.AddToClassList("dbg-console-detail__field");
            // 纯展示：禁用交互（不可聚焦、点击穿透——避免选中态与文本光标）
            _detailField.focusable = false;
            _detailField.pickingMode = PickingMode.Ignore;
            panel.Add(_detailField);
            return panel;
        }

        private void RefreshDetailPanel()
        {
            if (_detailField == null)
            {
                return;
            }

            if (_selectedNode == null)
            {
                _detailField.value = string.Empty;
                return;
            }

            _detailField.value = StringUtility.Format("{0}\n\n{1}", _selectedNode.LogMessage, _selectedNode.StackTrack);
        }

        #endregion
    }
}
