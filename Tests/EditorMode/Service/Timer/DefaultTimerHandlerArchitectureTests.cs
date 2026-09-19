using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Timer;
using NUnit.Framework;

namespace Service.Timer
{
    /// <summary>
    /// 复合路由 + 双引擎架构的重构验收测试：跨泳道隔离、句柄复用 ABA、Fixed/Late 延后派发、
    /// Unsafe 绑定、进度回调、All 系列、GetLeftFrames、复合真实并发峰值、WaitAsync 完成/取消/已结束。
    /// <para>经 <see cref="GameTime.Handler"/> 注入虚拟时钟，手动驱动 <c>Tick/FixedTick/LateTick</c>。</para>
    /// </summary>
    public class DefaultTimerHandlerArchitectureTests
    {
        private DefaultTimerHandler _handler;
        private double _now;
        private double _unscaledNow;

        private sealed class VirtualClockHandler : GameTimeHandler
        {
            private readonly Func<double> _scaled;
            private readonly Func<double> _unscaled;

            public VirtualClockHandler(Func<double> scaled, Func<double> unscaled)
            {
                _scaled = scaled;
                _unscaled = unscaled;
            }

            public override double ScaledNow => _scaled();

            public override double UnscaledNow => _unscaled();
        }

        [SetUp]
        public void SetUp()
        {
            _now = 1000.0;
            _unscaledNow = 1000.0;
            GameTime.Handler = new VirtualClockHandler(() => _now, () => _unscaledNow);
            _handler = new DefaultTimerHandler();
            _handler.Internal_Init();
        }

        [TearDown]
        public void TearDown()
        {
            _handler.Internal_Shutdown();
            _handler = null;
            GameTime.Handler = new DefaultGameTimeHandler();
        }

        /// <summary>推进缩放与非缩放时钟（默认二者同步）并按 50ms 步进驱动 Update Tick。</summary>
        private void Advance(double seconds, bool advanceUnscaled = true)
        {
            int steps = (int)Math.Ceiling(seconds / 0.05);
            for (int i = 0; i < steps; i++)
            {
                _now += 0.05;
                if (advanceUnscaled)
                {
                    _unscaledNow += 0.05;
                }

                _handler.Tick(0.05f, 0.05f);
            }
        }

        /// <summary>仅推进帧计数（冻结时钟），驱动 Update Tick 一帧。</summary>
        private void FrameTick(int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                _handler.Tick(0f, 0f);
            }
        }

        #region 跨泳道隔离 & ABA

        [Test]
        public void WheelAndFrameHandles_AreIndependent()
        {
            int wheelFired = 0;
            int frameFired = 0;
            ulong wheel = _handler.Delay(5f, () => wheelFired++);
            ulong frame = _handler.WaitFrame(3, () => frameFired++);

            // 取消 wheel 不应影响 frame（路由按泳道号，二者互不串台）。
            _handler.Cancel(wheel);
            FrameTick(3);

            Assert.AreEqual(1, frameFired, "frame 计时器应照常在第 3 帧触发");
            Assert.AreEqual(0, wheelFired, "被取消的 wheel 计时器不应触发");
            Assert.IsTrue(_handler.IsDone(wheel));
            Assert.IsTrue(_handler.IsDone(frame));
        }

        [Test]
        public void LaneTags_DisjointHandleNamespaces_SameSlotDoesNotCollide()
        {
            // wheel 与 frame 各自首个槽位 index 相同，但句柄因内嵌泳道号而不同，且互不可解析。
            ulong wheel = _handler.Delay(1f, () => { });
            ulong frame = _handler.WaitFrame(1, () => { });

            Assert.AreNotEqual(wheel, frame, "同一槽位序号 + 同一版本，泳道位应使句柄不同");

            // 用 frame 句柄调用取消，不应误伤 wheel 计时器（复合外观按泳道路由，frame 句柄只会进 frame 引擎）。
            _handler.Cancel(frame);
            Assert.IsFalse(_handler.IsDone(wheel), "wheel 句柄不应因取消 frame 句柄而失效");
            Assert.IsTrue(_handler.IsDone(frame));
        }

