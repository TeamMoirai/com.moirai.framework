using System;
using System.Collections;
using System.Diagnostics;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Timer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Service.Timer
{
    /// <summary>
    /// 计时器回调触发基准（<c>[Explicit]</c> <c>[UnityTest]</c>——依赖真实帧推进，只住 PlayMode）。
    /// <para>测量一次性/循环/泛型回调的触发、回调中自移除、以及同刻突发批量派发（先等真实帧完成到期转换，
    /// 再手动 <c>Tick(0,0)</c> 计量派发本身）。原为独立菜单基准（TimerServiceBenchmark MonoBehaviour）的
    /// fire 部分，按基准归一裁定迁入 Tests；同步矩阵在 <see cref="TimerBenchmarkRunner"/>
    /// （Debugger 窗口与 EditorMode 薄壳共用），本文件只保留帧依赖用例。</para>
    /// <para>运行前提：PlayMode 测试域中框架已 Boot（GameServices.Tick 每帧驱动定时器）——
    /// 桥跑 PlayMode 满足此前提。跑完 XML 落统一文件夹 timerservicefire-benchmark.xml。</para>
    /// </summary>
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

            for (int i = 0; i < BURST_FIRE_COUNT; i++)
            {
                ulong handle = TimerService.Delay(delay, s_CountHandler, isLoop, true);
                Assert.AreNotEqual(0UL, handle, "burst add returned invalid handle");
            }

            TimerService.GetStatistics(out int setupActive, out _, out _, out _);
            Assert.AreEqual(BURST_FIRE_COUNT, setupActive, "burst setup active count mismatch");

            // 等真实帧完成到期转换（wall 时钟推进），再手动泵一次 Tick 计量「同刻全量派发」本身
            float endTime = Time.unscaledTime + delay + 0.004f;
            while (Time.unscaledTime < endTime)
                yield return null;

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
