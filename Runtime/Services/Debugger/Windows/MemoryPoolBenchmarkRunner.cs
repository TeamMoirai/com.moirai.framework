using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Mp = Moirai.Atropos.MemoryPool;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 内存池性能基准运行时驱动器：跑完整用例矩阵并产出统一的 <see cref="BenchmarkReport"/>。
    /// <para>放运行程序集以便 <see cref="MemoryPoolInformationWindow"/> 直接触发（Debugger 是运行时窗口，
    /// 够不到测试程序集里的 [Test]）。测量：每用例先跑一轮预热再计时 N 次取最小 ms，采 GC 分配增量；
    /// 编辑器 Mono 噪声约 ±2×，数据用于同配置 before/after 趋势对照。</para>
    /// <para>不变量校验降级为软校验：命中问题只累加 failures 计数并 LogWarning，不抛出——正确性回归由
    /// MemoryPoolMaintenanceTests / MemoryPoolOwnershipTests 负责。</para>
    /// </summary>
    public static class MemoryPoolBenchmarkRunner
    {
        #region 配置 [CONFIGURATION]

        private const int TRIAL_COUNT = 5;
        private const int OBJECT_COUNT = 10000;
        private const int LOOP_COUNT = 100000;
        private const int BURST_SIZE = 4096;
        private const int EXTREME_BURST_SIZE = 32768;
        private const int WAVE_COUNT = 24;
        private const int MULTI_TYPE_COUNT = 2048;
        private const int ADAPTIVE_FRAME_COUNT = 420;
        private const int DECAY_FRAMES = 48;

        private const EMemoryPoolPhase MEASURE_PHASE = EMemoryPoolPhase.Gameplay;

        #endregion

        #region 静态状态 [SHARED STATE]

        private static readonly Stopwatch s_Stopwatch = new();
        private static BenchmarkReport s_Report;

        private static BenchMemory[] s_Buffer = Array.Empty<BenchMemory>();
        private static MultiTypeA[] s_MultiA = Array.Empty<MultiTypeA>();
        private static MultiTypeB[] s_MultiB = Array.Empty<MultiTypeB>();
        private static MultiTypeC[] s_MultiC = Array.Empty<MultiTypeC>();
        private static MemoryPoolInfo[] s_InfoBuffer = Array.Empty<MemoryPoolInfo>();

        private static int s_TickFrame;
        private static int s_Failures;

        #endregion

        #region 入口 [ENTRY]

        /// <summary>
        /// 同步跑完全部基准用例并返回报告。整轮在 Gameplay 相位下测，收尾恢复相位并清空池。
        /// </summary>
        public static BenchmarkReport Run()
        {
            BenchmarkReport report = new BenchmarkReport("MemoryPool");
            report.SetMetadata("phase", MEASURE_PHASE.ToString());
            report.SetMetadata("trials", TRIAL_COUNT.ToString());
            s_Report = report;
            s_Failures = 0;
            s_TickFrame = 100000;

            EnsureBuffer(Math.Max(Math.Max(OBJECT_COUNT, BURST_SIZE), Math.Max(EXTREME_BURST_SIZE, MULTI_TYPE_COUNT)));
            EnsureMultiBuffer(MULTI_TYPE_COUNT);
            EnsureInfoBuffer(64);

            EMemoryPoolPhase previousPhase = MemoryPoolRegistry.Phase;
            MemoryPoolRegistry.Phase = MEASURE_PHASE;
            long wallStart = Stopwatch.GetTimestamp();
            try
            {
                RunApiSurfaceCases();
                RunBurstCases();
                RunMaintenanceCases();
            }
            finally
            {
                MemoryPoolRegistry.Phase = previousPhase;
                Mp.ClearAll();
            }

            report.TotalMs = (Stopwatch.GetTimestamp() - wallStart) * 1000.0 / Stopwatch.Frequency;
            report.SetMetadata("failures", s_Failures.ToString());
            Debug.Log($"[MemoryPoolBenchmark] cases={report.CaseCount}, failures={s_Failures}, total={report.TotalMs:F1}ms");
            return report;
        }

        #endregion

        #region 用例：API 表面热路径 [API SURFACE HOT PATHS]

        private static void RunApiSurfaceCases()
        {
            Measure("Direct Acquire/Release", "HotPath", LOOP_COUNT, TRIAL_COUNT,
                () => WarmBenchPool(),
                () =>
                {
                    for (int i = 0; i < LOOP_COUNT; i++)
                    {
                        BenchMemory item = MemoryPool<BenchMemory>.Acquire();
                        item.Value = i;
                        MemoryPool<BenchMemory>.Release(item);
                    }
                },
                result =>
                {
                    CheckZeroAlloc(result, "直连热路径产生了 GC 分配");
                    CheckUsed(0, "直连热路径结束后仍有对象在外");
                });

            Measure("Facade Acquire / Owner Release", "ApiSurface", LOOP_COUNT, TRIAL_COUNT,
                () => WarmBenchPool(),
                () =>
                {
                    for (int i = 0; i < LOOP_COUNT; i++)
                    {
                        BenchMemory item = Mp.Acquire<BenchMemory>();
                        Mp.Release((MemoryObject)item);
                    }
                },
                result =>
                {
                    CheckZeroAlloc(result, "owner 归还热路径产生了 GC 分配");
                    CheckUsed(0, "owner 归还热路径结束后仍有对象在外");
                });

            Measure("Facade Acquire<T> / Release<T>", "ApiSurface", LOOP_COUNT, TRIAL_COUNT,
                () => WarmBenchPool(),
                () =>
                {
                    for (int i = 0; i < LOOP_COUNT; i++)
                    {
                        BenchMemory item = Mp.Acquire<BenchMemory>();
                        Mp.Release<BenchMemory>(item);
                    }
                },
                result =>
                {
                    CheckZeroAlloc(result, "Release<T> 热路径产生了 GC 分配");
                    CheckUsed(0, "Release<T> 热路径结束后仍有对象在外");
                });

            Measure("Facade Acquire / Direct Release", "ApiSurface", LOOP_COUNT, TRIAL_COUNT,
                () => WarmBenchPool(),
                () =>
                {
                    for (int i = 0; i < LOOP_COUNT; i++)
                    {
                        BenchMemory item = Mp.Acquire<BenchMemory>();
                        MemoryPool<BenchMemory>.Release(item);
                    }
                },
                result =>
                {
                    CheckZeroAlloc(result, "混合取还热路径产生了 GC 分配");
                    CheckUsed(0, "混合取还热路径结束后仍有对象在外");
                });

            MemoryPoolHandle handle = default;
            Measure("Cached Handle Acquire/Release", "ApiSurface", LOOP_COUNT, TRIAL_COUNT,
                () =>
                {
                    WarmBenchPool();
                    handle = Mp.GetHandle(typeof(BenchMemory));
                },
                () =>
                {
                    for (int i = 0; i < LOOP_COUNT; i++)
                    {
                        MemoryObject item = handle.Acquire();
                        handle.Release(item);
                    }
                },
                result =>
                {
                    CheckZeroAlloc(result, "缓存句柄热路径产生了 GC 分配");
                    CheckUsed(0, "缓存句柄热路径结束后仍有对象在外");
                });

            Measure("Interleaved Acquire/Release", "HotPath", OBJECT_COUNT, TRIAL_COUNT,
                () => WarmBenchPool(),
                () =>
                {
                    int count = Math.Min(OBJECT_COUNT, s_Buffer.Length);
                    for (int i = 0; i < count; i++)
                    {
                        BenchMemory item = MemoryPool<BenchMemory>.Acquire();
                        if ((i & 1) == 0)
                            MemoryPool<BenchMemory>.Release(item);
                        else
                            s_Buffer[i] = item;
                    }

                    for (int i = 1; i < count; i += 2)
                    {
                        MemoryPool<BenchMemory>.Release(s_Buffer[i]);
                        s_Buffer[i] = null;
                    }
                },
                result => CheckUsed(0, "交错取还结束后仍有对象在外"));

            Measure("Dynamic Type Acquire/Release", "Dynamic", LOOP_COUNT, TRIAL_COUNT,
                () =>
                {
                    MemoryPool<DynamicMemory>.SetCapacity(OBJECT_COUNT, OBJECT_COUNT << 2);
                    WarmPool<DynamicMemory>(OBJECT_COUNT);
                },
                () =>
                {
                    for (int i = 0; i < LOOP_COUNT; i++)
                    {
                        MemoryObject item = Mp.Acquire(typeof(DynamicMemory));
                        Mp.Release(item);
                    }
                },
                result =>
                {
                    CheckZeroAlloc(result, "动态 Type 热路径产生了 GC 分配");
                    CheckUsed(0, "动态 Type 热路径结束后仍有对象在外", typeof(DynamicMemory));
                });
        }

        #endregion

        #region 用例：突发与衰减 [BURST & DECAY]

        private static void RunBurstCases()
        {
            int burst = Math.Min(BURST_SIZE, s_Buffer.Length);
            Measure("Adaptive Burst Fill", "Burst", burst * 2, TRIAL_COUNT,
                () => MemoryPool<BenchMemory>.SetCapacity(Math.Max(64, burst >> 1), burst << 1),
                () =>
                {
                    for (int i = 0; i < burst; i++)
                        s_Buffer[i] = MemoryPool<BenchMemory>.Acquire();
                    for (int i = 0; i < burst; i++)
                    {
                        MemoryPool<BenchMemory>.Release(s_Buffer[i]);
                        s_Buffer[i] = null;
                    }
                },
                result =>
                {
                    MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
                    result.Metric("reserve", info.UnusedCount).Metric("pages", info.PageCapacity);
                    Check(info.UnusedCount > 0, "突发填充未保留储备");
                    Check(info.PageCapacity >= info.UnusedCount, "页容量小于空闲量");
                });

            int prevShort = 0;
            int prevLong = 0;
            int prevZero = 0;
            Measure("Idle Decay Tick", "Decay", DECAY_FRAMES, 1,
                () =>
                {
                    MemoryPool<BenchMemory>.SetCapacity(burst, burst << 1);
                    WarmPool<BenchMemory>(burst);
                    prevShort = Mp.ShortDecayStartFrames;
                    prevLong = Mp.LongDecayStartFrames;
                    prevZero = Mp.ZeroFreeReserveStartFrames;
                    Mp.ShortDecayStartFrames = 4;
                    Mp.LongDecayStartFrames = 8;
                    Mp.ZeroFreeReserveStartFrames = 8;
                },
                () => TickAll(DECAY_FRAMES),
                result =>
                {
                    Mp.ShortDecayStartFrames = prevShort;
                    Mp.LongDecayStartFrames = prevLong;
                    Mp.ZeroFreeReserveStartFrames = prevZero;
                    MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
                    result.Metric("reserve", info.UnusedCount).Metric("frames", DECAY_FRAMES);
                    Check(info.PageCapacity >= info.UnusedCount, "衰减后页容量小于空闲量");
                });

            int wave = Math.Min(BURST_SIZE, s_Buffer.Length);
            Measure("Wave Burst Anti-Thrash", "Extreme", WAVE_COUNT * (wave + (wave >> 2)), TRIAL_COUNT,
                () => MemoryPool<BenchMemory>.SetCapacity(wave, wave << 1),
                () =>
                {
                    for (int w = 0; w < WAVE_COUNT; w++)
                    {
                        int waveSize = (w & 1) == 0 ? wave : wave >> 2;
                        for (int i = 0; i < waveSize; i++)
                            s_Buffer[i] = MemoryPool<BenchMemory>.Acquire();
                        for (int i = 0; i < waveSize; i++)
                        {
                            MemoryPool<BenchMemory>.Release(s_Buffer[i]);
                            s_Buffer[i] = null;
                        }

                        TickAll(12);
                    }
                },
                result =>
                {
                    MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
                    result.Metric("reserve", info.UnusedCount).Metric("pages", info.PageCapacity);
                    Check(info.UnusedCount > 0, "波浪突发未保留储备");
                    Check(info.PageCapacity >= info.UnusedCount, "波浪突发页容量小于空闲量");
                });

            int extreme = Math.Min(EXTREME_BURST_SIZE, s_Buffer.Length);
            Measure("Extreme Single Burst", "Extreme", extreme * 2, 1,
                () => MemoryPool<BenchMemory>.SetCapacity(Math.Max(128, extreme >> 2), extreme),
                () =>
                {
                    for (int i = 0; i < extreme; i++)
                        s_Buffer[i] = MemoryPool<BenchMemory>.Acquire();
                    for (int i = 0; i < extreme; i++)
                    {
                        MemoryPool<BenchMemory>.Release(s_Buffer[i]);
                        s_Buffer[i] = null;
                    }
                },
                result =>
                {
                    MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
                    result.Metric("reserve", info.UnusedCount).Metric("pages", info.PageCapacity);
                    Check(info.UnusedCount == extreme, "极端突发硬上限内未留全部对象");
                    Check(info.PageCapacity >= info.UnusedCount, "极端突发页容量小于空闲量");
                });

            int overflow = Math.Min(BURST_SIZE, s_Buffer.Length);
            int hard = Math.Max(32, overflow >> 3);
            Measure("Hard Capacity Overflow", "Extreme", overflow * 2, 1,
                () => MemoryPool<BenchMemory>.SetCapacity(hard >> 1, hard),
                () =>
                {
                    for (int i = 0; i < overflow; i++)
                        s_Buffer[i] = MemoryPool<BenchMemory>.Acquire();
                    for (int i = 0; i < overflow; i++)
                    {
                        MemoryPool<BenchMemory>.Release(s_Buffer[i]);
                        s_Buffer[i] = null;
                    }
                },
                result =>
                {
                    MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
                    result.Metric("reserve", info.UnusedCount).Metric("hard", hard);
                    Check(info.UnusedCount == hard, "越限保留了超过硬上限的对象");
                    Check(info.PageCapacity >= hard, "越限页容量小于硬上限");
                });

            int multi = Math.Min(MULTI_TYPE_COUNT, s_MultiA.Length);
            Measure("Multi Type Active Queue", "MultiType", multi * 6, TRIAL_COUNT,
                () =>
                {
                    MemoryPool<MultiTypeA>.SetCapacity(multi, multi << 1);
                    MemoryPool<MultiTypeB>.SetCapacity(multi, multi << 1);
                    MemoryPool<MultiTypeC>.SetCapacity(multi, multi << 1);
                },
                () =>
                {
                    for (int i = 0; i < multi; i++)
                    {
                        s_MultiA[i] = Mp.Acquire<MultiTypeA>();
                        s_MultiB[i] = Mp.Acquire<MultiTypeB>();
                        s_MultiC[i] = Mp.Acquire<MultiTypeC>();
                    }

                    for (int i = 0; i < multi; i++)
                    {
                        Mp.Release(s_MultiA[i]);
                        Mp.Release(s_MultiB[i]);
                        Mp.Release(s_MultiC[i]);
                    }

                    TickAll(ADAPTIVE_FRAME_COUNT);
                },
                result =>
                {
                    Check(GetInfo(typeof(MultiTypeA)).UnusedCount > 0, "类型 A 未 tick");
                    Check(GetInfo(typeof(MultiTypeB)).UnusedCount > 0, "类型 B 未 tick");
                    Check(GetInfo(typeof(MultiTypeC)).UnusedCount > 0, "类型 C 未 tick");
                });
        }

        #endregion

        #region 用例：维护路径 [MAINTENANCE PATHS]

        private static void RunMaintenanceCases()
        {
            int count = 96;
            Measure("Page Boundary Reuse", "PageReuse", count * 2, TRIAL_COUNT,
                () =>
                {
                    MemoryPool<BenchMemory>.SetCapacity(256, 1024);
                    for (int i = 0; i < count; i++)
                        s_Buffer[i] = MemoryPool<BenchMemory>.Acquire();
                    for (int i = 0; i < count; i++)
                    {
                        MemoryPool<BenchMemory>.Release(s_Buffer[i]);
                        s_Buffer[i] = null;
                    }

                    TickAll(1);
                },
                () =>
                {
                    for (int i = 0; i < count; i++)
                        s_Buffer[i] = MemoryPool<BenchMemory>.Acquire();
                    for (int i = 0; i < count; i++)
                    {
                        MemoryPool<BenchMemory>.Release(s_Buffer[i]);
                        s_Buffer[i] = null;
                    }
                },
                result =>
                {
                    MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
                    result.Metric("pages", info.PageCapacity);
                    CheckZeroAlloc(result, "跨页复用产生了 GC 分配");
                    Check(info.PageCapacity >= count, "页边界未分配足够页容量");
                });

            Measure("Explicit Compact", "Compact", 1, TRIAL_COUNT,
                () => WarmBenchPool(),
                () =>
                {
                    MemoryPool<BenchMemory>.Shrink(8);
                    MemoryPool<BenchMemory>.Compact();
                },
                result =>
                {
                    MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
                    result.Metric("reserve", info.UnusedCount);
                    Check(info.UnusedCount <= 8, "Compact 未收敛到目标水位");
                });

            Measure("Info Buffer Fill", "Info", LOOP_COUNT, TRIAL_COUNT,
                () =>
                {
                    WarmBenchPool();
                    EnsureInfoBuffer(Math.Max(16, Mp.Count));
                },
                () =>
                {
                    for (int i = 0; i < LOOP_COUNT; i++)
                        Mp.GetAllMemoryPoolInfos(s_InfoBuffer);
                },
                result =>
                {
                    CheckZeroAlloc(result, "GetAllMemoryPoolInfos 产生了 GC 分配");
                    result.Metric("pools", Mp.Count);
                });

            int prevSoft = 0;
            int prevHard = 0;
            Measure("Global Maintenance (Remove/CompactAll)", "Compact", 1, TRIAL_COUNT,
                () =>
                {
                    MemoryPool<BenchMemory>.SetCapacity(64, 256);
                    WarmPool<BenchMemory>(64);
                    prevSoft = Mp.DefaultSoftFreeReserveLimit;
                    prevHard = Mp.DefaultHardFreeReserveLimit;
                },
                () =>
                {
                    Mp.SetDefaultCapacity(8, 16);
                    Mp.Remove<BenchMemory>(16);
                    Mp.CompactAll();
                },
                result =>
                {
                    // 全局默认先还原，避免泄漏给后续用例。
                    Mp.SetDefaultCapacity(prevSoft, prevHard);
                    result.Metric("reserve", GetInfo(typeof(BenchMemory)).UnusedCount);
                });

            Measure("Many Type Handle Cache", "ApiSurface", s_HandleTypes.Length, TRIAL_COUNT,
                null,
                () =>
                {
                    for (int i = 0; i < s_HandleTypes.Length; i++)
                    {
                        MemoryPoolHandle handle = Mp.GetHandle(s_HandleTypes[i]);
                        handle.Release(handle.Acquire());
                    }
                },
                result =>
                {
                    result.Metric("types", s_HandleTypes.Length);
                    for (int i = 0; i < s_HandleTypes.Length; i++)
                        Check(GetInfo(s_HandleTypes[i]).AcquireCount > 0, $"类型 {s_HandleTypes[i].Name} 未物化");
                });
        }

        #endregion

        #region 测量工具 [MEASUREMENT UTILITIES]

        private static BenchmarkCaseResult Measure(string name, string category, int iterations, int trials,
            Action setup, Action measured, Action<BenchmarkCaseResult> verify)
        {
            BenchmarkCaseResult result = new BenchmarkCaseResult
            {
                Name = name,
                Category = category,
                Iterations = iterations,
                Trials = Math.Max(1, trials),
            };

            Mp.ClearAll();
            setup?.Invoke();
            measured(); // 预热：JIT 与池状态，不计量

            double min = double.MaxValue;
            double max = 0;
            double sum = 0;
            for (int t = 0; t < result.Trials; t++)
            {
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                s_Stopwatch.Restart();
                measured();
                s_Stopwatch.Stop();
                long allocAfter = GC.GetAllocatedBytesForCurrentThread();

                if (t == 0)
                    result.GcAllocBytes = allocAfter - allocBefore;

                double ms = s_Stopwatch.Elapsed.TotalMilliseconds;
                if (ms < min) min = ms;
                if (ms > max) max = ms;
                sum += ms;
            }

            result.MinMs = min == double.MaxValue ? 0 : min;
            result.MaxMs = max;
            result.MeanMs = sum / result.Trials;
            if (iterations > 0)
                result.NsPerOp = result.MinMs * 1_000_000.0 / iterations;

            verify?.Invoke(result);
            s_Report.Add(result);
            Debug.Log(result.FormatLine("[MemoryPoolBenchmark]"));
            return result;
        }

        private static void WarmBenchPool()
        {
            MemoryPool<BenchMemory>.SetCapacity(OBJECT_COUNT, OBJECT_COUNT << 2);
            WarmPool<BenchMemory>(OBJECT_COUNT);
        }

        private static void WarmPool<T>(int count) where T : MemoryObject, new()
        {
            EMemoryPoolPhase previousPhase = MemoryPoolRegistry.Phase;
            MemoryPoolRegistry.Phase = EMemoryPoolPhase.Loading;
            Mp.Add<T>(count);
            int guard = (count >> 5) + 64;
            while (MemoryPool<T>.UnusedCount < count && guard-- > 0)
                TickAll(1);
            MemoryPoolRegistry.Phase = previousPhase;
        }

        private static void TickAll(int frames)
        {
            for (int i = 0; i < frames; i++)
                MemoryPoolRegistry.TickAll(++s_TickFrame);
        }

        private static MemoryPoolInfo GetInfo(Type targetType)
        {
            EnsureInfoBuffer(Math.Max(1, Mp.Count));
            int actual = Mp.GetAllMemoryPoolInfos(s_InfoBuffer);
            for (int i = 0; i < actual; i++)
            {
                if (s_InfoBuffer[i].Type == targetType)
                    return s_InfoBuffer[i];
            }

            return default;
        }

        #region 软校验 [SOFT CHECKS]

        private static void Check(bool condition, string message)
        {
            if (condition)
                return;

            s_Failures++;
            Debug.LogWarning($"[MemoryPoolBenchmark] {message}");
        }

        private static void CheckZeroAlloc(BenchmarkCaseResult result, string message)
        {
            Check(result.GcAllocBytes == 0, message + " gcAlloc=" + result.GcAllocBytes);
        }

        private static void CheckUsed(int expected, string message, Type type = null)
        {
            Check(GetInfo(type ?? typeof(BenchMemory)).UsingCount == expected, message);
        }

        #endregion

        private static void EnsureBuffer(int count)
        {
            if (s_Buffer == null || s_Buffer.Length < count)
                s_Buffer = new BenchMemory[count];
        }

        private static void EnsureMultiBuffer(int count)
        {
            if (s_MultiA == null || s_MultiA.Length < count)
            {
                s_MultiA = new MultiTypeA[count];
                s_MultiB = new MultiTypeB[count];
                s_MultiC = new MultiTypeC[count];
            }
        }

        private static void EnsureInfoBuffer(int count)
        {
            if (s_InfoBuffer == null || s_InfoBuffer.Length < count)
                s_InfoBuffer = new MemoryPoolInfo[count];
        }

        #endregion

        #region 基准类型 [BENCH TYPES]

        private sealed class BenchMemory : MemoryObject
        {
            public int Value;

            public override void Clear()
            {
                Value = 0;
            }
        }

        private sealed class DynamicMemory : MemoryObject
        {
            public override void Clear() { }
        }

        private sealed class MultiTypeA : MemoryObject
        {
            public override void Clear() { }
        }

        private sealed class MultiTypeB : MemoryObject
        {
            public override void Clear() { }
        }

        private sealed class MultiTypeC : MemoryObject
        {
            public override void Clear() { }
        }

        private static readonly Type[] s_HandleTypes =
        {
            typeof(Handle01), typeof(Handle02), typeof(Handle03), typeof(Handle04),
            typeof(Handle05), typeof(Handle06), typeof(Handle07), typeof(Handle08),
        };

        private sealed class Handle01 : MemoryObject { public override void Clear() { } }
        private sealed class Handle02 : MemoryObject { public override void Clear() { } }
        private sealed class Handle03 : MemoryObject { public override void Clear() { } }
        private sealed class Handle04 : MemoryObject { public override void Clear() { } }
        private sealed class Handle05 : MemoryObject { public override void Clear() { } }
        private sealed class Handle06 : MemoryObject { public override void Clear() { } }
        private sealed class Handle07 : MemoryObject { public override void Clear() { } }
        private sealed class Handle08 : MemoryObject { public override void Clear() { } }

        #endregion
    }
}
