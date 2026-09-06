using Moirai.Atropos.Debugger;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程服务调试视图（原生 UI Toolkit，经 <see cref="ProcedureService.OnInit"/> 注册进游戏内调试器 "Profiler/Procedure"）。
    /// <para>展示当前流程状态与持续时长，按 0.5s 节流重建。</para>
    /// </summary>
    public sealed class ProcedureServiceDebugView : PollingDebuggerWindowBase
    {
        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化流程调试视图的新实例。
        /// </summary>
        public ProcedureServiceDebugView() : base(0.5f)
        {
        }

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            if (!ProcedureService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("Procedure Service"));
                root.Add(DebuggerUI.CreateHintLabel("流程服务未就绪（需进入运行时并完成初始化）。"));
                return;
            }

            VisualElement card = AddSection(root, "当前流程 [CURRENT PROCEDURE]");
            ProcedureBase current = ProcedureService.CurrentProcedure;
            if (current == null)
            {
                card.Add(DebuggerUI.CreateHintLabel("流程状态机尚未启动。"));
                return;
            }

            AddRow(card, "当前流程 [Procedure]", current.GetType().Name);
            AddRow(card, "持续时长 [Elapsed]", StringUtility.Format("{0:F2}s", ProcedureService.CurrentProcedureTime));
        }

        #endregion
    }
}
