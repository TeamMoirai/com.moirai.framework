using Moirai.Atropos.Audio;
using NUnit.Framework;
using Testing;

namespace Service.Audio
{
    /// <summary>
    /// Clip 缓存热路径的 0-GC 验收（<b>真机计量</b>，经 <see cref="AllocationCapture.MeasureManaged"/>）。
    /// <para>这五格原住在 EditorMode 程序集：编辑器观测不到托管分配、恒 <c>Assert.Ignore</c>——死格。
    /// 迁入 Tests/Player 与 <c>AudioPerformanceTests</c> 同宿主：编辑器套件可见——采样可用的运行时
    /// 真跑断言、探不到的运行时整组 Ignore（不是假绿），验收以 L3 玩家运行收到的 GC.Alloc 采样为准
    /// （Unity 官方 AllocatingGCMemory 同机制）。</para>
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
                b => Assert.AreEqual(0, b, "已加载条目的重复取用不应产生任何托管分配"));
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
            }, b => Assert.AreEqual(0, b, "取用/归还一次不得装箱任何东西（留池视图尤其容易踩到）"));
        }

        [Test]
        public void Tick_WithNothingExpiredAllocatesZeroBytes()
        {
            _fixture.Cache.Preload(A, EAudioCachePolicy.Ttl);
            _fixture.Cache.Preload(B, EAudioCachePolicy.Ttl);

            AllocationCapture.MeasureManaged("idle-tick", 200,
                () => _fixture.Cache.Tick(),
                b => Assert.AreEqual(0, b, "TTL 扫描每帧都跑，无可驱逐时必须零分配"));
        }

        [Test]
        public void EvictionAtCapacity_BoundedAllocPerCycle()
        {
            _fixture.Dispose();
            _fixture = new AudioCacheTestSupport(capacity: 2, ttl: 30f);
            _fixture.Cache.Preload(A, EAudioCachePolicy.Ttl);
            _fixture.Cache.Preload(B, EAudioCachePolicy.Ttl);
            _fixture.PrepareLeases(8);

            // 单次满载驱逐 + 卸载链路：条目与等待者节点都应走对象池；此路径实测恒 2 个分配事件
            // （与旧字节预算 256B≈2 次小分配对应，2026-09-28 L3 实测）——锁「不随规模增长」，
            // 给 2 事件常数预算而非 0（AssertMainThread 插值修复后 Preload/Unload 守卫已零分配）。
            AllocationCapture.MeasureManaged("evict-cycle", 1, () =>
            {
                _fixture.Cache.Preload("Audio/Sfx/Coin", EAudioCachePolicy.Ttl);
                _fixture.Cache.Unload("Audio/Sfx/Coin", force: true);
            }, b => Assert.LessOrEqual(b, 2, $"满载驱逐与卸载路径分配事件数超出常数预算（本次 {b} 次——锁的是不随规模增长）"));
        }

        [Test]
        public void FailedCooldownCheck_AllocatesZeroBytes()
        {
            _fixture.Dispose();
            _fixture = new AudioCacheTestSupport(capacity: 4, ttl: 30f, failureCooldown: 60f);
            _fixture.FailLoads = true;
            Assert.IsFalse(_fixture.Cache.Preload(A));
            _fixture.FailLoads = false;

            // NUnit 断言自身会分配（Constraint 链每格 5~9 个 GC.Alloc 事件，2026-09-28 实测）——
            // 断言必须留在测量窗外：窗内只取值，窗外判结果。
            bool rejected = true;
            AllocationCapture.MeasureManaged("cooldown-check", 200,
                () => rejected = _fixture.Cache.Preload(A),
                b => Assert.AreEqual(0, b, "冷却查询本身不应分配；被拒的加载不得留下任何记账开销"));
            Assert.IsFalse(rejected, "冷却期内同地址的 Preload 应被拒");
        }
    }
}
