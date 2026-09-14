using System;
using System.Collections.Generic;
using Moirai.Atropos.Debugger;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程服务调试视图（原生 UI Toolkit，经 <see cref="ProcedureService.OnInit"/> 注册进游戏内调试器 "Profiler/Procedure"）。
    /// <para>当前流程卡常驻（轮询仅写 text）；流程列表行仅在注册集变化时重建——切换按钮不落在轮询重建边界被吞掉；
    /// 切换历史为纯展示行，随轮询重建。按 0.5s 节流刷新。</para>
    /// </summary>
    public sealed class ProcedureServiceDebuggerWindow : ScrollableDebuggerWindowBase
    {
        #region 常量 [CONSTANTS]

        private const float REFRESH_INTERVAL = 0.5f;

        /// <summary>历史区最多展示的记录条数（超出取最近 N 条）。</summary>
        private const int MaxHistoryRows = 10;

        #endregion

        #region 字段 [FIELDS]

        // 常驻卡持引用行（轮询只写 text，不重建）
        private Button _currentProcedureValue;
        private Button _currentElapsedValue;

        // 动态区：流程列表仅注册集变化时重建；切换历史随轮询重建（纯展示行）
        private VisualElement _listRoot;
        private VisualElement _historyRoot;
        private readonly List<Type> _registeredTypes = new List<Type>();
        private readonly List<Button> _listRowValues = new List<Button>();

        private float _countdown;
        private bool _viewBuilt;
        private bool _builtValidState;

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            _viewBuilt = true;
            _builtValidState = ProcedureService.IsValid;

            if (!ProcedureService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("Procedure Service"));
                root.Add(DebuggerUI.CreateHintLabel("流程服务未就绪（需进入运行时并完成初始化）。"));
                return;
            }

            // ① 当前流程（常驻卡，轮询仅更新值文本）
            VisualElement currentCard = AddSection(root, "当前流程 [CURRENT PROCEDURE]");
            AddRow(currentCard, "当前流程 [Procedure]", "-", out _currentProcedureValue);
            AddRow(currentCard, "持续时长 [Elapsed]", "-", out _currentElapsedValue);

            // ② 已注册流程（常驻卡 + 内部动态区，值按钮点击强制切换）
            VisualElement listCard = AddSection(root, "已注册流程 [REGISTERED PROCEDURES]");
            _listRoot = new VisualElement();
            listCard.Add(_listRoot);

            // ③ 切换历史（纯展示行，随轮询重建）
            VisualElement historyCard = AddSection(root, "切换历史 [TRANSITION HISTORY]");
            _historyRoot = new VisualElement();
            historyCard.Add(_historyRoot);

            SnapshotRegisteredTypes();
            RebuildProcedureList();
            RebuildHistory();
            RefreshCurrent();
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public override void OnEnter()
        {
            // 重新进入窗口立即刷新一次，避免展示陈旧内容
            _countdown = 0f;
        }

        /// <inheritdoc />
        public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            // 视图尚未构建（选中后首帧 Tick 可能先于 CreateView）——跳过刷新
            if (!_viewBuilt)
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

        #region 刷新 [REFRESH]

        /// <summary>
        /// 节流刷新：就绪态切换时整体重建；就绪期间仅更新常驻行文本与动态区。
        /// </summary>
        private void Refresh()
        {
            if (ProcedureService.IsValid != _builtValidState)
            {
                // 世界初始化/关停导致就绪态翻转——常驻卡结构不同，整体重建
                Rebuild();
                return;
            }

            if (!_builtValidState)
            {
                return;
            }

            RefreshCurrent();

            IReadOnlyCollection<ProcedureBase> registered = ProcedureService.Procedures;
            if (RegisteredSetChanged(registered))
            {
                SnapshotRegisteredTypes();
                RebuildProcedureList();
            }
            else
            {
                RefreshListMarkers();
            }

            RebuildHistory();
        }

        /// <summary>
        /// 更新当前流程卡文本（未启动状态机时显示占位，避免命中处理器的未就绪 fail-fast）。
        /// </summary>
        private void RefreshCurrent()
        {
            if (ProcedureService.IsStateReady)
            {
                ProcedureBase current = ProcedureService.CurrentProcedure;
                _currentProcedureValue.text = current != null ? current.GetType().Name : "（未启动）";
                _currentElapsedValue.text = StringUtility.Format("{0:F2}s", ProcedureService.CurrentProcedureTime);
            }
            else
            {
                _currentProcedureValue.text = "（未初始化）";
                _currentElapsedValue.text = "-";
            }
        }

        /// <summary>
        /// 重建流程列表行（仅注册集变化时调用——切换按钮接线不随轮询丢失）。
        /// </summary>
        private void RebuildProcedureList()
        {
            _listRoot.Clear();
            _listRowValues.Clear();

            IReadOnlyCollection<ProcedureBase> registered = ProcedureService.Procedures;
            if (registered.Count == 0)
            {
                _listRoot.Add(DebuggerUI.CreateHintLabel("流程集为空（状态机未初始化）。"));
                return;
            }

            foreach (ProcedureBase procedure in registered)
            {
                AddRow(_listRoot, procedure.GetType().Name, "-", out Button rowValue);
                Type procedureType = procedure.GetType();
                rowValue.clicked += () => ProcedureService.ChangeState(procedureType);
                _listRowValues.Add(rowValue);
            }

            RefreshListMarkers();
        }

        /// <summary>
        /// 刷新流程列表行标记（当前流程高亮，其余为可切换）。
        /// </summary>
        private void RefreshListMarkers()
        {
            ProcedureBase current = ProcedureService.IsStateReady ? ProcedureService.CurrentProcedure : null;
            int index = 0;
            foreach (ProcedureBase procedure in ProcedureService.Procedures)
            {
                if (index < _listRowValues.Count)
                {
                    _listRowValues[index].text = ReferenceEquals(procedure, current) ? "● 当前" : "切换 ▶";
                }

                index++;
            }
        }

        /// <summary>
        /// 重建切换历史区（时间升序存储，倒序展示最近记录）。
        /// </summary>
        private void RebuildHistory()
        {
            _historyRoot.Clear();

            IReadOnlyList<ProcedureTransitionRecord> history = ProcedureService.TransitionHistory;
            if (history.Count == 0)
            {
                _historyRoot.Add(DebuggerUI.CreateHintLabel("暂无切换记录。"));
                return;
            }

            int firstIndex = history.Count > MaxHistoryRows ? history.Count - MaxHistoryRows : 0;
            for (int i = history.Count - 1; i >= firstIndex; i--)
            {
                ProcedureTransitionRecord record = history[i];
                string fromName = record.From != null ? record.From.GetType().Name : "(启动)";
                string toName = record.To != null ? record.To.GetType().Name : "(关停)";
                AddRow(_historyRoot,
                    StringUtility.Format("#{0} {1}", i + 1, record.Kind),
                    StringUtility.Format("{0} → {1} ({2:F2}s)", fromName, toName, record.FromElapsed));
            }
        }

        #endregion

        #region 注册集比对 [SET COMPARISON]

        /// <summary>
        /// 以类型快照比对注册集是否变化（仅 Initialize/Restart 会改写注册集，轮询期通常恒定）。
        /// </summary>
        private bool RegisteredSetChanged(IReadOnlyCollection<ProcedureBase> registered)
        {
            if (registered.Count != _registeredTypes.Count)
            {
                return true;
            }

            int index = 0;
            foreach (ProcedureBase procedure in registered)
            {
                if (_registeredTypes[index] != procedure.GetType())
                {
                    return true;
                }

                index++;
            }

            return false;
        }

        /// <summary>
        /// 快照当前注册集类型（行序与列表行一一对应）。
        /// </summary>
        private void SnapshotRegisteredTypes()
        {
            _registeredTypes.Clear();
            foreach (ProcedureBase procedure in ProcedureService.Procedures)
            {
                _registeredTypes.Add(procedure.GetType());
            }
        }

        #endregion
    }
}
