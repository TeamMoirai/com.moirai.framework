using System;
using Moirai.Atropos.Debugger;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源服务调试视图（原生 UI Toolkit，经 <see cref="ResourceService.OnInit"/> 注册进游戏内调试器 "Profiler/Resource"）。
    /// <para>展示运行模式与已加载资产快照（定位地址/状态/引用计数），按 0.5s 节流重建。</para>
    /// </summary>
    public sealed class ResourceServiceDebuggerWindow : PollingDebuggerWindowBase
    {
        #region 常量 [CONSTANTS]

        private const int SAMPLE_COUNT = 64;

        /// <summary>资产定位与信息展示的行宽占比（定位 2/3，信息 1/3）。</summary>
        private const float ASSET_TITLE_RATIO = 2f / 3f;

        #endregion

        #region 字段 [FIELDS]

        private readonly ResourceAssetInfo[] _infoBuffer = new ResourceAssetInfo[SAMPLE_COUNT];

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
