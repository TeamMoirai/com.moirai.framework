using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Moirai.Atropos.Save;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor.Save
{
    /// <summary>
    /// 存档浏览器编辑器窗口：浏览 <c>persistentDataPath/Data/</c> 下的存档文件夹与槽位，
    /// 展示块表（键/版本/后端/大小/逐块错误）、未加密档内容预览（JSON 块原文 / KVT 块十六进制采样），
    /// 支持备份/恢复备份/删除操作。
    /// <para>加密档/压缩档在缺管线配置时解析失败按提示展示（编辑器以明文处理器 + 设置的压缩提供方读取）。</para>
    /// </summary>
    public sealed class SaveBrowserWindow : EditorWindow
    {
        #region 常量 [CONSTANTS]

        /// <summary>内容预览十六进制采样字节数上限。</summary>
        private const int HEX_PREVIEW_MAX_BYTES = 128;

        #endregion

        #region 字段 [FIELDS]

        private PlainSaveHandler _handler;
        private string[] _folders = Array.Empty<string>();
        private int _selectedFolderIndex;
        private SaveFileInfo[] _slots = Array.Empty<SaveFileInfo>();
        private int _selectedSlotIndex = -1;
        private SaveBlockInfo[] _blockInfos = Array.Empty<SaveBlockInfo>();
        private Dictionary<string, byte[]> _rawBlocks;
        private int _selectedBlockIndex = -1;
        private Vector2 _folderScroll;
        private Vector2 _slotScroll;
        private Vector2 _blockScroll;
        private Vector2 _previewScroll;
        private string _statusMessage = string.Empty;

        #endregion

        #region 窗口入口 [ENTRY]

        /// <summary>
        /// 打开存档浏览器窗口。
        /// </summary>
        [MenuItem("Window/Moirai/Save Browser")]
        public static void Open()
        {
            var window = GetWindow<SaveBrowserWindow>(false, "Save Browser", true);
            window.minSize = new Vector2(760f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            // 明文处理器 + 设置中的压缩提供方（未初始化实例回退本地文件后端；加密档在此不可读属预期）
            _handler = new PlainSaveHandler
            {
                _compression = SaveServiceSettings.CompressionProvider
            };
            RefreshFolders();
        }

        #endregion

        #region 绘制 [GUI]

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();

            DrawFolderColumn();
            DrawSlotColumn();
            DrawDetailColumn();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 文件夹列（数据根目录下的子目录 + 刷新按钮）。
        /// </summary>
        private void DrawFolderColumn()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(180));
            EditorGUILayout.LabelField("文件夹", EditorStyles.boldLabel);
            _folderScroll = EditorGUILayout.BeginScrollView(_folderScroll);
            for (int i = 0; i < _folders.Length; i++)
            {
                GUIStyle style = i == _selectedFolderIndex ? EditorStyles.whiteBoldLabel : EditorStyles.label;
                if (GUILayout.Button(_folders[i], style))
                {
                    _selectedFolderIndex = i;
                    _selectedSlotIndex = -1;
                    ClearSlotDetail();
                    RefreshSlots();
                }
            }

            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("刷新"))
            {
                RefreshFolders();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 槽位列（当前文件夹的存档清单 + 新建提示）。
        /// </summary>
        private void DrawSlotColumn()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            EditorGUILayout.LabelField("槽位", EditorStyles.boldLabel);
            _slotScroll = EditorGUILayout.BeginScrollView(_slotScroll);
            for (int i = 0; i < _slots.Length; i++)
            {
                GUIStyle style = i == _selectedSlotIndex ? EditorStyles.whiteBoldLabel : EditorStyles.label;
                if (GUILayout.Button(_slots[i].FileName, style))
                {
                    _selectedSlotIndex = i;
                    _selectedBlockIndex = -1;
                    _rawBlocks = null;
                    LoadSlotDetail();
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 详情列（槽位元信息 + 操作按钮 + 块表 + 内容预览）。
        /// </summary>
        private void DrawDetailColumn()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("详情", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }

            if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Length)
            {
                EditorGUILayout.HelpBox("选择左侧槽位查看详情。", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            SaveFileInfo slot = _slots[_selectedSlotIndex];
            EditorGUILayout.LabelField("文件", slot.FileName + SaveServiceSettings.SaveFileExtension);
            EditorGUILayout.LabelField("大小", slot.SizeBytes + " B");
            EditorGUILayout.LabelField("最后写入 (UTC)", slot.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("备份", GUILayout.Width(80)))
            {
                RunOperation("已创建备份。", () => _handler.CreateBackup(slot.FileName, CurrentFolder));
            }

            if (GUILayout.Button("恢复备份", GUILayout.Width(80)) && EditorUtility.DisplayDialog("恢复备份", "以 .bak 覆盖当前存档？", "恢复", "取消"))
            {
                RunOperation("已从备份恢复。", () => _handler.RestoreBackup(slot.FileName, CurrentFolder));
                LoadSlotDetail();
            }

            if (GUILayout.Button("删除", GUILayout.Width(80)) && EditorUtility.DisplayDialog("删除存档", "删除该存档（含截图 sidecar）？不可撤销。", "删除", "取消"))
            {
                RunOperation("已删除。", () => _handler.DeleteSave(slot.FileName, CurrentFolder));
                RefreshSlots();
                ClearSlotDetail();
            }

            if (GUILayout.Button("打开目录", GUILayout.Width(80)))
            {
                EditorUtility.RevealInFinder(_handler.DetermineSavePath(CurrentFolder));
            }

            EditorGUILayout.EndHorizontal();

            DrawBlockTable();
            DrawBlockPreview();

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 块表（键/版本/后端/大小；坏块红色标注）。
        /// </summary>
        private void DrawBlockTable()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("数据块", EditorStyles.boldLabel);
            if (_blockInfos.Length == 0)
            {
                EditorGUILayout.HelpBox("无数据块（缺档/损坏/加密不可读）。", MessageType.Warning);
                return;
            }

            _blockScroll = EditorGUILayout.BeginScrollView(_blockScroll, GUILayout.MaxHeight(180));
            for (int i = 0; i < _blockInfos.Length; i++)
            {
                SaveBlockInfo block = _blockInfos[i];
                string label = block.Key ?? "<结构不可读>";
                string meta = block.HasMetadata
                    ? $"{block.Backend} | v{block.DataVersion} | {block.SizeBytes} B"
                    : "结构不可读";
                if (block.Error != SaveError.None)
                {
                    meta = meta + " | " + block.Error;
                }

                Color previous = GUI.color;
                if (block.Error != SaveError.None)
                {
                    GUI.color = new Color(1f, 0.45f, 0.45f);
                }

                GUIStyle style = i == _selectedBlockIndex ? EditorStyles.whiteBoldLabel : EditorStyles.label;
                if (GUILayout.Button(label + "  —  " + meta, style))
                {
                    _selectedBlockIndex = i;
                }

                GUI.color = previous;
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 内容预览（JSON 块原文；KVT 块十六进制采样；其它后端按字节长度提示）。
        /// </summary>
        private void DrawBlockPreview()
        {
            if (_selectedBlockIndex < 0 || _selectedBlockIndex >= _blockInfos.Length || _rawBlocks == null)
            {
                return;
            }

            SaveBlockInfo block = _blockInfos[_selectedBlockIndex];
            if (block.Error != SaveError.None || block.Key == null || !_rawBlocks.TryGetValue(block.Key, out byte[] bytes))
            {
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("预览 — " + block.Key, EditorStyles.boldLabel);
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll);
            if (block.Backend == ESaveBackend.Json)
            {
                EditorGUILayout.TextArea(Encoding.UTF8.GetString(bytes), EditorStyles.textArea);
            }
            else if (block.Backend == ESaveBackend.KeyValue)
            {
                EditorGUILayout.TextArea(ToHexPreview(bytes), EditorStyles.textArea);
            }
            else
            {
                EditorGUILayout.HelpBox($"二进制后端（{block.Backend}）载荷 {bytes.Length} B，不提供文本预览。", MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region 数据加载 [DATA]

        /// <summary>当前选中文件夹名。</summary>
        private string CurrentFolder => _folders.Length == 0 ? SaveServiceHandler.DEFAULT_FOLDER_NAME : _folders[Mathf.Clamp(_selectedFolderIndex, 0, _folders.Length - 1)];

        /// <summary>
        /// 刷新文件夹清单（数据根目录不存在时仅剩默认文件夹）。
        /// </summary>
        private void RefreshFolders()
        {
            var folders = new List<string> { SaveServiceHandler.DEFAULT_FOLDER_NAME };
            string rootDirectory = _handler.DetermineSavePath(string.Empty);
            if (Directory.Exists(rootDirectory))
            {
                string[] directories = Directory.GetDirectories(rootDirectory);
                for (int i = 0; i < directories.Length; i++)
                {
                    folders.Add(Path.GetFileName(directories[i]));
                }
            }

            _folders = folders.ToArray();
            _selectedFolderIndex = 0;
            RefreshSlots();
        }

        /// <summary>
        /// 刷新当前文件夹槽位清单。
        /// </summary>
        private void RefreshSlots()
        {
            _slots = _handler.GetSaveFiles(CurrentFolder);
            _selectedSlotIndex = -1;
        }

        /// <summary>
        /// 加载选中槽位的块表与原始块内容（同步核心读取，编辑器主线程即时完成——浏览粒度文件级，无大档卡顿风险）。
        /// </summary>
        private void LoadSlotDetail()
        {
            if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Length)
            {
                return;
            }

            SaveFileInfo slot = _slots[_selectedSlotIndex];
            _blockInfos = _handler.GetBlockInfos(slot.FileName, CurrentFolder);

            SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(slot.FileName, CurrentFolder);
            _rawBlocks = _handler.ReadRawBlocks(paths);
        }

        /// <summary>
        /// 清空槽位详情状态。
        /// </summary>
        private void ClearSlotDetail()
        {
            _blockInfos = Array.Empty<SaveBlockInfo>();
            _rawBlocks = null;
            _selectedBlockIndex = -1;
        }

        /// <summary>
        /// 执行存储操作并收敛状态文案（失败弹错误框）。
        /// </summary>
        private void RunOperation(string successMessage, Action operation)
        {
            try
            {
                operation();
                _statusMessage = successMessage;
            }
            catch (Exception exception)
            {
                _statusMessage = string.Empty;
                EditorUtility.DisplayDialog("存档操作失败", exception.Message, "确定");
            }
        }

        /// <summary>
        /// 生成十六进制采样文本（前 <see cref="HEX_PREVIEW_MAX_BYTES"/> 字节）。
        /// </summary>
        private static string ToHexPreview(byte[] bytes)
        {
            int count = Math.Min(bytes.Length, HEX_PREVIEW_MAX_BYTES);
            var builder = new StringBuilder(count * 3 + 32);
            for (int i = 0; i < count; i++)
            {
                builder.Append(bytes[i].ToString("X2"));
                builder.Append(' ');
                if ((i & 15) == 15)
                {
                    builder.Append('\n');
                }
            }

            if (bytes.Length > count)
            {
                builder.Append("… (").Append(bytes.Length).Append(" B total)");
            }

            return builder.ToString();
        }

        #endregion
    }
}