        [Test]
        public void ReusedSlot_OldHandle_InvalidatedByVersion()
        {
            ulong first = _handler.Delay(10f, () => { });
            _handler.Cancel(first);
            Assert.IsTrue(_handler.IsDone(first), "取消后旧句柄应结束");

            // 新句柄大概率复用同一槽位，但版本号自增 → 旧句柄仍失效。
            ulong second = _handler.Delay(10f, () => { });
            Assert.AreNotEqual(first, second);
            Assert.IsTrue(_handler.IsDone(first), "槽位复用后旧句柄必须失效（防 ABA）");
            Assert.IsFalse(_handler.IsDone(second));

            // 用旧句柄操作应安全无副作用。
            _handler.Cancel(first);
            Assert.IsFalse(_handler.IsDone(second), "以旧句柄取消不应影响复用它的新计时器");
        }

        #endregion

        #region Fixed/Late 延后派发

        [Test]
        public void Delay_FixedPhase_DeferredUntilFixedTick()
        {
            int fired = 0;
            _handler.Delay(0.5f, () => fired++, phase: TimerPhase.FixedUpdate);

            Advance(1.0);
            Assert.AreEqual(0, fired, "Update Tick 只登记延后，不应在 Update 阶段触发 Fixed 计时器");

            _handler.FixedTick(0f, 0f);
            Assert.AreEqual(1, fired, "FixedTick 才派发 Fixed 阶段计时器");
        }

        [Test]
        public void Delay_LatePhase_DeferredUntilLateTick()
        {
            int fired = 0;
            _handler.Delay(0.5f, () => fired++, phase: TimerPhase.LateUpdate);

            Advance(1.0);
            Assert.AreEqual(0, fired);

            _handler.LateTick(0f, 0f);
            Assert.AreEqual(1, fired, "LateTick 才派发 Late 阶段计时器");
        }

        #endregion

        #region Unsafe 绑定（Action 委托路径，无需 unsafe 上下文）

        [Test]
        public void DelayUnsafe_ActionBinding_FiresOnTime()
        {
            int fired = 0;
            TimerUnsafeBinding binding = new TimerUnsafeBinding((Action)(() => fired++));
            ulong handle = _handler.DelayUnsafe(1f, binding);

            Advance(0.5);
            Assert.AreEqual(0, fired);
            Advance(1.0);
            Assert.AreEqual(1, fired);
            Assert.IsTrue(_handler.IsDone(handle));
        }

