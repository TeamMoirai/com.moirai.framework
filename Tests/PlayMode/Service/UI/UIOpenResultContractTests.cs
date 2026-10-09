using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.UI;
using NUnit.Framework;
using Testing;
using UnityEngine.TestTools;

namespace Service.UI
{
    /// <summary>
    /// 开窗结果契约的跨帧档：超时、延迟失败与延迟就绪——这三档要真帧推进才落得定，EditMode 钉不了。
    /// </summary>
    /// <remarks>
    /// 播放态测试域框架自动 Boot，延迟失败与延迟就绪两格走门面生产入口，回滚与就绪都过真驱动者。 <br />
    /// Shown/Closed 的配对在这一侧判：门面的 <c>onWindowShown</c> / <c>onWindowClosed</c> 是静态广播， <br />
    /// 装载失败回滚补的那一次 Closed 只有这里看得见。 <br />
    /// 超时档不进栈、不经门面：直接对窗口调 <see cref="UIWindowLedger.WaitWindowResultAsync"/>，等待上限压到本格常量。
    /// </remarks>
    [TestFixture]
    public sealed class UIOpenResultContractTests
    {
        /// <summary>结果等待的测试上限（秒）：远低于生产档的 60 秒，只为本格落定。</summary>
        private const float TEST_TIMEOUT_SECONDS = 0.05f;

        private int _shownCount;
        private int _closedCount;

        [SetUp]
        public void SetUp()
        {
            _shownCount = 0;
            _closedCount = 0;
            UIService.onWindowShown += CountShown;
            UIService.onWindowClosed += CountClosed;
        }

        [TearDown]
        public void TearDown()
        {
            UIService.onWindowShown -= CountShown;
            UIService.onWindowClosed -= CountClosed;
            // 用例半途抛错时也把 DelayReady 留在栈上的窗收掉：共享栈要还给后面的夹具一个干净面
            if (UIService.IsValid && UIService.SharedLedger.GetWindow("DelayReady") != null)
            {
                UIService.CloseUI<DelayReadyProbeWindow>("DelayReady");
            }
        }

        private void CountShown(UIWindow window) => _shownCount++;

        private void CountClosed(UIWindow window) => _closedCount++;

        /// <summary>永不落定的装载：等待只能按超时收口，交回的仍是装载中的那一只。</summary>
        /// <remarks>等待走 awaiter 轮询：UniTask 单发，<c>ToCoroutine</c> 已消费 await，结果须在 <c>IsCompleted</c> 后取一次。</remarks>
        [UnityTest]
        public IEnumerator WaitWindowResult_NeverLoadingWindow_TimesOutWithTimeoutStatus()
        {
            var window = new NeverLoadProbeWindow();
            window.Init("NeverLoad", (int)EUILayer.Tips, false, "Where/NoPanel", false, 10);

            var awaiter = UIWindowLedger.WaitWindowResultAsync(window, "NeverLoad", TEST_TIMEOUT_SECONDS).GetAwaiter();
            UtfLogExpect.Warning();
            var frames = 0;
            while (!awaiter.IsCompleted && frames < 600)
            {
                frames++;
                yield return null;
            }

            Assert.IsTrue(awaiter.IsCompleted, "超时档等待要在帧上限内落定");
            var result = awaiter.GetResult();
            Assert.AreEqual(EUIOpenStatus.Timeout, result.Status, "永不落定的装载按超时档交回");
            Assert.IsFalse(result, "隐式布尔在超时档为假");
            Assert.AreSame(window, result.Window, "超时档交回仍在装载的那一只，供诊断与后续决策");
            Assert.IsFalse(window.IsLoadDone, "窗口本身仍未就绪");
        }

        /// <summary>延迟落定的装载失败：跨帧走完回滚链，结果按 Failed 档交回，Shown/Closed 配对平衡。</summary>
        [UnityTest]
        public IEnumerator ShowUIAwaitResult_DelayedLoadFailure_ReturnsFailedStatusAfterRollback()
        {
            Assert.IsTrue(UIService.IsValid, "量具前提：播放态域的自动 Boot 要已把 UI 服务立起来");

            UtfLogExpect.Error();
            var awaiter = UIService.ShowUIAwaitResult<DelayFailProbeWindow>("DelayFail").GetAwaiter();
            var frames = 0;
            while (!awaiter.IsCompleted && frames < 600)
            {
                frames++;
                yield return null;
            }

            Assert.IsTrue(awaiter.IsCompleted, "失败档等待要在帧上限内落定");
            var result = awaiter.GetResult();
            Assert.AreEqual(EUIOpenStatus.Failed, result.Status, "延迟落定的装载失败按 Failed 档交回");
            Assert.IsFalse(result, "隐式布尔在失败档为假");
            Assert.IsNull(UIService.SharedLedger.GetWindow("DelayFail"), "失败窗已从共享栈回滚");
            Assert.AreEqual(1, _shownCount, "压栈发过一次 Shown");
            Assert.AreEqual(1, _closedCount, "回滚补一次对称的 Closed，配对保持平衡");
        }

        /// <summary>延迟就绪的装载：按实际就绪帧落定 Opened 档，交回的就是栈上那一只。</summary>
        [UnityTest]
        public IEnumerator ShowUIAwaitResult_DelayedLoadReady_ReturnsOpenedStatusAndStaysOnStack()
        {
            Assert.IsTrue(UIService.IsValid, "量具前提：播放态域的自动 Boot 要已把 UI 服务立起来");

            var awaiter = UIService.ShowUIAwaitResult<DelayReadyProbeWindow>("DelayReady").GetAwaiter();
            var frames = 0;
            while (!awaiter.IsCompleted && frames < 600)
            {
                frames++;
                yield return null;
            }

            Assert.IsTrue(awaiter.IsCompleted, "就绪档等待要在帧上限内落定");
            var result = awaiter.GetResult();
            Assert.AreEqual(EUIOpenStatus.Opened, result.Status, "延迟就绪按 Opened 档交回");
            Assert.IsTrue(result, "隐式布尔在就绪档为真");
            Assert.AreSame(UIService.SharedLedger.GetWindow("DelayReady"), result.Window, "交回的就是栈上那一只");

            UIService.CloseUI<DelayReadyProbeWindow>("DelayReady");
        }

        #region 探针 [PROBES]

        /// <summary>异步装载两帧后失败的探针窗：失败跨帧才落定，回滚链在真驱动者下走全。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class DelayFailProbeWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => false;

            protected internal override async UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct)
            {
                await UniTask.DelayFrame(2, PlayerLoopTiming.Update, ct);
                return false;
            }
        }

        /// <summary>异步装载两帧后就绪的探针窗：就绪档按实际就绪帧落定。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class DelayReadyProbeWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override async UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct)
            {
                await UniTask.DelayFrame(2, PlayerLoopTiming.Update, ct);
                return true;
            }
        }

        /// <summary>异步装载永不落定的探针窗：不进栈、不走门面，只服务超时档。</summary>
        internal sealed class NeverLoadProbeWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => false;

            protected internal override async UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct)
            {
                await UniTask.WaitUntil(() => false);
                return false;
            }
        }

        #endregion
    }
}
