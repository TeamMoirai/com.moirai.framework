using System;
using System.Collections;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器性能基准。
    /// <para>测量 TimerService 静态外观（四级时间轮）的 Add/Remove 热路径、控制操作、时间轮插入、
    /// Tick 开销、调试 API、句柄复用、以及 Play Mode 下的回调触发与突发批量处理。</para>
    /// <para>使用方法：挂载到场景 GameObject → Inspector 右键 Run Timer Benchmark，
    /// 或菜单 Window/Moirai/Timer Benchmark 创建实例并自动运行。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Moirai/Timer Benchmark")]
    public sealed class TimerServiceBenchmark : MonoBehaviour
    {
        #region 配置 [CONFIGURATION]

        [SerializeField] private bool m_RunOnStart = true;
        [SerializeField] private bool m_IncludeFireCases = true;
        [SerializeField] private int m_TimerCount = 10000;
        [SerializeField] private int m_LoopCount = 100000;
        [SerializeField] private int m_FireTimerCount = 1024;
        [SerializeField] private int m_BurstFireCount = 4096;
        [SerializeField] private int m_TickLoopCount = 1024;
        [SerializeField] private float m_ControlDuration = 10f;
        [SerializeField] private float m_FireDelay = 0.001f;
        [SerializeField] private float m_FireWaitSeconds = 0.05f;
        [SerializeField] private float m_BurstFireDelay = 0.001f;
        [SerializeField] private bool m_LogEachCase = true;
        [SerializeField] private bool m_LogMemoryDelta = true;
        [SerializeField] private int m_MaxCapturedLogChars = 128 * 1024;

        #endregion

        #region Profiler 标记 [PROFILER MARKERS]

        private static readonly ProfilerMarker s_TotalMarker = new("TimerBenchmark.Total");
        private static readonly ProfilerMarker s_AddRemoveMarker = new("TimerBenchmark.AddRemove");
        private static readonly ProfilerMarker s_ControlMarker = new("TimerBenchmark.Control");
        private static readonly ProfilerMarker s_WheelMarker = new("TimerBenchmark.Wheel");
        private static readonly ProfilerMarker s_DebugMarker = new("TimerBenchmark.Debug");
        private static readonly ProfilerMarker s_BurstMarker = new("TimerBenchmark.BurstTick");

        #endregion

        #region 静态回调 [STATIC CALLBACKS]

        private static readonly Action s_NoOpHandler = OnNoOp;
        private static readonly Action s_CountHandler = OnCount;
        private static readonly Action s_RemoveSelfHandler = OnRemoveSelf;
        private static readonly Action<BenchmarkArg> s_GenericHandler = OnGeneric;
        private static readonly Action<BenchmarkArg> s_GenericCountHandler = OnGenericCount;

        private static int s_CallbackCount;

        #endregion

        #region 字段 [FIELDS]

        private readonly Stopwatch _stopwatch = new();
        private readonly BenchmarkArg _genericArg = new();
        private readonly float[] _wheelDelays =
        {
            0.001f, 0.05f, 0.2f, 1f, 10f, 60f, 300f, 3600f
        };

        private readonly TimerService _tickService = new();

        private StringBuilder _logBuilder = new();
        private ulong[] _handles;
        private TimerDebugInfo[] _infoBuffer;
        private Coroutine _routine;
        private int _failCount;
        private int _caseCount;
        private bool _running;
        private long _caseAllocBefore;
        private long _caseAllocAfter;

        #endregion

        #region 生命周期 [LIFECYCLE]

        private void OnEnable()
        {
            ClearCapturedConsoleOutput();
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            _running = false;
            ClearAllTimers();
        }

        private void Start()
        {
            if (m_RunOnStart)
                RunAll();
        }

        #endregion

        #region 公共入口 [PUBLIC ENTRY]

        /// <summary>
        /// 创建挂载了 TimerServiceBenchmark 的 GameObject 并运行。
        /// </summary>
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Window/Moirai/Timer Benchmark")]
#endif
        public static void RunFromMenu()
        {
            var existing = FindFirstObjectByType<TimerServiceBenchmark>();
            if (existing != null)
            {
                existing.RunAll();
                return;
            }

            var go = new GameObject("[Timer Benchmark]");
            var benchmark = go.AddComponent<TimerServiceBenchmark>();
            benchmark.m_RunOnStart = false;
            benchmark.RunAll();
        }

        /// <summary>
        /// 运行全部基准用例。
        /// </summary>
        [ContextMenu("Run Timer Benchmark")]
        public void RunAll()
        {
            if (_running) return;

            if (_routine != null)
                StopCoroutine(_routine);

            _routine = StartCoroutine(RunAllRoutine());
        }

        /// <summary>
        /// 将捕获的控制台输出复制到系统剪贴板。
        /// </summary>
        [ContextMenu("Copy Captured Console Output")]
        public void CopyCapturedConsoleOutput()
        {
            string text = _logBuilder.ToString();
            GUIUtility.systemCopyBuffer = text;
            Debug.Log($"TimerServiceBenchmark copied console output chars={text.Length}, max={m_MaxCapturedLogChars}");
        }

        /// <summary>
        /// 清空已捕获的控制台输出。
        /// </summary>
        [ContextMenu("Clear Captured Console Output")]
        public void ClearCapturedConsoleOutput()
        {
            _logBuilder.Clear();
        }

        #endregion

        #region 主流程 [MAIN FLOW]

        private IEnumerator RunAllRoutine()
        {
            _running = true;
            ClearCapturedConsoleOutput();
            EnsureService();
            if (!TimerService.IsValid)
            {
                _running = false;
                _routine = null;
                yield break;
            }

            _failCount = 0;
            _caseCount = 0;
            EnsureHandleBuffer(Math.Max(Math.Max(Math.Max(m_TimerCount, m_FireTimerCount), m_BurstFireCount), 8));
            EnsureInfoBuffer(Math.Max(Math.Max(m_TimerCount, m_FireTimerCount), m_BurstFireCount) + 16);
            ClearAllTimers();

            using (s_TotalMarker.Auto())
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

            if (m_IncludeFireCases)
            {
                if (Application.isPlaying)
                {
                    yield return RunFireCase("Fire OneShot", RunFireOneShot);
                    yield return RunFireCase("Fire Loop Callbacks", RunFireLoopCallbacks);
                    yield return RunFireCase("Generic Fire", RunGenericFire);
                    yield return RunFireCase("Remove During Callback", RunRemoveDuringCallback);
                    yield return RunFireCase("Burst Same-Tick OneShot", RunBurstSameTickOneShot);
                    yield return RunFireCase("Burst Same-Tick Loop", RunBurstSameTickLoop);
                }
                else
                {
                    Debug.Log("TimerServiceBenchmark skipped fire cases outside Play Mode.");
                }
            }

            ClearAllTimers();
            Debug.Log($"Timer benchmark finished. cases={_caseCount}, fails={_failCount}");
            _running = false;
            _routine = null;
        }

        #endregion

        #region 服务初始化 [SERVICE INITIALIZATION]

        private void EnsureService()
        {
            if (TimerService.IsValid) return;

            // 触发 HandlerHost 懒加载——从 TimerServiceSettings 创建 DefaultTimerHandler
            _ = TimerService.Handler;
        }

        #endregion

        #region 测量工具 [MEASUREMENT UTILITIES]

        private void RunCase(string caseName, Action action)
        {
            _caseCount++;
            _caseAllocBefore = GetAllocatedBytesForCurrentThread();
            _caseAllocAfter = _caseAllocBefore;
            _stopwatch.Restart();
            action();
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                _caseAllocAfter = GetAllocatedBytesForCurrentThread();
            }

            LogCase(caseName);
        }

        private IEnumerator RunFireCase(string caseName, Func<IEnumerator> action)
        {
            _caseCount++;
            _caseAllocBefore = GetAllocatedBytesForCurrentThread();
            _caseAllocAfter = _caseAllocBefore;
            _stopwatch.Restart();
            yield return action();
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                _caseAllocAfter = GetAllocatedBytesForCurrentThread();
            }

            LogCase(caseName);
        }

        private void LogCase(string caseName)
        {
            if (!m_LogEachCase)
                return;

            if (m_LogMemoryDelta)
                Debug.Log($"[TimerServiceBenchmark] {caseName} ms={_stopwatch.Elapsed.TotalMilliseconds:F4} gcAlloc={_caseAllocAfter - _caseAllocBefore}");
            else
                Debug.Log($"[TimerServiceBenchmark] {caseName} ms={_stopwatch.Elapsed.TotalMilliseconds:F4}");
        }

        private void RestartCaseMeasure()
        {
            _caseAllocBefore = GetAllocatedBytesForCurrentThread();
            _caseAllocAfter = _caseAllocBefore;
            _stopwatch.Restart();
        }

        private void StopCaseMeasure()
        {
            _stopwatch.Stop();
            _caseAllocAfter = GetAllocatedBytesForCurrentThread();
        }

        private long GetAllocatedBytesForCurrentThread()
        {
            return m_LogMemoryDelta ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        }

        #endregion

        #region 用例：Add/Remove 热路径 [ADD/REMOVE HOT LOOPS]

        private void RunAddRemoveOneShotHotLoop()
        {
            using (s_AddRemoveMarker.Auto())
            {
                ClearAllTimers();
                RestartCaseMeasure();
                for (int i = 0; i < m_LoopCount; i++)
                {
                    ulong handle = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration);
                    AssertNotZero(handle, "one-shot add returned invalid handle");
                    TimerService.RemoveTimer(handle);
                }

                StopCaseMeasure();
                AssertActiveCount(0, "one-shot add/remove left active timers");
            }
        }

        private void RunAddRemoveLoopHotLoop()
        {
            using (s_AddRemoveMarker.Auto())
            {
                ClearAllTimers();
                RestartCaseMeasure();
                for (int i = 0; i < m_LoopCount; i++)
                {
                    ulong handle = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration, true);
                    AssertNotZero(handle, "loop add returned invalid handle");
                    TimerService.RemoveTimer(handle);
                }

                StopCaseMeasure();
                AssertActiveCount(0, "loop add/remove left active timers");
            }
        }

        private void RunGenericAddRemoveHotLoop()
        {
            using (s_AddRemoveMarker.Auto())
            {
                ClearAllTimers();
                _genericArg.Value = 0;
                RestartCaseMeasure();
                for (int i = 0; i < m_LoopCount; i++)
                {
                    ulong handle = TimerService.AddTimer(s_GenericHandler, _genericArg, m_ControlDuration);
                    AssertNotZero(handle, "generic add returned invalid handle");
                    TimerService.RemoveTimer(handle);
                }

                StopCaseMeasure();
                AssertActiveCount(0, "generic add/remove left active timers");
            }
        }

        private void RunAddUnscaledOneShot()
        {
            using (s_AddRemoveMarker.Auto())
            {
                int count = Math.Min(m_TimerCount, _handles.Length);
                ClearAllTimers();
                RestartCaseMeasure();
                for (int i = 0; i < count; i++)
                {
                    _handles[i] = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration, false, true);
                    AssertNotZero(_handles[i], "unscaled add returned invalid handle");
                    AssertTrue(TimerService.IsRunning(_handles[i]), "unscaled timer is not running");
                }

                AssertActiveCount(count, "unscaled add active count mismatch");
                for (int i = 0; i < count; i++)
                    TimerService.RemoveTimer(_handles[i]);
                StopCaseMeasure();
                AssertActiveCount(0, "unscaled remove left active timers");
            }
        }

        #endregion

        #region 用例：控制操作 [CONTROL OPERATIONS]

        private void RunStopResume()
        {
            using (s_ControlMarker.Auto())
            {
                ClearAllTimers();
                ulong handle = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration);
                RestartCaseMeasure();
                for (int i = 0; i < m_LoopCount; i++)
                {
                    TimerService.Stop(handle);
                    TimerService.Resume(handle);
                }

                StopCaseMeasure();
                AssertTrue(TimerService.IsRunning(handle), "timer is not running after stop/resume");
                AssertTrue(TimerService.GetLeftTime(handle) > 0f, "timer left time was cleared by stop/resume");
                TimerService.RemoveTimer(handle);
            }
        }

        private void RunRestart()
        {
            using (s_ControlMarker.Auto())
            {
                ClearAllTimers();
                ulong handle = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration);
                TimerService.Stop(handle);
                RestartCaseMeasure();
                for (int i = 0; i < m_LoopCount; i++)
                    TimerService.Restart(handle);
                StopCaseMeasure();

                AssertTrue(TimerService.IsRunning(handle), "timer is not running after restart");
                float leftTime = TimerService.GetLeftTime(handle);
                AssertTrue(leftTime > m_ControlDuration * 0.5f, "restart did not restore remaining time");
                TimerService.RemoveTimer(handle);
            }
        }

        private void RunQueryHotLoop()
        {
            using (s_ControlMarker.Auto())
            {
                ClearAllTimers();
                ulong handle = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration);
                bool running = false;
                float leftTime = 0f;
                RestartCaseMeasure();
                for (int i = 0; i < m_LoopCount; i++)
                {
                    running = TimerService.IsRunning(handle);
                    leftTime = TimerService.GetLeftTime(handle);
                }

                StopCaseMeasure();
                AssertTrue(running, "query hot loop saw timer as not running");
                AssertTrue(leftTime > 0f, "query hot loop saw zero left time");
                TimerService.RemoveTimer(handle);
            }
        }

        #endregion

        #region 用例：时间轮插入 [WHEEL INSERT]

        private void RunMixedDelayWheelInsert()
        {
            using (s_WheelMarker.Auto())
            {
                int count = Math.Min(m_TimerCount, _handles.Length);
                ClearAllTimers();
                RestartCaseMeasure();
                for (int i = 0; i < count; i++)
                {
                    float delay = _wheelDelays[i & (_wheelDelays.Length - 1)];
                    bool isLoop = (i & 1) == 0;
                    bool isUnscaled = (i & 2) == 0;
                    _handles[i] = TimerService.AddTimer(s_NoOpHandler, delay, isLoop, isUnscaled);
                    AssertNotZero(_handles[i], "mixed wheel add returned invalid handle");
                }

                StopCaseMeasure();
                AssertActiveCount(count, "mixed wheel insert active count mismatch");
                for (int i = 0; i < count; i++)
                    TimerService.RemoveTimer(_handles[i]);
                AssertActiveCount(0, "mixed wheel remove left active timers");
            }
        }

        private void RunPageGrowth()
        {
            using (s_WheelMarker.Auto())
            {
                int count = Math.Min(m_TimerCount, _handles.Length);
                ClearAllTimers();
                GetStats(out _, out int capacityBefore, out _, out _);
                RestartCaseMeasure();
                for (int i = 0; i < count; i++)
                    _handles[i] = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration);
                StopCaseMeasure();

                GetStats(out int activeCount, out int capacityAfter, out int peakActiveCount, out int freeCount);
                AssertEqual(activeCount, count, "page growth active count mismatch");
                AssertTrue(capacityAfter >= count, "page growth did not expand capacity");
                AssertTrue(capacityAfter >= capacityBefore, "page growth shrunk capacity");
                AssertTrue(peakActiveCount >= count, "page growth peak active count mismatch");
                AssertTrue(freeCount >= 0, "page growth free count is invalid");

                for (int i = 0; i < count; i++)
                    TimerService.RemoveTimer(_handles[i]);
                AssertActiveCount(0, "page growth remove left active timers");
            }
        }

        #endregion

        #region 用例：防御性检查 [DEFENSIVE GUARDS]

        private void RunInvalidHandleGuards()
        {
            using (s_ControlMarker.Auto())
            {
                ClearAllTimers();
                ulong staleHandle = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration);
                TimerService.RemoveTimer(staleHandle);

                RestartCaseMeasure();
                TimerService.Stop(0UL);
                TimerService.Resume(0UL);
                TimerService.Restart(0UL);
                TimerService.RemoveTimer(0UL);
                TimerService.Stop(staleHandle);
                TimerService.Resume(staleHandle);
                TimerService.Restart(staleHandle);
                TimerService.RemoveTimer(staleHandle);
                bool running = TimerService.IsRunning(staleHandle);
                float leftTime = TimerService.GetLeftTime(staleHandle);
                StopCaseMeasure();

                AssertTrue(!running, "stale handle was reported as running");
                AssertTrue(leftTime == 0f, "stale handle returned leftover time");
                AssertActiveCount(0, "invalid handle guards created timers");
            }
        }

        private void RunNullCallbackGuard()
        {
            ClearAllTimers();
            RestartCaseMeasure();
            ulong noArgsHandle = TimerService.AddTimer(null, m_ControlDuration);
            ulong genericHandle = TimerService.AddTimer<BenchmarkArg>(null, _genericArg, m_ControlDuration);
            StopCaseMeasure();

            AssertEqual((int)noArgsHandle, 0, "null no-args callback produced a handle");
            AssertEqual((int)genericHandle, 0, "null generic callback produced a handle");
            AssertActiveCount(0, "null callback created an active timer");
        }

        #endregion

        #region 用例：Tick 性能 [TICK PERFORMANCE]

        private void RunTickIdle()
        {
            using (s_WheelMarker.Auto())
            {
                ClearAllTimers();
                RestartCaseMeasure();
                TickService(m_TickLoopCount);
                StopCaseMeasure();
                AssertActiveCount(0, "idle tick created timers");
            }
        }

        private void RunTickWithPendingTimers()
        {
            using (s_WheelMarker.Auto())
            {
                int count = Math.Min(m_TimerCount, _handles.Length);
                ClearAllTimers();
                for (int i = 0; i < count; i++)
                    _handles[i] = TimerService.AddTimer(s_NoOpHandler, 3600f, (i & 1) == 0, (i & 2) == 0);

                RestartCaseMeasure();
                TickService(m_TickLoopCount);
                StopCaseMeasure();

                AssertActiveCount(count, "pending tick changed active timer count");
                for (int i = 0; i < count; i++)
                    TimerService.RemoveTimer(_handles[i]);
            }
        }

        #endregion

        #region 用例：调试 API [DEBUG API]

        private void RunGetStatistics()
        {
            using (s_DebugMarker.Auto())
            {
                int count = Math.Min(64, _handles.Length);
                ClearAllTimers();
                for (int i = 0; i < count; i++)
                    _handles[i] = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration);

                RestartCaseMeasure();
                int activeCount = 0;
                int poolCapacity = 0;
                int peakActiveCount = 0;
                int freeCount = 0;
                for (int i = 0; i < m_LoopCount; i++)
                    TimerService.GetStatistics(out activeCount, out poolCapacity, out peakActiveCount, out freeCount);
                StopCaseMeasure();

                AssertEqual(activeCount, count, "statistics active count mismatch");
                AssertTrue(poolCapacity >= count, "statistics capacity smaller than active count");
                AssertTrue(peakActiveCount >= count, "statistics peak smaller than active count");
                AssertTrue(freeCount == poolCapacity - count, "statistics free count mismatch");

                for (int i = 0; i < count; i++)
                    TimerService.RemoveTimer(_handles[i]);
            }
        }

        private void RunGetAllTimersBuffer()
        {
            using (s_DebugMarker.Auto())
            {
                int count = Math.Min(64, _handles.Length);
                ClearAllTimers();
                for (int i = 0; i < count; i++)
                    _handles[i] = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration, i == 0, i == 1);

                EnsureInfoBuffer(count);
                RestartCaseMeasure();
                int filled = 0;
                for (int i = 0; i < m_LoopCount; i++)
                    filled = TimerService.GetAllTimers(_infoBuffer);
                StopCaseMeasure();

                AssertEqual(filled, count, "debug buffer fill count mismatch");
                AssertEqual(TimerService.GetAllTimers(null), 0, "null debug buffer should return 0");
                AssertEqual(TimerService.GetAllTimers(Array.Empty<TimerDebugInfo>()), 0, "empty debug buffer should return 0");

                bool foundRunning = false;
                bool foundLoop = false;
                bool foundUnscaled = false;
                for (int i = 0; i < filled; i++)
                {
                    if ((_infoBuffer[i].Flags & TimerDebugFlags.RUNNING) != 0)
                        foundRunning = true;
                    if ((_infoBuffer[i].Flags & TimerDebugFlags.LOOP) != 0)
                        foundLoop = true;
                    if ((_infoBuffer[i].Flags & TimerDebugFlags.UNSCALED) != 0)
                        foundUnscaled = true;
                }

                AssertTrue(foundRunning, "debug buffer missed running flag");
                AssertTrue(foundLoop, "debug buffer missed loop flag");
                AssertTrue(foundUnscaled, "debug buffer missed unscaled flag");

                for (int i = 0; i < count; i++)
                    TimerService.RemoveTimer(_handles[i]);
            }
        }

        #endregion

        #region 用例：句柄复用 [HANDLE REUSE]

        private void RunHandleReuseAfterRemove()
        {
            ClearAllTimers();
            ulong first = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration);
            TimerService.RemoveTimer(first);
            ulong second = TimerService.AddTimer(s_NoOpHandler, m_ControlDuration);
            AssertTrue(first != 0UL && second != 0UL, "handle reuse produced an invalid handle");
            AssertTrue(first != second, "removed handle was reused without version change");
            AssertTrue(!TimerService.IsRunning(first), "old handle stayed valid after reuse");
            AssertTrue(TimerService.IsRunning(second), "new handle is not running");
            TimerService.RemoveTimer(second);
        }

        #endregion

        #region 用例：回调触发 [FIRE CASES]

        private IEnumerator RunFireOneShot()
        {
            int count = Math.Min(m_FireTimerCount, _handles.Length);
            ClearAllTimers();
            s_CallbackCount = 0;
            for (int i = 0; i < count; i++)
                _handles[i] = TimerService.AddTimer(s_CountHandler, m_FireDelay);

            RestartCaseMeasure();
            yield return WaitForFire();
            StopCaseMeasure();

            AssertEqual(s_CallbackCount, count, "one-shot fire callback count mismatch");
            AssertActiveCount(0, "one-shot fire left active timers");
        }

        private IEnumerator RunFireLoopCallbacks()
        {
            int count = Math.Min(m_FireTimerCount, _handles.Length);
            ClearAllTimers();
            s_CallbackCount = 0;
            for (int i = 0; i < count; i++)
                _handles[i] = TimerService.AddTimer(s_CountHandler, m_FireDelay, true);

            RestartCaseMeasure();
            yield return WaitForFire();
            StopCaseMeasure();

            AssertTrue(s_CallbackCount >= count, "loop fire did not invoke callbacks");
            AssertActiveCount(count, "loop fire changed active timer count");
            for (int i = 0; i < count; i++)
                TimerService.RemoveTimer(_handles[i]);
            AssertActiveCount(0, "loop fire remove left active timers");
        }

        private IEnumerator RunGenericFire()
        {
            int count = Math.Min(m_FireTimerCount, _handles.Length);
            ClearAllTimers();
            _genericArg.Value = 0;
            for (int i = 0; i < count; i++)
                _handles[i] = TimerService.AddTimer(s_GenericCountHandler, _genericArg, m_FireDelay);

            RestartCaseMeasure();
            yield return WaitForFire();
            StopCaseMeasure();

            AssertEqual(_genericArg.Value, count, "generic fire callback count mismatch");
            AssertActiveCount(0, "generic fire left active timers");
        }

        private IEnumerator RunRemoveDuringCallback()
        {
            ClearAllTimers();
            s_CallbackService = this;
            s_CallbackHandle = TimerService.AddTimer(s_RemoveSelfHandler, m_FireDelay, true);
            AssertNotZero(s_CallbackHandle, "remove-during-callback add returned invalid handle");

            RestartCaseMeasure();
            yield return WaitForFire();
            StopCaseMeasure();

            AssertTrue(!TimerService.IsRunning(s_CallbackHandle), "self-removed timer is still running");
            AssertActiveCount(0, "self-removed timer stayed active");
            s_CallbackService = null;
            s_CallbackHandle = 0UL;
        }

        private IEnumerator RunBurstSameTickOneShot()
        {
            yield return RunIsolatedBurstTick("oneshot", false);
        }

        private IEnumerator RunBurstSameTickLoop()
        {
            yield return RunIsolatedBurstTick("loop", true);
        }

        private IEnumerator RunIsolatedBurstTick(string label, bool isLoop)
        {
            int count = Math.Max(1, Math.Min(m_BurstFireCount, _handles.Length));
            float delay = m_BurstFireDelay > 0.001f ? m_BurstFireDelay : 0.001f;
            ClearAllTimers();
            s_CallbackCount = 0;

            for (int i = 0; i < count; i++)
            {
                _handles[i] = TimerService.AddTimer(s_CountHandler, delay, isLoop, true);
                AssertNotZero(_handles[i], "burst add returned invalid handle");
            }

            TimerService.GetStatistics(out int setupActive, out _, out _, out _);
            AssertEqual(setupActive, count, "burst setup active count mismatch");

            float waitSeconds = delay + 0.004f;
            float endTime = Time.unscaledTime + waitSeconds;
            while (Time.unscaledTime < endTime)
                yield return null;

            RestartCaseMeasure();
            using (s_BurstMarker.Auto())
                _tickService.Tick(0f, 0f);
            StopCaseMeasure();

            if (isLoop)
            {
                AssertEqual(s_CallbackCount, count, "burst loop tick callback count mismatch");
                TimerService.GetStatistics(out int activeAfter, out _, out _, out _);
                AssertEqual(activeAfter, count, "burst loop tick changed active count");
                for (int i = 0; i < count; i++)
                    TimerService.RemoveTimer(_handles[i]);
            }
            else
            {
                AssertEqual(s_CallbackCount, count, "burst oneshot tick callback count mismatch");
                TimerService.GetStatistics(out int activeAfter, out _, out _, out _);
                AssertEqual(activeAfter, 0, "burst oneshot tick left active timers");
            }

            TimerService.GetStatistics(out int leftover, out _, out _, out _);
            AssertEqual(leftover, 0, "burst leftover timers after cleanup");
            Debug.Log($"[TimerServiceBenchmark] Burst {label} count={count} callbacks={s_CallbackCount}");
        }

        private IEnumerator WaitForFire()
        {
            float endTime = Time.unscaledTime + Mathf.Max(0.02f, m_FireWaitSeconds);
            while (Time.unscaledTime < endTime)
                yield return null;
        }

        #endregion

        #region 辅助方法 [UTILITIES]

        private void TickService(int times)
        {
            for (int i = 0; i < times; i++)
                _tickService.Tick(0f, 0f);
        }

        private void ClearAllTimers()
        {
            if (!TimerService.IsValid)
                return;

            EnsureInfoBuffer(16);
            while (true)
            {
                int count = TimerService.GetAllTimers(_infoBuffer);
                if (count <= 0)
                    break;

                for (int i = 0; i < count; i++)
                    TimerService.RemoveTimer(_infoBuffer[i].TimerHandle);

                if (count < _infoBuffer.Length)
                    break;

                EnsureInfoBuffer(_infoBuffer.Length << 1);
            }
        }

        private void GetStats(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount)
        {
            TimerService.GetStatistics(out activeCount, out poolCapacity, out peakActiveCount, out freeCount);
        }

        private void EnsureHandleBuffer(int count)
        {
            if (_handles == null || _handles.Length < count)
                _handles = new ulong[count];
        }

        private void EnsureInfoBuffer(int count)
        {
            if (_infoBuffer == null || _infoBuffer.Length < count)
                _infoBuffer = new TimerDebugInfo[count];
        }

        private void AssertActiveCount(int expected, string message)
        {
            GetStats(out int activeCount, out _, out _, out _);
            AssertEqual(activeCount, expected, message);
        }

        private void AssertNotZero(ulong handle, string message)
        {
            if (handle != 0UL)
                return;

            _failCount++;
            Debug.LogError(message);
        }

        private void AssertTrue(bool value, string message)
        {
            if (value)
                return;

            _failCount++;
            Debug.LogError(message);
        }

        private void AssertEqual(int actual, int expected, string message)
        {
            if (actual == expected)
                return;

            _failCount++;
            Debug.LogError($"{message} actual={actual}, expected={expected}");
        }

        #endregion

        #region 控制台捕获 [CONSOLE CAPTURE]

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (_logBuilder.Length >= m_MaxCapturedLogChars)
                return;

            _logBuilder.Append('[');
            _logBuilder.Append(type);
            _logBuilder.Append("] ");
            _logBuilder.Append(condition);
            _logBuilder.AppendLine();

            if (type is LogType.Exception or LogType.Error or LogType.Assert)
            {
                _logBuilder.Append(stackTrace);
                _logBuilder.AppendLine();
            }
        }

        #endregion

        #region 静态回调实现 [STATIC CALLBACK IMPLEMENTATIONS]

        private static void OnNoOp()
        {
        }

        private static void OnCount()
        {
            s_CallbackCount++;
        }

        private static void OnRemoveSelf()
        {
            if (s_CallbackService != null)
                TimerService.RemoveTimer(s_CallbackHandle);
        }

        private static void OnGeneric(BenchmarkArg arg)
        {
        }

        private static void OnGenericCount(BenchmarkArg arg)
        {
            arg.Value++;
        }

        #endregion

        #region 内部类型 [INTERNAL TYPES]

        private sealed class BenchmarkArg
        {
            public int Value;
        }

        #endregion

        #region 静态状态 [STATIC STATE]

        private static TimerServiceBenchmark s_CallbackService;
        private static ulong s_CallbackHandle;

        #endregion
    }
}