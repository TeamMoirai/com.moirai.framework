using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.UI;
using NUnit.Framework;
using Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.UI
{
    /// <summary>
    /// 栈回入交叉的播放态用例：关闭回叫里开窗（载荷直达、事件不丢）、在飞关开同帧交叉（栈不裂、终载荷落定），
    /// 以及只有真帧才落得定的超时档（<c>Cancelled</c> 与 <c>Timeout</c> 可分、超时落定的等待者不摘计数）。
    /// </summary>
    /// <remarks>
    /// 运行前提：播放态测试域里框架已自动 Boot，本文件的开窗一律走门面 <see cref="UIService"/> 落到那一份生产协调者，
    /// 夹具不自建协调者——回叫链与在飞回滚都要在真驱动者下走全，替身答不了这一档。 <br />
    /// 探针窗的面板是代码建出的 <see cref="Canvas"/> 物体（不新增资产），交 <c>BindPanel</c> 装配；壳登记进清理表，
    /// 出门按 <c>Object.DestroyImmediate</c> 收（帧末落账的 <c>Object.Destroy</c> 在 teardown 里来不及）。 <br />
    /// 在飞档的闸门用 <see cref="UniTaskCompletionSource{TResult}"/>：放闸让装载续体当场落定，取消则随装载令牌一起撤销。 <br />
    /// 事件记账订的是门面的 <see cref="UIService.onWindowShown"/> / <see cref="UIService.onWindowClosed"/> 两枚静态广播，模式与窗口名一起落在列表里；
    /// 回叫（A 的 <c>OnClose</c>）里开的窗其 <c>Shown</c> 排在结算侧的 <c>Closed</c> 之前——这是账本 <c>CloseUI</c> 的现行次序
    /// （<c>InternalClose</c> 的回叫跑在 <c>Pop</c> 之前），本文件按现场次序钉「两枚各发一次、都不丢」这一半。 <br />
    /// <b>浮点超时档归本文件管</b>（fix round 2 的 R2-timeout 裁定）：<c>CancellationTokenSource.CancelAfter</c> 排的是线程池定时
    /// （<c>TimeSpan.Zero</c> 也只是约 1 毫秒的定时，不是当场撤销），等待腿必然先越过一枚 <c>UniTask.Yield</c> 才读得到撤销位
    /// ⇒ 这一档在 EditMode 推不出来，「Cancelled 与 Timeout 可分档」与「超时落定的等待者不摘计数」两半都改由这里按真帧钉
    /// （region 超时与取消分档，超时那一枚直呼账本接缝 <c>UIWindowLedger.WaitWindowResultAsync</c>，上限压到本格常量）。 <br />
    /// 帧推进口径：需要等条件走带超时上界的 <see cref="PumpUntil"/>。线程契约：仅主线程。
    /// </remarks>
    [TestFixture]
    public sealed class UIStackReentryTests
    {
        /// <summary>回叫里给 B 的那一枚载荷的字面量：编译期常量，intern 之后同一份文本全场只一枚对象。</summary>
        private const string ReentryPayloadText = "payload-from-A-onclose";

        /// <summary>
        /// 回叫里给 B 的那一枚载荷：<b>运行期现建</b>的 <see cref="string"/> 实例（<c>new string(char[])</c> 不经 intern 表）。
        /// </summary>
        /// <remarks>
        /// 载荷直达的判据是 <c>AreSame</c>，拿 intern 过的字面量比等于白比——任何「按值重造一份」的实装都能蒙过它；
        /// 换成运行期实例后，引用没原样传下去就当场红。跨用例不归零：这一份实例全程只读。
        /// </remarks>
        private static readonly string ReentryPayload = new string(ReentryPayloadText.ToCharArray());

        /// <summary>帧推进的等待上界（秒）：超过即把现场交回用例判红，不写无限等。</summary>
        private const float PUMP_TIMEOUT_SECONDS = 5f;

        /// <summary>
        /// 超时档的测试上限（秒）：远低于生产档的 <c>LOAD_WAIT_TIMEOUT_SECONDS</c>（60 秒），只为本格在帧上落进 <see cref="EUIOpenStatus.Timeout"/>。
        /// </summary>
        /// <remarks>取正数而非 0：<c>CancelAfter(TimeSpan.Zero)</c> 是约 1 毫秒的线程池定时，量不到「当场撤销」那一档，写 0 只会让判据含糊。</remarks>
        private const float TIMEOUT_UNDER_TEST_SECONDS = 0.2f;

        /// <summary>记账里入栈回执的模式前缀：与出栈那一枚合成「模式:窗口名」的键。</summary>
        private const string ShownMode = "Shown";

        /// <summary>记账里出栈回执的模式前缀。</summary>
        private const string ClosedMode = "Closed";

        private readonly List<GameObject> _trackedShells = new List<GameObject>();
        private readonly List<string> _eventLog = new List<string>();
        private UIWindowLedger _ledger;

        [SetUp]
        public void SetUp()
        {
            Assert.IsTrue(UIService.IsValid, "量具前提坏了：播放态框架没把 UI 服务立起来，开窗腿拿不到共享栈");
            _ledger = UIService.SharedLedger;
            Assert.AreEqual(0, _ledger.PeekStack().Count, "量具前提坏了：进门时共享栈上不干净");
            ReentryAWindow.OpenedByCallback = false;
            _eventLog.Clear();
            UIService.onWindowShown += OnWindowShown;
            UIService.onWindowClosed += OnWindowClosed;
        }

        [TearDown]
        public void TearDown()
        {
            UIService.onWindowShown -= OnWindowShown;
            UIService.onWindowClosed -= OnWindowClosed;
            UIService.CloseAll(true);

            for (var i = 0; i < _trackedShells.Count; i++)
            {
                if (_trackedShells[i] != null)
                {
                    Object.DestroyImmediate(_trackedShells[i]);
                }
            }

            _trackedShells.Clear();
            _eventLog.Clear();
            _ledger = null;
        }

        #region 关闭回叫里开窗 [OPEN FROM ONCLOSE]

        /// <summary>
        /// 关闭回叫里开窗：B 落进栈且走完装载、载荷按引用直达、两支回执都不丢。
        /// </summary>
        /// <remarks>
        /// A 是缓存窗（关闭走 <c>InternalClose</c> ⇒ <c>OnClose</c> 回叫），B 走异步腿：就绪要过一轮帧，判据在 <see cref="PumpUntil"/> 之后取。 <br />
        /// 次序钉现行为：账本 <c>CloseUI</c> 把回叫排在 <c>Pop</c> 之前，故 B 的 <c>Shown</c> 落在 A 的 <c>Closed</c> 之前——
        /// 这一格真正守的是「事件不丢 + 各发一次 + 载荷直达」，次序档留档供契约裁定（见 task-7c-report）。
        /// </remarks>
        [UnityTest]
        public IEnumerator CloseCallbackOpensPayloadWindow_LandsPreparedWithSamePayloadAndNoLostEvent()
        {
            UIService.ShowUI<ReentryAWindow>("ReentryA", "ReentryA");
            var a = UIService.GetWindow<ReentryAWindow>("ReentryA");
            Assert.IsNotNull(a, "量具前提坏了：A 已开进栈");
            Assert.IsTrue(a.IsPrepare, "量具前提坏了：同步装载的 A 当场就绪");
            Track(a);
            _eventLog.Clear();

            UIService.CloseUI<ReentryAWindow>("ReentryA");

            Assert.IsTrue(ReentryAWindow.OpenedByCallback, "回叫确实开出了 B：OnClose 里的那一次调用落进了账本");

            yield return PumpUntil(() =>
            {
                var b = UIService.GetWindow<ReentryBWindow>("ReentryB");
                return b != null && b.IsPrepare;
            }, PUMP_TIMEOUT_SECONDS);

            var b = UIService.GetWindow<ReentryBWindow>("ReentryB");
            Track(b);
            Assert.IsNotNull(b, "回叫里开的 B 在栈上：关闭链里的开窗不被吞");
            Assert.IsTrue(b.IsPrepare, "B 走完装载：回叫里的异步腿在真帧下落定");
            Assert.AreSame(ReentryPayload, b.Payload, "载荷按引用直达 B 的槽：吃的是运行期现建的那一枚实例，intern 字面量蒙不过这一句");
            Assert.GreaterOrEqual(b.RefreshCount, 1, "B 至少回执过一次（A 摘栈后新栈顶还会再补一次，不数死次数）");

            Assert.AreEqual(1, CountEvent(_eventLog, ShownMode, "ReentryB"), "B 的 Shown 发过一次：事件不丢");
            Assert.AreEqual(1, CountEvent(_eventLog, ClosedMode, "ReentryA"), "A 的 Closed 发过一次：事件不丢");
            Assert.Greater(IndexOfEvent(_eventLog, ClosedMode, "ReentryA"),
                IndexOfEvent(_eventLog, ShownMode, "ReentryB"),
                "现行为：回叫里的开窗（B 的 Shown）排在结算（A 的 Closed）之前");
            Assert.IsNull(UIService.GetWindow<ReentryAWindow>("ReentryA"), "A 已摘栈");
            Assert.IsTrue(_ledger.IsParked("ReentryA"), "A 是缓存窗：关闭后落进停放表");

            yield return null;
        }

        #endregion

        #region 在飞关开交叉 [CLOSE-AND-REOPEN DURING FLIGHT]

        /// <summary>
        /// 同帧「关在途装载的窗 + 再以新载荷开同名窗」：栈不裂、最终一只实例、回执只见终载荷。
        /// </summary>
        [UnityTest]
        public IEnumerator CloseAndReopenDuringGatedFlight_SameFrame_LeavesSingleInstanceWithFinalPayload()
        {
            UIService.ShowUIAsync<CrossFlightProbeWindow, string>("P1", "CrossFlight", "CrossFlight");
            var first = UIService.GetWindow<CrossFlightProbeWindow>("CrossFlight");
            Assert.IsNotNull(first, "量具前提坏了：第一次调用把窗口压上了栈");
            Assert.AreEqual(1, first.LoadCalls, "量具前提坏了：装载已发起");
            Assert.IsFalse(first.IsLoadDone, "量具前提坏了：闸门未放，装载在途");

            UIService.CloseUI<CrossFlightProbeWindow>("CrossFlight");
            UIService.ShowUIAsync<CrossFlightProbeWindow, string>("P2", "CrossFlight", "CrossFlight");

            var second = UIService.GetWindow<CrossFlightProbeWindow>("CrossFlight");
            Track(second);
            Assert.IsNotNull(second, "同帧再开：栈上有同名的这一只");
            Assert.AreNotSame(first, second, "第一次那只已随取消作废：再开交回的是新实例");
            Assert.IsFalse(first.IsLoadDone, "作废的窗不得带就绪位");
            Assert.AreEqual(1, CountNamed("CrossFlight"), "栈不裂：同名只有一只");
            Assert.AreEqual(1, _ledger.PeekStack().Count, "栈不裂：整条栈上只这一只");

            second.Gate.TrySetResult(true);

            Assert.IsTrue(second.IsLoadDone, "放闸后新实例照常落定");
            Assert.AreEqual(1, second.RefreshCount, "终载荷那只窗的回执恰好一次");
            Assert.AreEqual("P2", second.RefreshPayload, "OnRefresh 只见终载荷");
            Assert.AreSame(second, UIService.GetWindow<CrossFlightProbeWindow>("CrossFlight"), "栈上仍是那一只实例");

            yield return null;
        }

        #endregion

        #region 超时与取消分档 [TIMEOUT VS CANCELLED]

        /// <summary>档位可分（跨帧）：同一只在途装载，浮点超时上限落 <see cref="EUIOpenStatus.Timeout"/>，调用方撤销落 <see cref="EUIOpenStatus.Cancelled"/>。</summary>
        /// <remarks>
        /// 这一档整半从 EditMode 的 <c>UIFlightMergeTests</c> 搬来（fix round 2 的 R2-timeout）：EditMode 只能同帧取取消档
        /// （预先撤销的令牌排在任何 await 之前的那道 <c>ThrowIfCancellationRequested</c> 上），浮点超时档要真帧才看得见。 <br />
        /// 超时那一枚直呼账本接缝（<c>WaitWindowResultAsync</c>，它<b>不 join</b> 等待者，故也不摘任何计数）；
        /// 取消那一枚走门面结果腿（<c>ShowUIAwaitResult</c>，自己 join 一名、离场摘的就是自己那一枚）。
        /// 另有一枚撑场的可撤销真腿（keeper）先登记着，判的是「两档都不把在途装载半途掐断、都不摘栈」；
        /// 「超时的那一枚不代答离场」那一半由 <see cref="WaiterBalance_TimedOutWaiterDoesNotDecrement_LoadStillCompletes"/> 专门钉。 <br />
        /// 超时那条 Warning 经 <c>UtfLogExpect.Warning()</c> 声明（PlayMode 程序集本地副本，与 <c>UIOpenResultContractTests</c> 同一口径）。
        /// </remarks>
        [UnityTest]
        public IEnumerator WaitWindowResult_TimeoutAndCancelSettleAsDistinctStatusesAcrossFrames()
        {
            using (var keeper = new CancellationTokenSource())
            {
                UIService.ShowUIAsync<CrossFlightProbeWindow, string>("P", "StatusSplitPM", "StatusSplitPM", false, keeper.Token);
                var window = UIService.GetWindow<CrossFlightProbeWindow>("StatusSplitPM");
                Track(window);
                Assert.IsNotNull(window, "量具前提坏了：门面腿把窗口压上了栈");
                Assert.AreEqual(1, window.LoadCalls, "量具前提坏了：装载已发起");
                Assert.IsFalse(window.IsLoadDone, "量具前提坏了：闸门未放，装载在途");

                UtfLogExpect.Warning();
                var timeoutAwaiter = UIWindowLedger.WaitWindowResultAsync(window, "StatusSplitPM", TIMEOUT_UNDER_TEST_SECONDS).GetAwaiter();
                yield return PumpUntil(() => timeoutAwaiter.IsCompleted, PUMP_TIMEOUT_SECONDS);
                Assert.IsTrue(timeoutAwaiter.IsCompleted, "超时档要在帧上界内落定（线程池定时到点即由轮询那一格读出撤销位）");

                var timedOut = timeoutAwaiter.GetResult();
                Assert.AreEqual(EUIOpenStatus.Timeout, timedOut.Status, "上限到点交回仍在装载的那一只：超时档");
                Assert.AreSame(window, timedOut.Window, "超时档交回仍在装载的那一只");
                Assert.IsFalse(timedOut, "隐式布尔在超时档为假");
                Assert.IsFalse(window.IsDestroyed, "超时档不作废窗口");

                using (var caller = new CancellationTokenSource())
                {
                    caller.Cancel();

                    var cancelled = UIService.ShowUIAwaitResult<CrossFlightProbeWindow, string>("Q", "StatusSplitPM", "StatusSplitPM", false, caller.Token)
                        .GetAwaiter().GetResult();

                    Assert.AreEqual(EUIOpenStatus.Cancelled, cancelled.Status, "同一只在途装载的取消档");
                    Assert.AreSame(window, cancelled.Window, "取消档交回的就是那一只仍在装载的窗");
                    Assert.AreNotEqual(timedOut.Status, cancelled.Status, "两档各落各的：Timeout 与 Cancelled 分得住");
                }

                Assert.AreEqual(1, window.LoadCalls, "两档都不重开发装载：复用支路只挪栈顶");
                Assert.IsFalse(window.IsLoadDone, "两档都没掐断在途装载：闸门仍挂着");
                Assert.IsFalse(window.IsDestroyed, "keeper 那一枚等待者在场：装载不被半途作废");
                Assert.IsNotNull(UIService.SharedLedger.GetWindow("StatusSplitPM"), "两档都不摘栈：还有等待者在场");

                window.Gate.TrySetResult(true);
                yield return PumpUntil(() => window.IsLoadDone, PUMP_TIMEOUT_SECONDS);
                Assert.IsTrue(window.IsLoadDone, "两档都落定后在途装载照常完成");
            }
        }

        /// <summary>超时落定的等待者不摘计数：超时档之后装载仍在途，由在场的那枚可撤销真腿撑着，闸门照旧能放、窗照常完成。</summary>
        /// <remarks>
        /// 判据落在超时落定那一刻：账本 <c>WaitForPanelReady</c> 的超时分支只回假、不叫 <c>Internal_LeaveOpenWaiter</c>。
        /// 若它代答离场，场上唯一那枚已登记的可撤销等待者计数即归零 ⇒ <c>CancelLoadCts</c> 掐断装载 ⇒ <c>RollbackFailedLoad</c> 摘栈作废，
        /// 下面那三句「不作废 / 不摘栈 / 仍在途」当场红——这一半要看得见超时落定，故随 R2-timeout 从 EditMode 搬来。 <br />
        /// 迟到取消那一半（就绪后 <c>SettleOpenWaiters</c> 已清账、离场是空操作）与搬来前同一格同判据，照旧钉在末段。
        /// </remarks>
        [UnityTest]
        public IEnumerator WaiterBalance_TimedOutWaiterDoesNotDecrement_LoadStillCompletes()
        {
            using (var cts = new CancellationTokenSource())
            {
                UIService.ShowUIAsync<CrossFlightProbeWindow, string>("P", "TimedOutWaiterPM", "TimedOutWaiterPM", false, cts.Token);
                var window = UIService.GetWindow<CrossFlightProbeWindow>("TimedOutWaiterPM");
                Track(window);
                Assert.IsNotNull(window, "量具前提坏了：门面腿把窗口压上了栈");
                Assert.AreEqual(1, window.LoadCalls, "量具前提坏了：装载已发起");
                Assert.IsFalse(window.IsLoadDone, "量具前提坏了：闸门未放，装载在途");

                UtfLogExpect.Warning();
                var awaiter = UIWindowLedger.WaitWindowResultAsync(window, "TimedOutWaiterPM", TIMEOUT_UNDER_TEST_SECONDS).GetAwaiter();
                yield return PumpUntil(() => awaiter.IsCompleted, PUMP_TIMEOUT_SECONDS);
                Assert.IsTrue(awaiter.IsCompleted, "超时档要在帧上界内落定");
                Assert.AreEqual(EUIOpenStatus.Timeout, awaiter.GetResult().Status, "量具前提坏了：这一档走超时");

                Assert.IsFalse(window.IsDestroyed, "超时落定不作废窗口：那一枚在场等待者的计数没被代答摘掉");
                Assert.IsNotNull(UIService.SharedLedger.GetWindow("TimedOutWaiterPM"), "超时落定不摘栈：装载仍在途");
                Assert.IsFalse(window.IsLoadDone, "超时落定不掐断装载：闸门还挂着");

                window.Gate.TrySetResult(true);
                yield return PumpUntil(() => window.IsLoadDone, PUMP_TIMEOUT_SECONDS);

                Assert.IsTrue(window.IsLoadDone, "超时的那枚等待者没把在途装载掐断：放闸照常完成");
                Assert.IsNotNull(UIService.SharedLedger.GetWindow("TimedOutWaiterPM"), "超时落定不摘栈：装载照常完成");
                Assert.GreaterOrEqual(window.RefreshCount, 1, "就绪的那一只回执过至少一次");

                cts.Cancel();

                Assert.IsTrue(window.IsLoadDone, "已就绪的窗不受迟到的取消影响：离场是空操作");
                Assert.IsFalse(window.IsDestroyed, "就绪即清账：迟到的取消不作废窗口");
                Assert.IsNotNull(UIService.SharedLedger.GetWindow("TimedOutWaiterPM"), "迟到的取消不摘栈");
            }
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>带超时上界的帧推进：条件成立即出门，超过上界把现场交回用例判红，不写无限等。</summary>
        /// <param name="condition">出门条件。</param>
        /// <param name="timeoutSeconds">等待上界（秒，真实时间）。</param>
        private static IEnumerator PumpUntil(System.Func<bool> condition, float timeoutSeconds)
        {
            var deadline = Time.unscaledTime + timeoutSeconds;
            while (!condition() && Time.unscaledTime < deadline)
            {
                yield return null;
            }
        }

        /// <summary>把窗口此刻的面板本体登记进出门清理表；面板不在场时留一条空位，清理侧自会跳过。</summary>
        private void Track(UIWindow window)
        {
            _trackedShells.Add(window?.gameObject);
        }

        /// <summary>共享栈上同名窗口的只数：栈不裂的判据按名字数，不按索引猜。</summary>
        private int CountNamed(string windowName)
        {
            var stack = _ledger.PeekStack();
            var count = 0;
            for (var i = 0; i < stack.Count; i++)
            {
                if (stack[i].WindowName == windowName) count++;
            }

            return count;
        }

        /// <summary>数某一枚「模式 + 窗口名」回执在记账里出现了几次：事件不丢与「恰好一次」都按这一份数。</summary>
        private static int CountEvent(List<string> log, string mode, string windowName)
        {
            var key = mode + ":" + windowName;
            var count = 0;
            for (var i = 0; i < log.Count; i++)
            {
                if (log[i] == key) count++;
            }

            return count;
        }

        /// <summary>取某一枚「模式 + 窗口名」回执在记账里的序号；没发过时回 -1（两条序号比较即露馅）。</summary>
        private static int IndexOfEvent(List<string> log, string mode, string windowName)
        {
            return log.IndexOf(mode + ":" + windowName);
        }

        /// <summary>入栈回执记账：与出栈那一枚合成同一份「模式:窗口名」列表，配对与次序都读它。</summary>
        private void OnWindowShown(UIWindow window)
        {
            _eventLog.Add(ShownMode + ":" + (window == null ? "<null>" : window.WindowName));
        }

        /// <summary>出栈回执记账：停放与销毁都走这一道，一次出栈恰一行。</summary>
        private void OnWindowClosed(UIWindow window)
        {
            _eventLog.Add(ClosedMode + ":" + (window == null ? "<null>" : window.WindowName));
        }

        /// <summary>代码建出的 uGUI 面板：只带一枚 <see cref="Canvas"/>——它是 <c>UGUIWindow.BindPanel</c> 认的那一枚组件。</summary>
        private static GameObject NewCodeUGUIPanel(string name)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.SetActive(true);
            panel.transform.SetParent(UIService.UIRoot, false);
            panel.AddComponent<Canvas>();
            return panel;
        }

        /// <summary>关闭回叫里开窗的探针窗（缓存实例）：<c>OnClose</c> 里经载荷腿开出 B，用来判回叫链上的事件与载荷。</summary>
        [Window(EUILayer.Tips, cacheTimeToDestroy: -1f)]
        internal sealed class ReentryAWindow : UGUIWindow
        {
            /// <summary>回叫是否真把 B 开出去了（用例出门前归零）。</summary>
            internal static bool OpenedByCallback;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));

            protected override void OnClose()
            {
                UIService.ShowUIAsync<ReentryBWindow, string>(ReentryPayload, "ReentryB", "ReentryB");
                OpenedByCallback = UIService.SharedLedger.GetWindow("ReentryB") != null;
            }
        }

        /// <summary>回叫里被开出的带载荷探针窗：就绪回执次数与回看见的载荷都记账。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class ReentryBWindow : UGUIWindow<string>
        {
            /// <summary>就绪回执被叫到的次数。</summary>
            internal int RefreshCount;

            /// <summary>回执那一刻读到的载荷。</summary>
            internal string RefreshPayload;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));

            protected override void OnRefresh()
            {
                RefreshCount++;
                RefreshPayload = Payload;
            }
        }

        /// <summary>在飞关开交叉的探针窗：异步装载挂在闸门后，装载次数与终载荷回执都记账。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class CrossFlightProbeWindow : UGUIWindow<string>
        {
            /// <summary>本窗最近一次装载的闸门。</summary>
            internal UniTaskCompletionSource<bool> Gate;

            /// <summary>装载钩子被叫到的次数。</summary>
            internal int LoadCalls;

            /// <summary>就绪回执被叫到的次数。</summary>
            internal int RefreshCount;

            /// <summary>回执那一刻读到的载荷。</summary>
            internal string RefreshPayload;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override async UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct)
            {
                LoadCalls++;
                Gate = new UniTaskCompletionSource<bool>();
                using (ct.Register(() => Gate.TrySetCanceled()))
                {
                    if (!await Gate.Task)
                    {
                        return false;
                    }
                }

                return LoadPanel(assetLocation, fromResources);
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
