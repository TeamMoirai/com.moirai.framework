using System;
using Moirai.Atropos.Timer;
using NUnit.Framework;
using Testing;

namespace Service.Timer
{
    /// <summary>
    /// 定时器热路径 0-GC 验收（<b>真机计量</b>）：调度+取消、含挂起定时器的 Tick 均不得产生托管分配。
    /// <para>定时器是每帧驱动的核心服务，一次几十字节的抖动在真机上就是 GC 峰值与掉帧。编辑器与 Mono
    /// 玩家都观测不到托管分配（见 <see cref="AllocationCapture"/>，探针整组 Ignore），真计量只在实现
    /// 计数器的 IL2CPP 玩家（L3）发生。</para>
    /// <para>经 <c>DefaultTimerHandler</c> 直驱（绕过外观的懒加载链路），延迟取 3600s 保证测量窗内不触发；
    /// 回调为缓存的方法组字段——C# 9 不缓存方法组转换，裸写每次都会分配一只委托。</para>
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class TimerHotPathAllocationTests
    {
        // 缓存的委托：方法组到字段的转换只在静态初始化发生一次，测量窗内不分配
        private static readonly Action s_NoOp = OnNoOp;

        private static void OnNoOp()
        {
        }

        private DefaultTimerHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _handler = new DefaultTimerHandler();
            _handler.Internal_Init();
        }

        [TearDown]
        public void TearDown()
        {
            _handler?.Internal_Shutdown();
            _handler = null;
        }

        [Test]
        public void MeasureManaged_DetectsKnownAllocation()
        {
            AllocationCapture.CalibrateKnownAllocation();
        }

        [Test]
        public void ScheduleCancel_HotPath_AllocatesZeroBytes()
        {
            AllocationCapture.MeasureManaged("timer-schedule-cancel", 200, () =>
            {
                ulong handle = _handler.Delay(3600f, s_NoOp);
                _handler.Cancel(handle);
            }, b => Assert.AreEqual(0L, b, "调度+取消热路径不得产生任何托管分配"));
        }

        [Test]
        public void Tick_WithPendingTimers_AllocatesZeroBytes()
        {
            const int pending = 1000;
            for (int i = 0; i < pending; i++)
                _handler.Delay(3600f, s_NoOp);

            AllocationCapture.MeasureManaged("timer-tick-pending", 200,
                () => _handler.Tick(0f, 0.016f),
                b => Assert.AreEqual(0L, b, $"含 {pending} 只挂起定时器的 Tick 不得分配"));
        }
    }
}
