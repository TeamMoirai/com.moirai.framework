using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Moirai.Atropos.Save;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Editor.Save
{
    /// <summary>
    /// 存档浏览器编辑器窗口：三栏工作台（文件夹 / 槽位卡片 / 详情），
    /// 浏览 <c>persistentDataPath/Data/</c> 下的存档，展示块表、元数据、截图 sidecar 与内容预览。
    /// <para>支持搜索过滤、排序、备份/恢复/删除/复制槽位、导出块、十六进制与 JSON 美化预览、自动刷新。</para>
    /// <para>加密档/压缩档在缺管线配置时解析失败按提示展示（编辑器以明文处理器 + 设置的压缩提供方读取）。</para>
    /// </summary>
    public sealed class SaveBrowserWindow : EditorWindow
    {
        #region 常量 [CONSTANTS]

        /// <summary>十六进制预览默认采样字节数上限。</summary>
        private const int HEX_PREVIEW_MAX_BYTES = 512;

        /// <summary>备份文件后缀（与 <c>FileSaveStorageBackend</c> 一致）。</summary>
        private const string BACKUP_SUFFIX = ".bak";

        /// <summary>保留元数据块键。</summary>
        private const string META_BLOCK_KEY = "__meta";

        /// <summary>槽位列表项高度。</summary>
        private const float SLOT_ITEM_HEIGHT = 56f;

        /// <summary>文件夹列表项高度。</summary>
        private const float FOLDER_ITEM_HEIGHT = 28f;

        /// <summary>块列表项高度。</summary>
        private const float BLOCK_ITEM_HEIGHT = 34f;

        /// <summary>截图预览最大高度。</summary>
        private const float THUMBNAIL_MAX_HEIGHT = 160f;

        #endregion

        #region 枚举 [ENUMS]

        private enum SlotSortMode
        {
            TimeDesc = 0,
            TimeAsc = 1,
            NameAsc = 2,
            NameDesc = 3,
            SizeDesc = 4,
            SizeAsc = 5,
        }

        private enum PreviewMode
        {
            Auto = 0,
            Text = 1,
            Hex = 2,
        }

        #endregion

        #region 数据模型 [MODELS]

        /// <summary>槽位列表展示模型。</summary>
        private sealed class SlotView
        {
            public SaveFileInfo Info;
            public SaveBlockInfo[] Blocks;
            public SaveMetadata Metadata;
            public bool HasScreenshot;
            public bool HasBackup;
            public int HealthyBlocks;
            public int CorruptedBlocks;
            public bool IsEncryptedLikely;
            public bool DetailLoaded;
        }

        /// <summary>文件夹列表展示模型。</summary>
        private sealed class FolderView
        {
            public string Name;
            public int SlotCount;
        }

        #endregion

        #region 字段 [FIELDS]

        private PlainSaveHandler _handler;
        private string[] _folders = Array.Empty<string>();
        private readonly List<FolderView> _folderViews = new List<FolderView>();
        private readonly List<SlotView> _slotViews = new List<SlotView>();
        private readonly List<SlotView> _filteredSlots = new List<SlotView>();
        private readonly List<SaveBlockInfo> _blockViews = new List<SaveBlockInfo>();
        private readonly List<SaveBlockInfo> _filteredBlocks = new List<SaveBlockInfo>();

        private int _selectedFolderIndex;
        private string _selectedSlotName;
        private int _selectedBlockIndex = -1;
        private Dictionary<string, byte[]> _rawBlocks;
        private SlotView _selectedSlot;
        private Texture2D _thumbnailTexture;

        private SlotSortMode _sortMode = SlotSortMode.TimeDesc;
        private PreviewMode _previewMode = PreviewMode.Auto;
        private bool _prettyJson = true;
        private bool _autoRefresh;
        private bool _showOnlyIssues;
        private double _nextAutoRefreshTime;
        private string _statusMessage = string.Empty;
        private MessageType _statusType = MessageType.Info;
        private double _statusExpireTime;

        private string _folderFilter = string.Empty;
        private string _slotFilter = string.Empty;
        private string _blockFilter = string.Empty;

        // UI Toolkit 控件
        private ToolbarSearchField _slotSearchField;
        private ToolbarSearchField _blockSearchField;
        private ToolbarSearchField _folderSearchField;
        private ListView _folderList;
        private ListView _slotList;
        private ListView _blockList;
        private Label _folderHeaderLabel;
        private Label _slotHeaderLabel;
        private Label _detailHeaderLabel;
        private Label _pipelineLabel;
        private Label _statusLabel;
        private VisualElement _thumbnailElement;
        private VisualElement _metadataContainer;
        private VisualElement _actionBar;
        private ScrollView _previewScroll;
        private Label _previewLabel;
        private HelpBox _previewHelp;
        private VisualElement _detailRoot;
        private VisualElement _emptyDetail;
        private VisualElement _detailContent;

        private IVisualElementScheduledItem _autoRefreshSchedule;

        #endregion

        #region 窗口入口 [ENTRY]

        /// <summary>
        /// 打开存档浏览器窗口。
        /// </summary>
        [MenuItem("Window/Moirai/Save Browser", priority = 2200)]
        public static void Open()
        {
            var window = GetWindow<SaveBrowserWindow>();
            window.titleContent = new GUIContent("Save Browser", EditorGUIUtility.IconContent("SaveActive").image);
            window.minSize = new Vector2(1040f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            _handler = new PlainSaveHandler
            {
                _compression = SaveServiceSettings.CompressionProvider,
            };
            _autoRefresh = SessionState.GetBool("Moirai.SaveBrowser.AutoRefresh", false);
            _sortMode = (SlotSortMode)SessionState.GetInt("Moirai.SaveBrowser.SortMode", (int)SlotSortMode.TimeDesc);
            _prettyJson = SessionState.GetBool("Moirai.SaveBrowser.PrettyJson", true);
            _showOnlyIssues = SessionState.GetBool("Moirai.SaveBrowser.ShowOnlyIssues", false);
        }

        private void OnDisable()
        {
            SessionState.SetBool("Moirai.SaveBrowser.AutoRefresh", _autoRefresh);
            SessionState.SetInt("Moirai.SaveBrowser.SortMode", (int)_sortMode);
            SessionState.SetBool("Moirai.SaveBrowser.PrettyJson", _prettyJson);
            SessionState.SetBool("Moirai.SaveBrowser.ShowOnlyIssues", _showOnlyIssues);
            ReleaseThumbnail();
            _autoRefreshSchedule?.Pause();
        }

        #endregion

        #region 构建 UI [BUILD]

        private void CreateGUI()
        {
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            BuildToolbar();
            BuildStatusStrip();
            BuildMainLayout();
            RefreshAll();
            RegisterShortcuts();
            SetupAutoRefresh();
        }

        /// <summary>构建顶部工具栏。</summary>
        private void BuildToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.style.flexShrink = 0;

            var refreshButton = new ToolbarButton(RefreshAll) { tooltip = "刷新全部 (F5)" };
            refreshButton.Add(new Label("刷新"));
            toolbar.Add(refreshButton);

            var autoToggle = new ToolbarToggle
            {
                text = "自动",
                tooltip = "每 2 秒自动刷新（槽位列表与详情）",
                value = _autoRefresh,
            };
            autoToggle.RegisterValueChangedCallback(evt =>
            {
                _autoRefresh = evt.newValue;
                if (_autoRefresh)
                {
                    _autoRefreshSchedule?.Resume();
                    _nextAutoRefreshTime = EditorApplication.timeSinceStartup + 2.0;
                }
                else
                {
                    _autoRefreshSchedule?.Pause();
                }
            });
            toolbar.Add(autoToggle);

            toolbar.Add(new ToolbarSpacer());

            _folderSearchField = new ToolbarSearchField { tooltip = "过滤文件夹" };
            _folderSearchField.style.width = 120;
            _folderSearchField.RegisterValueChangedCallback(evt =>
            {
                _folderFilter = evt.newValue ?? string.Empty;
                RebuildFolderList(keepSelection: true);
            });
            toolbar.Add(_folderSearchField);

            _slotSearchField = new ToolbarSearchField { tooltip = "过滤槽位名" };
            _slotSearchField.style.width = 160;
            _slotSearchField.RegisterValueChangedCallback(evt =>
            {
                _slotFilter = evt.newValue ?? string.Empty;
                RebuildSlotList(keepSelection: true);
            });
            toolbar.Add(_slotSearchField);

            var sortEnum = new EnumField(_sortMode) { tooltip = "槽位排序" };
            sortEnum.style.width = 110;
            sortEnum.RegisterValueChangedCallback(evt =>
            {
                _sortMode = (SlotSortMode)evt.newValue;
                RebuildSlotList(keepSelection: true);
            });
            toolbar.Add(sortEnum);

            var issuesToggle = new ToolbarToggle
            {
                text = "仅问题",
                tooltip = "只显示含坏块或解析失败的槽位",
                value = _showOnlyIssues,
            };
            issuesToggle.RegisterValueChangedCallback(evt =>
            {
                _showOnlyIssues = evt.newValue;
                RebuildSlotList(keepSelection: true);
            });
            toolbar.Add(issuesToggle);

            toolbar.Add(new ToolbarSpacer());

            var openRootButton = new ToolbarButton(() =>
            {
                string root = SafeDetermineSavePath(string.Empty);
                if (!string.IsNullOrEmpty(root))
                {
                    EditorUtility.RevealInFinder(root);
                }
            })
            { tooltip = "在资源管理器中打开存档根目录" };
            openRootButton.Add(new Label("根目录"));
            toolbar.Add(openRootButton);

            var settingsButton = new ToolbarButton(() => EditorApplication.ExecuteMenuItem("Tools/Framework Settings"));
            settingsButton.Add(new Label("设置"));
            settingsButton.tooltip = "打开框架设置（存档管线配置）";
            toolbar.Add(settingsButton);

            rootVisualElement.Add(toolbar);
        }

        /// <summary>构建状态条（管线信息 + 操作反馈）。</summary>
        private void BuildStatusStrip()
        {
            var strip = new VisualElement();
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.alignItems = Align.Center;
            strip.style.minHeight = 22;
            strip.style.paddingLeft = 8;
            strip.style.paddingRight = 8;
            strip.style.borderBottomWidth = 1;
            strip.style.borderBottomColor = GetHairlineColor();
            strip.style.backgroundColor = GetStripBackgroundColor();

            _pipelineLabel = new Label();
            _pipelineLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            _pipelineLabel.style.fontSize = 11;
            _pipelineLabel.style.color = GetMutedColor();
            _pipelineLabel.style.flexGrow = 1;
            strip.Add(_pipelineLabel);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _statusLabel.style.flexShrink = 0;
            strip.Add(_statusLabel);

            rootVisualElement.Add(strip);
            UpdatePipelineLabel();
        }

        /// <summary>构建三栏主布局。</summary>
        private void BuildMainLayout()
        {
            var main = new TwoPaneSplitView(0, 180, TwoPaneSplitViewOrientation.Horizontal);
            main.style.flexGrow = 1;

            // ── 左：文件夹 ──
            var folderPane = new VisualElement();
            folderPane.style.flexGrow = 1;
            folderPane.style.borderRightWidth = 1;
            folderPane.style.borderRightColor = GetHairlineColor();

            _folderHeaderLabel = MakeSectionHeader("文件夹");
            folderPane.Add(_folderHeaderLabel);

            _folderList = new ListView
            {
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                fixedItemHeight = FOLDER_ITEM_HEIGHT,
                selectionType = SelectionType.Single,
                showBorder = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                makeItem = MakeFolderItem,
                bindItem = BindFolderItem,
            };
            _folderList.style.flexGrow = 1;
            _folderList.style.overflow = Overflow.Hidden;
#if UNITY_2023_1_OR_NEWER
            _folderList.selectionChanged += OnFolderSelectionChanged;
#else
            _folderList.onSelectionChange += OnFolderSelectionChanged;
#endif
            folderPane.Add(_folderList);

            var folderActions = new VisualElement();
            folderActions.style.flexDirection = FlexDirection.Row;
            folderActions.style.paddingLeft = 4;
            folderActions.style.paddingRight = 4;
            folderActions.style.paddingTop = 4;
            folderActions.style.paddingBottom = 4;
            folderActions.style.borderTopWidth = 1;
            folderActions.style.borderTopColor = GetHairlineColor();

            var newFolderButton = new Button(CreateFolderDialog) { text = "新建", tooltip = "新建存档文件夹" };
            newFolderButton.style.flexGrow = 1;
            folderActions.Add(newFolderButton);

            var deleteFolderButton = new Button(DeleteCurrentFolderDialog) { text = "删除", tooltip = "删除当前文件夹（含全部槽位）" };
            deleteFolderButton.style.flexGrow = 1;
            folderActions.Add(deleteFolderButton);

            var openFolderButton = new Button(() =>
            {
                string path = SafeDetermineSavePath(CurrentFolder);
                if (!string.IsNullOrEmpty(path))
                {
                    EditorUtility.RevealInFinder(path);
                }
            })
            { text = "定位", tooltip = "在资源管理器中打开当前文件夹" };
            openFolderButton.style.flexGrow = 1;
            folderActions.Add(openFolderButton);

            folderPane.Add(folderActions);
            main.Add(folderPane);

            // ── 右：槽位 + 详情 ──
            var rightSplit = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Horizontal);
            rightSplit.style.flexGrow = 1;

            var slotPane = new VisualElement();
            slotPane.style.flexGrow = 1;
            slotPane.style.borderRightWidth = 1;
            slotPane.style.borderRightColor = GetHairlineColor();

            _slotHeaderLabel = MakeSectionHeader("槽位");
            slotPane.Add(_slotHeaderLabel);

            _slotList = new ListView
            {
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                fixedItemHeight = SLOT_ITEM_HEIGHT,
                selectionType = SelectionType.Single,
                showBorder = false,
                makeItem = MakeSlotItem,
                bindItem = BindSlotItem,
            };
            _slotList.style.flexGrow = 1;
            _slotList.style.overflow = Overflow.Hidden;
#if UNITY_2023_1_OR_NEWER
            _slotList.selectionChanged += OnSlotSelectionChanged;
#else
            _slotList.onSelectionChange += OnSlotSelectionChanged;
#endif
            slotPane.Add(_slotList);

            var slotActions = new VisualElement();
            slotActions.style.flexDirection = FlexDirection.Row;
            slotActions.style.paddingLeft = 4;
            slotActions.style.paddingRight = 4;
            slotActions.style.paddingTop = 4;
            slotActions.style.paddingBottom = 4;
            slotActions.style.borderTopWidth = 1;
            slotActions.style.borderTopColor = GetHairlineColor();

            var refreshSlotsButton = new Button(RefreshSlots) { text = "刷新槽位" };
            refreshSlotsButton.style.flexGrow = 1;
            slotActions.Add(refreshSlotsButton);

            var copySlotButton = new Button(DuplicateSelectedSlot) { text = "复制槽位" };
            copySlotButton.style.flexGrow = 1;
            copySlotButton.tooltip = "复制当前槽位为新文件名";
            slotActions.Add(copySlotButton);

            slotPane.Add(slotActions);
            rightSplit.Add(slotPane);

            // ── 最右：详情 ──
            _detailRoot = new VisualElement();
            _detailRoot.style.flexGrow = 1;
            BuildDetailPane();
            rightSplit.Add(_detailRoot);

            main.Add(rightSplit);
            rootVisualElement.Add(main);
        }

        /// <summary>构建详情栏骨架（整栏纵向滚动，避免窗口变矮时区块互相压叠）。</summary>
        private void BuildDetailPane()
        {
            _detailHeaderLabel = MakeSectionHeader("详情");
            _detailRoot.Add(_detailHeaderLabel);

            _emptyDetail = new HelpBox("选择左侧槽位查看详情。", HelpBoxMessageType.Info);
            _emptyDetail.style.marginLeft = 8;
            _emptyDetail.style.marginRight = 8;
            _emptyDetail.style.marginTop = 8;
            _detailRoot.Add(_emptyDetail);

            var detailScroll = new ScrollView(ScrollViewMode.Vertical);
            detailScroll.style.flexGrow = 1;
            detailScroll.style.minHeight = 0;
            _detailRoot.Add(detailScroll);

            _detailContent = new VisualElement();
            _detailContent.style.display = DisplayStyle.None;
            _detailContent.style.flexShrink = 0;
            detailScroll.Add(_detailContent);

            // 缩略图
            _thumbnailElement = new VisualElement();
            _thumbnailElement.style.height = THUMBNAIL_MAX_HEIGHT;
            _thumbnailElement.style.flexShrink = 0;
            _thumbnailElement.style.marginLeft = 8;
            _thumbnailElement.style.marginRight = 8;
            _thumbnailElement.style.marginTop = 6;
            _thumbnailElement.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            _thumbnailElement.style.borderTopLeftRadius = 4;
            _thumbnailElement.style.borderTopRightRadius = 4;
            _thumbnailElement.style.borderBottomLeftRadius = 4;
            _thumbnailElement.style.borderBottomRightRadius = 4;
            _thumbnailElement.style.alignItems = Align.Center;
            _thumbnailElement.style.justifyContent = Justify.Center;
            _thumbnailElement.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            var thumbPlaceholder = new Label("无截图");
            thumbPlaceholder.name = "thumb-placeholder";
            thumbPlaceholder.style.color = GetMutedColor();
            thumbPlaceholder.style.fontSize = 11;
            _thumbnailElement.Add(thumbPlaceholder);
            _detailContent.Add(_thumbnailElement);

            // 元数据
            _metadataContainer = new VisualElement();
            _metadataContainer.style.flexShrink = 0;
            _metadataContainer.style.marginLeft = 8;
            _metadataContainer.style.marginRight = 8;
            _metadataContainer.style.marginTop = 6;
            _metadataContainer.style.paddingLeft = 8;
            _metadataContainer.style.paddingRight = 8;
            _metadataContainer.style.paddingTop = 6;
            _metadataContainer.style.paddingBottom = 6;
            _metadataContainer.style.backgroundColor = GetCardBackgroundColor();
            _metadataContainer.style.borderTopLeftRadius = 4;
            _metadataContainer.style.borderTopRightRadius = 4;
            _metadataContainer.style.borderBottomLeftRadius = 4;
            _metadataContainer.style.borderBottomRightRadius = 4;
            _detailContent.Add(_metadataContainer);

            // 操作栏
            _actionBar = new VisualElement();
            _actionBar.style.flexDirection = FlexDirection.Row;
            _actionBar.style.flexWrap = Wrap.Wrap;
            _actionBar.style.flexShrink = 0;
            _actionBar.style.marginLeft = 8;
            _actionBar.style.marginRight = 8;
            _actionBar.style.marginTop = 6;
            _actionBar.style.marginBottom = 2;
            _detailContent.Add(_actionBar);

            // 数据块区（固定高度列表，不随 flex 塌陷）
            var blockSection = MakeSubSection("数据块");
            blockSection.style.flexShrink = 0;
            var blockToolbar = new VisualElement();
            blockToolbar.style.flexDirection = FlexDirection.Row;
            blockToolbar.style.flexShrink = 0;
            blockToolbar.style.marginBottom = 4;

            _blockSearchField = new ToolbarSearchField { tooltip = "过滤块键" };
            _blockSearchField.style.flexGrow = 1;
            _blockSearchField.RegisterValueChangedCallback(evt =>
            {
                _blockFilter = evt.newValue ?? string.Empty;
                RebuildBlockList(keepSelection: true);
            });
            blockToolbar.Add(_blockSearchField);
            blockSection.Add(blockToolbar);

            _blockList = new ListView
            {
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                fixedItemHeight = BLOCK_ITEM_HEIGHT,
                selectionType = SelectionType.Single,
                showBorder = false,
                makeItem = MakeBlockItem,
                bindItem = BindBlockItem,
            };
            _blockList.style.flexShrink = 0;
            _blockList.style.flexGrow = 0;
            _blockList.style.height = 132;
            _blockList.style.marginBottom = 4;
            _blockList.style.overflow = Overflow.Hidden;
#if UNITY_2023_1_OR_NEWER
            _blockList.selectionChanged += OnBlockSelectionChanged;
#else
            _blockList.onSelectionChange += OnBlockSelectionChanged;
#endif
            blockSection.Add(_blockList);
            _detailContent.Add(blockSection);

            // 预览区
            var previewSection = MakeSubSection("预览");
            previewSection.style.flexShrink = 0;
            previewSection.style.flexGrow = 0;
            previewSection.style.marginBottom = 16;
            var previewToolbar = new VisualElement();
            previewToolbar.style.flexDirection = FlexDirection.Row;
            previewToolbar.style.flexShrink = 0;
            previewToolbar.style.marginBottom = 4;

            var previewModeField = new EnumField("模式", _previewMode);
            previewModeField.style.width = 120;
            previewModeField.RegisterValueChangedCallback(evt =>
            {
                _previewMode = (PreviewMode)evt.newValue;
                RebuildPreview();
            });
            previewToolbar.Add(previewModeField);

            var prettyToggle = new Toggle("JSON 美化") { value = _prettyJson };
            prettyToggle.style.marginLeft = 8;
            prettyToggle.RegisterValueChangedCallback(evt =>
            {
                _prettyJson = evt.newValue;
                RebuildPreview();
            });
            previewToolbar.Add(prettyToggle);

            var copyPreviewButton = new Button(CopyPreviewToClipboard) { text = "复制" };
            copyPreviewButton.style.marginLeft = 8;
            previewToolbar.Add(copyPreviewButton);

            var exportPreviewButton = new Button(ExportSelectedBlock) { text = "导出块" };
            exportPreviewButton.style.marginLeft = 4;
            previewToolbar.Add(exportPreviewButton);

            previewSection.Add(previewToolbar);

            _previewScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _previewScroll.style.flexShrink = 0;
            _previewScroll.style.flexGrow = 0;
            _previewScroll.style.height = 200;
            _previewScroll.style.overflow = Overflow.Hidden;
            _previewScroll.style.backgroundColor = GetCardBackgroundColor();
            _previewScroll.style.borderTopLeftRadius = 4;
            _previewScroll.style.borderTopRightRadius = 4;
            _previewScroll.style.borderBottomLeftRadius = 4;
            _previewScroll.style.borderBottomRightRadius = 4;

            _previewLabel = new Label();
            _previewLabel.style.whiteSpace = WhiteSpace.Normal;
            _previewLabel.style.fontSize = 11;
            _previewLabel.style.color = GetPreviewTextColor();
            _previewLabel.style.paddingLeft = 8;
            _previewLabel.style.paddingRight = 8;
            _previewLabel.style.paddingTop = 6;
            _previewLabel.style.paddingBottom = 6;
            _previewScroll.Add(_previewLabel);
            previewSection.Add(_previewScroll);

            _previewHelp = new HelpBox(string.Empty, HelpBoxMessageType.None);
            _previewHelp.style.display = DisplayStyle.None;
            _previewHelp.style.flexShrink = 0;
            _previewHelp.style.marginTop = 4;
            previewSection.Add(_previewHelp);

            _detailContent.Add(previewSection);
        }

        /// <summary>注册快捷键。</summary>
        private void RegisterShortcuts()
        {
            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.F5)
                {
                    RefreshAll();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Delete && _selectedSlot != null
                                                         && (evt.target as VisualElement)?.GetFirstAncestorOfType<TextField>() == null
                                                         && (evt.target as VisualElement)?.GetFirstAncestorOfType<ToolbarSearchField>() == null)
                {
                    DeleteSelectedSlotDialog();
                    evt.StopPropagation();
                }
            });
        }

        /// <summary>配置自动刷新调度。</summary>
        private void SetupAutoRefresh()
        {
            _autoRefreshSchedule = rootVisualElement.schedule.Execute(() =>
            {
                if (!_autoRefresh)
                {
                    return;
                }

                if (EditorApplication.timeSinceStartup < _nextAutoRefreshTime)
                {
                    return;
                }

                _nextAutoRefreshTime = EditorApplication.timeSinceStartup + 2.0;
                RefreshSlots(preserveSelection: true);
                if (_selectedSlot != null)
                {
                    LoadSlotDetail(_selectedSlot);
                    RefreshDetailUI();
                }
            }).Every(500);
            if (!_autoRefresh)
            {
                _autoRefreshSchedule.Pause();
            }
        }

        #endregion

        #region 列表项工厂 [LIST ITEMS]

        private static Label MakeSectionHeader(string title)
        {
            var label = new Label(title);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 12;
            label.style.paddingLeft = 8;
            label.style.paddingTop = 6;
            label.style.paddingBottom = 4;
            label.style.borderBottomWidth = 1;
            label.style.borderBottomColor = GetHairlineColor();
            label.style.flexShrink = 0;
            return label;
        }

        private static VisualElement MakeSubSection(string title)
        {
            var section = new VisualElement();
            section.style.flexGrow = 0;
            section.style.flexShrink = 0;
            section.style.flexDirection = FlexDirection.Column;
            section.style.marginLeft = 8;
            section.style.marginRight = 8;
            section.style.marginTop = 8;

            var header = new Label(title);
            header.name = "section-header";
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 11;
            header.style.marginBottom = 4;
            header.style.color = GetMutedColor();
            section.Add(header);
            return section;
        }

        private VisualElement MakeFolderItem()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;

            var icon = new Label("▸");
            icon.name = "icon";
            icon.style.width = 14;
            icon.style.color = GetMutedColor();
            icon.style.fontSize = 11;
            root.Add(icon);

            var name = new Label();
            name.name = "name";
            name.style.flexGrow = 1;
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            name.style.fontSize = 12;
            root.Add(name);

            var count = new Label();
            count.name = "count";
            count.style.fontSize = 11;
            count.style.color = GetMutedColor();
            root.Add(count);

            return root;
        }

        private void BindFolderItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _folderViews.Count)
            {
                return;
            }

            FolderView view = _folderViews[index];
            var name = element.Q<Label>("name");
            var count = element.Q<Label>("count");
            var icon = element.Q<Label>("icon");

            name.text = view.Name;
            count.text = view.SlotCount > 0 ? view.SlotCount.ToString() : string.Empty;

            // 选中态交给 ListView 原生样式，避免自定义背景与内建选中叠加
            bool selected = index == _folderList.selectedIndex;
            name.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
            name.style.color = selected ? GetSelectedTextColor() : GetPrimaryTextColor();
            icon.text = selected ? "▾" : "▸";
        }

        private VisualElement MakeSlotItem()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;

            var badge = new Label();
            badge.name = "badge";
            badge.style.width = 6;
            badge.style.height = 32;
            badge.style.marginRight = 8;
            badge.style.borderTopLeftRadius = 2;
            badge.style.borderBottomLeftRadius = 2;
            root.Add(badge);

            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.flexDirection = FlexDirection.Column;

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;

            var name = new Label();
            name.name = "name";
            name.style.flexGrow = 1;
            name.style.fontSize = 12;
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            titleRow.Add(name);

            var size = new Label();
            size.name = "size";
            size.style.fontSize = 11;
            size.style.color = GetMutedColor();
            size.style.marginLeft = 6;
            titleRow.Add(size);
            body.Add(titleRow);

            var meta = new Label();
            meta.name = "meta";
            meta.style.fontSize = 11;
            meta.style.color = GetMutedColor();
            meta.style.unityTextAlign = TextAnchor.MiddleLeft;
            body.Add(meta);

            root.Add(body);
            return root;
        }

        private void BindSlotItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _filteredSlots.Count)
            {
                return;
            }

            SlotView view = _filteredSlots[index];
            SaveFileInfo info = view.Info;

            var badge = element.Q<Label>("badge");
            var name = element.Q<Label>("name");
            var size = element.Q<Label>("size");
            var meta = element.Q<Label>("meta");

            name.text = info.FileName;
            size.text = FormatBytes(info.SizeBytes);

            var metaParts = new List<string>(4)
            {
                FormatRelativeTime(info.LastWriteTimeUtc),
            };
            if (view.DetailLoaded)
            {
                if (view.CorruptedBlocks > 0)
                {
                    metaParts.Add($"{view.HealthyBlocks}块/{view.CorruptedBlocks}坏");
                }
                else
                {
                    metaParts.Add($"{view.Blocks.Length}块");
                }

                if (view.HasScreenshot)
                {
                    metaParts.Add("图");
                }

                if (view.HasBackup)
                {
                    metaParts.Add("备份");
                }

                if (view.Metadata != null && !string.IsNullOrEmpty(view.Metadata.SceneName))
                {
                    metaParts.Add(view.Metadata.SceneName);
                }
            }
            else
            {
                metaParts.Add("…");
            }

            meta.text = string.Join("  ·  ", metaParts);

            bool selected = view.Info.FileName == _selectedSlotName;
            element.style.backgroundColor = selected ? GetSelectedRowColor() : Color.clear;
            name.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
            badge.style.backgroundColor = ResolveSlotHealthColor(view);
        }

        private VisualElement MakeBlockItem()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;

            var badge = new Label();
            badge.name = "badge";
            badge.style.width = 4;
            badge.style.height = 22;
            badge.style.marginRight = 8;
            badge.style.borderTopLeftRadius = 2;
            badge.style.borderBottomLeftRadius = 2;
            root.Add(badge);

            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.flexDirection = FlexDirection.Column;

            var key = new Label();
            key.name = "key";
            key.style.fontSize = 12;
            key.style.unityTextAlign = TextAnchor.MiddleLeft;
            body.Add(key);

            var info = new Label();
            info.name = "info";
            info.style.fontSize = 11;
            info.style.color = GetMutedColor();
            info.style.unityTextAlign = TextAnchor.MiddleLeft;
            body.Add(info);

            root.Add(body);

            var backend = new Label();
            backend.name = "backend";
            backend.style.fontSize = 10;
            backend.style.paddingLeft = 6;
            backend.style.paddingRight = 6;
            backend.style.paddingTop = 1;
            backend.style.paddingBottom = 1;
            backend.style.borderTopLeftRadius = 3;
            backend.style.borderTopRightRadius = 3;
            backend.style.borderBottomLeftRadius = 3;
            backend.style.borderBottomRightRadius = 3;
            root.Add(backend);

            return root;
        }

        private void BindBlockItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _filteredBlocks.Count)
            {
                return;
            }

            SaveBlockInfo block = _filteredBlocks[index];
            bool isSelected = index == _selectedBlockIndex;

            var badge = element.Q<Label>("badge");
            var key = element.Q<Label>("key");
            var info = element.Q<Label>("info");
            var backend = element.Q<Label>("backend");

            string keyText = block.Key ?? (block.Error != SaveError.None ? "<结构不可读>" : "<未知>");
            key.text = keyText;
            key.style.unityFontStyleAndWeight = isSelected ? FontStyle.Bold : FontStyle.Normal;

            if (!block.HasMetadata)
            {
                info.text = block.Error != SaveError.None ? $"结构不可读 · {block.Error}" : "结构不可读";
                badge.style.backgroundColor = GetErrorColor();
                backend.text = "?";
                backend.style.color = GetErrorColor();
                backend.style.backgroundColor = WithAlpha(GetErrorColor(), 0.2f);
            }
            else
            {
                var parts = new List<string>
                {
                    $"v{block.DataVersion}",
                    FormatBytes(block.SizeBytes),
                };
                if (block.Error != SaveError.None)
                {
                    parts.Add(block.Error.ToString());
                }

                info.text = string.Join("  ·  ", parts);
                badge.style.backgroundColor = block.Error != SaveError.None ? GetErrorColor() : GetOkColor();
                backend.text = ShortBackendName(block.Backend);
                backend.style.color = BackendColor(block.Backend);
                backend.style.backgroundColor = WithAlpha(BackendColor(block.Backend), 0.18f);
            }

            element.style.backgroundColor = isSelected ? GetSelectedRowColor() : Color.clear;
        }

        #endregion

        #region 选择与刷新 [SELECTION / REFRESH]

        /// <summary>当前选中文件夹名。</summary>
        private string CurrentFolder =>
            _folderViews.Count == 0 || _selectedFolderIndex < 0 || _selectedFolderIndex >= _folderViews.Count
                ? SaveServiceHandler.DEFAULT_FOLDER_NAME
                : _folderViews[_selectedFolderIndex].Name;

        /// <summary>刷新全部数据。</summary>
        private void RefreshAll()
        {
            UpdatePipelineLabel();
            RefreshFolders();
            SetStatus("已刷新。", MessageType.Info, 2.5);
        }

        private void RefreshFolders()
        {
            string previous = CurrentFolder;
            var folders = new List<string> { SaveServiceHandler.DEFAULT_FOLDER_NAME };
            string rootDirectory = SafeDetermineSavePath(string.Empty);
            if (!string.IsNullOrEmpty(rootDirectory) && Directory.Exists(rootDirectory))
            {
                string[] directories = Directory.GetDirectories(rootDirectory);
                for (int i = 0; i < directories.Length; i++)
                {
                    string folderName = Path.GetFileName(directories[i]);
                    if (!string.IsNullOrEmpty(folderName) && !folders.Contains(folderName))
                    {
                        folders.Add(folderName);
                    }
                }
            }

            _folders = folders.ToArray();

            int newIndex = folders.IndexOf(previous);
            _selectedFolderIndex = newIndex >= 0 ? newIndex : 0;

            RebuildFolderList(keepSelection: true);
            RefreshSlots(preserveSelection: true);
        }

        private void RebuildFolderList(bool keepSelection)
        {
            string selectedName = keepSelection ? CurrentFolder : null;
            _folderViews.Clear();
            for (int i = 0; i < _folders.Length; i++)
            {
                string folderName = _folders[i];
                if (!string.IsNullOrEmpty(_folderFilter) &&
                    folderName.IndexOf(_folderFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                SaveFileInfo[] slots = SafeGetSaveFiles(folderName);
                _folderViews.Add(new FolderView
                {
                    Name = folderName,
                    SlotCount = slots?.Length ?? 0,
                });
            }

            if (!string.IsNullOrEmpty(selectedName))
            {
                int idx = _folderViews.FindIndex(f => string.Equals(f.Name, selectedName, StringComparison.Ordinal));
                _selectedFolderIndex = idx >= 0 ? idx : 0;
            }
            else if (_selectedFolderIndex >= _folderViews.Count)
            {
                _selectedFolderIndex = Mathf.Max(0, _folderViews.Count - 1);
            }

            _folderList.itemsSource = _folderViews;
            _folderList.RefreshItems();
            _folderList.SetSelectionWithoutNotify(new[] { _selectedFolderIndex });

            _folderHeaderLabel.text = $"文件夹  ({_folderViews.Count})";
        }

        private void OnFolderSelectionChanged(IEnumerable<object> _)
        {
            if (_folderList.selectedIndex < 0 || _folderList.selectedIndex >= _folderViews.Count)
            {
                return;
            }

            if (_folderList.selectedIndex == _selectedFolderIndex)
            {
                return;
            }

            _selectedFolderIndex = _folderList.selectedIndex;
            _selectedSlotName = null;
            ClearSlotDetail();
            RebuildFolderList(keepSelection: true);
            _folderList.RefreshItems();
            RefreshSlots();
            RefreshDetailUI();
        }

        private void RefreshSlots()
        {
            RefreshSlots(preserveSelection: false);
        }

        private void RefreshSlots(bool preserveSelection)
        {
            string previousName = preserveSelection ? _selectedSlotName : null;
            string folder = CurrentFolder;
            SaveFileInfo[] files = SafeGetSaveFiles(folder) ?? Array.Empty<SaveFileInfo>();

            _slotViews.Clear();
            for (int i = 0; i < files.Length; i++)
            {
                _slotViews.Add(new SlotView { Info = files[i] });
            }

            // 轻量详情：批量读块信息与 sidecar 存在性（元数据仅选中槽加载，避免列表刷盘开销）
            for (int i = 0; i < _slotViews.Count; i++)
            {
                EnrichSlotView(_slotViews[i], folder, loadMetadata: false);
            }

            RebuildSlotList(keepSelection: false);

            if (!string.IsNullOrEmpty(previousName))
            {
                int idx = _filteredSlots.FindIndex(s => string.Equals(s.Info.FileName, previousName, StringComparison.Ordinal));
                if (idx >= 0)
                {
                    _slotList.SetSelection(new[] { idx });
                    return;
                }
            }

            _selectedSlotName = null;
            _selectedSlot = null;
            ClearSlotDetail();
            RefreshDetailUI();
        }

        private void EnrichSlotView(SlotView view, string folder, bool loadMetadata = false)
        {
            try
            {
                view.Blocks = _handler.GetBlockInfos(view.Info.FileName, folder) ?? Array.Empty<SaveBlockInfo>();
                view.HealthyBlocks = 0;
                view.CorruptedBlocks = 0;
                for (int i = 0; i < view.Blocks.Length; i++)
                {
                    if (view.Blocks[i].Error == SaveError.None)
                    {
                        view.HealthyBlocks++;
                    }
                    else
                    {
                        view.CorruptedBlocks++;
                    }
                }

                view.IsEncryptedLikely = view.Blocks.Length == 0 && view.Info.SizeBytes > 64;

                SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(view.Info.FileName, folder);
                string screenshotPath = Path.Combine(paths.DirectoryPath, SaveScreenshotUtility.DetermineScreenshotFileName(view.Info.FileName));
                view.HasScreenshot = File.Exists(screenshotPath);
                view.HasBackup = File.Exists(paths.SaveFilePath + BACKUP_SUFFIX);

                if (loadMetadata)
                {
                    SaveResult<SaveMetadata> metaResult = _handler.TryLoadBlock<SaveMetadata>(view.Info.FileName, META_BLOCK_KEY, folder);
                    view.Metadata = metaResult.IsSuccess ? metaResult.Data : null;
                }

                view.DetailLoaded = true;
            }
            catch
            {
                view.Blocks = Array.Empty<SaveBlockInfo>();
                view.DetailLoaded = true;
            }
        }

        private void RebuildSlotList(bool keepSelection)
        {
            string selected = keepSelection ? _selectedSlotName : null;

            IEnumerable<SlotView> query = _slotViews;
            if (!string.IsNullOrEmpty(_slotFilter))
            {
                query = query.Where(s => s.Info.FileName.IndexOf(_slotFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (_showOnlyIssues)
            {
                query = query.Where(s => s.CorruptedBlocks > 0 || s.IsEncryptedLikely || (s.Blocks != null && s.Blocks.Length == 0));
            }

            query = _sortMode switch
            {
                SlotSortMode.TimeAsc => query.OrderBy(s => s.Info.LastWriteTimeUtc),
                SlotSortMode.NameAsc => query.OrderBy(s => s.Info.FileName, StringComparer.OrdinalIgnoreCase),
                SlotSortMode.NameDesc => query.OrderByDescending(s => s.Info.FileName, StringComparer.OrdinalIgnoreCase),
                SlotSortMode.SizeDesc => query.OrderByDescending(s => s.Info.SizeBytes),
                SlotSortMode.SizeAsc => query.OrderBy(s => s.Info.SizeBytes),
                _ => query.OrderByDescending(s => s.Info.LastWriteTimeUtc),
            };

            _filteredSlots.Clear();
            _filteredSlots.AddRange(query);

            _slotList.itemsSource = _filteredSlots;
            _slotList.RefreshItems();
            _slotHeaderLabel.text = $"槽位  ({_filteredSlots.Count})";

            int selectedIndex = -1;
            if (!string.IsNullOrEmpty(selected))
            {
                selectedIndex = _filteredSlots.FindIndex(s => string.Equals(s.Info.FileName, selected, StringComparison.Ordinal));
            }

            if (selectedIndex >= 0)
            {
                _slotList.SetSelectionWithoutNotify(new[] { selectedIndex });
            }
        }

        private void OnSlotSelectionChanged(IEnumerable<object> selection)
        {
            if (_slotList.selectedIndex < 0 || _slotList.selectedIndex >= _filteredSlots.Count)
            {
                _selectedSlotName = null;
                _selectedSlot = null;
                ClearSlotDetail();
                RefreshDetailUI();
                return;
            }

            SlotView view = _filteredSlots[_slotList.selectedIndex];
            _selectedSlotName = view.Info.FileName;
            _selectedSlot = view;
            _selectedBlockIndex = -1;
            _rawBlocks = null;
            LoadSlotDetail(view);
            // 整表重绑：否则上一个选中项的自定义背景不会被清掉，出现双选中
            _slotList.RefreshItems();
            RefreshDetailUI();
        }

        /// <summary>加载选中槽位的块表与原始块内容。</summary>
        private void LoadSlotDetail(SlotView view)
        {
            if (view == null)
            {
                return;
            }

            string folder = CurrentFolder;
            EnrichSlotView(view, folder, loadMetadata: true);

            try
            {
                SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(view.Info.FileName, folder);
                _rawBlocks = _handler.ReadRawBlocks(paths);
            }
            catch
            {
                _rawBlocks = null;
            }

            RebuildBlockList(keepSelection: false);
            LoadThumbnail(view, folder);
        }

        private void ClearSlotDetail()
        {
            _blockViews.Clear();
            _filteredBlocks.Clear();
            _rawBlocks = null;
            _selectedBlockIndex = -1;
            _selectedSlot = null;
            ReleaseThumbnail();
        }

        private void RebuildBlockList(bool keepSelection)
        {
            _blockViews.Clear();
            if (_selectedSlot?.Blocks != null)
            {
                _blockViews.AddRange(_selectedSlot.Blocks);
            }

            IEnumerable<SaveBlockInfo> query = _blockViews;
            if (!string.IsNullOrEmpty(_blockFilter))
            {
                query = query.Where(b => b.Key != null &&
                                         b.Key.IndexOf(_blockFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            _filteredBlocks.Clear();
            _filteredBlocks.AddRange(query);

            if (_blockList != null)
            {
                _blockList.itemsSource = _filteredBlocks;
                _blockList.RefreshItems();
                if (keepSelection && _selectedBlockIndex >= 0 && _selectedBlockIndex < _filteredBlocks.Count)
                {
                    _blockList.SetSelectionWithoutNotify(new[] { _selectedBlockIndex });
                }
            }
        }

        private void OnBlockSelectionChanged(IEnumerable<object> selection)
        {
            _selectedBlockIndex = _blockList.selectedIndex;
            _blockList.RefreshItems();
            RebuildPreview();
        }

        #endregion

        #region 详情 UI [DETAIL UI]

        /// <summary>重建详情栏内容。</summary>
        private void RefreshDetailUI()
        {
            if (_selectedSlot == null)
            {
                _emptyDetail.style.display = DisplayStyle.Flex;
                _detailContent.style.display = DisplayStyle.None;
                _detailHeaderLabel.text = "详情";
                return;
            }

            SaveFileInfo info = _selectedSlot.Info;
            _emptyDetail.style.display = DisplayStyle.None;
            _detailContent.style.display = DisplayStyle.Flex;
            _detailHeaderLabel.text = $"详情 — {info.FileName}";

            RebuildMetadataPanel();
            RebuildActionBar();
            RebuildBlockList(keepSelection: true);
            RebuildPreview();
            RefreshThumbnailUI();
        }

        private void RebuildMetadataPanel()
        {
            _metadataContainer.Clear();
            SlotView slot = _selectedSlot;
            SaveFileInfo info = slot.Info;

            AddMetaRow(_metadataContainer, "文件", info.FileName + SaveServiceSettings.SaveFileExtension);
            AddMetaRow(_metadataContainer, "大小", FormatBytes(info.SizeBytes));
            AddMetaRow(_metadataContainer, "最后写入", info.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            AddMetaRow(_metadataContainer, "路径", TruncatePath(SafeGetSlotFullPath(info.FileName, CurrentFolder), 64));

            int total = slot.Blocks?.Length ?? 0;
            string health = slot.CorruptedBlocks > 0
                ? $"{total} 块  ·  {slot.HealthyBlocks} 健康  ·  {slot.CorruptedBlocks} 坏块"
                : total == 0
                    ? (slot.IsEncryptedLikely ? "0 块  ·  疑似加密/不可解析" : "0 块")
                    : $"{total} 块  ·  全部健康";
            AddMetaRow(_metadataContainer, "数据块", health);
            AddMetaRow(_metadataContainer, "截图", slot.HasScreenshot ? "有" : "无");
            AddMetaRow(_metadataContainer, "备份", slot.HasBackup ? "有 (.bak)" : "无");

            SaveMetadata metadata = slot.Metadata;
            if (metadata != null)
            {
                _metadataContainer.Add(MakeDivider());
                AddMetaRow(_metadataContainer, "游戏版本", string.IsNullOrEmpty(metadata.GameVersion) ? "-" : metadata.GameVersion);
                AddMetaRow(_metadataContainer, "存档版本", metadata.SaveVersion.ToString());
                AddMetaRow(_metadataContainer, "场景", string.IsNullOrEmpty(metadata.SceneName) ? "-" : metadata.SceneName);
                AddMetaRow(_metadataContainer, "游玩时长", metadata.PlayTimeTicks > 0L ? FormatTimeSpan(new TimeSpan(metadata.PlayTimeTicks)) : "-");
                int migrations = metadata.MigrationHistory?.Count ?? 0;
                AddMetaRow(_metadataContainer, "迁移历史", migrations > 0 ? $"{migrations} 次" : "-");

                if (metadata.Custom != null && metadata.Custom.Count > 0)
                {
                    foreach (KeyValuePair<string, string> pair in metadata.Custom)
                    {
                        AddMetaRow(_metadataContainer, pair.Key, pair.Value ?? "-");
                    }
                }

                if (metadata.MigrationHistory is { Count: > 0 })
                {
                    var foldout = new Foldout { text = "迁移明细", value = false };
                    foldout.style.marginTop = 4;
                    for (int i = 0; i < metadata.MigrationHistory.Count; i++)
                    {
                        var line = new Label(metadata.MigrationHistory[i]);
                        line.style.fontSize = 10;
                        line.style.color = GetMutedColor();
                        line.style.whiteSpace = WhiteSpace.Normal;
                        foldout.Add(line);
                    }

                    _metadataContainer.Add(foldout);
                }
            }
            else if (slot.IsEncryptedLikely)
            {
                _metadataContainer.Add(MakeDivider());
                var warn = new HelpBox("无法解析元数据。当前项目可能使用加密处理器，或档文件已损坏。", HelpBoxMessageType.Warning);
                _metadataContainer.Add(warn);
            }
        }

        private void RebuildActionBar()
        {
            _actionBar.Clear();
            if (_selectedSlot == null)
            {
                return;
            }

            AddActionButton("备份", "创建 .bak 备份（覆盖旧备份）", RunBackup, isDestructive: false);
            AddActionButton("恢复备份", "用 .bak 覆盖当前存档", RestoreBackupDialog, isDestructive: true);
            AddActionButton("删除", "删除存档（含截图）；不可撤销", DeleteSelectedSlotDialog, isDestructive: true);
            AddActionButton("复制槽位", "复制为新槽位文件", DuplicateSelectedSlot, isDestructive: false);
            AddActionButton("定位文件", "在资源管理器中选中存档文件", RevealSelectedFile, isDestructive: false);
            AddActionButton("复制路径", "复制存档完整路径到剪贴板", CopySelectedPath, isDestructive: false);
            AddActionButton("导出块", "导出选中数据块为文件", ExportSelectedBlock, isDestructive: false);
        }

        private void AddActionButton(string text, string tooltip, Action onClick, bool isDestructive)
        {
            var button = new Button(onClick) { text = text, tooltip = tooltip };
            button.style.marginRight = 4;
            button.style.marginBottom = 2;
            if (isDestructive)
            {
                button.style.color = GetErrorColor();
            }

            _actionBar.Add(button);
        }

        private static void AddMetaRow(VisualElement container, string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.minHeight = 16;
            row.style.marginBottom = 2;
            row.style.flexShrink = 0;

            var key = new Label(label);
            key.style.width = 84;
            key.style.minWidth = 84;
            key.style.maxWidth = 84;
            key.style.fontSize = 11;
            key.style.color = GetMutedColor();
            key.style.unityTextAlign = TextAnchor.MiddleLeft;
            key.style.whiteSpace = WhiteSpace.NoWrap;
            key.style.flexShrink = 0;
            row.Add(key);

            var val = new Label(value ?? "-");
            val.style.flexGrow = 1;
            val.style.flexShrink = 1;
            val.style.fontSize = 11;
            val.style.color = GetPrimaryTextColor();
            val.style.unityTextAlign = TextAnchor.MiddleLeft;
            val.style.whiteSpace = WhiteSpace.Normal;
            val.style.marginLeft = 4;
            row.Add(val);

            container.Add(row);
        }

        private static VisualElement MakeDivider()
        {
            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.marginTop = 6;
            divider.style.marginBottom = 6;
            divider.style.backgroundColor = GetHairlineColor();
            return divider;
        }

        #endregion

        #region 预览 [PREVIEW]

        private void RebuildPreview()
        {
            if (_previewLabel == null)
            {
                return;
            }

            if (_selectedBlockIndex < 0 || _selectedBlockIndex >= _filteredBlocks.Count || _rawBlocks == null)
            {
                _previewLabel.text = string.Empty;
                SetPreviewHelp("选中数据块以查看内容。", MessageType.None, visible: true);
                return;
            }

            SaveBlockInfo block = _filteredBlocks[_selectedBlockIndex];
            if (block.Error != SaveError.None)
            {
                _previewLabel.text = string.Empty;
                SetPreviewHelp($"该块损坏（{block.Error}），载荷不可信。", MessageType.Warning, visible: true);
                return;
            }

            if (block.Key == null || !_rawBlocks.TryGetValue(block.Key, out byte[] bytes) || bytes == null)
            {
                _previewLabel.text = string.Empty;
                SetPreviewHelp("无法读取块载荷。", MessageType.Warning, visible: true);
                return;
            }

            string text;
            bool isBinaryBackend = block.Backend != ESaveBackend.Json && block.Backend != ESaveBackend.KeyValue;

            switch (_previewMode)
            {
                case PreviewMode.Text:
                    text = TryDecodeText(bytes, isBinaryBackend);
                    break;
                case PreviewMode.Hex:
                    text = BuildHexDump(bytes);
                    break;
                default:
                    if (block.Backend == ESaveBackend.Json)
                    {
                        text = FormatJson(Encoding.UTF8.GetString(bytes), _prettyJson);
                    }
                    else if (block.Backend == ESaveBackend.KeyValue)
                    {
                        text = BuildHexDump(bytes, HEX_PREVIEW_MAX_BYTES);
                    }
                    else
                    {
                        text = BuildHexDump(bytes);
                    }

                    break;
            }

            if (string.IsNullOrEmpty(text))
            {
                _previewLabel.text = string.Empty;
                SetPreviewHelp($"二进制后端（{block.Backend}）载荷 {FormatBytes(bytes.Length)}，不提供文本预览。请切换到十六进制模式。", MessageType.Info, visible: true);
                return;
            }

            _previewLabel.text = text;
            string footer = $"{block.Key}  ·  {block.Backend}  ·  v{block.DataVersion}  ·  {FormatBytes(bytes.Length)}";
            SetPreviewHelp(footer, MessageType.None, visible: true);
        }

        private string TryDecodeText(byte[] bytes, bool isBinaryBackend)
        {
            if (isBinaryBackend)
            {
                return string.Empty;
            }

            try
            {
                string text = Encoding.UTF8.GetString(bytes);
                return _prettyJson ? FormatJson(text, true) : text;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void CopyPreviewToClipboard()
        {
            if (!string.IsNullOrEmpty(_previewLabel?.text))
            {
                EditorGUIUtility.systemCopyBuffer = _previewLabel.text;
                SetStatus("预览内容已复制到剪贴板。", MessageType.Info, 2.5);
            }
        }

        private void SetPreviewHelp(string message, MessageType type, bool visible)
        {
            if (_previewHelp == null)
            {
                return;
            }

            _previewHelp.text = message;
            _previewHelp.messageType = type switch
            {
                MessageType.Error => HelpBoxMessageType.Error,
                MessageType.Warning => HelpBoxMessageType.Warning,
                MessageType.Info => HelpBoxMessageType.Info,
                _ => HelpBoxMessageType.None,
            };
            _previewHelp.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        #endregion

        #region 操作 [OPERATIONS]

        private void RunBackup()
        {
            if (_selectedSlot == null)
            {
                return;
            }

            RunOperation("已创建备份。", () => _handler.CreateBackup(_selectedSlot.Info.FileName, CurrentFolder), after: () =>
            {
                RefreshSlots(preserveSelection: true);
                RefreshDetailUI();
            });
        }

        private void RestoreBackupDialog()
        {
            if (_selectedSlot == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("恢复备份", $"以 {_selectedSlot.Info.FileName}{BACKUP_SUFFIX} 覆盖当前存档？", "恢复", "取消"))
            {
                return;
            }

            RunOperation("已从备份恢复。", () => _handler.RestoreBackup(_selectedSlot.Info.FileName, CurrentFolder), after: () =>
            {
                RefreshSlots(preserveSelection: true);
                if (_selectedSlot != null)
                {
                    LoadSlotDetail(_selectedSlot);
                }

                RefreshDetailUI();
            });
        }

        private void DeleteSelectedSlotDialog()
        {
            if (_selectedSlot == null)
            {
                return;
            }

            string name = _selectedSlot.Info.FileName;
            if (!EditorUtility.DisplayDialog("删除存档", $"删除存档「{name}」（含截图 sidecar）？\n不可撤销。", "删除", "取消"))
            {
                return;
            }

            RunOperation("已删除。", () => _handler.DeleteSave(name, CurrentFolder), after: () =>
            {
                _selectedSlotName = null;
                _selectedSlot = null;
                ClearSlotDetail();
                RefreshSlots();
                RefreshDetailUI();
            });
        }

        private void DuplicateSelectedSlot()
        {
            if (_selectedSlot == null)
            {
                SetStatus("请先选择槽位。", MessageType.Warning, 2.5);
                return;
            }

            string sourceName = _selectedSlot.Info.FileName;
            string folder = CurrentFolder;
            string directory = SafeDetermineSavePath(folder);
            if (string.IsNullOrEmpty(directory))
            {
                SetStatus("无法解析存档目录。", MessageType.Error, 3);
                return;
            }

            string extension = SaveServiceSettings.SaveFileExtension;
            string sourcePath = Path.Combine(directory, sourceName + extension);
            if (!File.Exists(sourcePath))
            {
                SetStatus("源存档文件不存在。", MessageType.Error, 3);
                return;
            }

            string newName = sourceName + "_copy";
            int suffix = 1;
            while (File.Exists(Path.Combine(directory, newName + extension)))
            {
                newName = $"{sourceName}_copy{suffix++}";
            }

            string newPath = Path.Combine(directory, newName + extension);
            RunOperation($"已复制为「{newName}」。", () =>
            {
                File.Copy(sourcePath, newPath, overwrite: false);
                string srcShot = Path.Combine(directory, SaveScreenshotUtility.DetermineScreenshotFileName(sourceName));
                string dstShot = Path.Combine(directory, SaveScreenshotUtility.DetermineScreenshotFileName(newName));
                if (File.Exists(srcShot))
                {
                    File.Copy(srcShot, dstShot, overwrite: false);
                }

                string srcBak = sourcePath + BACKUP_SUFFIX;
                string dstBak = newPath + BACKUP_SUFFIX;
                if (File.Exists(srcBak))
                {
                    File.Copy(srcBak, dstBak, overwrite: false);
                }
            }, after: () =>
            {
                _selectedSlotName = newName;
                RefreshSlots(preserveSelection: true);
                int idx = _filteredSlots.FindIndex(s => s.Info.FileName == newName);
                if (idx >= 0)
                {
                    _slotList.SetSelection(new[] { idx });
                }
            });
        }

        private void RevealSelectedFile()
        {
            if (_selectedSlot == null)
            {
                return;
            }

            string path = SafeGetSlotFullPath(_selectedSlot.Info.FileName, CurrentFolder);
            if (File.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
            }
            else
            {
                string directory = SafeDetermineSavePath(CurrentFolder);
                if (Directory.Exists(directory))
                {
                    EditorUtility.RevealInFinder(directory);
                }
            }
        }

        private void CopySelectedPath()
        {
            if (_selectedSlot == null)
            {
                return;
            }

            string path = SafeGetSlotFullPath(_selectedSlot.Info.FileName, CurrentFolder);
            EditorGUIUtility.systemCopyBuffer = path;
            SetStatus("路径已复制。", MessageType.Info, 2.5);
        }

        private void ExportSelectedBlock()
        {
            if (_selectedBlockIndex < 0 || _selectedBlockIndex >= _filteredBlocks.Count || _rawBlocks == null)
            {
                SetStatus("请先选中数据块。", MessageType.Warning, 2.5);
                return;
            }

            SaveBlockInfo block = _filteredBlocks[_selectedBlockIndex];
            if (block.Key == null || !_rawBlocks.TryGetValue(block.Key, out byte[] bytes) || bytes == null)
            {
                SetStatus("块载荷不可用。", MessageType.Warning, 2.5);
                return;
            }

            string defaultName = SanitizeFileName(block.Key);
            string extension = block.Backend == ESaveBackend.Json ? "json" : "bin";
            string path = EditorUtility.SaveFilePanel("导出数据块", "", defaultName, extension);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            RunOperation($"已导出 {Path.GetFileName(path)}", () => File.WriteAllBytes(path, bytes));
        }

        private void CreateFolderDialog()
        {
            string root = SafeDetermineSavePath(string.Empty);
            if (string.IsNullOrEmpty(root))
            {
                SetStatus("无法解析存档根目录。", MessageType.Error, 3);
                return;
            }

            string name = EditorUtility.SaveFolderPanel("新建存档文件夹", root, string.Empty);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            string folderName = Path.GetFileName(name);
            if (string.IsNullOrEmpty(folderName))
            {
                return;
            }

            string target = Path.Combine(root, folderName);
            RunOperation($"已创建文件夹「{folderName}」。", () =>
            {
                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(target);
                }
            }, after: RefreshFolders);
        }

        private void DeleteCurrentFolderDialog()
        {
            string folder = CurrentFolder;
            if (folder == SaveServiceHandler.DEFAULT_FOLDER_NAME)
            {
                SetStatus("默认文件夹不允许删除。", MessageType.Warning, 3);
                return;
            }

            if (!EditorUtility.DisplayDialog("删除文件夹", $"删除文件夹「{folder}」及其下全部存档？\n不可撤销。", "删除", "取消"))
            {
                return;
            }

            RunOperation($"已删除文件夹「{folder}」。", () => _handler.DeleteSaveFolder(folder), after: RefreshFolders);
        }

        private void RunOperation(string successMessage, Action operation, Action after = null)
        {
            try
            {
                operation();
                SetStatus(successMessage, MessageType.Info, 3);
                after?.Invoke();
            }
            catch (Exception exception)
            {
                SetStatus(string.Empty, MessageType.None, 0);
                EditorUtility.DisplayDialog("存档操作失败", exception.Message, "确定");
            }
        }

        #endregion

        #region 缩略图 [THUMBNAIL]

        private void LoadThumbnail(SlotView view, string folder)
        {
            ReleaseThumbnail();
            if (view == null || !view.HasScreenshot)
            {
                return;
            }

            try
            {
                SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(view.Info.FileName, folder);
                string screenshotPath = Path.Combine(paths.DirectoryPath, SaveScreenshotUtility.DetermineScreenshotFileName(view.Info.FileName));
                if (!File.Exists(screenshotPath))
                {
                    return;
                }

                byte[] bytes = File.ReadAllBytes(screenshotPath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(bytes))
                {
                    _thumbnailTexture = texture;
                }
                else
                {
                    DestroyImmediate(texture);
                }
            }
            catch
            {
                // 缩略图加载失败不影响主流程
            }
        }

        private void RefreshThumbnailUI()
        {
            if (_thumbnailElement == null)
            {
                return;
            }

            _thumbnailElement.Clear();
            if (_thumbnailTexture != null)
            {
                _thumbnailElement.style.backgroundImage = new StyleBackground(_thumbnailTexture);
            }
            else
            {
                _thumbnailElement.style.backgroundImage = StyleKeyword.Null;
                var placeholder = new Label(_selectedSlot != null && _selectedSlot.HasScreenshot ? "截图加载失败" : "无截图");
                placeholder.style.color = GetMutedColor();
                placeholder.style.fontSize = 11;
                _thumbnailElement.Add(placeholder);
            }
        }

        private void ReleaseThumbnail()
        {
            if (_thumbnailTexture != null)
            {
                DestroyImmediate(_thumbnailTexture);
                _thumbnailTexture = null;
            }

            if (_thumbnailElement != null)
            {
                _thumbnailElement.style.backgroundImage = StyleKeyword.Null;
            }
        }

        #endregion

        #region 工具方法 [UTILITIES]

        private void UpdatePipelineLabel()
        {
            string handlerName = SaveServiceSettings.SaveServiceHandler?.GetType().Name ?? "PlainSaveHandler";
            string compression = SaveServiceSettings.CompressionProvider?.GetType().Name ?? "不压缩";
            string backend = SaveServiceSettings.DefaultBackend.ToString();
            string extension = SaveServiceSettings.SaveFileExtension;
            string root = SafeDetermineSavePath(string.Empty);
            string rootHint = string.IsNullOrEmpty(root) ? "(未解析)" : root;

            if (_pipelineLabel != null)
            {
                _pipelineLabel.text = $"管线  {handlerName}  ·  压缩 {compression}  ·  默认后端 {backend}  ·  扩展名 {extension}  ·  根目录 {rootHint}";
                _pipelineLabel.tooltip = root;
            }
        }

        private void SetStatus(string message, MessageType type, double durationSeconds)
        {
            _statusMessage = message;
            _statusType = type;
            _statusExpireTime = durationSeconds <= 0 ? 0 : EditorApplication.timeSinceStartup + durationSeconds;
            UpdateStatusLabel();
        }

        private void UpdateStatusLabel()
        {
            if (_statusLabel == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(_statusMessage) ||
                (_statusExpireTime > 0 && EditorApplication.timeSinceStartup > _statusExpireTime))
            {
                _statusLabel.text = string.Empty;
                return;
            }

            _statusLabel.text = _statusMessage;
            _statusLabel.style.color = _statusType switch
            {
                MessageType.Error => GetErrorColor(),
                MessageType.Warning => GetWarningColor(),
                _ => GetMutedColor(),
            };
        }

        private string SafeDetermineSavePath(string folderName)
        {
            try
            {
                return _handler.DetermineSavePath(folderName);
            }
            catch
            {
                return string.Empty;
            }
        }

        private SaveFileInfo[] SafeGetSaveFiles(string folderName)
        {
            try
            {
                return _handler.GetSaveFiles(folderName);
            }
            catch
            {
                return Array.Empty<SaveFileInfo>();
            }
        }

        private string SafeGetSlotFullPath(string fileName, string folderName)
        {
            try
            {
                SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(fileName, folderName);
                return paths.SaveFilePath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Color ResolveSlotHealthColor(SlotView view)
        {
            if (!view.DetailLoaded)
            {
                return GetMutedColor();
            }

            if (view.CorruptedBlocks > 0)
            {
                return GetErrorColor();
            }

            if (view.IsEncryptedLikely || view.Blocks == null || view.Blocks.Length == 0)
            {
                return GetWarningColor();
            }

            return GetOkColor();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024L)
            {
                return $"{bytes} B";
            }

            if (bytes < 1024L * 1024L)
            {
                return (bytes / 1024f).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            }

            if (bytes < 1024L * 1024L * 1024L)
            {
                return (bytes / (1024f * 1024f)).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
            }

            return (bytes / (1024f * 1024f * 1024f)).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }

        private static string FormatRelativeTime(DateTime lastWriteUtc)
        {
            DateTime local = lastWriteUtc.ToLocalTime();
            TimeSpan delta = DateTime.Now - local;
            if (delta.TotalSeconds < 60)
            {
                return "刚刚";
            }

            if (delta.TotalMinutes < 60)
            {
                return $"{(int)delta.TotalMinutes} 分钟前";
            }

            if (delta.TotalHours < 24)
            {
                return $"{(int)delta.TotalHours} 小时前";
            }

            if (delta.TotalDays < 7)
            {
                return $"{(int)delta.TotalDays} 天前";
            }

            return local.ToString("yyyy-MM-dd HH:mm");
        }

        private static string FormatTimeSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1)
            {
                return $"{(int)span.TotalHours}h {span.Minutes:D2}m";
            }

            if (span.TotalMinutes >= 1)
            {
                return $"{(int)span.TotalMinutes}m {span.Seconds:D2}s";
            }

            return $"{span.Seconds}s";
        }

        private static string TruncatePath(string path, int maxChars)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= maxChars)
            {
                return path;
            }

            return "…" + path.Substring(path.Length - maxChars + 1);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "block";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return builder.ToString();
        }

        private static string ShortBackendName(ESaveBackend backend)
        {
            return backend switch
            {
                ESaveBackend.Json => "JSON",
                ESaveBackend.KeyValue => "KVT",
                ESaveBackend.MessagePack => "MsgPack",
                ESaveBackend.MemoryPack => "MemPack",
                ESaveBackend.Protobuf => "Proto",
                _ => backend.ToString(),
            };
        }

        private static string BuildHexDump(byte[] bytes, int maxBytes = HEX_PREVIEW_MAX_BYTES)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            int count = Math.Min(bytes.Length, maxBytes);
            var builder = new StringBuilder(count * 5 + 64);
            for (int offset = 0; offset < count; offset += 16)
            {
                builder.Append(offset.ToString("X8")).Append("  ");

                int lineCount = Math.Min(16, count - offset);
                for (int i = 0; i < 16; i++)
                {
                    if (i < lineCount)
                    {
                        builder.Append(bytes[offset + i].ToString("X2")).Append(' ');
                    }
                    else
                    {
                        builder.Append("   ");
                    }

                    if (i == 7)
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append(' ');
                for (int i = 0; i < lineCount; i++)
                {
                    byte b = bytes[offset + i];
                    builder.Append(b >= 32 && b < 127 ? (char)b : '.');
                }

                builder.Append('\n');
            }

            if (bytes.Length > count)
            {
                builder.Append($"… 共 {FormatBytes(bytes.Length)}");
            }

            return builder.ToString();
        }

        /// <summary>轻量 JSON 美化（基于花括号/方括号缩进；非完整解析器，预览用）。</summary>
        private static string FormatJson(string json, bool pretty)
        {
            if (string.IsNullOrEmpty(json))
            {
                return string.Empty;
            }

            if (!pretty)
            {
                return json;
            }

            var builder = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    builder.Append(c);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        builder.Append(c);
                        break;
                    case '{':
                    case '[':
                        builder.Append(c);
                        builder.Append('\n');
                        indent++;
                        builder.Append(new string(' ', indent * 2));
                        break;
                    case '}':
                    case ']':
                        builder.Append('\n');
                        indent = Math.Max(0, indent - 1);
                        builder.Append(new string(' ', indent * 2));
                        builder.Append(c);
                        break;
                    case ',':
                        builder.Append(c);
                        builder.Append('\n');
                        builder.Append(new string(' ', indent * 2));
                        break;
                    case ':':
                        builder.Append(c).Append(' ');
                        break;
                    case ' ':
                    case '\t':
                    case '\r':
                    case '\n':
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        #endregion

        #region 主题颜色 [THEME COLORS]

        private static bool IsProSkin => EditorGUIUtility.isProSkin;

        private static Color GetHairlineColor() =>
            IsProSkin ? new Color(0f, 0f, 0f, 0.35f) : new Color(0f, 0f, 0f, 0.15f);

        private static Color GetStripBackgroundColor() =>
            IsProSkin ? new Color(0.18f, 0.18f, 0.18f, 1f) : new Color(0.88f, 0.88f, 0.88f, 1f);

        private static Color GetCardBackgroundColor() =>
            IsProSkin ? new Color(0.22f, 0.22f, 0.22f, 0.6f) : new Color(1f, 1f, 1f, 0.55f);

        private static Color GetSelectedRowColor() =>
            IsProSkin ? new Color(0.24f, 0.48f, 0.90f, 0.35f) : new Color(0.24f, 0.48f, 0.90f, 0.25f);

        private static Color GetSelectedTextColor() =>
            IsProSkin ? new Color(0.75f, 0.85f, 1f) : new Color(0.1f, 0.25f, 0.55f);

        private static Color GetPrimaryTextColor() =>
            IsProSkin ? new Color(0.88f, 0.88f, 0.88f) : new Color(0.15f, 0.15f, 0.15f);

        private static Color GetMutedColor() =>
            IsProSkin ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.4f, 0.4f, 0.4f);

        private static Color GetPreviewTextColor() =>
            IsProSkin ? new Color(0.82f, 0.85f, 0.82f) : new Color(0.15f, 0.2f, 0.15f);

        private static Color GetOkColor() =>
            IsProSkin ? new Color(0.35f, 0.75f, 0.45f) : new Color(0.18f, 0.55f, 0.28f);

        private static Color GetWarningColor() =>
            IsProSkin ? new Color(0.92f, 0.72f, 0.25f) : new Color(0.75f, 0.5f, 0.05f);

        private static Color GetErrorColor() =>
            IsProSkin ? new Color(0.90f, 0.35f, 0.35f) : new Color(0.75f, 0.2f, 0.2f);

        private static Color BackendColor(ESaveBackend backend)
        {
            return backend switch
            {
                ESaveBackend.Json => IsProSkin ? new Color(0.40f, 0.70f, 0.95f) : new Color(0.15f, 0.40f, 0.70f),
                ESaveBackend.KeyValue => IsProSkin ? new Color(0.95f, 0.80f, 0.30f) : new Color(0.70f, 0.50f, 0.05f),
                _ => IsProSkin ? new Color(0.70f, 0.70f, 0.75f) : new Color(0.40f, 0.40f, 0.45f),
            };
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        #endregion

        #region 生命周期钩子 [UPDATE]

        private void Update()
        {
            if (!string.IsNullOrEmpty(_statusMessage) && _statusExpireTime > 0 &&
                EditorApplication.timeSinceStartup > _statusExpireTime)
            {
                _statusMessage = string.Empty;
                UpdateStatusLabel();
            }
        }

        #endregion
    }
}
