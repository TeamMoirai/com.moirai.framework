using System;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace Testing
{
    /// <summary>一次分配测量的汇总结果。</summary>
    internal readonly struct AllocationSample
    {
        internal readonly int Allocations;
        internal readonly double Milliseconds;

        internal AllocationSample(int allocations, double milliseconds)
        {
            Allocations = allocations;
            Milliseconds = milliseconds;
        }
    }

    /// <summary>
    /// 0-GC 测量台：以 <c>GC.Alloc</c> 采样计数（<see cref="Recorder"/>，Unity 官方
    /// <c>AllocatingGCMemoryConstraint</c> 同机制、同款 API——这是 UTF 在本引擎版本上唯一可用的分配观测通道）。
    /// <para><b>为什么不是 GC 计数 API</b>（2026-09-28 三处实证）：<c>GC.GetAllocatedBytesForCurrentThread</c>
    /// 在编辑器 Mono、Mono2x 玩家、IL2CPP 玩家全部恒 0；<c>GC.GetTotalAllocatedBytes</c> 在 Unity 的
    /// .NET profile 里根本不存在（CS0117）。字节口径在 Unity 内无实现，事件口径是官方唯一口径。</para>
    /// <para><b>事件数语义</b>：计的是测量窗内的 GC 分配事件数——任何一次分配（无论大小）都 ≥1 事件，
    /// 因此「0 事件」是比「0 字节」更强的零分配断言；上限类断言给常数事件预算（锁「不随规模增长」，
    /// 不再精确到字节）。</para>
    /// <para>约定：MeasureManaged 先做一次预热并丢弃（JIT/池扩容/字典容量增长都落在这一发里），再计
    /// <paramref name="iterations"/> 次；先用一次"必然分配"探测采样能力，探不到就 <c>Assert.Ignore</c>——
    /// 绝不把"测不出分配"当成"没有分配"，否则零分配断言全部假绿。</para>
    /// </summary>
    internal static class AllocationCapture
    {
        /// <summary>
        /// 采样能力探测缓存：-1 未探测、0 不可用、1 可用（同一运行时内不会翻转）。
        /// </summary>
        private static int s_CounterUsable = -1;

        /// <summary>
        /// 测量窗重入守卫：<see cref="Recorder.Get"/> 返回进程共享单例，嵌套测量窗会互相开关
        /// enabled、把对方窗内的分配事件错记进自己的计数——真发生时当场红掉，绝不静默错账。
        /// </summary>
        private static bool s_InWindow;

        /// <summary>
        /// 当前运行时能否观测到 GC 分配（GC.Alloc 采样）。
        /// </summary>
        private static bool IsCounterUsable()
        {
            if (s_CounterUsable < 0)
            {
                s_CounterUsable = 0;
                Recorder recorder = Recorder.Get("GC.Alloc");
                if (recorder != null && recorder.isValid)
                {
                    // 与 UTF 约束同款时序：先 enabled 一次让创建期分配落进块里，
                    // 关闭即冲刷，之后的采样窗才是干净的。
                    recorder.enabled = true;
                    recorder.FilterToCurrentThread();
                    recorder.enabled = false;

                    recorder.enabled = true;
                    GC.KeepAlive(new object[64]);   // 必然分配；经 KeepAlive 保证不被优化掉
                    recorder.enabled = false;
                    recorder.CollectFromAllThreads();

                    s_CounterUsable = recorder.sampleBlockCount > 0 ? 1 : 0;
                }
            }

            return s_CounterUsable == 1;
        }

        /// <summary>
        /// 主路径：以 <c>GC.Alloc</c> 采样计数计量测量窗内的 GC 分配事件数。
        /// <para>运行时观测不到分配时直接 Ignore：采样失效下零分配断言会无条件成立，那不是通过而是失明。</para>
        /// </summary>
        public static int MeasureManaged(string name, int iterations, Action action, Action<int> verifyAllocs)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (iterations < 1) iterations = 1;

            if (s_InWindow)
            {
                Assert.Fail("MeasureManaged 不可重入：GC.Alloc Recorder 为进程共享单例，嵌套测量窗会互相开关 enabled、错记对方的分配事件");
            }

            s_InWindow = true;
            try
            {
                if (!IsCounterUsable())
                {
                    TestContext.WriteLine($"MANAGED_ALLOC,{name},unavailable");
                    Assert.Ignore("当前运行时收不到 GC.Alloc 采样，无法观测托管分配");
                }

                // 预热：JIT、池扩容、字典容量增长都落在这一发里，结果丢弃
                action();

                Recorder recorder = Recorder.Get("GC.Alloc");
                if (recorder == null || !recorder.isValid)
                {
                    TestContext.WriteLine($"MANAGED_ALLOC,{name},unavailable");
                    Assert.Ignore("当前运行时收不到 GC.Alloc 采样，无法观测托管分配");
                }

                recorder.enabled = true;
                recorder.FilterToCurrentThread();
                recorder.enabled = false;    // 创建与建块的初始分配随关闭冲刷出窗

                var clock = Stopwatch.StartNew();
                recorder.enabled = true;
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        action();
                    }
                }
                finally
                {
                    recorder.enabled = false;
                    recorder.CollectFromAllThreads();
                }

                clock.Stop();
                int allocs = recorder.sampleBlockCount;

                TestContext.WriteLine($"MANAGED_ALLOC,{name},{iterations},{allocs},{clock.Elapsed.TotalMilliseconds:F4}");
                verifyAllocs?.Invoke(allocs);
                return allocs;
            }
            finally
            {
                s_InWindow = false;
            }
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
            // 采样不可用时 MeasureManaged 已 Ignore；能走到这里说明采样确实在推进，
            // 那它就必须抓到这次必然分配——抓不到即测量台自身有问题，必须红。
            int allocs = MeasureManaged("calibration", 1, () => GC.KeepAlive(new byte[4096]), null);

            Assert.GreaterOrEqual(allocs, 1, "GC.Alloc 采样应能捕获已知分配");
        }
    }
}
