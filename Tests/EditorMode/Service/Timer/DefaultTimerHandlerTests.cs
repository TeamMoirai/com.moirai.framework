using System;
using Moirai.Atropos;
using Moirai.Atropos.Timer;
using NUnit.Framework;

namespace Service.Timer
{
    /// <summary>
    /// 四级时间轮（<see cref="DefaultTimerHandler"/>）行为测试。
    /// <para>经 <see cref="GameTime.Handler"/> 注入虚拟时钟处理器，以 50ms 步进推进。</para>
    /// <para>对外 API：<c>Delay</c> / <c>Pause</c> / <c>Cancel</c>。</para>
    /// </summary>
    public class DefaultTimerHandlerTests
    {
        private DefaultTimerHandler _handler;
        private double _now;
        private double _unscaledNow;
        private int _fired;

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
            _now = 100.0;
            _unscaledNow = 50.0;
            _fired = 0;
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

        private void Fire() => _fired++;

        private void Advance(double seconds)
        {
            int steps = (int)Math.Ceiling(seconds / 0.05);
            for (int i = 0; i < steps; i++)
            {
                _now += 0.05;
                _handler.Tick(0.05f, 0.05f);
            }
        }

        [Test]
        public void OnceTimer_FiresOnce_AfterDelayNotBefore()
        {
            var handle = _handler.Delay(2f, Fire);

            Assert.AreNotEqual(0UL, handle, "成功添加应返回非零句柄");
            Assert.IsTrue(_handler.IsRunning(handle));

            Advance(1.0);
            Assert.AreEqual(0, _fired, "延迟未到不应触发");

            Advance(1.5);
            Assert.AreEqual(1, _fired, "到期应恰好触发一次");

            Advance(2.0);
            Assert.AreEqual(1, _fired, "一次性计时器不应重复触发");
            Assert.IsFalse(_handler.IsRunning(handle));
        }

        [Test]
        public void LoopTimer_FiresRepeatedly()
        {
            var handle = _handler.Delay(1f, Fire, isLooped: true);

            Advance(0.5);
            Assert.AreEqual(0, _fired);

            Advance(3.0);
            Assert.AreEqual(3, _fired, "1 秒循环计时器 3.5 秒内应触发 3 次");
            Assert.IsTrue(_handler.IsRunning(handle), "循环计时器触发后应继续运行");
        }

        [Test]
        public void Pause_PausesTimer_Resume_ContinuesWithRemainingTime()
        {
            var handle = _handler.Delay(2f, Fire);

            Advance(1.0);
            _handler.Pause(handle);

            Assert.IsFalse(_handler.IsRunning(handle), "暂停后不应处于运行态");
            Assert.AreEqual(1f, _handler.GetLeftTime(handle), 0.05f, "暂停时应记录剩余时间");

            Advance(5.0);
            Assert.AreEqual(0, _fired, "暂停期间不应触发");

            _handler.Resume(handle);
            Assert.IsTrue(_handler.IsRunning(handle));

            Advance(0.5);
            Assert.AreEqual(0, _fired, "恢复后剩余时间未耗尽不应触发");

            Advance(0.75);
            Assert.AreEqual(1, _fired, "恢复后应按剩余时间触发");
        }

        [Test]
        public void Cancel_Cancels_AndIsIdempotent()
        {
            var handle = _handler.Delay(1f, Fire);

            _handler.Cancel(handle);
            _handler.Cancel(handle);

            Advance(2.0);

            Assert.AreEqual(0, _fired);
            Assert.IsFalse(_handler.IsRunning(handle));
            Assert.AreEqual(0f, _handler.GetLeftTime(handle), "失效句柄剩余时间应为 0");
        }

        [Test]
        public void Restart_ResetsTriggerToNowPlusDuration()
        {
            var handle = _handler.Delay(2f, Fire);

            Advance(1.5);
            _handler.Restart(handle);

            Advance(1.0);
            Assert.AreEqual(0, _fired, "重启后应重新计时，原延迟不应再触发");

            Advance(1.5);
            Assert.AreEqual(1, _fired, "重启后应按完整延迟重新触发");
        }

        [Test]
        public void GetLeftTime_DecreasesMonotonically()
        {
            var handle = _handler.Delay(10f, Fire);

            Assert.AreEqual(10f, _handler.GetLeftTime(handle), 0.05f);

            Advance(4.0);
            Assert.AreEqual(6f, _handler.GetLeftTime(handle), 0.1f);
        }

        [Test]
        public void UnscaledTimer_IndependentOfScaledClock()
        {
            var handle = _handler.Delay(1f, Fire, ignoreTimeScale: true);

            Advance(5.0);
            Assert.AreEqual(0, _fired, "不受缩放时间影响的计时器不应被缩放时钟推进触发");

            _unscaledNow += 1.2;
            for (int i = 0; i < 32; i++)
            {
                _handler.Tick(0f, 0f);
            }

            Assert.AreEqual(1, _fired, "真实时钟推进后应触发");
            Assert.IsFalse(_handler.IsRunning(handle), "一次性计时器触发后应结束");
        }

        [Test]
        public void GenericTimer_PassesArg()
        {
            string received = null;

            _handler.Delay<string>(1f, arg => received = arg, "payload");

            Advance(1.5);
            Assert.AreEqual("payload", received);
        }

        [Test]
        public void NullCallback_IsRejected()
        {
            Assert.AreEqual(0UL, _handler.Delay(1f, (Action)null), "null 回调应返回零句柄");
        }

