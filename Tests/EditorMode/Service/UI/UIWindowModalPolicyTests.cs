using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Input;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 模态档解耦的用例：显式 <c>[Window(modal:…)]</c> 赢过层级档，继承档仍按层级结算。
    /// </summary>
    /// <remarks>
    /// 判据三面都对上：压栈压下层交互位、租约占压制位、门面模态查询三处读同一枚结算位。 <br />
    /// attribute 实参禁 nullable，显式模态档由 <see cref="EUIModal"/> 三态表达。
    /// </remarks>
    [TestFixture]
    public sealed class UIWindowModalPolicyTests
    {
        private bool _savedPreventInteraction;
        private UIServiceHandler[] _savedEnabledHandlers;

        /// <summary>进门归位后装一支 uGUI 驱动者并走生产初始化：压栈与租约都走生产链。</summary>
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

        /// <summary>继承档（缺省）：层级档即模态档——Popup 层探针结算为模态。</summary>
        [Test]
        public void Modal_InheritByLayer_ResolvesFromWindowLayer()
        {
            var window = Prepared<InheritModalProbeWindow>("InheritModal");
            Assert.IsTrue(UIService.IsModal(window), "Popup 层的继承档按层级结算为模态");
            Assert.AreSame(window, UIService.CurrentModal, "栈上唯一且模态的那只窗，就是当前模态遮挡窗");
        }

        /// <summary>非模态层显式强制模态：压栈照样压下层交互位。</summary>
        [Test]
        public void Modal_ExplicitModalOnNonModalLayer_SuppressesWindowBelow()
        {
            var below = PlainWindow("BelowForced", (int)UILayer.Bottom);
            below.Interactable = true;
            UIService.SharedLedger.Push(below);

            var modal = Prepared<ForcedModalProbeWindow>("ForcedOnTips");

            Assert.IsTrue(UIService.IsModal(modal), "Tips 层显式 modal:Modal 结算为模态");
            Assert.IsFalse(below.Interactable, "显式模态压栈照样压下层交互位");
        }

        /// <summary>模态层显式强制非模态：不压下层、不进租约、门面答非模态。</summary>
        [Test]
        public void Modal_ExplicitNonModalOnModalLayer_DoesNotSuppressAnything()
        {
            var below = PlainWindow("BelowSpare", (int)UILayer.Bottom);
            below.Interactable = true;
            UIService.SharedLedger.Push(below);

            var spare = Prepared<ForcedNonModalProbeWindow>("SpareOnUI");

            Assert.IsFalse(UIService.IsModal(spare), "UI 层显式 modal:NonModal 结算为非模态");
            Assert.IsTrue(below.Interactable, "非模态窗压上去不动下层交互位");
            Assert.IsNull(UIService.CurrentModal, "强制非模态的窗不当任当前模态");
        }

        /// <summary>造一只直接压栈的普通窗（继承档、显隐不落地）。</summary>
        private static UIWindow PlainWindow(string windowName, int layer)
        {
            var window = new ForcedModalProbeWindow();
            window.Init(windowName, layer, false, "Panel", false, 10, false);
            return window;
        }

        /// <summary>经生产入口开一只注册表探针窗（同步装载就绪）。</summary>
        private static T Prepared<T>(string windowName) where T : UIWindow, new()
        {
            var ledger = UIService.SharedLedger;
            ledger.ShowUIImp(typeof(T), false, windowName, "Panel", false, null);
            var window = ledger.GetWindow(windowName);
            Assert.IsNotNull(window, "量具前提坏了：探针窗要开出");
            Assert.IsTrue(window.IsLoadDone, "量具前提坏了：探针窗要同步装载就绪");
            return (T)window;
        }

        #region 探针 [PROBES]

        /// <summary>POPUP 层继承档探针窗：模态档缺省，按层级结算。</summary>
        [Window(UILayer.Popup)]
        internal sealed class InheritModalProbeWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }
        }

        /// <summary>非模态层强制模态的探针窗：装载成功、面板钩子只记账。</summary>
        [Window(UILayer.Tips, modal: EUIModal.Modal)]
        internal sealed class ForcedModalProbeWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }
        }

        /// <summary>模态层强制非模态的探针窗：同上记法。</summary>
        [Window(UILayer.UI, modal: EUIModal.NonModal)]
        internal sealed class ForcedNonModalProbeWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }
        }

        #endregion
    }
}
