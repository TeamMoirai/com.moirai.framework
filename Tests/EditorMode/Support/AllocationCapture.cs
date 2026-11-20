using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;

namespace Testing
{
    /// <summary>
    /// 字节级分配计量器：热路径 0-Alloc 断言的复用夹具（GC 线程字节差值为主口径，Profiler 稳态采样为对照）。
    /// </summary>
    /// <remarks>
    /// 主口径 <see cref="GC.GetAllocatedBytesForCurrentThread"/> 只计当前线程、不经 Unity Profiler 采样（无附加开销）； <br />
    /// IL2CPP/编辑器 Mono 语义一致。测量前置强制 GC 并留收敛窗口，防上轮残渣计入； <br />
    /// 预热由调用方承担（命中缓存/收敛 jit 的稳态才是断言对象）； <br />
    /// 断言失败的失败消息带字节差值，供基线对照。
    /// </remarks>
    internal static class AllocationCapture
    {
        /// <summary>
        /// 测量回调在当前线程上的字节增量（前置 GC + 多轮收敛，稳定态下可低至 0）。
        /// </summary>
        /// <param name="action">被测操作（调用方保证预热与稳态）。</param>
        /// <param name="iterations">执行次数（增量按累计值返回，断言除以次数或直接断累计 0 均可）。</param>
        /// <returns>累计分配字节数（当前线程）。</returns>
        internal static long Measure(Action action, int iterations = 1)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++)
            {
                action();
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        /// <summary>
        /// 断言回调在当前线程上的累计分配不超过 <paramref name="maxBytes"/>（0-Alloc 门禁传 0）。
        /// </summary>
        /// <param name="action">被测操作（预热与稳态由调用方保证）。</param>
        /// <param name="maxBytes">允许的累计分配字节上限。</param>
        /// <param name="iterations">执行次数。</param>
        /// <param name="context">失败消息上下文（描述被测热路径）。</param>
        internal static void AssertNoAlloc(Action action, int maxBytes, int iterations = 1, string context = null)
        {
            long allocated = Measure(action, iterations);
            if (allocated > maxBytes)
            {
                string scope = context ?? "被测操作";
                Assert.Fail($"{scope} 累计分配 {allocated} 字节（{iterations} 次，上限 {maxBytes}）——热路径不得新增分配。");
            }
        }

        /// <summary>
        /// Profiler 稳态对照采样：返回累计分配字节数（Unity Profiler 口径，与 GC 口径互为交叉验证）。
        /// </summary>
        /// <param name="action">被测操作（调用方保证预热与稳态）。</param>
        /// <param name="iterations">执行次数。</param>
        /// <returns>Profiler 口径的累计分配字节数。</returns>
        internal static long MeasureWithProfiler(Action action, int iterations = 1)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Profiler.BeginSample("AllocationCapture");
            long before = Profiler.GetTotalAllocatedMemoryLong();
            for (int i = 0; i < iterations; i++)
            {
                action();
            }
            long delta = Profiler.GetTotalAllocatedMemoryLong() - before;
            Profiler.EndSample();
            return delta;
        }

        /// <summary>反射预热护栏：防止 jit/静态初始化把首轮分配计进测量（[MethodImpl] 内联提示，防包装层自身被量化）。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Warmup(Action action, int iterations = 10)
        {
            for (int i = 0; i < iterations; i++)
            {
                action();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
