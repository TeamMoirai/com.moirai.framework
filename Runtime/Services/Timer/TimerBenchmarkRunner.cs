using System;
using System.Diagnostics;
using Moirai.Atropos.Debugger;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器性能基准运行时驱动器：跑同步用例矩阵并产出统一的 <see cref="BenchmarkReport"/>。
    /// <para>双通道入口共用本驱动器：Debugger 的 Timer 调试窗口（Run Benchmark 按钮）与
    /// 测试程序集的 [Explicit] 薄壳——两个入口测的是同一份代码（口径同 <see cref="Debugger.MemoryPoolBenchmarkRunner"/>）。
    /// 会短暂时卡主线程。</para>
    /// <para>直驱隔离的 <see cref="DefaultTimerHandler"/>（不经 <see cref="TimerService"/> 门面）：
    /// 门面的懒加载要活服务世界（EditMode 下 GameApp 关闭态被 EnsureRegistered 阻断），
    /// 隔离实例让矩阵在编辑器与玩家、Boot 前后都跑得起来，且不扰动运行中的真实服务。
    /// 同步矩阵全部使用长延迟定时器（测量窗内不触发、不依赖时钟推进）：Add/Remove 热路径、
    /// 控制操作（Stop/Resume/Restart/查询）、时间轮混合延迟插入、页增长、句柄守卫、
    /// 空闲与挂起 Tick、调试 API、句柄复用。回调触发与同刻突发（依赖真实帧推进）
    /// 在 Tests 的 PlayMode 专属基准 <c>TimerFireBenchmarkTests</c>。</para>
    /// <para>测量：每用例先跑一轮预热再计时 N 轮取最小/均值/最大；软校验只累加 failures 计数
    /// 并 LogWarning——正确性回归由 Timer 测试族负责。</para>
    /// </summary>
    public static class TimerBenchmarkRunner
    {
        #region 配置 [CONFIGURATION]

        private const int TIMER_COUNT = 10000;
        private const int LOOP_COUNT = 100000;
        private const int TICK_LOOP_COUNT = 1024;
        private const float CONTROL_DURATION = 10f;

        private const int TRIAL_COUNT = 3;

        private static readonly float[] s_WheelDelays =
        {
            0.001f, 0.05f, 0.2f, 1f, 10f, 60f, 300f, 3600f
        };

        #endregion

        #region 静态状态 [SHARED STATE]

        private static readonly Stopwatch s_Stopwatch = new();
        private static BenchmarkReport s_Report;
        private static int s_Failures;

        // 静态回调（缓存方法组：C#9 不缓存方法组转换，裸写每次分配一只委托）
        private static readonly Action s_NoOpHandler = OnNoOp;
        private static readonly Action<BenchmarkArg> s_GenericHandler = OnGeneric;

        private static DefaultTimerHandler s_Handler;
        private static ulong[] s_Handles;
        private static TimerDebugInfo[] s_InfoBuffer;

        #endregion

        #region 入口 [ENTRY]

        /// <summary>
        /// 同步跑完全部同步基准用例并返回报告。收尾关闭隔离处理器并清空全部计时器。
        /// </summary>
        public static BenchmarkReport Run()
        {
            BenchmarkReport report = new BenchmarkReport("TimerService");
            report.SetMetadata("trials", TRIAL_COUNT.ToString());
            s_Report = report;
            s_Failures = 0;

            EnsureHandleBuffer(TIMER_COUNT);
            EnsureInfoBuffer(Math.Max(TIMER_COUNT, 64) + 16);

            s_Handler = new DefaultTimerHandler();
            s_Handler.Internal_Init();

            long wallStart = Stopwatch.GetTimestamp();
            try
            {
                RunCase("Add/Remove OneShot Hot Loop", RunAddRemoveOneShotHotLoop);
                RunCase("Add/Remove Loop Hot Loop", RunAddRemoveLoopHotLoop);
                RunCase("Generic Add/Remove Hot Loop", RunGenericAddRemoveHotLoop);
                RunCase("Add Unscaled OneShot", RunAddUnscaledOneShot);
                RunCase("Stop/Resume", RunStopResume);
                RunCase("Restart", RunRestart);
                RunCase("GetLeftTime/IsRunning", RunQueryHotLoop);
                RunCase("Mixed Delay Wheel Insert", RunMixedDelayWheelInsert);
                RunCase("Page Growth", RunPageGrowth);
                RunCase("Invalid Handle Guards", RunInvalidHandleGuards);
                RunCase("Null Callback Guard", RunNullCallbackGuard);
                RunCase("Tick Idle", RunTickIdle);
                RunCase("Tick With Pending Timers", RunTickWithPendingTimers);
                RunCase("GetStatistics", RunGetStatistics);
                RunCase("GetAllTimers Buffer", RunGetAllTimersBuffer);
                RunCase("Handle Reuse After Remove", RunHandleReuseAfterRemove);
            }
            finally
            {
                ClearAllTimers();
                s_Handler.Internal_Shutdown();
                s_Handler = null;
            }

            report.TotalMs = (Stopwatch.GetTimestamp() - wallStart) * 1000.0 / Stopwatch.Frequency;
            report.SetMetadata("failures", s_Failures.ToString());
            Debug.Log($"[TimerBenchmark] cases={report.CaseCount}, failures={s_Failures}, total={report.TotalMs:F1}ms");
            return report;
        }

        #endregion

        #region 用例 [CASES]

        private static void RunAddRemoveOneShotHotLoop()
        {
            for (int i = 0; i < LOOP_COUNT; i++)
            {
                ulong handle = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler);
                Check(handle != 0UL, "one-shot add returned invalid handle");
                s_Handler.Cancel(handle);
            }

            AssertActiveCount(0, "one-shot add/remove left active timers");
        }

        private static void RunAddRemoveLoopHotLoop()
        {
            for (int i = 0; i < LOOP_COUNT; i++)
            {
                ulong handle = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler, true);
                Check(handle != 0UL, "loop add returned invalid handle");
                s_Handler.Cancel(handle);
            }

            AssertActiveCount(0, "loop add/remove left active timers");
        }

        private static void RunGenericAddRemoveHotLoop()
        {
            BenchmarkArg arg = new BenchmarkArg();
            for (int i = 0; i < LOOP_COUNT; i++)
            {
                ulong handle = s_Handler.Delay(CONTROL_DURATION, s_GenericHandler, arg);
                Check(handle != 0UL, "generic add returned invalid handle");
                s_Handler.Cancel(handle);
            }

            AssertActiveCount(0, "generic add/remove left active timers");
        }

        private static void RunAddUnscaledOneShot()
        {
            int count = Math.Min(TIMER_COUNT, s_Handles.Length);
            for (int i = 0; i < count; i++)
            {
                s_Handles[i] = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler, false, true);
                Check(s_Handles[i] != 0UL, "unscaled add returned invalid handle");
                Check(s_Handler.IsRunning(s_Handles[i]), "unscaled timer is not running");
            }

            AssertActiveCount(count, "unscaled add active count mismatch");
            for (int i = 0; i < count; i++)
                s_Handler.Cancel(s_Handles[i]);
            AssertActiveCount(0, "unscaled remove left active timers");
        }

        private static void RunStopResume()
        {
            ulong handle = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler);
            for (int i = 0; i < LOOP_COUNT; i++)
            {
                s_Handler.Pause(handle);
                s_Handler.Resume(handle);
            }

            Check(s_Handler.IsRunning(handle), "timer is not running after stop/resume");
            Check(s_Handler.GetLeftTime(handle) > 0f, "timer left time was cleared by stop/resume");
            s_Handler.Cancel(handle);
        }

        private static void RunRestart()
        {
            ulong handle = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler);
            s_Handler.Pause(handle);
            for (int i = 0; i < LOOP_COUNT; i++)
                s_Handler.Restart(handle);

            Check(s_Handler.IsRunning(handle), "timer is not running after restart");
            Check(s_Handler.GetLeftTime(handle) > CONTROL_DURATION * 0.5f, "restart did not restore remaining time");
            s_Handler.Cancel(handle);
        }

        private static void RunQueryHotLoop()
        {
            ulong handle = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler);
            bool running = false;
            float leftTime = 0f;
            for (int i = 0; i < LOOP_COUNT; i++)
            {
                running = s_Handler.IsRunning(handle);
                leftTime = s_Handler.GetLeftTime(handle);
            }

            Check(running, "query hot loop saw timer as not running");
            Check(leftTime > 0f, "query hot loop saw zero left time");
            s_Handler.Cancel(handle);
        }

        private static void RunMixedDelayWheelInsert()
        {
            int count = Math.Min(TIMER_COUNT, s_Handles.Length);
            for (int i = 0; i < count; i++)
            {
                float delay = s_WheelDelays[i & (s_WheelDelays.Length - 1)];
                bool isLoop = (i & 1) == 0;
                bool isUnscaled = (i & 2) == 0;
                s_Handles[i] = s_Handler.Delay(delay, s_NoOpHandler, isLoop, isUnscaled);
                Check(s_Handles[i] != 0UL, "mixed wheel add returned invalid handle");
            }

            AssertActiveCount(count, "mixed wheel insert active count mismatch");
            for (int i = 0; i < count; i++)
                s_Handler.Cancel(s_Handles[i]);
            AssertActiveCount(0, "mixed wheel remove left active timers");
        }

        private static void RunPageGrowth()
        {
            int count = Math.Min(TIMER_COUNT, s_Handles.Length);
            GetStats(out _, out int capacityBefore, out _, out _);
            for (int i = 0; i < count; i++)
                s_Handles[i] = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler);

            GetStats(out int activeCount, out int capacityAfter, out int peakActiveCount, out int freeCount);
            Check(activeCount == count, "page growth active count mismatch");
            Check(capacityAfter >= count, "page growth did not expand capacity");
            Check(capacityAfter >= capacityBefore, "page growth shrunk capacity");
            Check(peakActiveCount >= count, "page growth peak active count mismatch");
            Check(freeCount >= 0, "page growth free count is invalid");

            for (int i = 0; i < count; i++)
                s_Handler.Cancel(s_Handles[i]);
            AssertActiveCount(0, "page growth remove left active timers");
        }

        private static void RunInvalidHandleGuards()
        {
            ulong staleHandle = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler);
            s_Handler.Cancel(staleHandle);

            s_Handler.Pause(0UL);
            s_Handler.Resume(0UL);
            s_Handler.Restart(0UL);
            s_Handler.Cancel(0UL);
            s_Handler.Pause(staleHandle);
            s_Handler.Resume(staleHandle);
            s_Handler.Restart(staleHandle);
            s_Handler.Cancel(staleHandle);
            bool running = s_Handler.IsRunning(staleHandle);
            float leftTime = s_Handler.GetLeftTime(staleHandle);

            Check(!running, "stale handle was reported as running");
            Check(leftTime == 0f, "stale handle returned leftover time");
            AssertActiveCount(0, "invalid handle guards created timers");
        }

        private static void RunNullCallbackGuard()
        {
            ulong noArgsHandle = s_Handler.Delay(CONTROL_DURATION, (Action)null);
            ulong genericHandle = s_Handler.Delay<BenchmarkArg>(CONTROL_DURATION, null, new BenchmarkArg());

            Check(noArgsHandle == 0UL, "null no-args callback produced a handle");
            Check(genericHandle == 0UL, "null generic callback produced a handle");
            AssertActiveCount(0, "null callback created an active timer");
        }

        private static void RunTickIdle()
        {
            PumpTick(TICK_LOOP_COUNT);
            AssertActiveCount(0, "idle tick created timers");
        }

        private static void RunTickWithPendingTimers()
        {
            int count = Math.Min(TIMER_COUNT, s_Handles.Length);
            for (int i = 0; i < count; i++)
                s_Handles[i] = s_Handler.Delay(3600f, s_NoOpHandler, (i & 1) == 0, (i & 2) == 0);

            PumpTick(TICK_LOOP_COUNT);

            AssertActiveCount(count, "pending tick changed active timer count");
            for (int i = 0; i < count; i++)
                s_Handler.Cancel(s_Handles[i]);
        }

        private static void RunGetStatistics()
        {
            int count = Math.Min(64, s_Handles.Length);
            for (int i = 0; i < count; i++)
                s_Handles[i] = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler);

            int activeCount = 0;
            int poolCapacity = 0;
            int peakActiveCount = 0;
            int freeCount = 0;
            for (int i = 0; i < LOOP_COUNT; i++)
                s_Handler.GetStatistics(out activeCount, out poolCapacity, out peakActiveCount, out freeCount);

            Check(activeCount == count, "statistics active count mismatch");
            Check(poolCapacity >= count, "statistics capacity smaller than active count");
            Check(peakActiveCount >= count, "statistics peak smaller than active count");
            Check(freeCount == poolCapacity - count, "statistics free count mismatch");

            for (int i = 0; i < count; i++)
                s_Handler.Cancel(s_Handles[i]);
        }

        private static void RunGetAllTimersBuffer()
        {
            int count = Math.Min(64, s_Handles.Length);
            for (int i = 0; i < count; i++)
                s_Handles[i] = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler, i == 0, i == 1);

            EnsureInfoBuffer(count);
            int filled = 0;
            for (int i = 0; i < LOOP_COUNT; i++)
                filled = s_Handler.GetAllTimers(s_InfoBuffer);

            Check(filled == count, "debug buffer fill count mismatch");
            Check(s_Handler.GetAllTimers(null) == 0, "null debug buffer should return 0");
            Check(s_Handler.GetAllTimers(Array.Empty<TimerDebugInfo>()) == 0, "empty debug buffer should return 0");

            bool foundRunning = false;
            bool foundLoop = false;
            bool foundUnscaled = false;
            for (int i = 0; i < filled; i++)
            {
                if ((s_InfoBuffer[i].Flags & TimerDebugFlags.RUNNING) != 0)
                    foundRunning = true;
                if ((s_InfoBuffer[i].Flags & TimerDebugFlags.LOOP) != 0)
                    foundLoop = true;
                if ((s_InfoBuffer[i].Flags & TimerDebugFlags.UNSCALED) != 0)
                    foundUnscaled = true;
            }

            Check(foundRunning, "debug buffer missed running flag");
            Check(foundLoop, "debug buffer missed loop flag");
            Check(foundUnscaled, "debug buffer missed unscaled flag");

            for (int i = 0; i < count; i++)
                s_Handler.Cancel(s_Handles[i]);
        }

        private static void RunHandleReuseAfterRemove()
        {
            ulong first = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler);
            s_Handler.Cancel(first);
            ulong second = s_Handler.Delay(CONTROL_DURATION, s_NoOpHandler);
            Check(first != 0UL && second != 0UL, "handle reuse produced an invalid handle");
            Check(first != second, "removed handle was reused without version change");
            Check(!s_Handler.IsRunning(first), "old handle stayed valid after reuse");
            Check(s_Handler.IsRunning(second), "new handle is not running");
            s_Handler.Cancel(second);
        }

        #endregion

        #region 测量工具 [MEASUREMENT UTILITIES]

        private static void RunCase(string caseName, Action action)
        {
            BenchmarkCaseResult result = new BenchmarkCaseResult
            {
                Name = caseName,
                Category = "Timer",
                Trials = TRIAL_COUNT,
            };

            action(); // 预热：JIT 与池状态，不计量

            double min = double.MaxValue;
            double max = 0;
            double sum = 0;
            for (int t = 0; t < TRIAL_COUNT; t++)
            {
                s_Stopwatch.Restart();
                action();
                s_Stopwatch.Stop();

                double ms = s_Stopwatch.Elapsed.TotalMilliseconds;
                if (ms < min) min = ms;
                if (ms > max) max = ms;
                sum += ms;
            }

            result.MinMs = min == double.MaxValue ? 0 : min;
            result.MaxMs = max;
            result.MeanMs = sum / TRIAL_COUNT;
            s_Report.Add(result);
            Debug.Log(result.FormatLine("[TimerBenchmark]"));
        }

        private static void PumpTick(int times)
        {
            for (int i = 0; i < times; i++)
                s_Handler.Tick(0f, 0f);
        }

        private static void ClearAllTimers()
        {
            if (s_Handler == null)
                return;

            EnsureInfoBuffer(16);
            while (true)
            {
                int count = s_Handler.GetAllTimers(s_InfoBuffer);
                if (count <= 0)
                    break;

                for (int i = 0; i < count; i++)
                    s_Handler.Cancel(s_InfoBuffer[i].TimerHandle);

                if (count < s_InfoBuffer.Length)
                    break;

                EnsureInfoBuffer(s_InfoBuffer.Length << 1);
            }
        }

        private static void GetStats(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount)
        {
            s_Handler.GetStatistics(out activeCount, out poolCapacity, out peakActiveCount, out freeCount);
        }

        private static void AssertActiveCount(int expected, string message)
        {
            GetStats(out int activeCount, out _, out _, out _);
            Check(activeCount == expected, message + $" actual={activeCount}");
        }

        #endregion

        #region 软校验 [SOFT CHECKS]

        private static void Check(bool condition, string message)
        {
            if (condition)
                return;

            s_Failures++;
            Debug.LogWarning($"[TimerBenchmark] {message}");
        }

        #endregion

        #region 辅助 [UTILITIES]

        private static void EnsureHandleBuffer(int count)
        {
            if (s_Handles == null || s_Handles.Length < count)
                s_Handles = new ulong[count];
        }

        private static void EnsureInfoBuffer(int count)
        {
            if (s_InfoBuffer == null || s_InfoBuffer.Length < count)
                s_InfoBuffer = new TimerDebugInfo[count];
        }

        #endregion

        #region 回调 [CALLBACKS]

        private static void OnNoOp()
        {
        }

        private static void OnGeneric(BenchmarkArg arg)
        {
        }

        #endregion

        #region 基准类型 [BENCH TYPES]

        private sealed class BenchmarkArg
        {
            public int Value;
        }

        #endregion
    }
}