        [Test]
        public void CapacityGrows_BeyondInitialPrewarm()
        {
            const int count = 2000;

            var handles = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                handles[i] = _handler.Delay(1f, Fire);
                Assert.AreNotEqual(0UL, handles[i], $"第 {i} 个计时器添加失败——页扩容应透明");
            }

            _handler.GetStatistics(out int active, out _, out int peak, out _);
            Assert.AreEqual(count, active);
            Assert.GreaterOrEqual(peak, count);

            Advance(1.5);

            Assert.AreEqual(count, _fired, "全部计时器到期应触发");
            _handler.GetStatistics(out active, out _, out _, out _);
            Assert.AreEqual(0, active, "一次性计时器触发后应全部释放");
        }

        #region 帧计时 [WAIT FRAME]

        /// <summary>逐帧驱动 Update 阶段（不推进虚拟时钟——只推进帧计数）。</summary>
        private void TickFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                _handler.Tick(0f, 0f);
            }
        }

        [Test]
        public void WaitFrame_FiresAfterExactFrames_ThenReleases()
        {
            int fired = 0;
            var handle = _handler.WaitFrame(3, () => fired++);

            TickFrames(2);
            Assert.AreEqual(0, fired, "帧数未到不应触发");
            Assert.IsTrue(_handler.IsRunning(handle));

            TickFrames(1);
            Assert.AreEqual(1, fired, "第 3 帧应恰好触发一次");
            Assert.IsTrue(_handler.IsDone(handle), "一次性帧计时完成后应结束");

            TickFrames(3);
            Assert.AreEqual(1, fired, "释放后不应重复触发");
        }

        [Test]
        public void WaitFrame_MultipleConcurrent_AdvanceExactlyOneFramePerTick()
        {
            // 回归：倒序遍历 + swap-remove 曾导致同一帧计时器在单次 Tick 内被扣多帧。
            int c1 = 0, c2 = 0, c3 = 0;
            _handler.WaitFrame(2, () => c1++);
            _handler.WaitFrame(4, () => c2++);
            _handler.WaitFrame(6, () => c3++);

            TickFrames(3);
            Assert.AreEqual(1, c1, "2 帧计时器应在第 2 帧触发一次");
            Assert.AreEqual(0, c2, "4 帧计时器第 3 帧不应触发");
            Assert.AreEqual(0, c3);

            TickFrames(1);
            Assert.AreEqual(1, c2, "第 4 帧触发 4 帧计时器");

            TickFrames(2);
            Assert.AreEqual(1, c3, "第 6 帧触发 6 帧计时器");
            Assert.AreEqual(1, c1, "已释放的计时器不应重复触发");
        }

        [Test]
        public void WaitFrame_CallbackCancelsSibling_SiblingStopsOthersContinue()
        {
            // 回归：回调内 Cancel 另一活跃帧计时器，曾因 ReleaseSlot + 手动移除双路径误删无辜元素。
            int aFired = 0, bFired = 0, cFired = 0;
            ulong bHandle = 0UL;
            _handler.WaitFrame(1, () =>
            {
                aFired++;
                _handler.Cancel(bHandle);
            });
            bHandle = _handler.WaitFrame(2, () => bFired++);
            var cHandle = _handler.WaitFrame(2, () => cFired++);

            TickFrames(1);
            Assert.AreEqual(1, aFired);
            Assert.IsTrue(_handler.IsDone(bHandle), "被取消的 b 应结束");
            Assert.IsFalse(_handler.IsDone(cHandle), "c 不应受 b 移除影响");

            TickFrames(1);
            Assert.AreEqual(0, bFired, "b 被取消不应触发");
            Assert.AreEqual(1, cFired, "c 应正常在第 2 帧触发");
        }

        [Test]
        public void WaitFrame_PauseThenResume_CompletesFromPausedPoint()
        {
            // 回归：Resume 曾对帧计时器误调用 AddToQueue 污染时间轮。
            int fired = 0;
            var handle = _handler.WaitFrame(3, () => fired++);

            TickFrames(1);
            _handler.Pause(handle);
            Assert.IsFalse(_handler.IsRunning(handle));

            TickFrames(5);
            Assert.AreEqual(0, fired, "暂停期间不应推进");

            _handler.Resume(handle);
            Assert.IsTrue(_handler.IsRunning(handle));

            TickFrames(2);
            Assert.AreEqual(1, fired, "恢复后应从未扣完的帧继续");
        }

        [Test]
        public void WaitFrame_OnUpdate_ReportsElapsedFrameCount()
        {
            var reported = new System.Collections.Generic.List<int>();
            var handle = _handler.WaitFrame(3, (Action<int>)(count => reported.Add(count)));

            TickFrames(3);
            Assert.AreEqual(3, reported.Count);
            Assert.AreEqual(1, reported[0]);
            Assert.AreEqual(2, reported[1]);
            Assert.AreEqual(3, reported[2]);
            Assert.IsTrue(_handler.IsDone(handle));
        }

        [Test]
        public void WaitFrame_Loop_RefiresEveryCycle()
        {
            int fired = 0;
            var handle = _handler.WaitFrame(2, () => fired++, isLooped: true);

            TickFrames(4);
            Assert.AreEqual(2, fired, "2 帧循环计时 4 帧内应触发 2 次");
            Assert.IsTrue(_handler.IsRunning(handle), "循环帧计时应持续运行");
        }

        #endregion
    }
}
