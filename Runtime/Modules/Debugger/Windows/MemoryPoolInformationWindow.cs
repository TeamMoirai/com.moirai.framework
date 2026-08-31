using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 内存池信息窗口（按程序集分组的类级统计，低利用率/高未命中告警着色）。
    /// <para>摘要卡（含开关）构建一次常驻——轮询只重建清单区，避免点击落在重建边界被吞掉。</para>
    /// </summary>
    public sealed class MemoryPoolInformationWindow : ScrollableDebuggerWindowBase
    {
        #region 常量 [CONSTANTS]

        private const float REFRESH_INTERVAL = 0.5f;

        #endregion

        #region 字段 [FIELDS]

        private readonly Dictionary<string, List<MemoryPoolInfo>> _infosByAssembly = new Dictionary<string, List<MemoryPoolInfo>>(8);
        private MemoryPoolInfo[] _infoBuffer = Array.Empty<MemoryPoolInfo>();
        private VisualElement _dynamicRoot;
        private Button _countButton;
        private Button _phaseButton;
        private float _countdown;
        private bool _showFullClassName;

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            // 摘要卡常驻（开关跨轮询存活）——整体重建会丢失落在重建边界的点击
            VisualElement summaryCard = AddSection(root, "Memory Pool Information");
            AddRow(summaryCard, "Memory Pool Count", MemoryPool.Count.ToString(), out _countButton);
            AddRow(summaryCard, "Phase", MemoryPoolRegistry.Phase.ToString(), out _phaseButton);

            VisualElement toggleRow = DebuggerUI.CreateToolbarRow();
            toggleRow.Add(DebuggerUI.CreateToggle("Show Full Class Name", _showFullClassName, value => _showFullClassName = value));
            summaryCard.Add(toggleRow);

            _dynamicRoot = new VisualElement();
            _dynamicRoot.style.flexDirection = FlexDirection.Column;
            root.Add(_dynamicRoot);

            Refresh();
        }

        /// <inheritdoc />
        public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            // 视图尚未构建（选中后首帧 Tick 可能先于 CreateView）——跳过刷新
            if (_countButton == null)
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

        #endregion

        #region 私有 [PRIVATE]

        private void Refresh()
        {
            _countButton.text = MemoryPool.Count.ToString();
            _phaseButton.text = MemoryPoolRegistry.Phase.ToString();
            RefreshList();
        }

        private void RefreshList()
        {
            VisualElement listRoot = _dynamicRoot;
            listRoot.Clear();

            _infosByAssembly.Clear();
            int count = MemoryPool.Count;
            if (_infoBuffer.Length < count)
            {
                _infoBuffer = new MemoryPoolInfo[Mathf.Max(count, _infoBuffer.Length * 2)];
            }

            int actualCount = MemoryPool.GetAllMemoryPoolInfos(_infoBuffer);
            for (int i = 0; i < actualCount; i++)
            {
                string assemblyName = _infoBuffer[i].Type.Assembly.GetName().Name;
                if (!_infosByAssembly.TryGetValue(assemblyName, out List<MemoryPoolInfo> list))
                {
                    list = new List<MemoryPoolInfo>(16);
                    _infosByAssembly[assemblyName] = list;
                }

                list.Add(_infoBuffer[i]);
            }

            foreach (KeyValuePair<string, List<MemoryPoolInfo>> assemblyInfo in _infosByAssembly)
            {
                VisualElement card = AddSection(listRoot, StringUtility.Format("Assembly: {0}", assemblyInfo.Key));
                assemblyInfo.Value.Sort(_showFullClassName ? FullClassNameComparer : NormalClassNameComparer);
                for (int i = 0; i < assemblyInfo.Value.Count; i++)
                {
                    AddMemoryPoolInfoRow(card, assemblyInfo.Value[i]);
                }

                if (assemblyInfo.Value.Count == 0)
                {
                    card.Add(DebuggerUI.CreateHintLabel("Memory Pool is Empty ..."));
                }
            }
        }

        private void AddMemoryPoolInfoRow(VisualElement card, MemoryPoolInfo info)
        {
            int pageCapacity = info.PageCapacity;
            int utilPercent = pageCapacity > 0 ? (int)((long)info.UnusedCount * 100 / pageCapacity) : 0;
            bool lowUtil = pageCapacity > 0 && utilPercent < 50;
            bool longIdle = info.IdleFrames > MemoryPool.ShortDecayStartFrames;
            bool highMissRate = info.AcquireCount > 0 && info.MissRate > 0.1f;

            string className = _showFullClassName ? info.Type.FullName : info.Type.Name;
            string entry = StringUtility.Format("Unused {0} | Using {1} | Acquire {2} | Release {3} | Miss {4} | Reserve {5} | Idle {6} | Pages {7} | Util {8}%",
                info.UnusedCount, info.UsingCount, info.AcquireCount, info.ReleaseCount, info.MissCount, info.TargetFreeReserve, info.IdleFrames, pageCapacity, utilPercent);

            VisualElement row = DebuggerUI.CreateRow(className, entry);
            if (highMissRate || lowUtil || longIdle)
            {
                // 值按钮自身即文本元素（Button:TextElement）——按严重度挂语义类着色
                Button valueButton = row.Q<Button>();
                if (valueButton != null)
                {
                    valueButton.AddToClassList(highMissRate ? "dbg-text--danger" : "dbg-text--warning");
                }
            }

            card.Add(row);
        }

        private static int NormalClassNameComparer(MemoryPoolInfo a, MemoryPoolInfo b)
        {
            return string.CompareOrdinal(a.Type.Name, b.Type.Name);
        }

        private static int FullClassNameComparer(MemoryPoolInfo a, MemoryPoolInfo b)
        {
            return string.CompareOrdinal(a.Type.FullName, b.Type.FullName);
        }

        #endregion
    }
}
