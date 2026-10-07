using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Input;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 过渡契约的用例：瞬时档零锁零占用，真过渡档播放期间锁交互、走完交还，关闭过渡走完才停放。
    /// </summary>
    /// <remarks>
    /// 探针过渡用 <see cref="UniTaskCompletionSource"/> 当闸门：<c>TrySetResult</c> 让续体当场落定，EditMode 不推帧也量得到。 <br />
    /// 瞬时档的判据是「不锁交互、面板当场停放」——默认开窗关闭都不再有内置延迟与输入锁。
    /// </remarks>
    [TestFixture]
    public sealed class UIWindowTransitionTests
    {
        private bool _savedPreventInteraction;
        private UIServiceHandler[] _savedEnabledHandlers;

        /// <summary>进门归位后装一支 uGUI 驱动者并走生产初始化：交互锁要过租约那一道才占得到压制位。</summary>
        [SetUp]
        public void SetUp()
        {
            _savedPreventInteraction = InputService.PreventInteractionUI;
            _savedEnabledHandlers = UIServiceSettings.EnabledHandlers;
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(new UIServiceHandler[] { new UGUIHandler() });
            new UIService().OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(_savedEnabledHandlers);
            InputService.PreventInteractionUI = _savedPreventInteraction;
        }

        /// <summary>瞬时档（默认）：开窗不锁交互——就绪回执后交互位保持原样。</summary>
        [Test]
        public void Open_NoTransition_KeepsInteractableWithoutLocking()
        {
            var window = Prepared<InstantProbeWindow>("InstantOpen");
            window.Interactable = true;

            window.InternalRefresh(true);

            Assert.IsTrue(window.Interactable, "瞬时开窗不锁交互：没有过渡就没有半秒输入锁");
        }

        /// <summary>真过渡档：开窗链自己播那一次——播放期间锁交互，走完才交还。</summary>
        /// <remarks>开窗的整条链（压栈→就绪→回执）在 <see cref="Prepared{T}"/> 里走完，过渡随之开播；用例只判播放期与落定后。</remarks>
        [Test]
        public void Open_WithTransition_LocksInteractableUntilPlayCompletes()
        {
            var window = Prepared<GatedProbeWindow>("GatedOpen");
            var gate = window.ProbeTransition;

            Assert.AreEqual(1, gate.Plays, "开窗经过渡播放恰好一次");
            Assert.IsTrue(gate.LastOpen, "方向是开窗档");
            Assert.IsFalse(window.Interactable, "过渡播放期间交互位锁着");

            gate.Gate.TrySetResult();

            Assert.IsTrue(window.Interactable, "过渡走完交还交互位");
        }

        /// <summary>瞬时关闭：面板当场停放，交互位不锁。</summary>
        [Test]
        public void Close_NoTransition_ParksPanelImmediatelyWithoutLocking()
        {
            var window = Prepared<InstantProbeWindow>("InstantClose");
            window.Interactable = true;

            window.InternalClose();

            Assert.AreEqual(1, window.ParkCalls, "瞬时关闭的停放与关闭同帧发生");
            Assert.IsTrue(window.Interactable, "瞬时关闭不锁交互");
        }

        /// <summary>真过渡关闭：播放期间锁交互、停放一次都不落，走完才交还并停放。</summary>
        [Test]
        public void Close_WithTransition_UnlocksAndParksAfterPlayCompletes()
        {
            var window = Prepared<GatedProbeWindow>("GatedClose");
            var gate = window.ProbeTransition;
            window.Interactable = true;

            window.InternalClose();

            Assert.AreEqual(0, window.ParkCalls, "停放只在过渡走完那一帧发生——播放期间一次都不落");
            Assert.IsFalse(window.Interactable, "关闭过渡播放期间交互位锁着");

            gate.Gate.TrySetResult();

            Assert.IsTrue(window.Interactable, "过渡走完交还交互位");
            Assert.AreEqual(1, window.ParkCalls, "过渡走完补停放");
        }

        /// <summary>造一只压栈并就绪的探针窗（探针经注册表登记，开窗语义走生产链）。</summary>
        private static T Prepared<T>(string windowName) where T : UIWindow, new()
        {
            var ledger = UIService.SharedLedger;
            ledger.ShowUIImp(typeof(T), false, windowName, "Panel", false, null);
            var window = ledger.GetWindow(windowName);
            Assert.IsTrue(window.IsLoadDone, "量具前提坏了：探针窗要同步装载就绪");
            return (T)window;
        }

        #region 探针 [PROBES]

        /// <summary>瞬时档探针窗：无过渡、面板钩子只记账；闸门探针窗从它派生。</summary>
        [Window(UILayer.Tips)]
        internal class InstantProbeWindow : UGUIWindow
        {
            /// <summary>停放钩子被叫到的次数。</summary>
            internal int ParkCalls;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel()
            {
                ParkCalls++;
            }
        }

        /// <summary>闸门过渡探针窗：每只实例一枚自己的探针过渡，闸门与播放记录都在它身上。</summary>
        [Window(UILayer.Tips)]
        internal sealed class GatedProbeWindow : InstantProbeWindow
        {
            /// <summary>本窗的探针过渡：走完时机由用例的 <c>TrySetResult</c> 决定。</summary>
            internal readonly GatedTransition ProbeTransition = new GatedTransition();

            protected internal override IUITransition Transition => ProbeTransition;
        }

        /// <summary>闸门过渡：播放挂起在 <c>UniTaskCompletionSource</c> 上，落定由用例驱动。</summary>
        internal sealed class GatedTransition : IUITransition
        {
            /// <summary>走完闸门：用例 <c>TrySetResult</c> 之前播放一直挂起。</summary>
            internal readonly UniTaskCompletionSource Gate = new UniTaskCompletionSource();

            /// <summary>播放次数。</summary>
            internal int Plays;

            /// <summary>最近一次播放的方向（开窗为真）。</summary>
            internal bool LastOpen;

            /// <summary>Snap 的最近方向记录。</summary>
            internal int Snaps;

            public UniTask Play(bool open, CancellationToken cancellationToken)
            {
                Plays++;
                LastOpen = open;
                return Gate.Task;
            }

            public void Snap(bool open)
            {
                Snaps++;
            }
        }

        #endregion
    }
}
