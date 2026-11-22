using System;
using Moirai.Atropos;
using Moirai.Atropos.Timer;
using Testing;
using NUnit.Framework;

namespace Service.Timer
{
    /// <summary>
    /// 时间轮热路径的 0-Alloc 门禁：万次级 add/remove 与 tick 驱动不得触发任何托管分配。
    /// </summary>
    /// <remarks>
    /// 分配断言经 <see cref="AllocationCapture"/>（GC 线程字节差值），虚拟时钟推进保证触发路径全部走完； <br />
    /// 回调缓存为静态方法组（订阅侧预热完成后的稳态才是断言对象）。
    /// </remarks>
    [TestFixture]
    public sealed class TimerAllocationTests
    {
        private DefaultTimerHandler _handler;
        private GameTimeHandler _originalGameTimeHandler;
        private double _now;
        private double _unscaledNow;
        private int _fired;

        [SetUp]
        public void SetUp()
        {
            _now = 100.0;
            _unscaledNow = 50.0;
            _fired = 0;
            _originalGameTimeHandler = GameTime.Handler;
            GameTime.Handler = new VirtualClockHandler(() => _now, () => _unscaledNow);
            _handler = new DefaultTimerHandler();
            ((Moirai.Atropos.IServiceLifecycle)_handler).Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                ((Moirai.Atropos.IServiceLifecycle)_handler).Destroy();
            }
            finally
            {
                _handler = null;
                GameTime.Handler = _originalGameTimeHandler;
            }
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
        public void DelayCancel_TenThousandCycles_AllocatesZeroBytes()
        {
            // 预热：首轮加桶/扩容/委托注册的分配收敛在断言之外
            for (int i = 0; i < 64; i++)
            {
                _handler.Cancel(_handler.Delay(1f, Fire));
            }

            AllocationCapture.AssertNoAlloc(() =>
            {
                ulong handle = _handler.Delay(1f, Fire);
                _handler.Cancel(handle);
            }, maxBytes: 0, iterations: 10_000, context: "Delay+Cancel 万次循环");
        }

        [Test]
        public void TickDrivesFiredTimers_AllocatesZeroBytes()
        {
            // 预热：注册 64 个 1s 定时器并推进过一轮触发，覆盖触发路径的首次分配
            for (int i = 0; i < 64; i++)
            {
                _handler.Delay(1f, Fire);
            }
            Advance(1.5);
            _handler.CancelAll();

            // 稳态：空轮 + 重复注册触发不应分配
            AllocationCapture.AssertNoAlloc(() =>
            {
                ulong handle = _handler.Delay(1f, Fire);
                _now += 1.0;
                _handler.Tick(1.0f, 1.0f);
            }, maxBytes: 0, iterations: 1_000, context: "定时器注册+触发稳态");
        }
    }
}
