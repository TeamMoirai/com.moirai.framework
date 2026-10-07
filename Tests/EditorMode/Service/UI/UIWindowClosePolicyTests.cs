using System.Collections.Generic;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 窗口自关策略的用例：立即档默认语义不变、延后档按开关改道、<see cref="UIWindow.CanClose"/> 门与 <c>OnCloseFail</c> 落点。
    /// </summary>
    /// <remarks>
    /// 过门与落 <c>OnCloseFail</c> 的用例从「已可交互」起步走 <c>TryClose</c> 的同步段；等待段挂在 PlayerLoop 上，EditMode 不驱动，只判得到「不立即结算」。 <br />
    /// 就绪回执后的开窗动画在 EditMode 不落定，窗口因此天然停在锁定位。线程契约：仅主线程。
    /// </remarks>
    [TestFixture]
    public sealed class UIWindowClosePolicyTests
    {
        private UIServiceHandler[] _savedEnabledHandlers;

        /// <summary>进门归位：处理器槽清回「干净域」，启用清单按进门值快照。</summary>
        [SetUp]
        public void SetUp()
        {
            _savedEnabledHandlers = UIServiceSettings.EnabledHandlers;
            UIService.Internal_ResetHandlerSlots();
        }

        /// <summary>出门收尾：槽归位、启用清单交回进门值——清单住在设置资产那份实例上，是跨夹具的静态位。</summary>
        [TearDown]
        public void TearDown()
        {
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(_savedEnabledHandlers);
        }

        #region 立即档（默认） [IMMEDIATE PATH, DEFAULT]

        /// <summary>
        /// 默认立即结算：没覆写开关的窗口即使被锁着（开窗动画在途、交互位为假）也当场出栈——默认语义一枚都不动。
        /// </summary>
        /// <remarks>把 <c>Close</c> 的默认档改成「一律走延后」就红在这里：被锁的窗会停在栈上等一个 EditMode 里永不推进的循环。</remarks>
        [Test]
        public void Close_DeferFlagOffDefault_SettlesImmediatelyEvenWhileLocked()
        {
            EnableBothTracksAndInit();
            var window = Prepared("ImmediateClose", (int)UILayer.UI, defer: false, interactable: false);
            Assert.IsFalse(window.Interactable, "量具前提坏了：这一格判的就是锁着（交互位为假）的那一档");

            window.Close();

            Assert.IsNull(UIService.SharedLedger.GetWindow<ProbeClosePolicyWindow>("ImmediateClose"),
                "默认档立即结算：不等交互位让位");
            Assert.AreEqual(0, window.CloseFailCalls, "立即档不过 CanClose 门，也不会落 OnCloseFail");
        }

        #endregion

        #region 延后档 [DEFERRED PATH]

        /// <summary>
        /// 延后档已可交互时同步过门：开关为真的窗口在交互位为真时当场结算，等待段一次都不进。
        /// </summary>
        [Test]
        public void Close_DeferFlagOnAndInteractable_ClosesThroughTheGate()
        {
            EnableBothTracksAndInit();
            var window = Prepared("DeferredReady", (int)UILayer.UI, defer: true, interactable: true);

            window.Close();

            Assert.IsNull(UIService.SharedLedger.GetWindow<ProbeClosePolicyWindow>("DeferredReady"),
                "已可交互的延后窗当场过门结算，不需要等帧");
            Assert.AreEqual(0, window.CloseFailCalls, "门是开的，不落 OnCloseFail");
        }

        /// <summary>
        /// 延后档的 <see cref="UIWindow.CanClose"/> 门：门为假时窗留在栈上、<c>OnCloseFail</c> 收到那一轮。
        /// </summary>
        /// <remarks>门关着就不得发关闭请求——门判据写反或绕过门直接关，就红在「窗还在栈上」这一行。</remarks>
        [Test]
        public void Close_DeferFlagOnAndCanCloseFalse_CallsOnCloseFailAndKeepsWindow()
        {
            EnableBothTracksAndInit();
            var window = Prepared("DeferredGated", (int)UILayer.UI, defer: true, interactable: true, canClose: false);

            window.Close();

            Assert.AreEqual(1, window.CloseFailCalls, "门为假的那一轮 OnCloseFail 恰好收到一次");
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeClosePolicyWindow>("DeferredGated"),
                "门为假不得发关闭请求：窗留在栈上");
        }

        /// <summary>
        /// 延后档被锁着时不立即结算：<c>Close</c> 改道等待，栈上原样——真关由交互位让位那一帧收口，EditMode 不驱动那一帧。
        /// </summary>
        [Test]
        public void Close_DeferFlagOnWhileLocked_DoesNotSettleImmediately()
        {
            EnableBothTracksAndInit();
            var window = Prepared("DeferredLocked", (int)UILayer.UI, defer: true, interactable: false);
            Assert.IsFalse(window.Interactable, "量具前提坏了：这一格判的就是锁着的延后窗");

            window.Close();

            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeClosePolicyWindow>("DeferredLocked"),
                "锁着的延后窗改道等待：此刻不得结算");
            Assert.AreEqual(0, window.CloseFailCalls, "还没过到门，OnCloseFail 不得被叫到");
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
        /// 造一只走完「压栈→面板就绪→就绪回执」的关闭策略探针窗，并按用例需要拨好交互位。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="layer">窗口层级。</param>
        /// <param name="defer">延后开关位。</param>
        /// <param name="interactable">要拨到的交互位；就绪回执后的窗口天然停在假位上。</param>
        /// <param name="canClose">门位。</param>
        /// <returns>已就绪的探针窗。</returns>
        private static ProbeClosePolicyWindow Prepared(string windowName, int layer, bool defer, bool interactable,
            bool canClose = true)
        {
            var ledger = UIService.SharedLedger;
            var window = new ProbeClosePolicyWindow
            {
                Defer = defer,
                CanCloseFlag = canClose,
            };
            window.Init(windowName, layer, false, "Panel", false, 10, false);
            ledger.Push(window);
            window.InternalLoad("Panel", null, false, null);
            ledger.OnWindowPrepare(window);
            window.Interactable = interactable;
            return window;
        }

        /// <summary>关闭策略探针窗：开关、门与 <c>OnCloseFail</c> 都由用例拨位，面板钩子一律只记账。</summary>
        private sealed class ProbeClosePolicyWindow : UIWindow
        {
            /// <summary>延后开关位：交给 <see cref="DeferCloseUntilInteractable"/> 读。</summary>
            internal bool Defer;

            /// <summary>门位：交给 <see cref="CanClose"/> 读。</summary>
            internal bool CanCloseFlag = true;

            /// <summary><c>OnCloseFail</c> 被叫到的次数。</summary>
            internal int CloseFailCalls;

            protected override bool DeferCloseUntilInteractable => Defer;

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
