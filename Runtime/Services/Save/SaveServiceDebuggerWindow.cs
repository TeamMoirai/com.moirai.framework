using System;
using System.Collections.Generic;
using System.IO;
using Moirai.Atropos.Debugger;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务调试视图（原生 UI Toolkit，经 <see cref="SaveService.OnInit"/> 注册进游戏内调试器 "Profiler/Save"）。
    /// <para>管线状态（处理器/存储后端/压缩/默认后端/截图开关）、槽位清单与选中槽位详情（块表、元数据、坏块可视化）。</para>
    /// <para>布局纪律：文件夹/槽位选择控件区常驻（不随轮询重建，避免吞点击）；数据区按 1s 节流重建。</para>
    /// </summary>
    public sealed class SaveServiceDebuggerWindow : ScrollableDebuggerWindowBase
    {
        #region 常量 [CONSTANTS]

        /// <summary>数据区刷新间隔（秒）。</summary>
        private const float REFRESH_INTERVAL = 1f;

        /// <summary>块表行宽占比（键 1/2，元信息 1/2）。</summary>
        private const float BLOCK_TITLE_RATIO = 0.5f;

        #endregion

        #region 字段 [FIELDS]

        private VisualElement _dataRoot;
        private DropdownField _folderField;
        private DropdownField _slotField;
        private string _selectedFileName;
        private float _countdown;

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            if (!SaveService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("Save Service"));
                root.Add(DebuggerUI.CreateHintLabel("存档服务未就绪（需进入运行时并完成初始化）。"));
                return;
            }

            VisualElement pipelineCard = AddSection(root, "管线 [PIPELINE]");
            AddRow(pipelineCard, "存储管线 [Handler]", SaveServiceSettings.SaveServiceHandler != null ? SaveServiceSettings.SaveServiceHandler.GetType().Name : "null");
            AddRow(pipelineCard, "存储后端 [Storage]", SaveServiceSettings.StorageBackend != null ? SaveServiceSettings.StorageBackend.GetType().Name : "null");
            AddRow(pipelineCard, "压缩 [Compression]", SaveServiceSettings.CompressionProvider != null ? SaveServiceSettings.CompressionProvider.GetType().Name : "不压缩");
            AddRow(pipelineCard, "默认序列化后端 [Default Backend]", SaveServiceSettings.DefaultBackend.ToString());
            AddRow(pipelineCard, "保存时截图 [Screenshot On Save]", SaveServiceSettings.CaptureScreenshotOnSave.ToString());

            VisualElement controlCard = AddSection(root, "槽位选择 [SELECTION]");
            _folderField = new DropdownField("文件夹 [Folder]");
            _folderField.RegisterValueChangedCallback(_ =>
            {
                _selectedFileName = null;
                RefreshSlotChoices();
                RefreshData();
            });
            controlCard.Add(_folderField);

            _slotField = new DropdownField("槽位 [Slot]");
            _slotField.RegisterValueChangedCallback(evt =>
            {
                _selectedFileName = evt.newValue;
                RefreshData();
            });
            controlCard.Add(_slotField);

            VisualElement dataCard = AddSection(root, "槽位详情 [DETAIL]");
            _dataRoot = dataCard;

            RefreshFolderChoices();
            RefreshSlotChoices();
            RefreshData();
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public override void OnEnter()
        {
            _countdown = 0f;
        }

        /// <inheritdoc />
        public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            _countdown -= realElapseSeconds;
            if (_countdown > 0f || _dataRoot == null)
            {
                return;
            }

            _countdown = REFRESH_INTERVAL;
            RefreshFolderChoices();
            RefreshSlotChoices();
            RefreshData();
        }

        #endregion

        #region 选择刷新 [CHOICES]

        /// <summary>
        /// 当前选择的文件夹名（缺省默认存档文件夹）。
        /// </summary>
        private string SelectedFolder => _folderField == null || string.IsNullOrEmpty(_folderField.value)
            ? SaveServiceHandler.DEFAULT_FOLDER_NAME
            : _folderField.value;

        /// <summary>
        /// 刷新文件夹选项（数据根目录枚举；默认文件夹恒在选项中，保持既有选择不失效）。
        /// </summary>
        private void RefreshFolderChoices()
        {
            if (_folderField == null)
            {
                return;
            }

            var choices = new List<string> { SaveServiceHandler.DEFAULT_FOLDER_NAME };
            string rootDirectory = SaveService.DetermineSavePath(string.Empty);
            if (!string.IsNullOrEmpty(rootDirectory) && Directory.Exists(rootDirectory))
            {
                string[] directories = Directory.GetDirectories(rootDirectory);
                for (int i = 0; i < directories.Length; i++)
                {
                    string folderName = Path.GetFileName(directories[i]);
                    if (!string.IsNullOrEmpty(folderName) && !choices.Contains(folderName))
                    {
                        choices.Add(folderName);
                    }
                }
            }

            string current = _folderField.value;
            _folderField.choices = choices;
            _folderField.value = choices.Contains(current) ? current : SaveServiceHandler.DEFAULT_FOLDER_NAME;
        }

        /// <summary>
        /// 刷新槽位选项（按当前文件夹枚举；保持既有选择不失效）。
        /// </summary>
        private void RefreshSlotChoices()
        {
            if (_slotField == null)
            {
                return;
            }

            SaveFileInfo[] files = SaveService.GetSaveFiles(SelectedFolder);
            var choices = new List<string>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                choices.Add(files[i].FileName);
            }

            string current = _selectedFileName;
            _slotField.choices = choices;
            _slotField.value = choices.Contains(current) ? current : null;
            _selectedFileName = _slotField.value;
        }

        #endregion

        #region 数据区 [DATA]

        /// <summary>
        /// 重建数据区（选中槽位的概要/块表/元数据/截图 sidecar 状态）。
        /// </summary>
        private void RefreshData()
        {
            if (_dataRoot == null)
            {
                return;
            }

            _dataRoot.Clear();
            if (string.IsNullOrEmpty(_selectedFileName))
            {
                _dataRoot.Add(DebuggerUI.CreateHintLabel("当前文件夹无存档槽位。"));
                return;
            }

            string folderName = SelectedFolder;

            SaveFileInfo[] files = SaveService.GetSaveFiles(folderName);
            for (int i = 0; i < files.Length; i++)
            {
                if (string.Equals(files[i].FileName, _selectedFileName, StringComparison.Ordinal))
                {
                    AddRow(_dataRoot, "大小 [Size]", StringUtility.Format("{0} B", files[i].SizeBytes));
                    AddRow(_dataRoot, "最后写入 [Last Write]", files[i].LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
                    break;
                }
            }

            string directoryPath = SaveService.DetermineSavePath(folderName);
            string screenshotPath = string.IsNullOrEmpty(directoryPath)
                ? null
                : Path.Combine(directoryPath, SaveScreenshotUtility.DetermineScreenshotFileName(_selectedFileName));
            AddRow(_dataRoot, "截图 sidecar [Screenshot]", screenshotPath != null && File.Exists(screenshotPath) ? "有" : "无");

            SaveResult<SaveMetadata> metadataResult = SaveService.TryLoadMetadata(_selectedFileName, folderName);
            if (metadataResult.IsSuccess)
            {
                SaveMetadata metadata = metadataResult.Data;
                AddRow(_dataRoot, "游戏版本 [Game Version]", metadata.GameVersion ?? "-");
                AddRow(_dataRoot, "存档数据版本 [Save Version]", metadata.SaveVersion.ToString());
                AddRow(_dataRoot, "场景 [Scene]", metadata.SceneName ?? "-");
                AddRow(_dataRoot, "游玩时长 [Play Time]", metadata.PlayTimeTicks > 0L ? new TimeSpan(metadata.PlayTimeTicks).ToString() : "-");
                AddRow(_dataRoot, "迁移历史 [Migrations]", metadata.MigrationHistory != null ? metadata.MigrationHistory.Count.ToString() : "0");
            }

            SaveBlockInfo[] blocks = SaveService.GetBlockInfos(_selectedFileName, folderName);
            if (blocks.Length == 0)
            {
                _dataRoot.Add(DebuggerUI.CreateHintLabel("无数据块（或整档损坏/未就绪）。"));
                return;
            }

            for (int i = 0; i < blocks.Length; i++)
            {
                SaveBlockInfo block = blocks[i];
                if (block.Error != SaveError.None)
                {
                    // 坏块可视化：键不可读时以序号占位；结构性坏块元信息为零值仅报错误码
                    string title = block.Key ?? StringUtility.Format("<坏块 #{0}>", i);
                    string content = block.HasMetadata
                        ? StringUtility.Format("{0} | v{1} | {2} B | {3}", block.Backend, block.DataVersion, block.SizeBytes, block.Error)
                        : StringUtility.Format("结构不可读 | {0}", block.Error);
                    VisualElement row = DebuggerUI.CreateRow(title, content, BLOCK_TITLE_RATIO);
                    row.AddToClassList("dbg-text--danger");
                    _dataRoot.Add(row);
                    continue;
                }

                AddRow(_dataRoot, block.Key, StringUtility.Format("{0} | v{1} | {2} B", block.Backend, block.DataVersion, block.SizeBytes), BLOCK_TITLE_RATIO);
            }
        }

        #endregion
    }
}
