using System;
using System.Text.RegularExpressions;
using Moirai.Atropos;
using Moirai.Atropos.Timer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Timer
{
    /// <summary>
    /// 时间轮面对被污染时间输入（NaN / 无穷 / 溢出）时的自愈性测试。
    /// <para>回归目标：tick 换算饱和化后，单帧 NaN 时钟不得把轮游标打到 long.MinValue
    /// （那等于时间轮此后每帧只追 64 tick，实际永久冻结）；非有限延时必须在占用槽位前被拒绝。</para>
    /// </summary>
    public class WheelTimerClockPoisonTests
    {
        private const double START_SCALED = 100.0;
        private const double START_UNSCALED = 50.0;

        private DefaultTimerHandler _handler;
        private GameTimeHandler _originalGameTimeHandler;
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
            _now = START_SCALED;
            _unscaledNow = START_UNSCALED;
            _fired = 0;
            _originalGameTimeHandler = GameTime.Handler;
            GameTime.Handler = new VirtualClockHandler(() => _now, () => _unscaledNow);
            _handler = new DefaultTimerHandler();
            _handler.Internal_Init();
        }

        [TearDown]
        public void TearDown()
        {
            _handler.Internal_Shutdown();
            _handler = null;
            GameTime.Handler = _originalGameTimeHandler;
        }

        private void Fire() => _fired++;

        private void Advance(double seconds)
        {
            int steps = (int)Math.Ceiling(seconds / 0.05);
            for (int i = 0; i < steps; i++)
            {
                _now += 0.05;
                _unscaledNow += 0.05;
                _handler.Tick(0.05f, 0.05f);
            }
        }

        [Test]
        public void InfinityDelay_IsRejectedAndNeverFires()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Schedule failed"));

            var handle = _handler.Delay(float.PositiveInfinity, Fire);

            Assert.AreEqual(0UL, handle, "正无穷延时不是有效排期，应返回 0 句柄");
            Advance(5.0);
            Assert.AreEqual(0, _fired, "被拒绝的排期不得触发");
        }

        [Test]
        public void NaNDelay_IsRejectedAndNeverFires()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Schedule failed"));

            var handle = _handler.Delay(float.NaN, Fire);

            Assert.AreEqual(0UL, handle, "NaN 延时应被拒绝");
            Advance(5.0);
            Assert.AreEqual(0, _fired, "被拒绝的排期不得触发");
        }

        [Test]
        public void NaNClockFrame_KeepsWheelUsable()
        {
            var early = _handler.Delay(1f, Fire);
            Advance(1.5);
            Assert.AreEqual(1, _fired, "注入污染帧之前计时器应正常触发");
            Assert.IsFalse(_handler.IsRunning(early));

            _now = double.NaN;
            _unscaledNow = double.NaN;
            _handler.Tick(0.05f, 0.05f);
            _now = START_SCALED + 1.5;
            _unscaledNow = START_UNSCALED + 1.5;

            var later = _handler.Delay(2f, Fire);
            Assert.AreNotEqual(0UL, later, "污染帧之后新排的计时器应能正常登记");

            Advance(3.0);
            Assert.AreEqual(2, _fired, "时间轮必须能从单帧 NaN 时钟中恢复并照常到期");
            Assert.IsTrue(_handler.IsDone(later));
        }

        [Test]
        public void OverflowDelay_SaturatesWithoutCorruptingWheel()
        {
            var overflowing = _handler.Delay(1e30f, Fire);
            Assert.AreNotEqual(0UL, overflowing, "有限但溢出 double→long 的延时代谢仍应被接受");

            Advance(1.0);
            Assert.AreEqual(0, _fired, "饱和到上限的计时器不应立即触发");

            var normal = _handler.Delay(1f, Fire);
            Advance(2.0);
            Assert.AreEqual(1, _fired, "tick 换算饱和后，普通计时器不受溢出计时器影响");
            Assert.IsFalse(_handler.IsRunning(normal), "普通计时器应正常完成");
        }
    }
}
