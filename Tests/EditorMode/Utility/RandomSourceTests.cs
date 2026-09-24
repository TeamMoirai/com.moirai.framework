using System;
using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;

namespace Utility
{
    /// <summary>
    /// 验证 <see cref="RandomSource"/> 的可复现性、边界、无偏性与全零自愈。
    /// 全部用固定种子，结论不随运行抖动。
    /// </summary>
    public class RandomSourceTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalStream()
        {
            var a = new RandomSource(12345UL);
            var b = new RandomSource(12345UL);
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(a.NextUInt32(), b.NextUInt32(), $"第 {i} 个输出不一致");
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentStreams()
        {
            var a = new RandomSource(1UL);
            var b = new RandomSource(2UL);
            var diff = 0;
            for (int i = 0; i < 100; i++)
            {
                if (a.NextUInt32() != b.NextUInt32()) diff++;
            }

            Assert.Greater(diff, 90, "相近种子不该产出近乎相同的流");
        }

        [Test]
        public void NextInt_Bound_StaysInScopeAndCoversEveryValue()
        {
            var rng = new RandomSource(7UL);
            var seen = new HashSet<int>();
            for (int i = 0; i < 20000; i++)
            {
                int v = rng.NextInt(6);
                Assert.GreaterOrEqual(v, 0);
                Assert.Less(v, 6);
                seen.Add(v);
            }

            Assert.AreEqual(6, seen.Count);
        }

        [Test]
        public void NextInt_BoundOfOne_AlwaysZero()
        {
            var rng = new RandomSource(3UL);
            for (int i = 0; i < 100; i++) Assert.AreEqual(0, rng.NextInt(1));
        }

        [Test]
        public void NextInt_NonPositiveBound_Throws()
        {
            var rng = new RandomSource(3UL);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-3));
        }

        [Test]
        public void NextInt_MinMax_HandlesNegativeAndReversedRanges()
        {
            var rng = new RandomSource(11UL);
            for (int i = 0; i < 5000; i++)
            {
                int v = rng.NextInt(-5, -1);
                Assert.GreaterOrEqual(v, -5);
                Assert.Less(v, -1);
            }

            Assert.AreEqual(4, rng.NextInt(4, 4), "min == max 时返回该值");
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(9, 3));
        }

        [Test]
        public void NextInt_FullIntDomain_StaysInScope()
        {
            var rng = new RandomSource(2024UL);
            for (int i = 0; i < 5000; i++)
            {
                int v = rng.NextInt(int.MinValue, int.MaxValue);
                Assert.AreNotEqual(int.MaxValue, v, "上界开区间");
            }
        }

        [Test]
        public void NextLong_FullDomain_AndSingleValueRanges()
        {
            var rng = new RandomSource(55UL);
            for (int i = 0; i < 20000; i++)
            {
                long v = rng.NextLong(long.MinValue, long.MaxValue);
                Assert.Less(v, long.MaxValue);
            }

            Assert.AreEqual(5L, rng.NextLong(5L, 5L));
            Assert.AreEqual(long.MaxValue - 1, rng.NextLong(long.MaxValue - 1, long.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextLong(9L, 3L));
        }

        [Test]
        public void NextFloat_And_NextDouble_AreHalfOpenAndWellDistributed()
        {
            var rng = new RandomSource(42UL);
            const int n = 200000;
            double sum = 0, sum2 = 0;
            for (int i = 0; i < n; i++)
            {
                float f = rng.NextFloat();
                Assert.GreaterOrEqual(f, 0f);
                Assert.Less(f, 1f);

                double d = rng.NextDouble();
                Assert.GreaterOrEqual(d, 0.0);
                Assert.Less(d, 1.0);
                sum += d;
                sum2 += d * d;
            }

            double mean = sum / n;
            double variance = sum2 / n - mean * mean;
            Assert.AreEqual(0.5, mean, 0.005, "均值应为 0.5");
            Assert.AreEqual(1.0 / 12.0, variance, 0.005, "方差应为 1/12");
        }

        [Test]
        public void NextInt_NoModuloBias_AcrossBounds()
        {
            // 取模法在 bound 不整除 2^32 时会留下系统性余数偏置，卡方会远超自由度阈值
            foreach (int bound in new[] { 3, 7, 100 })
            {
                var rng = new RandomSource(0xC0FFEEUL);
                var counts = new int[bound];
                const int n = 200000;
                for (int i = 0; i < n; i++) counts[rng.NextInt(bound)]++;

                double expect = (double)n / bound;
                double chi = 0;
                for (int i = 0; i < bound; i++)
                {
                    double d = counts[i] - expect;
                    chi += d * d / expect;
                }

                double limit = bound <= 3 ? 25.0 : (bound <= 7 ? 40.0 : 200.0);
                Assert.Less(chi, limit, $"bound={bound} 的分布偏离均匀（卡方 {chi:F1}）");
            }
        }

        [Test]
        public void DefaultValue_SelfHealsFromAllZeroState()
        {
            // 全零是 xoshiro 的不动点：不清零检查的话整条流永远吐 0 且毫不报错
            var rng = default(RandomSource);
            var seen = new HashSet<uint> { rng.NextUInt32(), rng.NextUInt32(), rng.NextUInt32(), rng.NextUInt32() };
            Assert.Greater(seen.Count, 1, "default 结构体应自愈而非卡在 0");
        }

        [Test]
        public void NextBool_RespectsProbabilityEdges()
        {
            var rng = new RandomSource(8UL);
            for (int i = 0; i < 100; i++)
            {
                Assert.IsFalse(rng.NextBool(0f));
                Assert.IsTrue(rng.NextBool(1f));
                Assert.IsTrue(rng.NextBool(2f));
            }

            int hits = 0;
            for (int i = 0; i < 10000; i++)
            {
                if (rng.NextBool(0.25f)) hits++;
            }

            Assert.Greater(hits, 1500);
            Assert.Less(hits, 3500);
        }
    }
}
