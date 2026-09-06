using System;
using Moirai.Atropos;
using Moirai.Atropos.Timer;
using NUnit.Framework;

namespace Timer
{
    /// <summary>
    /// 四级时间轮（<see cref="DefaultTimerHandler"/>）行为测试。
    /// <para>经 <see cref="GameTime.Handler"/> 注入虚拟时钟处理器（自定义 <see cref="GameTimeHandler"/>），
    /// 以 50ms 步进推进——每步低于时间轮单帧预算（MAX_WHEEL_TICKS_PER_FRAME = 64 tick ≈ 64ms），
    /// 确保步进全量生效。</para>
    /// </summary>
    public class DefaultTimerHandlerTests
    {
        private DefaultTimerHandler _handler;
        private double _now;
        private double _unscaledNow;
        private int _fired;

        /// <summary>虚拟时钟处理器——覆写双精度时钟读取点，与 Unity 主循环解耦。</summary>
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
            // 非零起点，覆盖"非零时刻添加计时器"路径
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

        /// <summary>以 50ms 步进推进虚拟时钟并驱动时间轮。</summary>
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
            var handle = _handler.AddTimer(Fire, 2f);

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
            var handle = _handler.AddTimer(Fire, 1f, isLoop: true);

            Advance(0.5);
            Assert.AreEqual(0, _fired);

            Advance(3.0);
            Assert.AreEqual(3, _fired, "1 秒循环计时器 3.5 秒内应触发 3 次");
            Assert.IsTrue(_handler.IsRunning(handle), "循环计时器触发后应继续运行");
        }

        [Test]
        public void Stop_PausesTimer_Resume_ContinuesWithRemainingTime()
        {
            var handle = _handler.AddTimer(Fire, 2f);

            Advance(1.0);
            _handler.Stop(handle);

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
        public void RemoveTimer_Cancels_AndIsIdempotent()
        {
            var handle = _handler.AddTimer(Fire, 1f);

            _handler.RemoveTimer(handle);
            _handler.RemoveTimer(handle); // 重复移除不应抛出

            Advance(2.0);

            Assert.AreEqual(0, _fired);
            Assert.IsFalse(_handler.IsRunning(handle));
            Assert.AreEqual(0f, _handler.GetLeftTime(handle), "失效句柄剩余时间应为 0");
        }

        [Test]
        public void Restart_ResetsTriggerToNowPlusDuration()
        {
            var handle = _handler.AddTimer(Fire, 2f);

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
            var handle = _handler.AddTimer(Fire, 10f);

            Assert.AreEqual(10f, _handler.GetLeftTime(handle), 0.05f);

            Advance(4.0);
            Assert.AreEqual(6f, _handler.GetLeftTime(handle), 0.1f);
        }

        [Test]
        public void UnscaledTimer_IndependentOfScaledClock()
        {
            var handle = _handler.AddTimer(Fire, 1f, isUnscaled: true);

            Advance(5.0); // 仅推进缩放时钟
            Assert.AreEqual(0, _fired, "不受缩放时间影响的计时器不应被缩放时钟推进触发");

            _unscaledNow += 1.2;
            _handler.Tick(0f, 0f);
            Assert.AreEqual(1, _fired, "真实时钟推进后应触发");
            Assert.IsFalse(_handler.IsRunning(handle), "一次性计时器触发后应结束");
        }

        [Test]
        public void GenericTimer_PassesArg()
        {
            string received = null;

            _handler.AddTimer<string>(arg => received = arg, "payload", 1f);

            Advance(1.5);
            Assert.AreEqual("payload", received);
        }

        [Test]
        public void NullCallback_IsRejected()
        {
            Assert.AreEqual(0UL, _handler.AddTimer(null, 1f), "null 回调应返回零句柄");
        }

        [Test]
        public void CapacityGrows_BeyondInitialPrewarm()
        {
            const int count = 2000; // 默认预热容量 1024

            var handles = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                handles[i] = _handler.AddTimer(Fire, 1f);
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
    }
}
