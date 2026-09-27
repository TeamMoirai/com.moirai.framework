using Moirai.Atropos.Audio;
using NUnit.Framework;
using Testing;

namespace Service.Audio
{
    /// <summary>
    /// Clip 缓存热路径的 0-GC 验收（<b>玩家专用</b>，经 <see cref="AllocationCapture.MeasureManaged"/> 计量）。
    /// <para>这五格原住在 EditorMode 程序集并自带探测：编辑器托管分配计数器不推进，任何环境下都
    /// <c>Assert.Ignore</c>——玩家构建又不含 Editor 程序集，等于哪个环境都不执行。按「依赖托管分配计量的
    /// 判定只能在 L3」迁入 Tests/Player，与 <c>AudioPerformanceTests</c> 同宿主。</para>
    /// <para>口径：预热一次丢弃（JIT/池扩容/新地址入账落在预热里），再计 N 次；新地址入账是冷路径
    /// （满载且有新地址时才走），驱逐链路单次给常数上限而不是 0——锁的是「不随规模增长」。</para>
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class AudioClipCacheAllocationTests
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

        [Test]
        public void MeasureManaged_DetectsKnownAllocation()
        {
            AllocationCapture.CalibrateKnownAllocation();
        }

        [Test]
        public void CacheHit_PreloadAllocatesZeroBytes()
        {
            Assert.IsTrue(_fixture.Cache.Preload(A, EAudioCachePolicy.Ttl));

            AllocationCapture.MeasureManaged("cache-hit", 200,
                () => _fixture.Cache.Preload(A, EAudioCachePolicy.Ttl),
                b => Assert.AreEqual(0L, b, "已加载条目的重复取用不应产生任何托管分配"));
        }

        [Test]
        public void RetainRelease_CycleAllocatesZeroBytes()
        {
            _fixture.Cache.Preload(A, EAudioCachePolicy.Ttl);
            var entry = _fixture.Entry(A);

            AllocationCapture.MeasureManaged("retain-release", 200, () =>
            {
                _fixture.Cache.Retain(entry);
                _fixture.Cache.Release(entry);
            }, b => Assert.AreEqual(0L, b, "取用/归还一次不得装箱任何东西（留池视图尤其容易踩到）"));
        }

        [Test]
        public void Tick_WithNothingExpiredAllocatesZeroBytes()
        {
            _fixture.Cache.Preload(A, EAudioCachePolicy.Ttl);
            _fixture.Cache.Preload(B, EAudioCachePolicy.Ttl);

            AllocationCapture.MeasureManaged("idle-tick", 200,
                () => _fixture.Cache.Tick(),
                b => Assert.AreEqual(0L, b, "TTL 扫描每帧都跑，无可驱逐时必须零分配"));
        }

        [Test]
        public void EvictionAtCapacity_BoundedAllocPerCycle()
        {
            _fixture = new AudioCacheTestSupport(capacity: 2, ttl: 30f);
            _fixture.Cache.Preload(A, EAudioCachePolicy.Ttl);
            _fixture.Cache.Preload(B, EAudioCachePolicy.Ttl);
            _fixture.PrepareLeases(8);

            // 单次满载驱逐 + 卸载链路：条目与等待者节点都应走对象池，分配只许停在常数内（不随规模增长）。
            AllocationCapture.MeasureManaged("evict-cycle", 1, () =>
            {
                _fixture.Cache.Preload("Audio/Sfx/Coin", EAudioCachePolicy.Ttl);
                _fixture.Cache.Unload("Audio/Sfx/Coin", force: true);
            }, b => Assert.LessOrEqual(b, 256L, $"满载驱逐与卸载路径单次分配超出常数预算（本次 {b}B）"));
        }

        [Test]
        public void FailedCooldownCheck_AllocatesZeroBytes()
        {
            _fixture = new AudioCacheTestSupport(capacity: 4, ttl: 30f, failureCooldown: 60f);
            _fixture.FailLoads = true;
            Assert.IsFalse(_fixture.Cache.Preload(A));
            _fixture.FailLoads = false;

            AllocationCapture.MeasureManaged("cooldown-check", 200,
                () => Assert.IsFalse(_fixture.Cache.Preload(A)),
                b => Assert.AreEqual(0L, b, "冷却查询本身不应分配；被拒的加载不得留下任何记账开销"));
        }
    }
}
