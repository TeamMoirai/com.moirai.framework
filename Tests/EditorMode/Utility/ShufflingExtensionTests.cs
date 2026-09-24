using System;
using System.Collections.Generic;
using Moirai.Atropos;
using Moirai.Atropos.Collections;
using NUnit.Framework;

namespace Utility
{
    /// <summary>
    /// 锁住 <see cref="ShufflingExtension"/> 改接统一随机源后的采样契约：
    /// 置换完整性、两条自适应采样路径都不重不漏、以及同种子可复现。
    /// </summary>
    public class ShufflingExtensionTests
    {
        private ulong _seedBefore;

        [SetUp]
        public void SetUp()
        {
            _seedBefore = RandomUtility.Seed;
            RandomUtility.Reseed(8181UL);
        }

        [TearDown]
        public void TearDown()
        {
            RandomUtility.Reseed(_seedBefore);
        }

        [Test]
        public void Shuffle_IsInPlacePermutation()
        {
            var list = new List<int>();
            for (int i = 0; i < 24; i++) list.Add(i);

            list.Shuffle();

            var sorted = new List<int>(list);
            sorted.Sort();
            for (int i = 0; i < 24; i++) Assert.AreEqual(i, sorted[i], "洗牌不该丢项或造项");
        }

        [Test]
        public void Shuffled_LeavesSourceUntouched()
        {
            var source = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };
            var copy = source.Shuffled();

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, source, "原表不该被打乱");
            Assert.AreEqual(8, copy.Count);
            Assert.AreEqual(8, new HashSet<int>(copy).Count, "副本是完整置换");
        }

        [Test]
        public void Shuffle_SameSeed_RepeatsSameOrder()
        {
            RandomUtility.Reseed(4711UL);
            var first = ShuffledRun();

            RandomUtility.Reseed(4711UL);
            var second = ShuffledRun();

            CollectionAssert.AreEqual(first, second, "同种子必须洗出同一顺序");
        }

        [Test]
        public void RandomElement_PicksFromSource_AndRejectsEmpty()
        {
            var list = new List<string> { "a", "b", "c" };
            var seen = new HashSet<string>();
            for (int i = 0; i < 600; i++) seen.Add(list.RandomElement());

            CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, seen);
            Assert.Throws<InvalidOperationException>(() => new List<int>().RandomElement());
        }

        [Test]
        public void RandomElements_SmallCount_SamplesWithoutReplacement()
        {
            var list = Identity(100);

            // count < n/3 走拒绝采样那条路
            var picked = list.RandomElements(5);

            Assert.AreEqual(5, picked.Count);
            Assert.AreEqual(5, new HashSet<int>(picked).Count, "无放回");
        }

        [Test]
        public void RandomElements_LargeCount_SamplesWithoutReplacement()
        {
            var list = Identity(100);

            // count ≥ n/3 走部分 Fisher-Yates 那条路
            var picked = list.RandomElements(80);

            Assert.AreEqual(80, picked.Count);
            Assert.AreEqual(80, new HashSet<int>(picked).Count, "无放回");
            foreach (var v in picked) Assert.GreaterOrEqual(v, 0);
        }

        [Test]
        public void RandomElements_CountAtLeastSize_ReturnsFullPermutation()
        {
            var list = Identity(10);
            var picked = list.RandomElements(10);

            Assert.AreEqual(10, picked.Count);
            var seen = new HashSet<int>(picked);
            Assert.AreEqual(10, seen.Count, "count ≥ n 时是完整置换");
            for (int i = 0; i < 10; i++) Assert.IsTrue(seen.Contains(i), $"丢了 {i}");
        }

        [Test]
        public void RandomElements_EdgeCountsAndGuards()
        {
            var list = Identity(10);
            Assert.AreEqual(0, list.RandomElements(0).Count);
            Assert.AreEqual(0, new List<int>().RandomElements(5).Count, "空表采样给空表");
            Assert.Throws<ArgumentOutOfRangeException>(() => list.RandomElements(-1));
            Assert.Throws<ArgumentNullException>(() => ((List<int>)null).RandomElements(1));
        }

        [Test]
        public void RandomElements_SingleItem_TakesFastPath()
        {
            var list = Identity(1000);
            var picked = list.RandomElements(1);

            Assert.AreEqual(1, picked.Count);
            Assert.GreaterOrEqual(picked[0], 0);
            Assert.Less(picked[0], 1000);
        }

        private static List<int> Identity(int count)
        {
            var list = new List<int>(count);
            for (int i = 0; i < count; i++) list.Add(i);
            return list;
        }

        private static List<int> ShuffledRun()
        {
            var list = Identity(20);
            list.Shuffle();
            return list;
        }
    }
}
