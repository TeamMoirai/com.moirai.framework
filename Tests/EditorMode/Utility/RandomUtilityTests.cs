using System.Collections.Generic;
using System.Threading;
using Moirai.Atropos;
using NUnit.Framework;

namespace Utility
{
    /// <summary>
    /// 验证 <see cref="RandomUtility"/> 的门面语义：可复现播种、每线程独立流、
    /// 并发取值安全、私有流不扰动全局。
    /// </summary>
    public class RandomUtilityTests
    {
        private ulong _seedBefore;

        [SetUp]
        public void SetUp()
        {
            _seedBefore = RandomUtility.Seed;
        }

        [TearDown]
        public void TearDown()
        {
            // 门面是全局状态，不还种子就会把顺序相关的用例串味
            RandomUtility.Reseed(_seedBefore);
        }

        [Test]
        public void Reseed_BackToEarlierValue_ReplaysThatStream()
        {
            RandomUtility.Reseed(999UL);
            var first = new int[200];
            for (int i = 0; i < first.Length; i++) first[i] = RandomUtility.NextInt(1000);

            // 中途换种子跑一段，再换回来应当仍从同一位置续上
            RandomUtility.Reseed(7UL);
            for (int i = 0; i < 50; i++) RandomUtility.NextInt(3);

            RandomUtility.Reseed(999UL);
            for (int i = 0; i < first.Length; i++)
            {
                Assert.AreEqual(first[i], RandomUtility.NextInt(1000), $"第 {i} 个值不复现");
            }
        }

        [Test]
        public void Reseed_WithSameValue_StillRestartsStream()
        {
            // 只看种子变没变的实现会让这次重播续在旧进度上——回放当场不复现
            RandomUtility.Reseed(512UL);
            var first = TakeSeries(60);

            RandomUtility.Reseed(512UL);
            var second = TakeSeries(60);

            Assert.AreEqual(first, second, "同值 Reseed 必须把流拉回起点");
        }

        [Test]
        public void Seed_ReflectsReseed()
        {
            RandomUtility.Reseed(0xDEADBEEFUL);
            Assert.AreEqual(0xDEADBEEFUL, RandomUtility.Seed);
        }

        [Test]
        public void Reseed_WithoutArgument_ChangesSeed()
        {
            RandomUtility.Reseed(1UL);
            RandomUtility.Reseed();
            Assert.AreNotEqual(1UL, RandomUtility.Seed, "取熵重播后不该还停在原种子上");
        }

        [Test]
        public void CreateSeeded_UsesPrivateStream_WithoutDisturbingGlobal()
        {
            RandomUtility.Reseed(4242UL);
            var expected = new int[5];
            for (int i = 0; i < expected.Length; i++) expected[i] = RandomUtility.NextInt(100000);

            var local = RandomUtility.CreateSeeded(1UL);
            for (int i = 0; i < 1000; i++) local.NextInt(100000);

            RandomUtility.Reseed(4242UL);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], RandomUtility.NextInt(100000), "私有流不该动全局");
            }
        }

        [Test]
        public void SameSeed_DifferentThreadsGetDifferentStreams()
        {
            RandomUtility.Reseed(4242UL);
            var mainValues = new int[8];
            for (int i = 0; i < mainValues.Length; i++) mainValues[i] = RandomUtility.NextInt();

            var otherValues = new int[8];
            var worker = new Thread(() =>
            {
                for (int i = 0; i < otherValues.Length; i++) otherValues[i] = RandomUtility.NextInt();
            });
            worker.Start();
            worker.Join();

            var main = new HashSet<int>(mainValues);
            main.IntersectWith(otherValues);
            Assert.AreEqual(0, main.Count, "共种子时两条线程流不该产出同一串数");
        }

        [Test]
        public void ConcurrentAccess_KeepsValuesInScope()
        {
            RandomUtility.Reseed(31337UL);
            var failure = 0;
            var threads = new List<Thread>();
            for (int t = 0; t < 8; t++)
            {
                var worker = new Thread(() =>
                {
                    for (int i = 0; i < 50000; i++)
                    {
                        int v = RandomUtility.NextInt(100);
                        double d = RandomUtility.NextDouble();
                        long l = RandomUtility.NextLong(-1000, 1000);
                        if (v < 0 || v >= 100 || d < 0 || d >= 1 || l < -1000 || l >= 1000) Interlocked.Increment(ref failure);
                    }
                });
                threads.Add(worker);
                worker.Start();
            }

            foreach (var worker in threads) worker.Join();
            Assert.AreEqual(0, failure);
        }

        [Test]
        public void Forwarders_RespectTheirBounds()
        {
            RandomUtility.Reseed(66UL);
            Assert.AreEqual(0, RandomUtility.NextInt(1));
            Assert.AreEqual(5, RandomUtility.NextInt(5, 5));

            var seen = new HashSet<int>();
            for (int i = 0; i < 20000; i++)
            {
                int v = RandomUtility.NextInt(-3, 4);
                Assert.GreaterOrEqual(v, -3);
                Assert.Less(v, 4);
                seen.Add(v);
            }

            Assert.AreEqual(7, seen.Count, "负到正的跨度应覆盖全部 7 个取值");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => RandomUtility.NextInt(0));
        }

        private static int[] TakeSeries(int count)
        {
            var values = new int[count];
            for (int i = 0; i < count; i++) values[i] = RandomUtility.NextInt(100000);
            return values;
        }
    }
}
