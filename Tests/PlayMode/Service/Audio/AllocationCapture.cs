using System;
using System.Diagnostics;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>一次分配测量的汇总结果。</summary>
    internal readonly struct AllocationSample
    {
        internal readonly long Bytes;
        internal readonly int Allocations;
        internal readonly double Milliseconds;

        internal AllocationSample(long bytes, int allocations, double milliseconds)
        {
            Bytes = bytes;
            Allocations = allocations;
            Milliseconds = milliseconds;
        }
    }

    /// <summary>
    /// 0-GC 测量台：主路径用当前线程托管分配计数器（便携、不依赖 Profiler 连接）。
    /// <para>约定：MeasureManaged 先做一次预热并丢弃，再测 <c>iterations</c> 次——
    /// 否则首次 JIT / 池扩容的分配会污染稳态断言。</para>
    /// </summary>
    internal static class AllocationCapture
    {
        /// <summary>
        /// 主路径：以 <see cref="GC.GetAllocatedBytesForCurrentThread"/> 前后差计量托管分配。
        /// </summary>
        public static long MeasureManaged(string name, int iterations, Action action, Action<long> verifyBytes)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (iterations < 1) iterations = 1;

            // 预热：JIT、池扩容、字典容量增长都落在这一发里，结果丢弃
            action();

            var clock = Stopwatch.StartNew();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++)
            {
                action();
            }

            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            clock.Stop();

            TestContext.WriteLine($"MANAGED_ALLOC,{name},{iterations},{bytes},{clock.Elapsed.TotalMilliseconds:F4}");
            verifyBytes?.Invoke(bytes);
            return bytes;
        }

        /// <summary>
        /// 带耗时的稳态测量（CPU 回归用）：预热一次后跑 <paramref name="iterations"/> 轮整个 <paramref name="action"/>。
        /// </summary>
        public static double MeasureMilliseconds(string name, int iterations, Action action, Action verify = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (iterations < 1) iterations = 1;

            action();
            verify?.Invoke();

            var clock = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                action();
            }

            clock.Stop();
            double ms = clock.Elapsed.TotalMilliseconds;
            TestContext.WriteLine($"MANAGED_MS,{name},{iterations},{ms:F4}");
            return ms;
        }

        /// <summary>已知分配校准：确认测量台能抓到分配（防 0 断言因测量失效而假绿）。</summary>
        public static void CalibrateKnownAllocation()
        {
            long bytes = MeasureManaged("calibration", 1, () =>
            {
                // 故意分配，结果丢弃
                _ = new byte[4096];
            }, null);

            // 预热 + 测量至少捕获 4KB
            if (bytes < 4096)
            {
                long direct = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                var sink = new byte[4096];
                direct = GC.GetAllocatedBytesForCurrentThread() - before;
                GC.KeepAlive(sink);
                Assert.GreaterOrEqual(direct, 4096, "分配计数器应能捕获已知分配");
            }
        }
    }
}
