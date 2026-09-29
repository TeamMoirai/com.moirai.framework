using System;
using System.Collections;
using System.Diagnostics;
using Moirai.Atropos;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Timer;
using NUnit.Framework;
using Testing;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Service.Timer
{
    /// <summary>
    /// 计时器回调触发基准：量化一次性 / 循环 / 泛型回调的触发、回调中自移除与同刻突发批量派发。
    /// </summary>
    /// <remarks>
    /// <c>[Explicit]</c> <c>[UnityTest]</c>——依赖真实帧推进，只住 PlayMode。
    /// 运行前提：PlayMode 测试域中框架已 Boot（<c>GameServices.Tick</c> 每帧驱动定时器）；跑完 XML 落统一文件夹 <c>timerservicefire-benchmark.xml</c>。
    /// 同步矩阵在 <see cref="TimerBenchmarkRunner"/>（Debugger 窗口与 EditorMode 薄壳共用），本文件只保留帧依赖用例。
    /// 突发用例的隔驱动口径：时间轮到期判定直读 <see cref="GameTime"/> 墙钟、无「到期转换 / 派发」两段式；只等真实帧再手动泵 <c>Tick</c> 时，帧驱动大概率已把到期回调全量派发完，泵到的只剩空转。
    /// 故先注入冻结的虚拟时钟再插定时器（帧驱动读同一时钟、见不到任何到期），跨一帧验证隔离后手动推进时钟、泵一次 <c>Tick(0,0)</c> 独占计量「同刻全量派发」本身。
    /// </remarks>
    [TestFixture]
    [Explicit]
    public sealed class TimerFireBenchmarkTests
    {
        private const int FIRE_TIMER_COUNT = 1024;
        private const int BURST_FIRE_COUNT = 4096;
        private const float FIRE_DELAY = 0.001f;
        private const float FIRE_WAIT_SECONDS = 0.05f;
        private const float BURST_FIRE_DELAY = 0.001f;

        private static readonly Action s_CountHandler = OnCount;
        private static readonly Action<BenchmarkArg> s_GenericCountHandler = OnGenericCount;
        private static readonly Action s_RemoveSelfHandler = OnRemoveSelf;

        private static int s_CallbackCount;
        private static ulong s_RemoveSelfHandle;

        // NUnit 每个 [Test] 新建 fixture 实例，跨用例累积只能走 static
        private static BenchmarkReport s_Report;
        private static long s_WallStart;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            s_Report = new BenchmarkReport("TimerServiceFire");
            s_Report.SetMetadata("fireTimerCount", FIRE_TIMER_COUNT.ToString());
            s_Report.SetMetadata("burstFireCount", BURST_FIRE_COUNT.ToString());
            s_WallStart = Stopwatch.GetTimestamp();
        }

        [OneTimeTearDown]
        public void ExportXml()
        {
            s_Report.TotalMs = (Stopwatch.GetTimestamp() - s_WallStart) * 1000.0 / Stopwatch.Frequency;
            s_Report.WriteXml(s_Report.ResolveXmlPath());
        }

        [SetUp]
        public void SetUp()
        {
            // 触发 HandlerHost 懒加载（不要求 GameApp 已 Boot；但 fire 用例依赖运行期每帧 Tick 驱动）
            _ = TimerService.Handler;
            ClearAllTimers();
            s_CallbackCount = 0;
            s_RemoveSelfHandle = 0UL;
        }

        [TearDown]
        public void TearDown()
        {
            ClearAllTimers();
        }

        private static void AddFireCase(string caseName, double ms, string note)
        {
            s_Report.Add(new BenchmarkCaseResult
            {
                Name = caseName,
                Category = "Fire",
                Trials = 1,
                MinMs = ms,
                MeanMs = ms,
                MaxMs = ms,
            }.Metric("note", note));
            Debug.Log($"[TimerFireBenchmark] {caseName} ms={ms:F4} {note}");
        }

        #region fire 用例 [FIRE CASES]

        [UnityTest]
        public IEnumerator FireOneShot_MeasuresDispatch()
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < FIRE_TIMER_COUNT; i++)
                TimerService.Delay(FIRE_DELAY, s_CountHandler);

            yield return WaitForFire();

            Assert.AreEqual(FIRE_TIMER_COUNT, s_CallbackCount, "one-shot fire callback count mismatch");
            AddFireCase("Fire OneShot", sw.Elapsed.TotalMilliseconds, $"callbacks={s_CallbackCount}");
        }

        [UnityTest]
        public IEnumerator FireLoopCallbacks_MeasuresDispatch()
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < FIRE_TIMER_COUNT; i++)
                TimerService.Delay(FIRE_DELAY, s_CountHandler, true);

            yield return WaitForFire();

            Assert.GreaterOrEqual(s_CallbackCount, FIRE_TIMER_COUNT, "loop fire did not invoke callbacks");
            // 活跃数走 GetStatistics——GetAllTimers(null) 按契约返回 0，不是计数通道
            TimerService.GetStatistics(out int stillActive, out _, out _, out _);
            Assert.AreEqual(FIRE_TIMER_COUNT, stillActive, "loop fire changed active timer count");
            AddFireCase("Fire Loop Callbacks", sw.Elapsed.TotalMilliseconds, $"callbacks={s_CallbackCount}");
        }

        [UnityTest]
        public IEnumerator GenericFire_MeasuresDispatch()
        {
            BenchmarkArg arg = new BenchmarkArg();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < FIRE_TIMER_COUNT; i++)
                TimerService.Delay(FIRE_DELAY, s_GenericCountHandler, arg);

            yield return WaitForFire();

            Assert.AreEqual(FIRE_TIMER_COUNT, arg.Value, "generic fire callback count mismatch");
            AddFireCase("Generic Fire", sw.Elapsed.TotalMilliseconds, $"callbacks={arg.Value}");
        }

        [UnityTest]
        public IEnumerator RemoveDuringCallback_LeavesNoActive()
        {
            s_RemoveSelfHandle = TimerService.Delay(FIRE_DELAY, s_RemoveSelfHandler, true);
            Assert.AreNotEqual(0UL, s_RemoveSelfHandle, "remove-during-callback add returned invalid handle");

            var sw = Stopwatch.StartNew();
            yield return WaitForFire();

            Assert.IsFalse(TimerService.IsRunning(s_RemoveSelfHandle), "self-removed timer is still running");
            TimerService.GetStatistics(out int active, out _, out _, out _);
            Assert.AreEqual(0, active, "self-removed timer stayed active");
            AddFireCase("Remove During Callback", sw.Elapsed.TotalMilliseconds, "self-removed");
            s_RemoveSelfHandle = 0UL;
        }

        [UnityTest]
        public IEnumerator BurstSameTickOneShot_MeasuresBurstDispatch()
        {
            yield return RunIsolatedBurstTick("oneshot", isLoop: false);
        }

        [UnityTest]
        public IEnumerator BurstSameTickLoop_MeasuresBurstDispatch()
        {
            yield return RunIsolatedBurstTick("loop", isLoop: true);
        }

        private static IEnumerator RunIsolatedBurstTick(string label, bool isLoop)
        {
            float delay = BURST_FIRE_DELAY > 0.001f ? BURST_FIRE_DELAY : 0.001f;
            s_CallbackCount = 0;

            // 先冻结再插：触发时刻落在冻结读数之后，帧驱动（每帧读同一时钟）在整个等待窗内见不到任何到期。
            // 时钟是进程级全局旋钮，注入与还原收在本方法 try/finally 内，断言失败也照常归还、不外溢。
            var savedClock = GameTime.Handler;
            double clockNow = savedClock.UnscaledNow;
            GameTime.Handler = new VirtualClockHandler(() => clockNow, () => clockNow);
            try
            {
                for (int i = 0; i < BURST_FIRE_COUNT; i++)
                {
                    ulong handle = TimerService.Delay(delay, s_CountHandler, isLoop, true);
                    Assert.AreNotEqual(0UL, handle, "burst add returned invalid handle");
                }

                TimerService.GetStatistics(out int setupActive, out _, out _, out _);
                Assert.AreEqual(BURST_FIRE_COUNT, setupActive, "burst setup active count mismatch");

                // 跨一帧验证隔离：帧驱动至少运行过一次，冻结时钟下不得有任何提前派发。
                yield return null;
                Assert.AreEqual(0, s_CallbackCount, "frozen clock leaked a frame-driven dispatch before the measured pump");

                // 推进时钟越过全部触发时刻，手动泵一次 Tick 独占计量「同刻全量派发」本身。
                // 隐式依赖：当前 Timer 引擎直读全局 GameTime（WheelTimerEngine 的 ScaledNow/UnscaledNow），
                // 不消费此处传入的两个零 elapse——推进只认全局时钟。若引擎日后改为消费传入 delta，
                // 本基准会当场红掉（回调计数对不上）：届时应改回「手动推进」语义，不得删用例。
                clockNow += delay * 2;
                var pump = new TimerService();
                var sw = Stopwatch.StartNew();
                pump.Tick(0f, 0f);
                sw.Stop();

                if (isLoop)
                {
                    Assert.AreEqual(BURST_FIRE_COUNT, s_CallbackCount, "burst loop tick callback count mismatch");
                    TimerService.GetStatistics(out int activeAfter, out _, out _, out _);
                    Assert.AreEqual(BURST_FIRE_COUNT, activeAfter, "burst loop tick changed active count");
                }
                else
                {
                    Assert.AreEqual(BURST_FIRE_COUNT, s_CallbackCount, "burst oneshot tick callback count mismatch");
                    TimerService.GetStatistics(out int activeAfter, out _, out _, out _);
                    Assert.AreEqual(0, activeAfter, "burst oneshot tick left active timers");
                }

                AddFireCase($"Burst Same-Tick {label}", sw.Elapsed.TotalMilliseconds, $"count={BURST_FIRE_COUNT} callbacks={s_CallbackCount}");
            }
            finally
            {
                GameTime.Handler = savedClock;
            }
        }

        private static IEnumerator WaitForFire()
        {
            float endTime = Time.unscaledTime + Mathf.Max(0.02f, FIRE_WAIT_SECONDS);
            while (Time.unscaledTime < endTime)
                yield return null;
        }

        #endregion

        #region 辅助 [UTILITIES]

        private static TimerDebugInfo[] s_InfoBuffer = new TimerDebugInfo[16];

        private static void ClearAllTimers()
        {
            if (!TimerService.IsValid)
                return;

            while (true)
            {
                int count = TimerService.GetAllTimers(s_InfoBuffer);
                if (count <= 0)
                    break;

                for (int i = 0; i < count; i++)
                    TimerService.Cancel(s_InfoBuffer[i].TimerHandle);

                if (count < s_InfoBuffer.Length)
                    break;

                s_InfoBuffer = new TimerDebugInfo[s_InfoBuffer.Length << 1];
            }
        }

        private static void OnCount()
        {
            s_CallbackCount++;
        }

        private static void OnRemoveSelf()
        {
            TimerService.Cancel(s_RemoveSelfHandle);
        }

        private static void OnGenericCount(BenchmarkArg arg)
        {
            arg.Value++;
        }

        #endregion

        private sealed class BenchmarkArg
        {
            public int Value;
        }
    }
}
