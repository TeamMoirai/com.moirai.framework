using System;
using Moirai.Atropos.Debugger;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源服务调试视图（原生 UI Toolkit），展示运行模式与已加载资产快照（定位地址/状态/引用计数）。
    /// </summary>
    /// <remarks>经 <see cref="ResourceService.OnInit"/> 注册进游戏内调试器 "Profiler/Resource"，按 0.5s 节流重建。</remarks>
    public sealed class ResourceServiceDebuggerWindow : PollingDebuggerWindowBase
    {
        #region 常量 [CONSTANTS]

        private const int SAMPLE_COUNT = 64;

        /// <summary>卸载账展示行数上限（最新优先；账本身比这长，靠 Dropped 读数知道截没截）。</summary>
        private const int PROVENANCE_ROW_COUNT = 12;

        /// <summary>资产定位与信息展示的行宽占比（定位 2/3，信息 1/3）。</summary>
        private const float ASSET_TITLE_RATIO = 2f / 3f;

        #endregion

        #region 字段 [FIELDS]

        private readonly ResourceAssetInfo[] _infoBuffer = new ResourceAssetInfo[SAMPLE_COUNT];

#if UNITY_EDITOR
        /// <summary>在途租约行缓冲（仅编辑器）：随面板节流重建复用。</summary>
        private readonly ResourceLeaseSourceInfo[] _leaseBuffer = new ResourceLeaseSourceInfo[PROVENANCE_ROW_COUNT];
#endif

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化资源调试视图的新实例。
        /// </summary>
        public ResourceServiceDebuggerWindow() : base(0.5f)
        {
        }

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            if (!ResourceService.IsInitialized)
            {
                root.Add(DebuggerUI.CreateSectionTitle("Resource Service"));
                root.Add(DebuggerUI.CreateHintLabel("Resource service not ready (enter Play Mode and finish initialization)."));
                return;
            }

            VisualElement summaryCard = AddSection(root, "RUNTIME STATE");
            AddRow(summaryCard, "Play Mode", ResourceService.PlayMode.ToString());
            AddRow(summaryCard, "Updatable While Playing", ResourceService.UpdatableWhilePlaying.ToString());

#if UNITY_EDITOR
            // 放在资产样本之前：卸载账最有话可说的时刻恰恰是栈上已经没东西可列的时候
            VisualElement provenanceCard = AddSection(root, "LEASE PROVENANCE (EDITOR)");
            provenanceCard.Add(DebuggerUI.CreateToggle("Capture lease source", ResourceLeaseProvenance.Enabled,
                value => ResourceLeaseProvenance.Enabled = value));
            provenanceCard.Add(DebuggerUI.CreateActionButton("Clear unload history",
                () => ResourceLeaseProvenance.ClearUnload()));

            // 在途租约先列、卸载账后列：先答"谁还欠着"，再看"何时卸的"
            int leaseWritten = ResourceService.GetLeaseSourceInfos(_leaseBuffer, 0, _leaseBuffer.Length,
                out int outstanding, out int sourced);
            if (outstanding <= 0)
            {
                provenanceCard.Add(DebuggerUI.CreateHintLabel("No outstanding leases."));
            }
            else
            {
                for (int i = 0; i < leaseWritten; i++)
                {
                    ref ResourceLeaseSourceInfo info = ref _leaseBuffer[i];
                    string source = info.Line > 0
                        ? StringUtility.Format("{0} @ {1}:{2}", info.Display, info.File, info.Line)
                        : info.Display;
                    AddRow(provenanceCard, StringUtility.Format("[{0}] {1}", info.Kind, info.Location), source,
                        ASSET_TITLE_RATIO);
                }

                if (outstanding > leaseWritten)
                {
                    string extra = sourced > leaseWritten
                        ? StringUtility.Format(" | showing first {0} with source", leaseWritten)
                        : string.Empty;
                    provenanceCard.Add(DebuggerUI.CreateHintLabel(StringUtility.Format(
                        "Outstanding: {0} leases, {1} without source{2}.", outstanding, outstanding - sourced, extra)));
                }
            }

            int unloaded = ResourceLeaseProvenance.UnloadCount;
            if (unloaded <= 0)
            {
                provenanceCard.Add(DebuggerUI.CreateHintLabel("No unload recorded since the last clear."));
            }
            else
            {
                int shown = Math.Min(unloaded, PROVENANCE_ROW_COUNT);
                for (int i = 0; i < shown; i++)
                {
                    ResourceLeaseProvenance.UnloadRecord record = ResourceLeaseProvenance.GetNewestUnload(i);
                    AddRow(provenanceCard, record.Location,
                        StringUtility.Format("{0} | {1:HH:mm:ss} UTC", record.TypeName, record.UnloadTimeUtc),
                        ASSET_TITLE_RATIO);
                }

                if (ResourceLeaseProvenance.UnloadDropped > 0)
                {
                    AddRow(provenanceCard, "Dropped by ring cap",
                        StringUtility.Format("{0}", ResourceLeaseProvenance.UnloadDropped));
                }
            }
#endif

            VisualElement assetCard = AddSection(root, "LOADED ASSET SAMPLE");
            int count = ResourceService.GetAssetInfos(_infoBuffer, 0, SAMPLE_COUNT);
            if (count <= 0)
            {
                assetCard.Add(DebuggerUI.CreateHintLabel("No assets currently loaded."));
                return;
            }

            if (count >= SAMPLE_COUNT)
            {
                assetCard.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("Showing first {0} entries (may be truncated).", SAMPLE_COUNT)));
            }
            else
            {
                assetCard.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("{0} entries.", count)));
            }

            for (int i = 0; i < count; i++)
            {
                ref ResourceAssetInfo info = ref _infoBuffer[i];
                string title = StringUtility.Format("[{0}] {1}", info.State, info.Location);
                string value = StringUtility.Format("{0} | Direct {1} | Binding {2} | KeepAlive {3}",
                    info.TypeName, info.DirectRefCount, info.BindingRefCount, info.KeepAliveRefCount);
                AddRow(assetCard, title, value, ASSET_TITLE_RATIO);
            }
        }

        #endregion
    }
}