        [Test]
        public void WaitFrameUnsafe_ActionBinding_FiresOnFrame()
        {
            int fired = 0;
            TimerUnsafeBinding binding = new TimerUnsafeBinding((Action)(() => fired++));
            _handler.WaitFrameUnsafe(3, binding);

            FrameTick(2);
            Assert.AreEqual(0, fired);
            FrameTick(1);
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void DelayUnsafe_InvalidBinding_ReturnsZero()
        {
            Assert.AreEqual(0UL, _handler.DelayUnsafe(1f, default), "无效绑定应返回零句柄");
        }

        #endregion

        #region 进度回调

        [Test]
        public void Delay_Progress_ReportsMonotonicRatioInRange()
        {
            var reports = new List<float>();
            _handler.Delay(1f, (Action)null, r => reports.Add(r));

            Advance(1.5);

            Assert.Greater(reports.Count, 0, "进度回调应被上报");
            foreach (float r in reports)
            {
                Assert.GreaterOrEqual(r, 0f);
                Assert.LessOrEqual(r, 1f);
            }

            Assert.GreaterOrEqual(reports[reports.Count - 1], reports[0], "进度应单调不减");
        }

        [Test]
        public void Delay_CompleteWithProgress_FiresCompleteAndProgress()
        {
            int complete = 0;
            int progress = 0;
            _handler.Delay(1f, () => complete++, _ => progress++);

            Advance(1.5);

            Assert.AreEqual(1, complete);
            Assert.Greater(progress, 0);
        }

        #endregion

        #region All 系列

        [Test]
        public void PauseAll_Resumes_All()
        {
            int fired = 0;
            _handler.Delay(1f, () => fired++);
            _handler.Delay(2f, () => fired++);

            _handler.PauseAll();
            Advance(3.0);
            Assert.AreEqual(0, fired, "全部暂停期间不应触发");

            _handler.ResumeAll();
            Advance(3.0);
            Assert.AreEqual(2, fired, "全部恢复后应陆续触发");
        }

        [Test]
        public void CancelAll_ClearsBothLanes()
        {
            int fired = 0;
            _handler.Delay(1f, () => fired++);
            _handler.WaitFrame(2, () => fired++);

            _handler.CancelAll();
            FrameTick(3);
            Advance(2.0);

            Assert.AreEqual(0, fired);
            _handler.GetStatistics(out int active, out _, out _, out _);
            Assert.AreEqual(0, active, "CancelAll 后两泳道都应清空");
        }

        #endregion

        #region GetLeftFrames

        [Test]
        public void GetLeftFrames_FrameCountsDown_WheelReturnsZero()
        {
            ulong frame = _handler.WaitFrame(5, () => { });
            Assert.AreEqual(5, _handler.GetLeftFrames(frame));

            FrameTick(2);
            Assert.AreEqual(3, _handler.GetLeftFrames(frame));

            ulong wheel = _handler.Delay(1f, () => { });
            Assert.AreEqual(0, _handler.GetLeftFrames(wheel), "时间轮句柄无帧语义");

            // 已结束句柄：0。
            FrameTick(3);
            Assert.AreEqual(0, _handler.GetLeftFrames(frame));
        }

        #endregion

        #region 复合真实并发峰值（#4）

        [Test]
        public void CompositePeak_IsTrueConcurrency_NotSumOfEnginePeaks()
        {
            // 先建立 wheel 峰值 3，随后全部取消；再在 frame 建 2。
            // 相加式两引擎峰值会误报 3+2=5，真实并发峰值应为 3。
            var wheelHandles = new List<ulong>();
            for (int i = 0; i < 3; i++)
            {
                wheelHandles.Add(_handler.Delay(100f, () => { }));
            }

            _handler.GetStatistics(out _, out _, out int peakAfterWheel, out _);
            Assert.AreEqual(3, peakAfterWheel);

            foreach (ulong h in wheelHandles)
            {
                _handler.Cancel(h);
            }

            _handler.WaitFrame(100, () => { });
            _handler.WaitFrame(100, () => { });

            _handler.GetStatistics(out int active, out _, out int peak, out _);
            Assert.AreEqual(2, active);
            Assert.AreEqual(3, peak, "复合峰值应记录真实并发最大值，而非两引擎峰值之和");
        }

        #endregion

        #region WaitAsync

        [Test]
        public async Task WaitAsync_OnCompletion_Resolves()
        {
            int fired = 0;
            ulong handle = _handler.Delay(1f, () => fired++);
            // 直接打 _handler 实例：handle.WaitAsync() 扩展走的是静态 TimerService.s_Handler，
            // 单测里通常为 null → 退化成 CompletedTask，根本测不到信号路径。
            UniTask awaiter = _handler.WaitAsync(handle);

            Advance(1.5);

            await awaiter;
            Assert.AreEqual(1, fired, "完成信号应在计时器释放的同帧唤醒 await 方");
            Assert.IsTrue(_handler.IsDone(handle));
        }

        [Test]
        public async Task WaitAsync_OnFrameTimer_Resolves()
        {
            int fired = 0;
            ulong handle = _handler.WaitFrame(3, () => fired++);
            UniTask awaiter = _handler.WaitAsync(handle);

            FrameTick(3);

            await awaiter;
            Assert.AreEqual(1, fired);
        }

        [Test]
        public async Task WaitAsync_MultipleWaiters_SignalThenPollingBothResolve()
        {
            int fired = 0;
            ulong handle = _handler.WaitFrame(2, () => fired++);
            UniTask first = _handler.WaitAsync(handle);   // 首等待者：挂 UTS 完成信号
            UniTask second = _handler.WaitAsync(handle);  // 次等待者：退回轮询

            FrameTick(2);

            await first;
            await second;
            Assert.AreEqual(1, fired);
            Assert.IsTrue(_handler.IsDone(handle));
        }

        [Test]
        public async Task WaitAsync_AlreadyCompleted_ResolvesImmediately()
        {
            ulong handle = _handler.Delay(1f, () => { });
            _handler.Cancel(handle);

            // 已结束句柄：直接完成、不挂信号；await 立即返回即证明同步完成。
            UniTask awaiter = _handler.WaitAsync(handle);
            await awaiter;
            Assert.IsTrue(_handler.IsDone(handle));
        }

        [Test]
        public async Task WaitAsync_Cancellation_Throws()
        {
            ulong handle = _handler.Delay(100f, () => { });
            using var cts = new CancellationTokenSource();
            UniTask awaiter = _handler.WaitAsync(handle, cts.Token);

            cts.Cancel();

            // 不依赖 NUnit ThrowsAsync 的跨版本返回差异，直接 await 并捕获。
            bool canceled = false;
            try
            {
                await awaiter;
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Assert.IsTrue(canceled, "取消 CancellationToken 应使 WaitAsync 抛 OperationCanceledException");
        }

        #endregion
    }
}
