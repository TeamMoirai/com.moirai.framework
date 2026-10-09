using Moirai.Atropos.Debugger;
using Moirai.Atropos.Input;
using UnityEngine.UIElements;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 服务调试视图（经 <see cref="UIService.OnInit"/> 注册进游戏内调试器 "Profiler/UI"）。
    /// </summary>
    /// <remarks>
    /// 展示门面有效性、全局交互压制位、共享栈快照（自顶向下）、停放表与交互租约持有人。 <br />
    /// 数据区按 1s 节流重建；数据全部走内核只读接缝，不拷贝状态。
    /// </remarks>
    public sealed class UIServiceDebuggerWindow : ScrollableDebuggerWindowBase
    {
        /// <summary>数据区刷新间隔（秒）。</summary>
        private const float REFRESH_INTERVAL = 1f;

        private VisualElement _dataRoot;
        private float _countdown;

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            if (!UIService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("UI Service"));
                root.Add(DebuggerUI.CreateHintLabel("UI service not ready (enter Play Mode and finish initialization)."));
                return;
            }

            root.Add(DebuggerUI.CreateSectionTitle("UI Service"));

            _dataRoot = new VisualElement();
            _dataRoot.style.flexDirection = FlexDirection.Column;
            root.Add(_dataRoot);

            Refresh();
        }

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
            Refresh();
        }

        /// <summary>重建数据区：门面、共享栈、停放表与租约按当前快照重画。</summary>
        private void Refresh()
        {
            if (_dataRoot == null)
            {
                return;
            }

            _dataRoot.Clear();

            var ledger = UIService.IsValid ? UIService.SharedLedger : null;

            VisualElement facadeCard = AddSection("FACADE");
            AddRow(facadeCard, "Valid", UIService.IsValid ? "Yes" : "No");
            AddRow(facadeCard, "UGUI Track", UIService.IsUGUIValid ? "In place" : "Not enabled");
            AddRow(facadeCard, "UI Toolkit Track", UIService.IsUITKValid ? "In place" : "Not enabled");
            AddRow(facadeCard, "Global Interaction Suppressed", InputService.PreventInteractionUI ? "Yes" : "No");

            if (ledger == null)
            {
                return;
            }

            VisualElement stackCard = AddSection("STACK (top-down)");
            var stack = ledger.PeekStack();
            if (stack.Count == 0)
            {
                AddRow(stackCard, "Stack", "(empty)");
            }
            else
            {
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    var window = stack[i];
                    var loading = window.IsLoadDone ? string.Empty : " (loading)";
                    AddRow(stackCard, i.ToString(),
                        $"{window.WindowId}  [{window.GetType().Name}]  layer {window.WindowLayer}  depth {window.Depth}  " +
                        $"{(window.Visible ? "visible" : "hidden")}  {(window.IsPrepare ? "ready" : "not ready")}{loading}  " +
                        $"{(window.IsModalWindow ? "modal" : "non-modal")}");
                }
            }

            VisualElement parkingCard = AddSection("PARKING (cached instances)");
            var parked = ledger.PeekParkedIds();
            if (parked.Count == 0)
            {
                AddRow(parkingCard, "Parked", "(none)");
            }
            else
            {
                foreach (string windowId in parked)
                {
                    AddRow(parkingCard, windowId, "cached");
                }
            }

            VisualElement leaseCard = AddSection("INTERACTION LEASE");
            var holder = ledger.InteractionLease.PeekHolder();
            AddRow(leaseCard, "Suppression Holder", holder != null ? holder.WindowId : "(none)");
        }

        /// <summary>加一个数据卡片分区。</summary>
        private VisualElement AddSection(string title)
        {
            var card = new VisualElement();
            card.style.flexDirection = FlexDirection.Column;
            card.Add(DebuggerUI.CreateSectionTitle(title));
            _dataRoot.Add(card);
            return card;
        }
    }
}
