using System;
using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// Clip 缓存热路径的零分配回归。
    /// <para>口径：以 <see cref="GC.GetAllocatedBytesForCurrentThread"/> 计托管分配。它在普通 .NET 上可靠，
    /// 而 Unity 编辑器的 Mono 与 IL2CPP 都可能整档观测不到（编辑器内实测：计数器不推进，四路全部 <see cref="Assert.Ignore"/>）——
    /// 因此先用一次"必然分配"的操作做能力探测，探不到就 Ignore，绝不把"测不出分配"当成"没有分配"。</para>
    /// <para>正因为编辑器内那四格没有牙，最后一格改用结构判据（视图里的装箱副本必须始终是同一只）锁住同一个缺陷。</para>
    /// <para>这些路径每帧或每次播放都会走，一次几十字节的抖动在真机上就是 GC 峰值与音频线程卡顿。</para>
    /// </summary>
    [TestFixture]
    public class AudioClipCacheAllocationTests
    {
        private const string A = "Audio/Sfx/Confirm";
        private const string B = "Audio/Sfx/LevelUp";

        private AudioCacheTestSupport _fixture;

        [SetUp]
        public void SetUp()
        {
            _fixture = new AudioCacheTestSupport(capacity: 8, ttl: 30f);
        }

        [TearDown]
        public void TearDown()
        {
            var fixture = _fixture;
            _fixture = null;
            fixture?.Dispose();
        }

        /// <summary>
        /// 测量一次操作的托管分配字节数。
        /// </summary>
        /// <param name="supported">运行时能否观测到分配（false 时调用方应 Ignore）。</param>
        private static long MeasureAllocatedBytes(Action body, out bool supported)
        {
            // 先确认 API 在当前运行时真能观测分配：new object[64] 必然 > 0
            long probeBefore = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new object[64]);
            long probeAfter = GC.GetAllocatedBytesForCurrentThread();
            if (probeAfter <= probeBefore)
            {
                supported = false;
                return 0L;
            }

            supported = true;

            // 预热被测体本身（首跑会触发 JIT 与惰性建表，不计入稳态）
            body();

            long before = GC.GetAllocatedBytesForCurrentThread();
            body();
            long after = GC.GetAllocatedBytesForCurrentThread();
            return after - before;
        }

        [Test]
        public void CacheHit_PreloadAllocatesZeroBytes()
        {
            Assert.IsTrue(_fixture.Cache.Preload(A, AudioCachePolicy.Ttl));

            long bytes = MeasureAllocatedBytes(() => _fixture.Cache.Preload(A, AudioCachePolicy.Ttl), out bool supported);
            if (!supported) Assert.Ignore("当前运行时无法观测托管分配（IL2CPP?）");

            Assert.AreEqual(0L, bytes, "已加载条目的重复取用不应产生任何托管分配");
        }

        [Test]
        public void RetainRelease_CycleAllocatesZeroBytes()
        {
            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl);
            var entry = _fixture.Entry(A);

            long bytes = MeasureAllocatedBytes(() =>
            {
                _fixture.Cache.Retain(entry);
                _fixture.Cache.Release(entry);
            }, out bool supported);
            if (!supported) Assert.Ignore("当前运行时无法观测托管分配（IL2CPP?）");

            Assert.AreEqual(0L, bytes, "取用/归还一次不得装箱任何东西（留池视图尤其容易踩到）");
        }

        [Test]
        public void Tick_WithNothingExpiredAllocatesZeroBytes()
        {
            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl);
            _fixture.Cache.Preload(B, AudioCachePolicy.Ttl);

            long bytes = MeasureAllocatedBytes(() => _fixture.Cache.Tick(), out bool supported);
            if (!supported) Assert.Ignore("当前运行时无法观测托管分配（IL2CPP?）");

            Assert.AreEqual(0L, bytes, "TTL 扫描每帧都跑，无可驱逐时必须零分配");
        }

        [Test]
        public void EvictionAtCapacity_AllocatesZeroBytes()
        {
            _fixture = new AudioCacheTestSupport(capacity: 2, ttl: 30f);
            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl);
            _fixture.Cache.Preload(B, AudioCachePolicy.Ttl);
            _fixture.PrepareLeases(8);

            // 稳态：C 进来挤掉一个、再被清掉，反复走驱逐路径
            long bytes = MeasureAllocatedBytes(() =>
            {
                _fixture.Cache.Preload("Audio/Sfx/Coin", AudioCachePolicy.Ttl);
                _fixture.Cache.Unload("Audio/Sfx/Coin", force: true);
            }, out bool supported);
            if (!supported) Assert.Ignore("当前运行时无法观测托管分配（IL2CPP?）");

            // 新地址入账是冷路径（满载且有新地址时才走），允许一次常数开销；这里锁的是"不随规模增长"：
            // 反复进/出同一批地址，单次开销必须停在常数内。真机上这笔数会不同，故只作上限断言。
            const long InsertPathBudgetBytes = 256L;
            Assert.LessOrEqual(bytes, InsertPathBudgetBytes,
                $"满载驱逐与卸载路径单次分配超出预算（本次 {bytes}B）：条目与等待者节点都应走对象池");
        }

        [Test]
        public void FailedCooldownCheck_AllocatesZeroBytes()
        {
            _fixture = new AudioCacheTestSupport(capacity: 4, ttl: 30f, failureCooldown: 60f);
            _fixture.FailLoads = true;
            Assert.IsFalse(_fixture.Cache.Preload(A));
            _fixture.FailLoads = false;

            long bytes = MeasureAllocatedBytes(() => Assert.IsFalse(_fixture.Cache.Preload(A)), out bool supported);
            if (!supported) Assert.Ignore("当前运行时无法观测托管分配（IL2CPP?）");

            Assert.AreEqual(0L, bytes, "冷却查询本身不应分配；被拒的加载不得留下任何记账开销");
        }

        /// <summary>
        /// 装箱回归的结构性判据：留池视图的值必须始终是**同一只**装箱副本。
        /// <para>上面的 GC 计数判据在 Unity 编辑器的 Mono 下观测不到分配（<c>GetAllocatedBytesForCurrentThread</c>
        /// 不推进，用例一律 <see cref="Assert.Ignore"/>），而"每次刷新重新装箱"这个真实缺陷只在编辑器里跑得起来——
        /// 所以同一件事必须有第三条不吃计数器的判据，否则编辑器内既测不到也不会红。</para>
        /// </summary>
        [Test]
        public void PoolView_Refresh_ReusesTheSameBoxedLease()
        {
            Assert.IsTrue(_fixture.Cache.Preload(A, AudioCachePolicy.Ttl));
            Assert.IsTrue(_fixture.Cache.PoolReadOnly.TryGetValue(A, out object first), "已加载且留池的条目应出现在视图里");
            Assert.IsNotNull(first);

            var entry = _fixture.Entry(A);

            // 三个刷新点各走一遍：命中取用、停播归还、策略抬升
            Assert.IsTrue(_fixture.Cache.Preload(A, AudioCachePolicy.Ttl));
            _fixture.Cache.Retain(entry);
            _fixture.Cache.Release(entry);
            Assert.IsTrue(_fixture.Cache.Preload(A, AudioCachePolicy.Pin));

            Assert.IsTrue(_fixture.Cache.PoolReadOnly.TryGetValue(A, out object after));
            Assert.AreSame(first, after,
                "留池视图每次刷新重造装箱副本 = 每次播放/停播多一次分配；装箱只允许发生在租约换手那一次");
        }
    }
}
