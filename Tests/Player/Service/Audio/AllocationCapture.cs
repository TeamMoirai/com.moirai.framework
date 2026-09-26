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
    /// <para><b>只在玩家（Player）里才有牙</b>：Unity 编辑器的 Mono 与部分 IL2CPP 配置整档观测不到托管分配
    /// （本机实测：<see cref="GC.GetAllocatedBytesForCurrentThread"/> 对一次 64MB 主线程分配仍返回 0，
    /// 于是"零分配断言"在编辑器里无条件成立）。所以测量台与它的用例同住 <c>Moirai.Atropos.Tests.Player</c>
    /// 程序集（<c>UNITY_INCLUDE_TESTS</c> + <c>!UNITY_EDITOR</c>），只在玩家构建的测试运行里参与，不进编辑器套件。</para>
    /// <para>约定：MeasureManaged 先做一次预热并丢弃，再测 <c>iterations</c> 次——
    /// 否则首次 JIT / 池扩容的分配会污染稳态断言。</para>
    /// <para>口径与 <c>AudioClipCacheAllocationTests</c> 一致：先用一次"必然分配"探测能力，探不到就
    /// <c>Assert.Ignore</c>——绝不把"测不出分配"当成"没有分配"，否则全部零分配断言都会假绿。</para>
    /// </summary>
    internal static class AllocationCapture
    {
        /// <summary>
        /// 计数器能力探测缓存：-1 未探测、0 不可用、1 可用（同一运行时内不会翻转）。
        /// </summary>
        private static int s_CounterUsable = -1;

        /// <summary>
        /// 当前运行时能否观测到托管分配。
        /// </summary>
        private static bool IsCounterUsable()
        {
            if (s_CounterUsable < 0)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                GC.KeepAlive(new object[64]);   // 必然分配；经 KeepAlive 保证不被优化掉
                s_CounterUsable = GC.GetAllocatedBytesForCurrentThread() > before ? 1 : 0;
            }

            return s_CounterUsable == 1;
        }

        /// <summary>
        /// 主路径：以 <see cref="GC.GetAllocatedBytesForCurrentThread"/> 前后差计量托管分配。
        /// <para>运行时观测不到分配时直接 Ignore：计数器失效下零分配断言会无条件成立，那不是通过而是失明。</para>
        /// </summary>
        public static long MeasureManaged(string name, int iterations, Action action, Action<long> verifyBytes)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (iterations < 1) iterations = 1;

            if (!IsCounterUsable())
            {
                TestContext.WriteLine($"MANAGED_ALLOC,{name},unavailable");
                Assert.Ignore("当前运行时无法观测托管分配（Unity 编辑器 Mono / IL2CPP 计数器不推进）");
            }

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
            // 计数器不可用时 MeasureManaged 已 Ignore；能走到这里说明计数器确实在推进，
            // 那它就必须抓到这次必然分配——抓不到即测量台自身有问题，必须红。
            long bytes = MeasureManaged("calibration", 1, () => GC.KeepAlive(new byte[4096]), null);

            Assert.GreaterOrEqual(bytes, 4096, "分配计数器应能捕获已知分配");
        }
    }
}
