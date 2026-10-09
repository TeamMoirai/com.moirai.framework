using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 在飞合并与双源取消的用例：同一次装载只跑一遍、last-wins 只回执一次、取消档在同帧与超时档分得住。
    /// </summary>
    /// <remarks>
    /// 装载闸门用 <see cref="UniTaskCompletionSource{TResult}"/>：<c>TrySetResult</c>/<c>TrySetCanceled</c> 让续体当场落定，
    /// EditMode 不推帧也量得到（与 <c>UIWindowTransitionTests</c> 同一口径）。 <br />
    /// 等待者配平的判据档：<b>超时落定的等待者不从等待者计数里摘</b>（<c>WaitForPanelReady</c> 超时只回假、不叫 <c>Leave</c>），
    /// 「全员离场才掐断装载」只在每个已登记且可撤销的等待者都离场时才触发——本文件钉「全员离场才掐断」那一格，
    /// 「超时的那个不摘计数」那一半要真帧才落得定，交 <c>UIStackReentryTests.WaiterBalance_TimedOutWaiterDoesNotDecrement_LoadStillCompletes</c>。 <br />
    /// 档位区分走的是既有的 internal 接缝直调（<c>WaitWindowResultAsync</c> / <c>WaitForPanelReady</c> / <c>WaitPanelReadyAsync</c>）：
    /// <b>本文件只钉取消档</b>——预先撤销的调用方令牌在轮询首轮那道 <c>ThrowIfCancellationRequested</c> 即落定（排在任何 await 之前），同帧可观测。 <br />
    /// <b>浮点超时档在 EditMode 无从落定</b>（fix round 2 的前提更正，先前此处写的「<c>CancelAfter(TimeSpan.Zero)</c> 当场撤销」是假的）：
    /// <c>CancellationTokenSource.CancelAfter(TimeSpan.Zero)</c> 不是同步撤销，它只把计时器排成约 1 毫秒的线程池定时，
    /// 令牌在那一刻尚未撤销 ⇒ 等待腿走到 <c>await UniTask.Yield()</c>，而 EditMode 不推帧，<c>GetResult()</c> 便报「Not yet completed」。
    /// 超时档因此整半移去 PlayMode（<c>UIStackReentryTests</c> 两格：档位可分 + 超时不摘计数）。 <br />
    /// 直呼接缝的那一位等待者<b>没有 join</b>，故它离场只用于落定档位，不得拿来摘别人的计数：
    /// 「取消档」那一格因此走真腿（<c>ShowUIAwaitResultImp</c> 自己 join、自己摘），配平才自洽。 <br />
    /// 腿级交回物的<b>跨帧那一半</b>（等待腿的第一个 <c>UniTask.Yield</c> 排在令牌判定之前）在 EditMode 推不出来，
    /// 端到端的交回物与 <see cref="EUIOpenStatus.Opened"/> 落定由 PlayMode 那两侧（<c>UIOpenResultContractTests</c>、
    /// <c>UIStackReentryTests</c>）钉；本文件判的是同帧可观测的那一半：装载只跑一遍、只一个实例、取消档档位、等待者配平。 <br />
    /// 驱动者经生产入口认领（<c>UIService.OnInit</c>）：<see cref="UIWindow"/> 的失败回滚要过 <c>UIService.IsValid</c> 那道守卫才交进共享栈，
    /// 因此本文件直呼 <see cref="UIService.SharedLedger"/> 那一份，不自建协调者。线程契约：仅主线程。
    /// </remarks>
    [TestFixture]
    public sealed class UIFlightMergeTests
    {
        /// <summary>生产档的等待上限（秒）：这一格只用于「不该落进超时档」的那一侧，取值远大于用例寿命。</summary>
        private const float LONG_TIMEOUT_SECONDS = 100f;

        private UIServiceHandler[] _savedEnabledHandlers;
        private UIWindowLedger _ledger;

        /// <summary>进门归位后装一支 uGUI 驱动者并走生产初始化：回滚链要过 <c>UIService.IsValid</c> 那道守卫。</summary>
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
            // 收干净在飞的装载：关停轮连缓存窗也一并销毁，掐断仍在等闸门的取消源，不给下一轮留悬挂续体
            UIService.CloseAll(true);
            _ledger = null;
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(_savedEnabledHandlers);
        }

        #region 在飞合并 [IN-FLIGHT MERGE]

        /// <summary>合并档：同一次开窗的两次调用只跑一遍装载，一次回执看到的是最后一个载荷（last-wins）。</summary>
        [Test]
        public void Merge_TwoVoidShowsDuringFlight_LoadsOnceAndRefreshSeesLastPayload()
        {
            _ledger.ShowUIImp<string>(typeof(GatedLoadProbeWindow), true, "MergeVoid", false, null, "P1");
            var window = (GatedLoadProbeWindow)_ledger.GetWindow("MergeVoid");
            Assert.IsNotNull(window, "量具前提坏了：第一次调用把窗口压上了栈");
            Assert.AreEqual(1, window.LoadCalls, "量具前提坏了：装载已发起");
            Assert.IsFalse(window.IsLoadDone, "量具前提坏了：闸门未放，装载仍在途");

            _ledger.ShowUIImp<string>(typeof(GatedLoadProbeWindow), true, "MergeVoid", false, null, "P2");

            Assert.AreEqual(1, window.LoadCalls, "在飞合并：第二次调用不重开发装载");
            Assert.AreEqual(1, _ledger.PeekStack().Count, "同一次开窗只一个实例");
            Assert.AreEqual(0, window.RefreshCount, "装载未落定：就绪回执一次都没发");

            window.Gate.TrySetResult(true);

            Assert.IsTrue(window.IsLoadDone, "放闸即就绪");
            Assert.AreEqual(1, window.RefreshCount, "合并后的装载只回执一次");
            Assert.AreEqual("P2", window.RefreshPayload, "那一次回执看到的是最后一个载荷");
            Assert.AreSame(window, _ledger.GetWindow("MergeVoid"), "回执之后栈上仍是同一个");
        }

        /// <summary>等待合并档：两个结果腿排在同一份在飞装载上——不重开、不压第二个，终态是同一个实例的 <see cref="EUIOpenStatus.Opened"/>。</summary>
        [Test]
        public void Merge_TwoResultLegsDuringFlight_JoinTheSameSingleLoad()
        {
            _ledger.ShowUIImp<string>(typeof(GatedLoadProbeWindow), true, "MergeAwait", false, null, "P0");
            var window = (GatedLoadProbeWindow)_ledger.GetWindow("MergeAwait");

            var first = _ledger.ShowUIAwaitResultImp(typeof(GatedLoadProbeWindow), true, "MergeAwait", false, null,
                UIPayload.From("P1")).GetAwaiter();
            var second = _ledger.ShowUIAwaitResultImp(typeof(GatedLoadProbeWindow), true, "MergeAwait", false, null,
                UIPayload.From("P2")).GetAwaiter();

            Assert.AreEqual(1, window.LoadCalls, "两个等待腿都没重开发装载");
            Assert.AreEqual(1, _ledger.PeekStack().Count, "两个等待腿都没压第二个");
            Assert.IsFalse(first.IsCompleted, "在途装载：两个腿都还挂在等待里");
            Assert.IsFalse(second.IsCompleted, "同上");

            window.Gate.TrySetResult(true);

            Assert.IsTrue(window.IsLoadDone, "放闸即就绪");
            Assert.AreEqual(1, window.RefreshCount, "合并后仍只回执一次");
            Assert.AreEqual("P2", window.RefreshPayload, "last-wins：终载荷是最后一个");

            // 交回物的落定要过一轮帧（腿的第一个 await 是 UniTask.Yield），EditMode 推不出来：
            // 同一个实例的终态档位经既有接缝同帧读回，端到端的交回物由 PlayMode 那两侧钉。
            // 这里的 0f 上限只是把接缝叫起来取档位，量的是「已就绪」那一档：闸门刚放、IsLoadDone 已真，
            // 轮询首轮尚未 await 便回真 ⇒ 同帧落定，压根走不到 CancelAfter 的那个定时（浮点超时档在 EditMode 落不了地，见文件头 remarks）。
            var settled = UIWindowLedger.WaitWindowResultAsync(window, "MergeAwait", 0f).GetAwaiter().GetResult();
            Assert.AreEqual(EUIOpenStatus.Opened, settled.Status, "两个腿等的是同一份装载：终态 Opened");
            Assert.AreSame(window, settled.Window, "交回的是同一个实例");
        }

        #endregion

        #region 调用方取消 [CALLER CANCEL]

        /// <summary>void 腿的取消回滚：装载在途时调用方撤销即掐断装载、静默摘栈，不开半个窗也不报错误。</summary>
        [Test]
        public void VoidLeg_CancelDuringFlight_RollsBackSilentlyWithoutError()
        {
            using (var cts = new CancellationTokenSource())
            {
                _ledger.ShowUIImp(typeof(GatedLoadProbeWindow), true, "VoidCancel", false, null,
                    UIPayload.From("P"), cts.Token);
                var window = (GatedLoadProbeWindow)_ledger.GetWindow("VoidCancel");
                Assert.AreEqual(1, window.LoadCalls, "量具前提坏了：装载已在途");

                cts.Cancel();

                Assert.AreEqual(1, window.LoadCalls, "取消不重开发装载");
                Assert.IsNull(_ledger.GetWindow("VoidCancel"), "取消回滚：窗口摘出栈");
                Assert.AreEqual(0, _ledger.PeekStack().Count, "取消回滚：栈上不留半个窗");
                Assert.IsTrue(window.IsDestroyed, "取消回滚按作废落定");
                Assert.IsFalse(window.IsLoadDone, "作废的窗不得带就绪位");
            }
        }

        /// <summary>结果腿的取消档：调用方令牌已撤销时按 <see cref="EUIOpenStatus.Cancelled"/> 交回，不抛、不落超时档。</summary>
        [Test]
        public void ResultLeg_CanceledCallerToken_ReturnsCancelledWithoutThrowing()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                var result = _ledger.ShowUIAwaitResultImp(typeof(GatedLoadProbeWindow), true, "ResultCancel", false,
                    null, UIPayload.From("P"), cts.Token).GetAwaiter().GetResult();

                Assert.AreEqual(EUIOpenStatus.Cancelled, result.Status, "调用方撤销落 Cancelled 档");
                Assert.IsFalse(result, "取消档不是就绪成功");
                Assert.IsNull(_ledger.GetWindow("ResultCancel"), "取消回滚：窗口摘出栈");
                Assert.IsTrue(result.Window.IsDestroyed, "取消档交回的那个已作废");
            }
        }

        /// <summary>等待腿的取消档：调用方撤销原样上抛 <see cref="System.OperationCanceledException"/>，与超时分档不混言。</summary>
        /// <remarks>
        /// 判在等待腿真正吃的那两道接缝上（<c>WaitPanelReadyAsync</c> 的双源判定与 <c>WaitForPanelReady</c> 的分档转交）：
        /// 腿本体 <c>ShowUIAwaitImp</c> 的第一个 <c>await UniTask.Yield()</c> 排在令牌判定之前，EditMode 推不出那一帧。
        /// </remarks>
        [Test]
        public void AwaitLegWaiter_CanceledCallerToken_ThrowsOperationCanceledNotTimeout()
        {
            _ledger.ShowUIImp(typeof(GatedLoadProbeWindow), true, "AwaitCancel", false, null, UIPayload.From("P"));
            var window = (GatedLoadProbeWindow)_ledger.GetWindow("AwaitCancel");
            Assert.IsFalse(window.IsLoadDone, "量具前提坏了：装载在途");

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                Assert.Throws<OperationCanceledException>(() => window.WaitPanelReadyAsync(CancellationToken.None, cts.Token)
                    .GetAwaiter().GetResult(), "调用方撤销原样上抛，不被超时档吞掉");
                Assert.Throws<OperationCanceledException>(() => UIWindowLedger.WaitForPanelReady(window, LONG_TIMEOUT_SECONDS, cts.Token)
                    .GetAwaiter().GetResult(), "同一道判据在轮询侧也上抛，交回 Cancelled 档由结果腿那一道落");
            }

            window.Gate.TrySetResult(true);
            Assert.IsTrue(window.IsLoadDone, "未被掐断的在途装载照常落定");
        }

        /// <summary>取消档同帧落定：真腿吃一个已撤销的调用方令牌按 <see cref="EUIOpenStatus.Cancelled"/> 交回，且只摘自己那个计数。</summary>
        /// <remarks>
        /// 取消那一档由<b>真腿</b>发起（账本 <c>UIWindowLedger.ShowUIAwaitResultImp</c> 收一个已撤销的令牌）：
        /// 那一道自己 <c>Internal_JoinOpenWaiter</c> 一名、离场时摘的就是自己那个，等待者配平在这一条腿里自洽——
        /// 直呼 <c>WaitWindowResultAsync(…, callerCt)</c> 的那一位等待者从未 join，让它去摘开场那个计数是伪造配对
        /// （档位判据本身仍成立，掐断与摘栈那一半却量假了）。<br />
        /// 因此本格只判取消档<b>落得住</b>与其<b>配平后果</b>：开场那个等待者仍在场 ⇒ 装载不被半途掐断、不摘栈，闸门照旧能放。
        /// 「全员离场才掐断并摘栈」那一半由 <see cref="VoidLeg_CancelDuringFlight_RollsBackSilentlyWithoutError"/> 与
        /// <see cref="WaiterBalance_PartialCancelKeepsLoadAlive_AllCancelAbortsIt"/> 各自钉住（两格吃的都是真腿）。<br />
        /// <b>「Cancelled 与 Timeout 可分档」的 Timeout 那一半不在这里</b>（fix round 2 的射程切割）：
        /// <c>CancelAfter(TimeSpan.Zero)</c> 不是当场撤销，而是约 1 毫秒的线程池定时，浮点超时档在 EditMode 只会停在等待腿的
        /// <c>await UniTask.Yield()</c> 上推不动 ⇒ 改由 <c>UIStackReentryTests.WaitWindowResult_TimeoutAndCancelSettleAsDistinctStatusesAcrossFrames</c>
        /// 按真帧钉两档。这一侧仍可分的凭据剩两条：<b>本格的 Cancelled 档</b>与
        /// <see cref="AwaitLegWaiter_CanceledCallerToken_ThrowsOperationCanceledNotTimeout"/> 里那道预先撤销令牌的
        /// <c>WaitPanelReadyAsync</c>/<c>WaitForPanelReady</c> 接缝（判定排在任何 await 之前，同帧落定）。
        /// </remarks>
        [Test]
        public void WaitWindowResult_CancelledSettlesInEditModeTimeoutSettlesInPlayMode()
        {
            _ledger.ShowUIImp(typeof(GatedLoadProbeWindow), true, "StatusSplit", false, null, UIPayload.From("P"));
            var window = (GatedLoadProbeWindow)_ledger.GetWindow("StatusSplit");

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                var cancelled = _ledger.ShowUIAwaitResultImp(typeof(GatedLoadProbeWindow), true, "StatusSplit", false,
                    null, UIPayload.Empty, cts.Token).GetAwaiter().GetResult();

                Assert.AreEqual(EUIOpenStatus.Cancelled, cancelled.Status, "同一个在途装载的取消档：超时档另在 PlayMode 钉");
                Assert.AreSame(window, cancelled.Window, "取消档交回的就是那个仍在装载的窗");
            }

            Assert.AreEqual(1, window.LoadCalls, "取消档不重开发装载：复用支路只挪栈顶");
            Assert.IsFalse(window.IsDestroyed, "开场那个等待者仍在场：在途装载不被半途掐断");
            Assert.IsNotNull(_ledger.GetWindow("StatusSplit"), "还有等待者在场：取消档不摘栈（离场只摘自己那个计数）");

            window.Gate.TrySetResult(true);
            Assert.IsTrue(window.IsLoadDone, "取消档落定后在途装载照常完成");
        }

        #endregion

        #region 等待者配平 [WAITER BALANCE]

        /// <summary>全员离场才掐断：两个可撤销等待者只走掉一个时装载照常在途，第二个走掉才回滚。</summary>
        /// <remarks>
        /// 等待者配平的<b>另一半</b>——「超时落定的等待者不摘计数」——不在 EditMode（fix round 2）：
        /// 那一半要先看得到超时落定，而浮点 <c>timeoutSeconds</c> 走的是 <c>CancelAfter</c> 的线程池定时（约 1 毫秒即撤销的写法是假的），
        /// 不推帧就取不到那一档 → 改由 <c>UIStackReentryTests.WaiterBalance_TimedOutWaiterDoesNotDecrement_LoadStillCompletes</c> 按真帧钉。
        /// 本格的两个等待者都是<b>可撤销真腿</b>，全程不依赖定时器，同帧可观测。
        /// </remarks>
        [Test]
        public void WaiterBalance_PartialCancelKeepsLoadAlive_AllCancelAbortsIt()
        {
            using (var first = new CancellationTokenSource())
            using (var second = new CancellationTokenSource())
            {
                _ledger.ShowUIImp(typeof(GatedLoadProbeWindow), true, "WaiterTwo", false, null, UIPayload.Empty, first.Token);
                _ledger.ShowUIImp(typeof(GatedLoadProbeWindow), true, "WaiterTwo", false, null, UIPayload.Empty, second.Token);
                var window = (GatedLoadProbeWindow)_ledger.GetWindow("WaiterTwo");

                first.Cancel();

                Assert.AreEqual(1, window.LoadCalls, "等待者只剩一个：装载不重开也不掐断");
                Assert.IsFalse(window.IsDestroyed, "还有等待者在场：在途装载不得被半途作废");
                Assert.IsNotNull(_ledger.GetWindow("WaiterTwo"), "还有等待者在场：窗口仍在栈上");

                second.Cancel();

                Assert.IsNull(_ledger.GetWindow("WaiterTwo"), "全部等待者离场：装载掐断并回滚");
                Assert.IsTrue(window.IsDestroyed, "等待者归零即掐断在途装载，回滚按取消落定");
            }
        }

        /// <summary><c>default(ct)</c> 回归：不收令牌的腿照常走完，未被登记过的离场也不掐断在途装载。</summary>
        [Test]
        public void DefaultToken_LegsSettleNormallyAndUnregisteredLeaveIsNoOp()
        {
            _ledger.ShowUIImp(typeof(GatedLoadProbeWindow), true, "NoToken", false, null, UIPayload.From("P"));
            var window = (GatedLoadProbeWindow)_ledger.GetWindow("NoToken");

            window.Gate.TrySetResult(true);
            Assert.IsTrue(window.IsLoadDone, "default(ct) 那一档照常落定");
            Assert.AreEqual(1, window.RefreshCount, "照常回执一次");

            var opened = _ledger.ShowUIAwaitResultImp(typeof(GatedLoadProbeWindow), true, "NoToken", false, null,
                UIPayload.From("Q"), default).GetAwaiter().GetResult();
            Assert.AreEqual(EUIOpenStatus.Opened, opened.Status, "复用已就绪的窗：结果腿同帧落 Opened，不吃令牌");
            Assert.AreEqual("Q", window.Payload, "last-wins 在复用支路同样覆盖载荷");

            // 下溢地板：未登记等待者的离场（取窗腿那一条来路）不得把计数拖成负数误掐在途装载。
            // 这一档要一个「装载在途但一次都没登记过等待者」的窗，开栈腿都登记，故直呼其装载钩子造样本。
            var orphan = new GatedLoadProbeWindow();
            orphan.Init("NoWaiterRegistered", (int)EUILayer.Tips, false, "Panel", false, 10);
            _ledger.Push(orphan);
            orphan.InternalLoad("Panel", null, true).Forget();
            Assert.AreEqual(1, orphan.LoadCalls, "量具前提坏了：这一路没经开栈腿登记等待者，装载已在途");

            orphan.Internal_LeaveOpenWaiter();

            Assert.IsFalse(orphan.IsDestroyed, "离场空转：未登记过等待者的离场不掐在途装载");
            Assert.IsNotNull(_ledger.GetWindow("NoWaiterRegistered"), "离场空转：不摘栈");
            orphan.Gate.TrySetResult(true);
            Assert.IsTrue(orphan.IsLoadDone, "迟到的一个离场不影响装载落定");
        }

        #endregion

        #region 探针 [PROBES]

        /// <summary>可闸装载探针窗：异步装载挂在 <see cref="UniTaskCompletionSource{TResult}"/> 上，装载次数与回执载荷都记账。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class GatedLoadProbeWindow : UGUIWindow<string>
        {
            /// <summary>本窗最近一次装载的闸门：用例放闸或撤销都经它。</summary>
            internal UniTaskCompletionSource<bool> Gate;

            /// <summary>装载钩子被叫到的次数：在飞合并的判据。</summary>
            internal int LoadCalls;

            /// <summary>就绪回执被叫到的次数。</summary>
            internal int RefreshCount;

            /// <summary>回执那一刻读到的载荷。</summary>
            internal string RefreshPayload;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override async UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                CancellationToken ct)
            {
                LoadCalls++;
                Gate = new UniTaskCompletionSource<bool>();
                using (ct.Register(() => Gate.TrySetCanceled()))
                {
                    return await Gate.Task;
                }
            }

            protected override void OnRefresh()
            {
                RefreshCount++;
                RefreshPayload = Payload;
            }
        }

        #endregion
    }
}
