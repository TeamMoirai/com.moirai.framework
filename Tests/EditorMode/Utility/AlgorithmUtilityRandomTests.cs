using System;
using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;

namespace Utility
{
    /// <summary>
    /// 锁住 <see cref="AlgorithmUtility"/> 随机面改接统一随机源后的行为：
    /// 位数区间不再有空集死循环、long 版不越界且上下界口径与 int 版一致、
    /// 每次 new Random 的老路子不会在同一 tick 里给出同一个数。
    /// </summary>
    public class AlgorithmUtilityRandomTests
    {
        private ulong _seedBefore;

        [SetUp]
        public void SetUp()
        {
            _seedBefore = RandomUtility.Seed;
            RandomUtility.Reseed(20260922UL);
        }

        [TearDown]
        public void TearDown()
        {
            RandomUtility.Reseed(_seedBefore);
        }

        [Test]
        public void RandomRange_ByLength_WithDisjointDigitCap_ThrowsInsteadOfSpinning()
        {
            // 旧实现：1 位数字（0..9）永远进不了 [100,200]，那个 do-while 出不来
            Assert.Throws<ArgumentException>(() => AlgorithmUtility.RandomRange(1, 100, 200));
        }

        [Test]
        public void RandomRange_ByLength_NegativeLowerBound_NoLongerHangs()
        {
            // 位数表达不出负值，旧实现会一直重试；现在按下界 0 收口
            for (int i = 0; i < 200; i++)
            {
                int v = AlgorithmUtility.RandomRange(2, -50, 50);
                Assert.GreaterOrEqual(v, 0);
                Assert.LessOrEqual(v, 50);
            }
        }

        [Test]
        public void RandomRange_ByLength_RespectsBothDigitCapAndInterval()
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < 5000; i++)
            {
                int v = AlgorithmUtility.RandomRange(2, 0, 9999);
                Assert.GreaterOrEqual(v, 0);
                Assert.LessOrEqual(v, 99, "两位能表达的最大值是 99");
                seen.Add(v);
            }

            Assert.Greater(seen.Count, 80, "应铺满两位数取值域而不是反复给同几个值");
        }

        [Test]
        public void RandomRange_ByLength_LargeLengthDoesNotOverflow()
        {
            // 10^10 远超 int，旧写法靠 Int32.Parse 溢出抛 FormatException
            for (int i = 0; i < 100; i++)
            {
                int v = AlgorithmUtility.RandomRange(12, 10, 20);
                Assert.GreaterOrEqual(v, 10);
                Assert.LessOrEqual(v, 20);
            }
        }

        [Test]
        public void RandomRange_Int_IsHalfOpenAndAllocatesNothingNewPerCall()
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < 3000; i++)
            {
                int v = AlgorithmUtility.RandomRange(10, 16);
                Assert.GreaterOrEqual(v, 10);
                Assert.Less(v, 16);
                seen.Add(v);
            }

            Assert.AreEqual(6, seen.Count);
            Assert.AreEqual(3, AlgorithmUtility.RandomRange(3, 3), "min == max 现在返回该值而不是抛");
        }

        [Test]
        public void RandomRange_Long_NeverEscapesTheInterval()
        {
            // 旧实现把可能为负的 NextBytes 结果按比例缩放，能给出低于 minValue 的值
            for (int i = 0; i < 5000; i++)
            {
                long v = AlgorithmUtility.RandomRange(-1000L, 1000L);
                Assert.GreaterOrEqual(v, -1000L);
                Assert.Less(v, 1000L, "上界口径已统一为不含");
            }
        }

        [Test]
        public void RandomRange_Long_CoversWideRange()
        {
            var seen = new HashSet<long>();
            for (int i = 0; i < 5000; i++) seen.Add(AlgorithmUtility.RandomRange(0L, 1L << 40));

            Assert.Greater(seen.Count, 4900, "宽域取值不该出现塌缩重复");
        }

        [Test]
        public void AverageRandom_And_RandomDouble_StayInRange()
        {
            for (int i = 0; i < 2000; i++)
            {
                double v = AlgorithmUtility.AverageRandom(1.5, 2.5);
                Assert.GreaterOrEqual(v, 1.5);
                Assert.Less(v, 2.5);

                double d = AlgorithmUtility.RandomDouble();
                Assert.GreaterOrEqual(d, 0.0);
                Assert.Less(d, 1.0);
            }
        }

        [Test]
        public void AverageRandom_NoLongerQuantizedToFourDecimals()
        {
            // 旧实现把值乘 10000 取整再除回来，任何结果的第 5 位小数恒为 0
            var subTick = false;
            for (int i = 0; i < 500 && !subTick; i++)
            {
                double v = AlgorithmUtility.AverageRandom(0, 1);
                if (Math.Abs(v * 10000 - Math.Round(v * 10000)) > 1e-6) subTick = true;
            }

            Assert.IsTrue(subTick, "仍可表达小于 1e-4 的差值");
        }

        [Test]
        public void OneOrMinusOne_OnlyReturnsTheTwoSigns()
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < 500; i++) seen.Add(AlgorithmUtility.OneOrMinusOne());

            CollectionAssert.AreEquivalent(new[] { -1, 1 }, seen);
        }

        [Test]
        public void NextGauss_IsFinite()
        {
            for (int i = 0; i < 200; i++)
            {
                double v = AlgorithmUtility.NextGauss(0, 1);
                Assert.IsTrue(double.IsFinite(v));
            }
        }

        [Test]
        public void Shuffle_SameSeed_RepeatsSameOrder_AndDoesNotTouchGlobalStream()
        {
            var first = Build(0);
            var second = Build(0);
            var other = Build(1);

            CollectionAssert.AreEqual(first, second, "同一种子必得同一顺序");
            CollectionAssert.AreNotEqual(first, other, "不同种子应给出不同顺序");

            // 中间猛抽全局流也不该改变"同种子同结果"——它走的是局部流
            for (int i = 0; i < 100; i++) RandomUtility.NextInt(1000);
            CollectionAssert.AreEqual(first, Build(0), "洗牌不吃全局流的进度");
        }

        [Test]
        public void Shuffle_IsPermutation()
        {
            var list = new List<int>();
            for (int i = 0; i < 30; i++) list.Add(i);

            AlgorithmUtility.Shuffle(list, 4242);

            var sorted = new List<int>(list);
            sorted.Sort();
            for (int i = 0; i < 30; i++) Assert.AreEqual(i, sorted[i]);
        }

        private static List<int> Build(int seed)
        {
            var list = new List<int>();
            for (int i = 0; i < 16; i++) list.Add(i);
            AlgorithmUtility.Shuffle(list, seed);
            return list;
        }
    }
}
