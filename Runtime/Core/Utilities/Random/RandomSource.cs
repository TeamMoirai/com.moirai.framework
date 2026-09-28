using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 框架统一随机流：xoshiro128**（周期 2^128），值语义、零分配、可在任意线程使用。
    /// <para>方法会就地推进自身状态，所以只能放在**可写**的局部变量或字段上调用；
    /// 经属性 / readonly 字段取到的副本会把同一批数重复吐出来。</para>
    /// <para>有界取值用带拒绝的 Lemire 乘移法，不做取模，因此不存在模偏置。</para>
    /// </summary>
    public struct RandomSource
    {
        private const ulong Golden = 0x9E3779B97F4A7C15UL;

        private uint _s0;
        private uint _s1;
        private uint _s2;
        private uint _s3;

        /// <summary>用 64 位种子建流（内部经 splitmix64 展开成四个字，避免相近种子产出相近流）。</summary>
        public RandomSource(ulong seed)
        {
            _s0 = _s1 = _s2 = _s3 = 0;
            Seed(seed);
        }

        /// <summary>重新播种并丢弃当前进度。</summary>
        public void Seed(ulong seed)
        {
            ulong state = seed;
            _s0 = (uint)NextSplitMix64(ref state);
            _s1 = (uint)(NextSplitMix64(ref state) >> 32);
            _s2 = (uint)NextSplitMix64(ref state);
            _s3 = (uint)(NextSplitMix64(ref state) >> 32);
        }

        /// <summary>原始 32 位输出。</summary>
        public uint NextUInt32()
        {
            // 全零是 xoshiro 的不动点；default(RandomSource) 或外部把结构体清零时自愈，
            // 否则整条流会永远吐 0 且不报错。
            if ((_s0 | _s1 | _s2 | _s3) == 0) Seed(Golden);

            uint result = Rotl(_s1 * 5, 7) * 9;
            uint t = _s1 << 9;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 11);

            return result;
        }

        /// <summary>原始 64 位输出（两次 32 位拼接）。</summary>
        public ulong NextUInt64()
        {
            return ((ulong)NextUInt32() << 32) | NextUInt32();
        }

        /// <summary>[0, int.MaxValue] 内的非负随机数。</summary>
        public int NextInt()
        {
            return (int)(NextUInt32() >> 1);
        }

        /// <summary>[0, maxExclusive) 内的均匀随机数。</summary>
        public int NextInt(int maxExclusive)
        {
            // 0 会让下面的取模除零，负数会让上界按 uint 解释成巨值——一并挡掉
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "上界必须为正");

            uint bound = (uint)maxExclusive;
            uint reject = (uint)(0u - bound) % bound;
            while (true)
            {
                uint r = NextUInt32();
                ulong m = (ulong)r * bound;
                uint low = (uint)m;
                if (low >= reject) return (int)(m >> 32);
            }
        }

        /// <summary>[minInclusive, maxExclusive) 内的均匀随机数；<c>min == max</c> 时返回 min。</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive > maxExclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "上界不得小于下界");

            long span = (long)maxExclusive - minInclusive;
            if (span == 0) return minInclusive;
            if (span > int.MaxValue) return (int)NextLong(minInclusive, maxExclusive);
            return minInclusive + NextInt((int)span);
        }

        /// <summary>[minInclusive, maxExclusive) 内的均匀随机数；<c>min == max</c> 时返回 min。跨整个 long 域可用。</summary>
        public long NextLong(long minInclusive, long maxExclusive)
        {
            if (minInclusive > maxExclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "上界不得小于下界");

            // ulong 补码减法给出的就是"区间内取值个数"，[long.MinValue, long.MaxValue) 这种全域
            // 跨度也不会溢出——只有上界为 long.MaxValue+1 时才归零，而那个值不可表示。
            ulong range = (ulong)maxExclusive - (ulong)minInclusive;
            if (range == 0) return minInclusive;

            int bits = BitLength(range);
            int shift = 64 - bits;
            while (true)
            {
                ulong r = NextUInt64() >> shift;
                if (r < range) return (long)((ulong)minInclusive + r);
            }
        }

        /// <summary>[0, 1) 内的均匀随机数。</summary>
        public float NextFloat()
        {
            return (NextUInt32() >> 8) * (1f / 16777216f);
        }

        /// <summary>[minInclusive, maxExclusive) 内的均匀随机数。</summary>
        public float NextFloat(float minInclusive, float maxExclusive)
        {
            return minInclusive + (maxExclusive - minInclusive) * NextFloat();
        }

        /// <summary>[0, 1) 内的均匀随机数（53 位有效）。</summary>
        public double NextDouble()
        {
            ulong r = ((ulong)(NextUInt32() >> 5) << 26) | (uint)(NextUInt32() >> 6);
            return r * (1.0 / 9007199254740992.0);
        }

        /// <summary>以概率 <paramref name="pTrue"/>（[0,1] 之外的值按端点截断）返回 true。</summary>
        public bool NextBool(float pTrue)
        {
            if (pTrue <= 0f) return false;
            if (pTrue >= 1f) return true;
            return NextFloat() < pTrue;
        }

        private static uint Rotl(uint value, int count)
        {
            return (value << count) | (value >> (32 - count));
        }

        private static ulong NextSplitMix64(ref ulong state)
        {
            unchecked
            {
                state += Golden;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private static int BitLength(ulong value)
        {
            int bits = 0;
            while (value != 0)
            {
                value >>= 1;
                bits++;
            }

            return bits;
        }
    }
}
