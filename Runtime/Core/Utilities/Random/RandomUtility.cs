using System;
using System.Diagnostics;
using System.Threading;

namespace Moirai.Atropos
{
    /// <summary>
    /// 框架统一随机入口：每线程一条 <see cref="RandomSource"/> 流，线程安全、取值路径零分配、可播种。
    /// <para>取值不受 <c>UnityEngine.Random.InitState</c> 影响——框架随机与 Unity 自身的随机是两条流，
    /// 要复现走 <see cref="Reseed(ulong)"/>。</para>
    /// <para>不播种时初始种子取自进程熵源（每次运行都不同）；<see cref="Reseed(ulong)"/> 后单线程逐位可复现，
    /// 多线程只保证"同一种子 + 同一线程"的流一致（跨线程的整体顺序取决于调度，任何 RNG 都如此）。</para>
    /// </summary>
    public static class RandomUtility
    {
        // 线程 id 与全局种子的混合常数：让不同线程落在彼此不重叠的流上
        private const ulong ThreadSalt = 0xFF51AFD7ED558CCDUL;

        private sealed class ThreadStream
        {
            public RandomSource Source;
            public ulong Generation;
        }

        [ThreadStatic] private static ThreadStream t_stream;

        private static long s_seed = (long)NewEntropy();

        // 每次 Reseed 都进位，哪怕种子同值：只比种子的话，"重播同一种子"会因为没有变化而
        // 让各线程继续跑在旧进度上，复现直接失效。从 1 起，好让新建的 ThreadStream（0）必然换血。
        private static long s_generation = 1;

        /// <summary>当前全局种子；<see cref="Reseed(ulong)"/> 后可读回。</summary>
        public static ulong Seed
        {
            get { return (ulong)Volatile.Read(ref s_seed); }
        }

        /// <summary>固定全局种子：本线程立刻换到新派生流，其余线程在下次取值时换。</summary>
        public static void Reseed(ulong seed)
        {
            Interlocked.Exchange(ref s_seed, (long)seed);
            Interlocked.Increment(ref s_generation);
        }

        /// <summary>重新取熵播种，回到"每次运行都不同"的默认状态。</summary>
        public static void Reseed()
        {
            Reseed(NewEntropy());
        }

        /// <summary>另建一条私有流，不影响全局——给"同一 seed 必须同一结果"的 API 用。</summary>
        public static RandomSource CreateSeeded(ulong seed)
        {
            return new RandomSource(seed);
        }

        /// <summary>[0, int.MaxValue] 内的非负随机数。</summary>
        public static int NextInt()
        {
            ref var stream = ref SharedStream();
            return stream.NextInt();
        }

        /// <summary>[0, maxExclusive) 内的均匀随机数。</summary>
        public static int NextInt(int maxExclusive)
        {
            ref var stream = ref SharedStream();
            return stream.NextInt(maxExclusive);
        }

        /// <summary>[minInclusive, maxExclusive) 内的均匀随机数。</summary>
        public static int NextInt(int minInclusive, int maxExclusive)
        {
            ref var stream = ref SharedStream();
            return stream.NextInt(minInclusive, maxExclusive);
        }

        /// <summary>[minInclusive, maxExclusive) 内的均匀随机数。</summary>
        public static long NextLong(long minInclusive, long maxExclusive)
        {
            ref var stream = ref SharedStream();
            return stream.NextLong(minInclusive, maxExclusive);
        }

        /// <summary>原始 32 位输出，供需要自造分布的调用方使用。</summary>
        public static uint NextUInt32()
        {
            ref var stream = ref SharedStream();
            return stream.NextUInt32();
        }

        /// <summary>[0, 1) 内的均匀随机数。</summary>
        public static float NextFloat()
        {
            ref var stream = ref SharedStream();
            return stream.NextFloat();
        }

        /// <summary>[minInclusive, maxExclusive) 内的均匀随机数。</summary>
        public static float NextFloat(float minInclusive, float maxExclusive)
        {
            ref var stream = ref SharedStream();
            return stream.NextFloat(minInclusive, maxExclusive);
        }

        /// <summary>[0, 1) 内的均匀随机数（53 位有效）。</summary>
        public static double NextDouble()
        {
            ref var stream = ref SharedStream();
            return stream.NextDouble();
        }

        /// <summary>以概率 <paramref name="pTrue"/> 返回 true。</summary>
        public static bool NextBool(float pTrue)
        {
            ref var stream = ref SharedStream();
            return stream.NextBool(pTrue);
        }

        /// <summary>
        /// 本线程那条流的可写引用；全局重新播种过就就地换血。
        /// <para>给需要在一轮循环里反复取数的调用方用（<see cref="ShuffleUtility"/>），省掉每次过门面的开销。
        /// 别把它存成值字段再长期持有——那是引用，不是句柄，换线程就换语义。</para>
        /// </summary>
        public static ref RandomSource SharedStream()
        {
            var holder = t_stream;
            if (holder == null)
            {
                holder = new ThreadStream();
                t_stream = holder;
            }

            ulong generation = (ulong)Volatile.Read(ref s_generation);
            if (holder.Generation != generation)
            {
                holder.Source.Seed(Derive(Seed));
                holder.Generation = generation;
            }

            return ref holder.Source;
        }

        private static ulong Derive(ulong seed)
        {
            return seed ^ ((ulong)(uint)(Environment.CurrentManagedThreadId + 1) * ThreadSalt);
        }

        private static ulong NewEntropy()
        {
            unchecked
            {
                ulong h = (ulong)Stopwatch.GetTimestamp();
                h = h * 0x9E3779B97F4A7C15UL + (uint)Environment.TickCount;
                h = h * 0x9E3779B97F4A7C15UL + (uint)Environment.CurrentManagedThreadId;
                h = h * 0x9E3779B97F4A7C15UL + (uint)Guid.NewGuid().GetHashCode();
                return h;
            }
        }
    }
}
