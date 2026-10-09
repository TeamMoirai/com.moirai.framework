using Cysharp.Threading.Tasks;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 导航面（开启序历史）的用例：深度可数、取最近开的那个走既有关闭政策、拒关不动历史、重开把它挪成最新。
    /// </summary>
    /// <remarks>
    /// 历史与栈是两份序：栈按层级排（答不出「最近开的是谁」），历史按开启序排——本文件的判据正是两者的分歧档：
    /// 层级最高的那个不是最近开的那个时，<see cref="UIService.TryCloseTopWindow"/> 关的必须是最近开的那个。 <br />
    /// 探针一律同步装载（<c>ShowUI&lt;T&gt;</c> 在编辑器那一档走同步钩子）：
    /// 只有真落到栈上的窗才进得了历史，导航的判据才有主语；层级取非模态与模态两档，模态那一档另证「压层不改开启序」。 <br />
    /// 拒关探针把 <c>CanClose</c> 写成假：<see cref="UIService.TryCloseTopWindow"/> 回假且历史不出栈（政策拒关不是空操作）。
    /// 驱动者经生产入口认领：<c>TryClose</c> 末尾的 <c>ForceClose</c> 要过 <c>UIService.IsValid</c> 那道守卫才结算共享栈。
    /// 线程契约：仅主线程。
    /// </remarks>
    [TestFixture]
    public sealed class UINavigationTests
    {
        private UIServiceHandler[] _savedEnabledHandlers;
        private UIWindowLedger _ledger;

        /// <summary>进门归位后装一支 uGUI 驱动者并走生产初始化：关闭结算与历史都要落在那一份共享栈上。</summary>
        [SetUp]
        public void SetUp()
        {
            _savedEnabledHandlers = UIServiceSettings.EnabledHandlers;
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(new UIServiceHandler[] { new UGUIHandler() });
            new UIService().OnInit();
            _ledger = UIService.SharedLedger;
        }

        [TearDown]
        public void TearDown()
        {
            UIService.CloseAll(true);
            _ledger = null;
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(_savedEnabledHandlers);
        }

        [Test]
        public void NavigationDepth_EmptyHistory_IsZeroAndTopCloseFails()
        {
            Assert.AreEqual(0, UIService.NavigationDepth, "干净栈的历史长度为零");
            Assert.IsFalse(UIService.TryCloseTopWindow(), "无历史时取最近开的那个回假，不空转关闭");
        }

        [Test]
        public void NavigationDepth_ThreeOpens_CountsThree()
        {
            UIService.ShowUI<NavBottomWindow>("NavA");
            UIService.ShowUI<NavTipsWindow>("NavB");
            UIService.ShowUI<NavBottomWindow>("NavC");

            Assert.AreEqual(3, UIService.NavigationDepth, "开启序历史按开窗次数计");
            Assert.AreEqual(3, _ledger.PeekStack().Count, "历史与栈同数：三个都落进了共享栈");
        }

        /// <summary>取最近开的那个而不是层级最高的那个：栈顶由层级排，历史顶由开启序排，两者分歧时以历史为准。</summary>
        [Test]
        public void TryCloseTopWindow_LayerOrderDiffersFromOpenOrder_ClosesMostRecentWindow()
        {
            UIService.ShowUI<NavBottomWindow>("NavA");
            UIService.ShowUI<NavTipsWindow>("NavB");
            UIService.ShowUI<NavBottomWindow>("NavC");

            Assert.AreSame(_ledger.GetWindow("NavB"), UIService.GetTopWindow(),
                "量具前提坏了：栈顶按层级是那个 Tips 层的窗");

            Assert.IsTrue(UIService.TryCloseTopWindow(), "最近开的那个可关");
            Assert.IsNull(UIService.GetWindow<NavBottomWindow>("NavC"), "关的是最近开的 NavC，不是层级最高的 NavB");
            Assert.AreEqual(2, UIService.NavigationDepth, "摘栈即出历史");
            Assert.IsNotNull(UIService.GetWindow<NavTipsWindow>("NavB"), "NavB 仍在栈上");

            Assert.IsTrue(UIService.TryCloseTopWindow(), "第二轮照常取到最近的那个");
            Assert.IsNull(UIService.GetWindow<NavTipsWindow>("NavB"), "开启序倒着退：第二轮收 NavB");
            Assert.IsTrue(UIService.TryCloseTopWindow(), "第三轮收 NavA");
            Assert.AreEqual(0, UIService.NavigationDepth, "历史退空");
        }

        [Test]
        public void TryCloseTopWindow_RefusedByPolicy_ReturnsFalseAndKeepsDepth()
        {
            UIService.ShowUI<NavBottomWindow>("NavRBelow");
            UIService.ShowUI<NavRefuseWindow>("NavRefuse");
            var depth = UIService.NavigationDepth;

            Assert.IsFalse(UIService.TryCloseTopWindow(), "政策拒关：回假且不代答成功");
            Assert.AreEqual(depth, UIService.NavigationDepth, "拒关那一轮历史不出栈");
            Assert.IsNotNull(UIService.GetWindow<NavRefuseWindow>("NavRefuse"), "拒关的窗仍在栈上");

            UIService.CloseUI<NavRefuseWindow>("NavRefuse");
            Assert.AreEqual(depth - 1, UIService.NavigationDepth,
                "绕开政策的 CloseUI 仍照常摘历史：拒关只挡 TryCloseTopWindow 这一道");
        }

        /// <summary>模态窗后开压层不改开启序：模态那一档压的是下层交互位与栈序，历史仍认「最后开的」。</summary>
        [Test]
        public void TryCloseTopWindow_ModalOpenedLast_StaysNewestWithoutScramblingOpenOrder()
        {
            UIService.ShowUI<NavTipsWindow>("NavKitHi");
            UIService.ShowUI<NavModalWindow>("NavModalLast");

            Assert.AreEqual(2, UIService.NavigationDepth, "模态窗后开：历史长度两格");
            Assert.AreSame(_ledger.GetWindow("NavKitHi"), UIService.GetTopWindow(),
                "量具前提坏了：栈顶是那个层级更高的非模态窗");

            Assert.IsTrue(UIService.TryCloseTopWindow(), "取最近开的那个：正是后开的模态窗");
            Assert.IsNull(UIService.GetWindow<NavModalWindow>("NavModalLast"), "关掉的必须是后开的模态窗");
            Assert.IsNotNull(UIService.GetWindow<NavTipsWindow>("NavKitHi"), "层级更高的那个没被代关");
            Assert.AreEqual(1, UIService.NavigationDepth, "摘一个即少一格");
        }

        [Test]
        public void TryCloseTopWindow_ReopenedWindow_IsNewestAgain()
        {
            UIService.ShowUI<NavBottomWindow>("NavReA");
            UIService.ShowUI<NavBottomWindow>("NavReB");
            Assert.AreEqual(2, UIService.NavigationDepth, "量具前提坏了：先开 A 再开 B");

            UIService.ShowUI<NavBottomWindow>("NavReA");

            Assert.AreEqual(2, UIService.NavigationDepth, "复用支路走 Pop→Push：历史长度不变");
            Assert.AreEqual(2, _ledger.PeekStack().Count, "复用不压第二个");
            Assert.IsTrue(UIService.TryCloseTopWindow(), "重开之后取到的最近是那个");
            Assert.IsNull(UIService.GetWindow<NavBottomWindow>("NavReA"), "重开的 NavReA 被挪成最新，先被关掉");
            Assert.IsNotNull(UIService.GetWindow<NavBottomWindow>("NavReB"), "NavReB 回到最新，仍留在栈上");
        }

        #region 探针 [PROBES]

        /// <summary><see cref="EUILayer.Bottom"/> 层探针窗：同步装载、面板留空，导航判据的主语。</summary>
        [Window(EUILayer.Bottom)]
        internal sealed class NavBottomWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;
        }

        /// <summary><see cref="EUILayer.Tips"/> 层探针窗：层级比 <see cref="NavBottomWindow"/> 高，用来造「栈顶≠历史顶」的分歧档。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class NavTipsWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;
        }

        /// <summary>模态层探针窗（<see cref="EUILayer.Popup"/> 按层级继承即模态）：后开时压层，不得将开启序打乱。</summary>
        [Window(EUILayer.Popup)]
        internal sealed class NavModalWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;
        }

        /// <summary>拒关探针窗：<c>CanClose</c> 恒假，取最近开的那个时政策初筛即回假。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class NavRefuseWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected override bool CanClose => false;
        }

        #endregion
    }
}
