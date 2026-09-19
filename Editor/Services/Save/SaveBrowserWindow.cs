using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Moirai.Atropos.Save;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor.Save
{
    /// <summary>
    /// 存档浏览器编辑器窗口（Odin 原生实现）：左侧菜单树（文件夹 → 槽位，健康状态图标），
    /// 右侧由 Odin 属性树绘制选中节点详情（概览、元数据、操作按钮、数据块表、内容预览与截图缩略图）。
    /// <para>浏览 <c>persistentDataPath/Data/</c> 下的存档，支持菜单搜索过滤、排序、仅问题过滤、
    /// 自动刷新、备份/恢复/删除/复制槽位、导出块、JSON 美化 / KVT 结构化树 / 十六进制预览。</para>
    /// <para>加密档/压缩档在缺管线配置时解析失败按提示展示（编辑器以明文处理器 + 设置的压缩提供方读取）。</para>
    /// </summary>
    public sealed class SaveBrowserWindow : OdinMenuEditorWindow
    {
        #region 常量 [CONSTANTS]

        /// <summary>十六进制预览默认采样字节数上限。</summary>
        private const int HEX_PREVIEW_MAX_BYTES = 512;

        /// <summary>备份文件后缀（与 <c>FileSaveStorageBackend</c> 一致）。</summary>
        private const string BACKUP_SUFFIX = ".bak";

        /// <summary>保留元数据块键。</summary>
        private const string META_BLOCK_KEY = "__meta";

        /// <summary>自动刷新间隔（秒）。</summary>
        private const double AUTO_REFRESH_INTERVAL = 2.0;

        private const string SESSION_AUTO_REFRESH = "Moirai.SaveBrowser.AutoRefresh";
        private const string SESSION_SORT_MODE = "Moirai.SaveBrowser.SortMode";
        private const string SESSION_PRETTY_JSON = "Moirai.SaveBrowser.PrettyJson";
        private const string SESSION_SHOW_ONLY_ISSUES = "Moirai.SaveBrowser.ShowOnlyIssues";

        /// <summary>缩略图预览最大高度。</summary>
        private const float THUMBNAIL_MAX_HEIGHT = 120f;

        /// <summary>菜单树宽度。</summary>
        private const float MENU_WIDTH = 300f;

        #endregion

        #region 枚举 [ENUMS]

        /// <summary>槽位排序模式。</summary>
        private enum SlotSortMode
        {
            TimeDesc = 0,
            TimeAsc = 1,
            NameAsc = 2,
            NameDesc = 3,
            SizeDesc = 4,
            SizeAsc = 5,
        }

        /// <summary>预览渲染模式。</summary>
        private enum PreviewMode
        {
            Auto = 0,
            Text = 1,
            Hex = 2,
        }

        #endregion

        #region 数据模型 [MODELS]

        /// <summary>槽位列表展示模型（窗口内数据管道，Odin 不直接绘制）。</summary>
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
            public bool StillExists;
        }

        /// <summary>文件夹数据模型：菜单树一级节点与其槽位集合。</summary>
        private sealed class FolderData
        {
            public string Name;
            public readonly List<SlotView> Slots = new List<SlotView>();
        }

        /// <summary>数据块表格行模型（Odin TableList 绘制，纯展示；列头取成员名，勿加 LabelText——Odin 4 会渲染成行内前缀标签导致列错位）。</summary>
        private sealed class BlockRow
        {
            [TableColumnWidth(230, false)]
            public string Key;

            [TableColumnWidth(70, false)]
            public string Backend;

            [TableColumnWidth(52, false)]
            public string Version;

            [TableColumnWidth(78, false)]
            public string Size;

            [GUIColor("StatusColor")]
            public string Status;

            [HideInTables]
            public bool Corrupted;

            private Color StatusColor =>
                Corrupted ? new Color(0.90f, 0.45f, 0.45f) : new Color(0.55f, 0.85f, 0.60f);
        }

        /// <summary>文件夹详情视图模型：由 Odin 属性树绘制（概览 + 定位/删除操作）。</summary>
        private sealed class FolderModel
        {
            private readonly SaveBrowserWindow _owner;
            private readonly FolderData _data;

            public FolderModel(SaveBrowserWindow owner, FolderData data)
            {
                _owner = owner;
                _data = data;
            }

            public string FolderName => _data.Name;

            [ShowInInspector, LabelText("文件夹"), ReadOnly]
            private string Name => _data.Name;

            [ShowInInspector, LabelText("路径"), ReadOnly]
            private string FullPath => _owner.SafeDetermineSavePath(_data.Name);

            [ShowInInspector, LabelText("槽位数"), ReadOnly]
            private int SlotCount => _data.Slots.Count;

            [ShowInInspector, LabelText("总大小"), ReadOnly]
            private string TotalSize
            {
                get
                {
                    long total = 0;
                    for (int i = 0; i < _data.Slots.Count; i++)
                    {
                        total += _data.Slots[i].Info.SizeBytes;
                    }

                    return FormatBytes(total);
                }
            }

            [Button("定位"), ButtonGroup("文件夹操作"), GUIColor(0.40f, 0.70f, 0.95f)]
            private void Reveal()
            {
                string path = _owner.SafeDetermineSavePath(_data.Name);
                if (!string.IsNullOrEmpty(path))
                {
                    EditorUtility.RevealInFinder(path);
                }
            }

            [Button("删除文件夹"), ButtonGroup("文件夹操作"), GUIColor(0.90f, 0.35f, 0.35f)]
            private void Delete() => _owner.DeleteFolderDialog(_data.Name);
        }

        /// <summary>槽位详情视图模型：由 Odin 属性树绘制（概览/元数据/操作/块表/预览/缩略图）。</summary>
        private sealed class SlotDetailModel
        {
            private readonly SaveBrowserWindow _owner;
            private readonly SlotView _view;
            private readonly string _folder;
            private readonly List<BlockRow> _blockRows = new List<BlockRow>();
            private Dictionary<string, byte[]> _rawBlocks;
            private string _blockFilter = string.Empty;
            private string _previewBlockKey = string.Empty;
            private PreviewMode _previewMode = PreviewMode.Auto;
            private string _previewText = string.Empty;

            public SlotDetailModel(SaveBrowserWindow owner, SlotView view, string folder)
            {
                _owner = owner;
                _view = view;
                _folder = folder;
                ReloadData();
            }

            public SlotView View => _view;

            public string Folder => _folder;

            public string PreviewKey => _previewBlockKey;

            public string PreviewTextValue => _previewText;

            #region 对外写入 [MUTATION]

            /// <summary>写入原始块载荷字典（窗口加载详情后调用）。</summary>
            public void SetRawBlocks(Dictionary<string, byte[]> rawBlocks)
            {
                _rawBlocks = rawBlocks;
            }

            /// <summary>重算块表与预览缓存（数据刷新后调用）。</summary>
            public void ReloadData()
            {
                RebuildBlockRows();
                if (string.IsNullOrEmpty(_previewBlockKey))
                {
                    AutoSelectPreviewBlock();
                }

                RebuildPreview();
            }

            /// <summary>读取指定块的原始载荷。</summary>
            public bool TryGetRawBytes(string blockKey, out byte[] bytes)
            {
                if (_rawBlocks != null && blockKey != null && _rawBlocks.TryGetValue(blockKey, out byte[] payload) && payload != null)
                {
                    bytes = payload;
                    return true;
                }

                bytes = null;
                return false;
            }

            #endregion

            #region 缩略图 [THUMBNAIL]

            [OnInspectorGUI, PropertyOrder(-100f)]
            private void DrawThumbnail()
            {
                Texture2D texture = _owner._thumbnailTexture;
                if (texture == null)
                {
                    return;
                }

                EditorGUILayout.Space(4);
                GUILayout.Box(texture, GUILayout.Height(THUMBNAIL_MAX_HEIGHT), GUILayout.ExpandWidth(true));
            }

            #endregion

            #region 概览 [OVERVIEW]

            [ShowInInspector, LabelText("文件"), ReadOnly]
            private string FileName => _view.Info.FileName + SaveServiceSettings.SaveFileExtension;

            [ShowInInspector, LabelText("大小"), ReadOnly]
            private string Size => FormatBytes(_view.Info.SizeBytes);

            [ShowInInspector, LabelText("最后写入"), ReadOnly]
            private string LastWrite => _view.Info.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            [ShowInInspector, LabelText("路径"), ReadOnly]
            private string PathText => TruncatePath(_owner.SafeGetSlotFullPath(_view.Info.FileName, _folder), 80);

            [ShowInInspector, LabelText("数据块"), ReadOnly]
            private string Health => BuildHealthSummary(_view);

            [ShowInInspector, LabelText("截图"), ReadOnly]
            private string Screenshot => _view.HasScreenshot ? "有" : "无";

            [ShowInInspector, LabelText("备份"), ReadOnly]
            private string BackupText => _view.HasBackup ? "有 (.bak)" : "无";

            #endregion

            #region 操作 [ACTIONS]

            [Button("备份"), ButtonGroup("操作行1", 9f), GUIColor(0.40f, 0.70f, 0.95f)]
            private void Backup() => _owner.RunBackupForActive();

            [Button("恢复备份"), ButtonGroup("操作行1", 9f), GUIColor(0.92f, 0.72f, 0.25f)]
            private void RestoreBackup() => _owner.RestoreBackupForActive();

            [Button("复制槽位"), ButtonGroup("操作行1", 9f)]
            private void Duplicate() => _owner.DuplicateSelectedSlot();

            [Button("定位文件"), ButtonGroup("操作行2", 12f), GUIColor(0.40f, 0.70f, 0.95f)]
            private void Reveal() => _owner.RevealSelectedFile();

            [Button("复制路径"), ButtonGroup("操作行2", 12f), GUIColor(0.40f, 0.70f, 0.95f)]
            private void CopyPath() => _owner.CopySelectedPath();

            [Button("导出块"), ButtonGroup("操作行2", 12f)]
            private void Export() => _owner.ExportSelectedBlock(this);

            [Button("删除存档"), ButtonGroup("操作行2", 12f), GUIColor(0.90f, 0.35f, 0.35f)]
            private void Delete() => _owner.DeleteSelectedSlotDialog();

            #endregion

            #region 元数据 [METADATA]

            [ShowInInspector, FoldoutGroup("元数据", true, 20f), LabelText("游戏版本"), ReadOnly, InfoBox("无法解析元数据。当前项目可能使用加密处理器，或档文件已损坏。", InfoMessageType.Warning, "IsMetadataMissing")]
            private string GameVersion => _view.Metadata != null && !string.IsNullOrEmpty(_view.Metadata.GameVersion) ? _view.Metadata.GameVersion : "-";

            [ShowInInspector, FoldoutGroup("元数据", true, 20f), LabelText("存档版本"), ReadOnly]
            private string SaveVersionText => _view.Metadata != null ? _view.Metadata.SaveVersion.ToString() : "-";

            [ShowInInspector, FoldoutGroup("元数据", true, 20f), LabelText("场景"), ReadOnly]
            private string Scene => _view.Metadata != null && !string.IsNullOrEmpty(_view.Metadata.SceneName) ? _view.Metadata.SceneName : "-";

            [ShowInInspector, FoldoutGroup("元数据", true, 20f), LabelText("游玩时长"), ReadOnly]
            private string PlayTime => _view.Metadata != null && _view.Metadata.PlayTimeTicks > 0L ? FormatTimeSpan(new TimeSpan(_view.Metadata.PlayTimeTicks)) : "-";

            [ShowInInspector, FoldoutGroup("元数据", true, 20f), LabelText("迁移历史"), ReadOnly]
            private string MigrationCount
            {
                get
                {
                    List<string> history = _view.Metadata?.MigrationHistory;
                    return history is { Count: > 0 } ? $"{history.Count} 次" : "-";
                }
            }

            [ShowInInspector, FoldoutGroup("元数据", true, 20f), LabelText("迁移明细"), ReadOnly, HideIf("HasNoMigrations")]
            private string MigrationText
            {
                get
                {
                    List<string> history = _view.Metadata?.MigrationHistory;
                    if (history is not { Count: > 0 })
                    {
                        return string.Empty;
                    }

                    return string.Join("\n", history);
                }
            }

            [ShowInInspector, FoldoutGroup("元数据", true, 20f), LabelText("自定义"), ReadOnly, HideIf("HasNoCustom")]
            private string CustomText
            {
                get
                {
                    Dictionary<string, string> custom = _view.Metadata?.Custom;
                    if (custom is not { Count: > 0 })
                    {
                        return string.Empty;
                    }

                    var builder = new StringBuilder(64 * custom.Count);
                    foreach (KeyValuePair<string, string> pair in custom)
                    {
                        builder.Append(pair.Key).Append(": ").Append(string.IsNullOrEmpty(pair.Value) ? "-" : pair.Value).Append('\n');
                    }

                    return builder.ToString().TrimEnd('\n');
                }
            }

            private bool HasNoMigrations => !(_view.Metadata?.MigrationHistory is { Count: > 0 });

            private bool HasNoCustom => !(_view.Metadata?.Custom is { Count: > 0 });

            private bool IsMetadataMissing => _view.Metadata == null && _view.IsEncryptedLikely;

            #endregion

            #region 数据块 [BLOCKS]

            [ShowInInspector, FoldoutGroup("数据块", true, 30f), LabelText("过滤块键")]
            private string BlockFilter
            {
                get => _blockFilter;
                set
                {
                    _blockFilter = value ?? string.Empty;
                    RebuildBlockRows();
                }
            }

            [ShowInInspector, FoldoutGroup("数据块", true, 30f), TableList, ListDrawerSettings(DraggableItems = false)]
            private List<BlockRow> BlockRows => _blockRows;

            #endregion

            #region 预览 [PREVIEW]

            [ShowInInspector, FoldoutGroup("预览", true, 40f), LabelText("预览块"), ValueDropdown("GetBlockKeyChoices")]
            private string PreviewBlockKey
            {
                get => _previewBlockKey;
                set
                {
                    _previewBlockKey = value ?? string.Empty;
                    RebuildPreview();
                }
            }

            [ShowInInspector, FoldoutGroup("预览", true, 40f), EnumToggleButtons, LabelText("模式")]
            private PreviewMode Mode
            {
                get => _previewMode;
                set
                {
                    _previewMode = value;
                    RebuildPreview();
                }
            }

            [ShowInInspector, FoldoutGroup("预览", true, 40f), LabelText("JSON 美化")]
            private bool PrettyJson
            {
                get => _owner._prettyJson;
                set
                {
                    _owner._prettyJson = value;
                    RebuildPreview();
                }
            }

            [ShowInInspector, FoldoutGroup("预览", true, 40f), HideLabel, PropertySpace(6f, 0f)]
            [TextAreaAdaptive(12, 28)]
            private string PreviewText => _previewText;

            [Button("复制"), ButtonGroup("预览操作", 45f), GUIColor(0.40f, 0.70f, 0.95f)]
            private void CopyPreview() => _owner.CopyPreviewToClipboard(this);

            [Button("导出块"), ButtonGroup("预览操作", 45f)]
            private void ExportPreview() => _owner.ExportSelectedBlock(this);

            private IEnumerable<ValueDropdownItem<string>> GetBlockKeyChoices()
            {
                var choices = new List<ValueDropdownItem<string>>();
                SaveBlockInfo[] blocks = _view.Blocks;
                if (blocks != null)
                {
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        SaveBlockInfo block = blocks[i];
                        if (block.Key == null)
                        {
                            choices.Add(new ValueDropdownItem<string>("<结构不可读>", string.Empty));
                            continue;
                        }

                        choices.Add(new ValueDropdownItem<string>(block.Key + "  ·  " + ShortBackendName(block.Backend), block.Key));
                    }
                }

                if (choices.Count == 0)
                {
                    choices.Add(new ValueDropdownItem<string>("(无数据块)", string.Empty));
                }

                return choices;
            }

            #endregion

            #region 内部重算 [REBUILD]

            private void RebuildBlockRows()
            {
                _blockRows.Clear();
                SaveBlockInfo[] blocks = _view.Blocks;
                if (blocks == null)
                {
                    return;
                }

                for (int i = 0; i < blocks.Length; i++)
                {
                    SaveBlockInfo block = blocks[i];
                    if (_blockFilter.Length > 0 &&
                        (block.Key == null || block.Key.IndexOf(_blockFilter, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        continue;
                    }

                    _blockRows.Add(new BlockRow
                    {
                        Key = block.Key ?? (block.Error != SaveError.None ? "<结构不可读>" : "<未知>"),
                        Backend = ShortBackendName(block.Backend),
                        Version = $"v{block.DataVersion}",
                        Size = FormatBytes(block.SizeBytes),
                        Status = !block.HasMetadata ? "结构不可读" : block.Error != SaveError.None ? block.Error.ToString() : "正常",
                        Corrupted = block.Error != SaveError.None || !block.HasMetadata,
                    });
                }
            }

            private void AutoSelectPreviewBlock()
            {
                SaveBlockInfo[] blocks = _view.Blocks;
                if (blocks == null)
                {
                    _previewBlockKey = string.Empty;
                    return;
                }

                for (int i = 0; i < blocks.Length; i++)
                {
                    if (blocks[i].Key != null && blocks[i].Error == SaveError.None)
                    {
                        _previewBlockKey = blocks[i].Key;
                        return;
                    }
                }

                _previewBlockKey = string.Empty;
            }

            private void RebuildPreview()
            {
                _previewText = _owner.BuildPreviewText(_view, _rawBlocks, _previewBlockKey, _previewMode, _owner._prettyJson);
            }

            #endregion
        }

        #endregion

        #region 字段 [FIELDS]

        private static readonly string[] s_SortModeLabels =
        {
            "排序：时间 ↓",
            "排序：时间 ↑",
            "排序：名称 ↑",
            "排序：名称 ↓",
            "排序：大小 ↓",
            "排序：大小 ↑",
        };

        private PlainSaveHandler _handler;
        private readonly List<FolderData> _folders = new List<FolderData>();
        private SlotSortMode _sortMode = SlotSortMode.TimeDesc;
        private bool _autoRefresh;
        private bool _prettyJson = true;
        private bool _showOnlyIssues;
        private double _nextAutoRefreshTime;
        private bool _refreshDataPending;
        private bool _treeRebuildQueued;
        private string _statusMessage = string.Empty;
        private MessageType _statusType = MessageType.Info;
        private double _statusExpireTime;
        private string _pipelineText = string.Empty;
        private OdinMenuItem _cachedSelection;
        private SlotDetailModel _activeDetail;
        private Texture2D _thumbnailTexture;
        private string _pendingSelectionFolder;
        private string _pendingSelectionSlot;

        #endregion

        #region 窗口入口 [ENTRY]

        /// <summary>
        /// 打开存档浏览器窗口。
        /// </summary>
        [MenuItem("Tools/Moirai/Save/Save Browser", priority = 2200)]
        public static void Open()
        {
            var window = GetWindow<SaveBrowserWindow>();
            window.titleContent = new GUIContent("Save Browser", EditorGUIUtility.IconContent("SaveActive").image);
            window.minSize = new Vector2(1040f, 560f);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _handler = new PlainSaveHandler
            {
                _compression = SaveServiceSettings.CompressionProvider,
            };
            _autoRefresh = SessionState.GetBool(SESSION_AUTO_REFRESH, false);
            _sortMode = (SlotSortMode)SessionState.GetInt(SESSION_SORT_MODE, (int)SlotSortMode.TimeDesc);
            _prettyJson = SessionState.GetBool(SESSION_PRETTY_JSON, true);
            _showOnlyIssues = SessionState.GetBool(SESSION_SHOW_ONLY_ISSUES, false);

            DrawMenuSearchBar = true;
            MenuWidth = MENU_WIDTH;
            ResizableMenuWidth = true;
            UpdatePipelineText();

            EditorApplication.update += OnEditorUpdate;

            // Odin 4 契约：基类 OnEnable 不建树；域重载早期 EditorStyles 未就绪（get_label 可抛 NRE 而非返回 null），
            // 因此守卫需吞异常——失败则保持未建树，由 OnImGUI 兜底重试。
            try
            {
                if (EditorStyles.label != null)
                {
                    ForceMenuTreeRebuild();
                }
            }
            catch
            {
                // 编辑器皮肤尚未初始化，等待下一次 OnImGUI 兜底建树
            }
        }

        protected override void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            SessionState.SetBool(SESSION_AUTO_REFRESH, _autoRefresh);
            SessionState.SetInt(SESSION_SORT_MODE, (int)_sortMode);
            SessionState.SetBool(SESSION_PRETTY_JSON, _prettyJson);
            SessionState.SetBool(SESSION_SHOW_ONLY_ISSUES, _showOnlyIssues);
            ReleaseThumbnail();
            base.OnDisable();
        }

        #endregion

        #region 菜单树 [MENU TREE]

        /// <summary>
        /// 构建菜单树：文件夹节点（FolderModel）→ 槽位子节点（SlotDetailModel，按健康状态给图标）。
        /// </summary>
        protected override OdinMenuTree BuildMenuTree()
        {
            RefreshFoldersAndSlots();

            var tree = new OdinMenuTree(false);
            tree.DefaultMenuStyle.SetHeight(26);

            for (int i = 0; i < _folders.Count; i++)
            {
                FolderData folder = _folders[i];
                tree.Add(folder.Name, new FolderModel(this, folder), SdfIconType.FolderFill);

                List<SlotView> slots = folder.Slots;
                for (int j = 0; j < slots.Count; j++)
                {
                    SlotView slot = slots[j];
                    if (!IsSlotVisible(slot))
                    {
                        continue;
                    }

                    var detail = new SlotDetailModel(this, slot, folder.Name);
                    tree.Add(folder.Name + "/" + slot.Info.FileName, detail, ResolveSlotHealthIcon(slot));
                }
            }

            TryRestoreSelection(tree);
            return tree;
        }

        /// <summary>「仅问题」过滤判定。</summary>
        private bool IsSlotVisible(SlotView view)
        {
            if (!_showOnlyIssues)
            {
                return true;
            }

            return view.CorruptedBlocks > 0
                   || view.IsEncryptedLikely
                   || (view.Blocks != null && view.Blocks.Length == 0);
        }

        /// <summary>按健康状态解析槽位菜单图标。</summary>
        private static SdfIconType ResolveSlotHealthIcon(SlotView view)
        {
            if (view.CorruptedBlocks > 0)
            {
                return SdfIconType.XCircleFill;
            }

            if (view.IsEncryptedLikely)
            {
                return SdfIconType.LockFill;
            }

            if (view.Blocks == null || view.Blocks.Length == 0)
            {
                return SdfIconType.Info;
            }

            return SdfIconType.CheckCircleFill;
        }

        /// <summary>重建后恢复选中节点（编程式 Select，不依赖窗口焦点）。</summary>
        private void TryRestoreSelection(OdinMenuTree tree)
        {
            if (_pendingSelectionFolder == null && _pendingSelectionSlot == null)
            {
                return;
            }

            string wantFolder = _pendingSelectionFolder;
            string wantSlot = _pendingSelectionSlot;
            _pendingSelectionFolder = null;
            _pendingSelectionSlot = null;

            OdinMenuItem match = null;
            tree.EnumerateTree(item =>
            {
                if (match != null || item.Value == null || item.Parent == null)
                {
                    return;
                }

                if (wantSlot != null)
                {
                    if (string.Equals(item.Name, wantSlot, StringComparison.Ordinal) &&
                        string.Equals(item.Parent.Name, wantFolder ?? string.Empty, StringComparison.Ordinal))
                    {
                        match = item;
                    }
                }
                else if (wantFolder != null && item.Parent.Parent == null &&
                         string.Equals(item.Name, wantFolder, StringComparison.Ordinal))
                {
                    match = item;
                }
            });

            if (match != null)
            {
                tree.Selection.Clear();
                match.Select(false);
            }
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        protected override void OnImGUI()
        {
            EnsureMenuTree();
            DrainQueuedRebuild();
            DrainRefreshDataPending();
            PollSelection();
            DrawToolbar();
            DrawStatusStrip();
            HandleShortcuts();
            base.OnImGUI();
            EnsureTreePopulated();
        }

        /// <summary>Odin 4 契约：样式未就绪不建树，就绪后 ForceMenuTreeRebuild 兜底。</summary>
        private void EnsureMenuTree()
        {
            if (MenuTree != null || EditorStyles.label == null)
            {
                return;
            }

            ForceMenuTreeRebuild();
        }

        private void DrainQueuedRebuild()
        {
            if (!_treeRebuildQueued)
            {
                return;
            }

            _treeRebuildQueued = false;
            if (EditorStyles.label == null)
            {
                return;
            }

            ForceMenuTreeRebuild();
        }

        private void DrainRefreshDataPending()
        {
            if (!_refreshDataPending)
            {
                return;
            }

            _refreshDataPending = false;
            RefreshFoldersAndSlots();
            if (_activeDetail != null && _activeDetail.View.StillExists)
            {
                RefreshActiveDetailInPlace();
            }
            else
            {
                _activeDetail = null;
                ReleaseThumbnail();
            }
        }

        /// <summary>轮询菜单树选中变化并按需加载槽位详情（选中条目在 Layout 遍缓存）。</summary>
        private void PollSelection()
        {
            OdinMenuItem current = null;
            OdinMenuTreeSelection selection = MenuTree?.Selection;
            if (selection != null)
            {
                foreach (OdinMenuItem item in selection)
                {
                    current = item;
                    break;
                }
            }

            if (ReferenceEquals(current, _cachedSelection))
            {
                return;
            }

            _cachedSelection = current;
            LoadDetailForSelection();
        }

        /// <summary>Odin 4 契约：空树经 delayCall 防抖重建。</summary>
        private void EnsureTreePopulated()
        {
            if (MenuTree != null && MenuTree.MenuItems.Count > 0)
            {
                return;
            }

            QueueTreeRebuild();
        }

        private void QueueTreeRebuild()
        {
            if (_treeRebuildQueued)
            {
                return;
            }

            _treeRebuildQueued = true;
            EditorApplication.delayCall += () =>
            {
                if (!_treeRebuildQueued)
                {
                    return;
                }

                _treeRebuildQueued = false;
                if (EditorStyles.label == null)
                {
                    return;
                }

                ForceMenuTreeRebuild();
                Repaint();
            };
        }

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_autoRefresh && now >= _nextAutoRefreshTime)
            {
                _nextAutoRefreshTime = now + AUTO_REFRESH_INTERVAL;
                _refreshDataPending = true;
                Repaint();
            }

            if (_statusExpireTime > 0 && _statusMessage.Length > 0 && now > _statusExpireTime)
            {
                _statusMessage = string.Empty;
                _statusExpireTime = 0;
                Repaint();
            }
        }

        #endregion

        #region 工具栏 [TOOLBAR]

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(48)))
            {
                RequestTreeRebuildPreservingSelection();
                SetStatus("已刷新。", MessageType.Info, 2.5);
            }

            bool auto = GUILayout.Toggle(_autoRefresh, "自动", EditorStyles.toolbarButton, GUILayout.Width(52));
            if (auto != _autoRefresh)
            {
                _autoRefresh = auto;
                if (_autoRefresh)
                {
                    _nextAutoRefreshTime = EditorApplication.timeSinceStartup + AUTO_REFRESH_INTERVAL;
                }

                SessionState.SetBool(SESSION_AUTO_REFRESH, _autoRefresh);
            }

            GUILayout.Space(35);
            // EditorGUILayout.LabelField("排序", EditorStyles.label, GUILayout.Width(30));
            int sortIndex = EditorGUILayout.Popup((int)_sortMode, s_SortModeLabels, EditorStyles.toolbarPopup, GUILayout.Width(92));
            if (sortIndex != (int)_sortMode)
            {
                _sortMode = (SlotSortMode)sortIndex;
                SessionState.SetInt(SESSION_SORT_MODE, sortIndex);
                RequestTreeRebuildPreservingSelection();
            }

            GUILayout.Space(8);
            bool issues = GUILayout.Toggle(_showOnlyIssues, "仅问题", EditorStyles.toolbarButton, GUILayout.Width(64));
            if (issues != _showOnlyIssues)
            {
                _showOnlyIssues = issues;
                SessionState.SetBool(SESSION_SHOW_ONLY_ISSUES, _showOnlyIssues);
                RequestTreeRebuildPreservingSelection();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("新建文件夹", EditorStyles.toolbarButton, GUILayout.Width(84)))
            {
                CreateFolderDialog();
            }

            if (GUILayout.Button("根目录", EditorStyles.toolbarButton, GUILayout.Width(58)))
            {
                RevealSaveRoot();
            }

            if (GUILayout.Button("设置", EditorStyles.toolbarButton, GUILayout.Width(46)))
            {
                FrameworkSettingsWindow.Open(typeof(SaveServiceSettings));
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusStrip()
        {
            if (_statusExpireTime > 0 && _statusMessage.Length > 0 &&
                EditorApplication.timeSinceStartup > _statusExpireTime)
            {
                _statusMessage = string.Empty;
                _statusExpireTime = 0;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_pipelineText, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            if (_statusMessage.Length > 0)
            {
                Color previous = GUI.contentColor;
                GUI.contentColor = _statusType switch
                {
                    MessageType.Error => new Color(0.90f, 0.45f, 0.45f),
                    MessageType.Warning => new Color(0.92f, 0.78f, 0.35f),
                    _ => new Color(0.62f, 0.62f, 0.62f),
                };
                EditorGUILayout.LabelField(_statusMessage, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                GUI.contentColor = previous;
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>F5 刷新、Delete 删除选中槽位（文本编辑焦点时跳过）。</summary>
        private void HandleShortcuts()
        {
            Event evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown)
            {
                return;
            }

            if (evt.keyCode == KeyCode.F5)
            {
                RequestTreeRebuildPreservingSelection();
                SetStatus("已刷新。", MessageType.Info, 2.5);
                evt.Use();
                return;
            }

            if (evt.keyCode == KeyCode.Delete && GUIUtility.keyboardControl == 0 && _activeDetail != null)
            {
                DeleteSelectedSlotDialog();
                evt.Use();
            }
        }

        #endregion

        #region 数据刷新 [DATA REFRESH]

        /// <summary>扫描存档根目录与全部文件夹，尽量复用既有 SlotView 实例（保持详情模型有效）。</summary>
        private void RefreshFoldersAndSlots()
        {
            var previous = new Dictionary<string, SlotView>(StringComparer.Ordinal);
            for (int i = 0; i < _folders.Count; i++)
            {
                FolderData folder = _folders[i];
                for (int j = 0; j < folder.Slots.Count; j++)
                {
                    SlotView slot = folder.Slots[j];
                    previous[folder.Name + "/" + slot.Info.FileName] = slot;
                    slot.StillExists = false;
                }
            }

            _folders.Clear();
            AddFolderData(SaveServiceHandler.DEFAULT_FOLDER_NAME, previous);

            string rootDirectory = SafeDetermineSavePath(string.Empty);
            if (!string.IsNullOrEmpty(rootDirectory) && Directory.Exists(rootDirectory))
            {
                string[] directories = Directory.GetDirectories(rootDirectory);
                for (int i = 0; i < directories.Length; i++)
                {
                    string folderName = Path.GetFileName(directories[i]);
                    if (!string.IsNullOrEmpty(folderName) && folderName != SaveServiceHandler.DEFAULT_FOLDER_NAME)
                    {
                        AddFolderData(folderName, previous);
                    }
                }
            }

            for (int i = 0; i < _folders.Count; i++)
            {
                SortSlots(_folders[i].Slots);
            }
        }

        private void AddFolderData(string folderName, Dictionary<string, SlotView> previous)
        {
            var data = new FolderData { Name = folderName };
            SaveFileInfo[] files = SafeGetSaveFiles(folderName);
            if (files != null)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    string key = folderName + "/" + files[i].FileName;
                    SlotView view = previous.TryGetValue(key, out SlotView existing) ? existing : new SlotView();
                    view.Info = files[i];
                    view.StillExists = true;
                    EnrichSlotView(view, folderName, loadMetadata: false);
                    data.Slots.Add(view);
                }
            }

            _folders.Add(data);
        }

        private void SortSlots(List<SlotView> slots)
        {
            switch (_sortMode)
            {
                case SlotSortMode.TimeAsc:
                    slots.Sort((a, b) => a.Info.LastWriteTimeUtc.CompareTo(b.Info.LastWriteTimeUtc));
                    break;
                case SlotSortMode.NameAsc:
                    slots.Sort((a, b) => string.Compare(a.Info.FileName, b.Info.FileName, StringComparison.OrdinalIgnoreCase));
                    break;
                case SlotSortMode.NameDesc:
                    slots.Sort((a, b) => string.Compare(b.Info.FileName, a.Info.FileName, StringComparison.OrdinalIgnoreCase));
                    break;
                case SlotSortMode.SizeDesc:
                    slots.Sort((a, b) => b.Info.SizeBytes.CompareTo(a.Info.SizeBytes));
                    break;
                case SlotSortMode.SizeAsc:
                    slots.Sort((a, b) => a.Info.SizeBytes.CompareTo(b.Info.SizeBytes));
                    break;
                default:
                    slots.Sort((a, b) => b.Info.LastWriteTimeUtc.CompareTo(a.Info.LastWriteTimeUtc));
                    break;
            }
        }

        /// <summary>轻量富化槽位视图：块表、健康统计、截图/备份 sidecar 存在性；选中槽位才加载元数据。</summary>
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

        /// <summary>选中变化后加载重详情：元数据 + 原始块载荷 + 缩略图。</summary>
        private void LoadDetailForSelection()
        {
            ReleaseThumbnail();
            object value = _cachedSelection?.Value;
            if (value is SlotDetailModel detail)
            {
                _activeDetail = detail;
                RefreshActiveDetailInPlace();
                LoadThumbnail(detail.View, detail.Folder);
            }
            else
            {
                _activeDetail = null;
            }
        }

        /// <summary>就地刷新激活详情（不重建菜单树，保持选中与展开状态）。</summary>
        private void RefreshActiveDetailInPlace()
        {
            if (_activeDetail == null)
            {
                return;
            }

            EnrichSlotView(_activeDetail.View, _activeDetail.Folder, loadMetadata: true);
            try
            {
                SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(_activeDetail.View.Info.FileName, _activeDetail.Folder);
                _activeDetail.SetRawBlocks(_handler.ReadRawBlocks(paths));
            }
            catch
            {
                _activeDetail.SetRawBlocks(null);
            }

            _activeDetail.ReloadData();
            Repaint();
        }

        #endregion

        #region 操作 [OPERATIONS]

        private void RequestTreeRebuildPreservingSelection()
        {
            if (_activeDetail != null)
            {
                _pendingSelectionFolder = _activeDetail.Folder;
                _pendingSelectionSlot = _activeDetail.View.Info.FileName;
            }
            else if (_cachedSelection?.Value is FolderModel folderModel)
            {
                _pendingSelectionFolder = folderModel.FolderName;
                _pendingSelectionSlot = null;
            }

            _treeRebuildQueued = true;
        }

        private void RequestTreeRebuild(string pendingFolder, string pendingSlot)
        {
            _pendingSelectionFolder = pendingFolder;
            _pendingSelectionSlot = pendingSlot;
            _treeRebuildQueued = true;
        }

        private void RunBackupForActive()
        {
            if (_activeDetail == null)
            {
                return;
            }

            RunOperation("已创建备份。", () => _handler.CreateBackup(_activeDetail.View.Info.FileName, _activeDetail.Folder), RefreshActiveDetailInPlace);
        }

        private void RestoreBackupForActive()
        {
            if (_activeDetail == null)
            {
                return;
            }

            string fileName = _activeDetail.View.Info.FileName;
            string folder = _activeDetail.Folder;
            if (!EditorUtility.DisplayDialog("恢复备份", $"以 {fileName}{BACKUP_SUFFIX} 覆盖当前存档？", "恢复", "取消"))
            {
                return;
            }

            RunOperation("已从备份恢复。", () => _handler.RestoreBackup(fileName, folder), RefreshActiveDetailInPlace);
        }

        private void DeleteSelectedSlotDialog()
        {
            if (_activeDetail == null)
            {
                return;
            }

            string name = _activeDetail.View.Info.FileName;
            string folder = _activeDetail.Folder;
            if (!EditorUtility.DisplayDialog("删除存档", $"删除存档「{name}」（含截图 sidecar）？\n不可撤销。", "删除", "取消"))
            {
                return;
            }

            RunOperation("已删除。", () => _handler.DeleteSave(name, folder), () => RequestTreeRebuild(folder, null));
        }

        private void DuplicateSelectedSlot()
        {
            if (_activeDetail == null)
            {
                SetStatus("请先选择槽位。", MessageType.Warning, 2.5);
                return;
            }

            string sourceName = _activeDetail.View.Info.FileName;
            string folder = _activeDetail.Folder;
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
            }, () => RequestTreeRebuild(folder, newName));
        }

        private void RevealSelectedFile()
        {
            if (_activeDetail == null)
            {
                return;
            }

            string path = SafeGetSlotFullPath(_activeDetail.View.Info.FileName, _activeDetail.Folder);
            if (File.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
            }
            else
            {
                string directory = SafeDetermineSavePath(_activeDetail.Folder);
                if (Directory.Exists(directory))
                {
                    EditorUtility.RevealInFinder(directory);
                }
            }
        }

        private void CopySelectedPath()
        {
            if (_activeDetail == null)
            {
                return;
            }

            string path = SafeGetSlotFullPath(_activeDetail.View.Info.FileName, _activeDetail.Folder);
            EditorGUIUtility.systemCopyBuffer = path;
            SetStatus("路径已复制。", MessageType.Info, 2.5);
        }

        private void ExportSelectedBlock(SlotDetailModel model)
        {
            string blockKey = model?.PreviewKey;
            if (string.IsNullOrEmpty(blockKey))
            {
                SetStatus("请先在「预览块」下拉中选择要导出的数据块。", MessageType.Warning, 3);
                return;
            }

            if (!model.TryGetRawBytes(blockKey, out byte[] bytes))
            {
                SetStatus("块载荷不可用。", MessageType.Warning, 2.5);
                return;
            }

            SaveBlockInfo? block = FindBlock(model.View, blockKey);
            string extension = block is { Backend: ESaveBackend.Json } ? "json" : "bin";
            string path = EditorUtility.SaveFilePanel("导出数据块", string.Empty, SanitizeFileName(blockKey), extension);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            RunOperation($"已导出 {Path.GetFileName(path)}", () => File.WriteAllBytes(path, bytes));
        }

        private void CopyPreviewToClipboard(SlotDetailModel model)
        {
            string text = model?.PreviewTextValue;
            if (!string.IsNullOrEmpty(text))
            {
                EditorGUIUtility.systemCopyBuffer = text;
                SetStatus("预览内容已复制到剪贴板。", MessageType.Info, 2.5);
            }
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
            }, () => RequestTreeRebuild(folderName, null));
        }

        private void DeleteFolderDialog(string folderName)
        {
            if (folderName == SaveServiceHandler.DEFAULT_FOLDER_NAME)
            {
                SetStatus("默认文件夹不允许删除。", MessageType.Warning, 3);
                return;
            }

            if (!EditorUtility.DisplayDialog("删除文件夹", $"删除文件夹「{folderName}」及其下全部存档？\n不可撤销。", "删除", "取消"))
            {
                return;
            }

            RunOperation($"已删除文件夹「{folderName}」。", () => _handler.DeleteSaveFolder(folderName), () => RequestTreeRebuild(SaveServiceHandler.DEFAULT_FOLDER_NAME, null));
        }

        private void RevealSaveRoot()
        {
            string root = SafeDetermineSavePath(string.Empty);
            if (!string.IsNullOrEmpty(root))
            {
                EditorUtility.RevealInFinder(root);
            }
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

        private void SetStatus(string message, MessageType type, double durationSeconds)
        {
            _statusMessage = message ?? string.Empty;
            _statusType = type;
            _statusExpireTime = durationSeconds <= 0 ? 0 : EditorApplication.timeSinceStartup + durationSeconds;
            Repaint();
        }

        #endregion

        #region 预览 [PREVIEW]

        private string BuildPreviewText(SlotView view, Dictionary<string, byte[]> rawBlocks, string blockKey, PreviewMode mode, bool prettyJson)
        {
            if (string.IsNullOrEmpty(blockKey))
            {
                return "在「预览块」下拉中选择数据块以查看内容。";
            }

            if (rawBlocks == null)
            {
                return "原始块载荷未加载。";
            }

            SaveBlockInfo? block = FindBlock(view, blockKey);
            if (block == null)
            {
                return "数据块信息缺失。";
            }

            if (block.Value.Error != SaveError.None)
            {
                return $"该块损坏（{block.Value.Error}），载荷不可信。";
            }

            if (!rawBlocks.TryGetValue(blockKey, out byte[] bytes) || bytes == null)
            {
                return "无法读取块载荷。";
            }

            bool isBinaryBackend = block.Value.Backend != ESaveBackend.Json && block.Value.Backend != ESaveBackend.KeyValue;
            string text;
            switch (mode)
            {
                case PreviewMode.Text:
                    text = TryDecodeText(bytes, isBinaryBackend, prettyJson);
                    break;
                case PreviewMode.Hex:
                    text = BuildHexDump(bytes);
                    break;
                default:
                    if (block.Value.Backend == ESaveBackend.Json)
                    {
                        text = FormatJson(Encoding.UTF8.GetString(bytes), prettyJson);
                    }
                    else if (block.Value.Backend == ESaveBackend.KeyValue)
                    {
                        // KVT 结构化树预览（解析失败回退十六进制采样）
                        text = SaveKvPreviewFormatter.Format(bytes) ?? BuildHexDump(bytes, HEX_PREVIEW_MAX_BYTES);
                    }
                    else
                    {
                        text = BuildHexDump(bytes);
                    }

                    break;
            }

            if (string.IsNullOrEmpty(text))
            {
                return $"二进制后端（{block.Value.Backend}）载荷 {FormatBytes(bytes.Length)}，不提供文本预览。请切换到十六进制模式。";
            }

            return text;
        }

        private static string TryDecodeText(byte[] bytes, bool isBinaryBackend, bool prettyJson)
        {
            if (isBinaryBackend)
            {
                return string.Empty;
            }

            try
            {
                string text = Encoding.UTF8.GetString(bytes);
                return prettyJson ? FormatJson(text, true) : text;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static SaveBlockInfo? FindBlock(SlotView view, string blockKey)
        {
            SaveBlockInfo[] blocks = view?.Blocks;
            if (blocks == null || blockKey == null)
            {
                return null;
            }

            for (int i = 0; i < blocks.Length; i++)
            {
                if (string.Equals(blocks[i].Key, blockKey, StringComparison.Ordinal))
                {
                    return blocks[i];
                }
            }

            return null;
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

        private void ReleaseThumbnail()
        {
            if (_thumbnailTexture != null)
            {
                DestroyImmediate(_thumbnailTexture);
                _thumbnailTexture = null;
            }
        }

        #endregion

        #region 工具方法 [UTILITIES]

        private void UpdatePipelineText()
        {
            string handlerName = SaveServiceSettings.SaveServiceHandler?.GetType().Name ?? "PlainSaveHandler";
            string compression = SaveServiceSettings.CompressionProvider?.GetType().Name ?? "不压缩";
            string backend = SaveServiceSettings.DefaultBackend.ToString();
            string extension = SaveServiceSettings.SaveFileExtension;
            string root = SafeDetermineSavePath(string.Empty);
            string rootHint = string.IsNullOrEmpty(root) ? "(未解析)" : root;
            _pipelineText = $"管线  {handlerName}  ·  压缩 {compression}  ·  默认后端 {backend}  ·  扩展名 {extension}  ·  根目录 {rootHint}";
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

        private static string BuildHealthSummary(SlotView view)
        {
            int total = view.Blocks?.Length ?? 0;
            if (view.CorruptedBlocks > 0)
            {
                return $"{total} 块  ·  {view.HealthyBlocks} 健康  ·  {view.CorruptedBlocks} 坏块";
            }

            if (total == 0)
            {
                return view.IsEncryptedLikely ? "0 块  ·  疑似加密/不可解析" : "0 块";
            }

            return $"{total} 块  ·  全部健康";
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
    }
}
