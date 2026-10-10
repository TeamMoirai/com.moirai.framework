using Moirai.Atropos.Input;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Moirai.Atropos.Tests.EditorMode;

namespace Service.UI
{
    [Window(EUILayer.Tips)]
    internal class CreateThrowProbeWindow : UGUIWindow
    {
        // 回滚同帧摘栈，栈上再拿不到这个窗，实例位只能由探针自己留
        internal static CreateThrowProbeWindow LastInstance;

        public CreateThrowProbeWindow()
        {
            LastInstance = this;
        }

        protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;
        protected override void OnCreate() => throw new System.InvalidOperationException("create probe");
    }

    [Window(EUILayer.Tips)]
    internal class RefreshThrowProbeWindow : UGUIWindow
    {
        protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;
        protected override void OnRefresh() => throw new System.InvalidOperationException("refresh probe");
    }

    [Window(EUILayer.Tips, cacheTimeToDestroy: -1f)]
    internal class CloseThrowProbeWindow : UGUIWindow
    {
        public int ParkCount;
        protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;
        protected override void OnClose() => throw new System.InvalidOperationException("close probe");
        protected internal override void ParkPanel() => ParkCount++;
    }

    [Window(EUILayer.Tips)]
    internal class DestroyThrowProbeWindow : UGUIWindow
    {
        public int DestroyPanelCount;
        protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;
        protected override void OnDestroy() => throw new System.InvalidOperationException("destroy probe");
        protected internal override void DestroyPanel() => DestroyPanelCount++;
    }

    [Window(EUILayer.Tips)]
    internal class CanCloseThrowProbeWindow : UGUIWindow
    {
        public int CloseFailCount;
        protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;
        protected override bool CanClose => throw new System.InvalidOperationException("canclose probe");
        protected override void OnCloseFail() => CloseFailCount++;
    }

    [Window(EUILayer.Tips)]
    internal class ApplyThrowProbeWindow : UGUIWindow
    {
        private int _applyVisibleCalls;

        protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

        protected internal override void ApplyVisible(bool value)
        {
            // 只抛首个：PanelLoaded 与 Visible setter 同帧各到一次，一条 Expect 只收一条 Error
            if (_applyVisibleCalls++ > 0) return;
            throw new System.InvalidOperationException("apply probe");
        }
    }

    /// <summary>N24b：OnRefresh/OnClose/OnDestroy/Apply* 抛 → Error 流程走完；CanClose 抛按拒关（fail-closed）。</summary>
    [TestFixture]
    public sealed class UILifecycleIsolationTests
    {
        private UIWindowLedger _ledger;

        [SetUp]
        public void SetUp() => _ledger = new UIWindowLedger();

        [TearDown]
        public void TearDown() => _ledger = null;

        [Test]
        public void OnRefreshThrows_WindowStillEntersStack()
        {
            UtfLogExpect.Error();
            _ledger.ShowUIImp(typeof(RefreshThrowProbeWindow), false, "RefreshThrow", null, UIPayload.Empty);

            Assert.IsTrue(_ledger.IsContains("RefreshThrow"), "OnRefresh 抛照常入栈");
        }

        [Test]
        public void OnCloseThrows_CloseFlowCompletes()
        {
            _ledger.ShowUIImp(typeof(CloseThrowProbeWindow), false, "CloseThrow", null, UIPayload.Empty);
            var window = (CloseThrowProbeWindow)_ledger.GetWindow("CloseThrow");

            UtfLogExpect.Error();
            Assert.DoesNotThrow(() => _ledger.CloseUI<CloseThrowProbeWindow>("CloseThrow"));
            Assert.IsTrue(_ledger.IsParked("CloseThrow"), "OnClose 抛不挡停放");
            Assert.AreEqual(1, window.ParkCount, "面板照常停放");
            Assert.AreEqual(0, _ledger.PeekStack().Count, "关窗流程走完");
        }

        [Test]
        public void OnDestroyThrows_DestroyFlowCompletes()
        {
            _ledger.ShowUIImp(typeof(DestroyThrowProbeWindow), false, "DestroyThrow", null, UIPayload.Empty);
            var window = (DestroyThrowProbeWindow)_ledger.GetWindow("DestroyThrow");

            UtfLogExpect.Error();
            Assert.DoesNotThrow(() => _ledger.CloseUI<DestroyThrowProbeWindow>("DestroyThrow"));
            Assert.AreEqual(1, window.DestroyPanelCount, "OnDestroy 抛不挡收面板");
            Assert.AreEqual(0, _ledger.PeekStack().Count, "销毁流程走完");
        }

        [Test]
        public void CanCloseThrows_TreatedAsRefuseClose()
        {
            _ledger.ShowUIImp(typeof(CanCloseThrowProbeWindow), false, "CanCloseThrow", null, UIPayload.Empty);
            var window = (CanCloseThrowProbeWindow)_ledger.GetWindow("CanCloseThrow");
            window.Interactable = true;

            UtfLogExpect.Error();
            window.TryClose().Forget();

            Assert.AreEqual(1, window.CloseFailCount, "CanClose 抛按拒关计并走 OnCloseFail");
            Assert.IsTrue(_ledger.IsContains("CanCloseThrow"), "拒关的窗留在栈上");
        }

        [Test]
        public void ApplyHookThrows_PanelLoadedCompletes()
        {
            UtfLogExpect.Error();
            Assert.DoesNotThrow(() =>
                _ledger.ShowUIImp(typeof(ApplyThrowProbeWindow), false, "ApplyThrow", null, UIPayload.Empty));

            var window = _ledger.GetWindow("ApplyThrow");
            Assert.IsNotNull(window, "Apply* 抛不挡入栈");
            Assert.IsTrue(window.IsPrepare, "装载结算照常落定");
        }
    }

    /// <summary>N24b 创建链：创建链抛 → 按装载失败回滚到显式失败态（摘栈、置失败位与作废位）。</summary>
    [TestFixture]
    public sealed class UILifecycleIsolationChainTests
    {
        private bool _savedPreventInteraction;
        private UIServiceHandler[] _savedEnabledHandlers;

        /// <summary>进门归位后装一支 uGUI 驱动者并走生产初始化：回滚收口要过 <see cref="UIService.IsValid"/> 那道守卫。</summary>
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

        [Test]
        public void CreateChainThrows_RollsBackToExplicitFailedState()
        {
            UtfLogExpect.Error();
            Assert.DoesNotThrow(() =>
                UIService.SharedLedger.ShowUIImp(typeof(CreateThrowProbeWindow), false, "CreateThrow", null, UIPayload.Empty),
                "创建链抛不得透出开窗调用");

            Assert.IsNull(UIService.SharedLedger.GetWindow("CreateThrow"), "失败窗不得留在栈上");
            Assert.IsTrue(CreateThrowProbeWindow.LastInstance.IsLoadFailed, "创建链抛按装载失败置失败位");
            Assert.IsTrue(CreateThrowProbeWindow.LastInstance.IsDestroyed, "创建链抛按装载失败置作废位");
        }
    }
}
