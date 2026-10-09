using System.Collections.Generic;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 窗口自关策略的用例：等可交互、过 <see cref="UIWindow.CanClose"/> 门、落 <c>OnCloseFail</c>，以及 <c>ForceClose</c> 的即时旁路。
    /// </summary>
    /// <remarks>
    /// 已过交互位的用例走 <c>TryClose</c> 的同步段；未过的那一段挂在 PlayerLoop 上，EditMode 不驱动，因此只判「不立即结算」。 <br />
    /// 就绪回执后的开窗动画在 EditMode 不落定，窗口天然停在锁定位。线程契约：仅主线程。
    /// </remarks>
    [TestFixture]
    public sealed class UIWindowClosePolicyTests
    {
        private UIServiceHandler[] _savedEnabledHandlers;

        /// <summary>进门归位：处理器槽清回干净状态，启用清单按进门值快照。</summary>
        [SetUp]
        public void SetUp()
        {
            _savedEnabledHandlers = UIServiceSettings.EnabledHandlers;
            UIService.Internal_ResetHandlerSlots();
        }

        /// <summary>出门收尾：槽归位，启用清单交回进门值——清单住在设置资产那份实例上，是跨夹具的静态位。</summary>
        [TearDown]
        public void TearDown()
        {
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(_savedEnabledHandlers);
        }

        #region 自关 [SELF CLOSE]

        /// <summary>
        /// 已可交互且门开着：当场过门结算，等待段一次都不进。
        /// </summary>
        [Test]
        public void Close_AlreadyInteractableAndGateOpen_SettlesThroughTheGate()
        {
            EnableBothTracksAndInit();
            var window = Prepared("GateOpen", (int)EUILayer.UI, interactable: true);

            window.Close();

            Assert.IsNull(UIService.SharedLedger.GetWindow<ProbeClosePolicyWindow>("GateOpen"),
                "已可交互的窗口当场过门结算，不需要等帧");
            Assert.AreEqual(0, window.CloseFailCalls, "门是开的，不落 OnCloseFail");
        }

        /// <summary>
        /// 已可交互但门关着：不发关闭请求，窗留在栈上，<c>OnCloseFail</c> 收到那一轮。
        /// </summary>
        /// <remarks>把门判据写反或绕过门直接关，就红在「窗还在栈上」这一行。</remarks>
        [Test]
        public void Close_AlreadyInteractableAndGateClosed_CallsOnCloseFailAndKeepsWindow()
        {
            EnableBothTracksAndInit();
            var window = Prepared("GateClosed", (int)EUILayer.UI, interactable: true, canClose: false);

            window.Close();

            Assert.AreEqual(1, window.CloseFailCalls, "门为假的那一轮 OnCloseFail 恰好收到一次");
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeClosePolicyWindow>("GateClosed"),
                "门为假不得发关闭请求：窗留在栈上");
        }

        /// <summary>
        /// 还没可交互：改道等待，此刻既不结算也不碰门——真关由交互位让位那一帧收口，EditMode 不驱动那一帧。
        /// </summary>
        /// <remarks>把 <c>Close</c> 改成无条件立即结算就红在这里：被锁的窗会被当场弹出栈。</remarks>
        [Test]
        public void Close_NotYetInteractable_WaitsInsteadOfSettling()
        {
            EnableBothTracksAndInit();
            var window = Prepared("Locked", (int)EUILayer.UI, interactable: false);
            Assert.IsFalse(window.Interactable, "量具前提坏了：这一格判的就是锁着（交互位为假）的那一档");

            window.Close();

            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeClosePolicyWindow>("Locked"),
                "锁着的窗改道等待：此刻不得结算");
            Assert.AreEqual(0, window.CloseFailCalls, "还没过到门，OnCloseFail 不得被叫到");
        }

        #endregion

        #region 即时旁路 [IMMEDIATE BYPASS]

        /// <summary>
        /// <c>ForceClose</c> 不等交互位、也不过门：锁着的窗当场出栈，<c>OnCloseFail</c> 一次都不收。
        /// </summary>
        /// <remarks>它是覆写者跳过 <see cref="UIWindow.TryClose"/> 的唯一落点；摘掉这道旁路就红在这里。</remarks>
        [Test]
        public void ForceClose_NotYetInteractable_SettlesImmediatelyBypassingWaitAndGate()
        {
            EnableBothTracksAndInit();
            var window = Prepared("Forced", (int)EUILayer.UI, interactable: false);
            Assert.IsFalse(window.Interactable, "量具前提坏了：这一格判的就是绕过等待与门的那一档");

            window.CloseNow();

            Assert.IsNull(UIService.SharedLedger.GetWindow<ProbeClosePolicyWindow>("Forced"),
                "强制关当场结算：不等交互位让位");
            Assert.AreEqual(0, window.CloseFailCalls, "强制关不过门，也不会落 OnCloseFail");
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>按启用清单认领两支内建驱动者：关闭请求要经门面守卫（<c>IsValid</c>）才落进共享栈。</summary>
        private static void EnableBothTracksAndInit()
        {
            UIServiceSettings.Internal_SetEnabledHandlers(new UIServiceHandler[]
            {
                new UGUIHandler(),
                new UITKHandler(),
            });
            new UIService().OnInit();
        }

        /// <summary>
        /// 造一只走完「压栈→面板就绪→就绪回执」的探针窗，并按用例需要拨好交互位与门位。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="layer">窗口层级。</param>
        /// <param name="interactable">要拨到的交互位；就绪回执后的窗口天然停在假位上。</param>
        /// <param name="canClose">门位。</param>
        /// <returns>已就绪的探针窗。</returns>
        private static ProbeClosePolicyWindow Prepared(string windowName, int layer, bool interactable,
            bool canClose = true)
        {
            var ledger = UIService.SharedLedger;
            var window = new ProbeClosePolicyWindow
            {
                CanCloseFlag = canClose,
            };
            window.Init(windowName, layer, false, "Panel", false, 10);
            ledger.Push(window);
            window.InternalLoad("Panel", null, false);
            ledger.OnWindowPrepare(window);
            window.Interactable = interactable;
            return window;
        }

        /// <summary>关闭策略探针窗：门位与 <c>OnCloseFail</c> 计数由用例拨位，面板钩子一律只记账。</summary>
        private sealed class ProbeClosePolicyWindow : UIWindow
        {
            /// <summary>门位：交给 <see cref="CanClose"/> 读。</summary>
            internal bool CanCloseFlag = true;

            /// <summary><c>OnCloseFail</c> 被叫到的次数。</summary>
            internal int CloseFailCalls;

            /// <summary><c>ForceClose</c> 是 protected 的，用例经这一道 internal 接缝叫它。</summary>
            internal void CloseNow() => ForceClose();

            protected override bool CanClose => CanCloseFlag;

            protected override void OnCloseFail() => CloseFailCalls++;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }

            protected internal override void DestroyPanel() { }
        }

        #endregion
    }
}
