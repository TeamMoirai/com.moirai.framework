using System;
using Moirai.Atropos.Debugger;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源服务调试视图（原生 UI Toolkit，经 <see cref="ResourceService.OnInit"/> 注册进游戏内调试器 "Profiler/Resource"）。
    /// <para>展示运行模式与已加载资产快照（定位地址/状态/引用计数），按 0.5s 节流重建。</para>
    /// </summary>
    public sealed class ResourceServiceDebugView : PollingDebuggerWindowBase
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
        public ResourceServiceDebugView() : base(0.5f)
        {
        }

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            if (!ResourceService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("Resource Service"));
                root.Add(DebuggerUI.CreateHintLabel("资源服务未就绪（需进入运行时并完成初始化）。"));
                return;
            }

            VisualElement summaryCard = AddSection(root, "运行状态 [RUNTIME STATE]");
            AddRow(summaryCard, "运行模式 [Play Mode]", ResourceService.PlayMode.ToString());
            AddRow(summaryCard, "运行时可更新 [Updatable While Playing]", ResourceService.UpdatableWhilePlaying.ToString());

            VisualElement assetCard = AddSection(root, "已加载资产采样 [LOADED ASSET SAMPLE]");
            int count = ResourceService.GetAssetInfos(_infoBuffer, 0, SAMPLE_COUNT);
            if (count <= 0)
            {
                assetCard.Add(DebuggerUI.CreateHintLabel("当前无已加载资产。"));
                return;
            }

            if (count >= SAMPLE_COUNT)
            {
                assetCard.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("仅显示前 {0} 条（可能截断）。", SAMPLE_COUNT)));
            }
            else
            {
                assetCard.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("共 {0} 条。", count)));
            }

            for (int i = 0; i < count; i++)
            {
                ref ResourceAssetInfo info = ref _infoBuffer[i];
                string title = StringUtility.Format("[{0}] {1}", info.State, info.Location);
                string value = StringUtility.Format("{0} | 直接引用 {1} | 绑定 {2} | 保持 {3}",
                    info.TypeName, info.DirectRefCount, info.BindingRefCount, info.KeepAliveRefCount);
                AddRow(assetCard, title, value, ASSET_TITLE_RATIO);
            }
        }

        #endregion
    }
}
