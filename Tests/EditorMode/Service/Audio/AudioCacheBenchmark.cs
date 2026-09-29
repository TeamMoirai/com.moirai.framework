using System;
using System.Diagnostics;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using Moirai.Atropos.Debugger;
using Debug = UnityEngine.Debug;

namespace Service.Audio
{
    /// <summary>
    /// Clip 缓存热路径的 CPU 预算基准（<c>[Explicit]</c>，不进常规回归，按名执行）。
    /// </summary>
    /// <remarks>
    /// 预热固定轮数后按固定调用次数计时，重复三轮取最快一轮换算单次纳秒。
    /// 逐条结果经 Unity 日志报出（与 PlayMode CPU 回归同前缀，便于 grep）；一轮跑完经 <see cref="BenchmarkReport"/> 写 XML 到
    /// &lt;工程根&gt;/Benchmarks/audiocache-benchmark.xml（<c>MOIRAI_BENCH_XML</c> 可覆盖），跨改动对比取该 XML 的数。
    /// 离线运行只反映量级；端到端（声部/混音）预算由 PlayMode 的 <c>AudioCpuRegressionTests</c> 负责。
    /// </remarks>
    [TestFixture]
    [Explicit]
    public class AudioCacheBenchmark
    {
        // NUnit 每个 [Test] 新建 fixture 实例，跨用例累积只能走 static
        private static BenchmarkReport s_Report;

        private const int WarmupCalls = 512;
        private const int RepeatWindows = 3;
        private const int HitCalls = 50_000;
        private const int IdleTickCalls = 50_000;
        private const int EvictCalls = 20_000;

        private const int FullEntries = 128;
        private const int EvictCapacity = 32;
        private const int EvictAddressPool = EvictCapacity * 4;

        // 预算放在实测区间上沿的 3~6 倍：拦得住"每发多一次装箱/每次查表变线性"这类数量级退化，
        // 拦不住 30% 的慢爬——那种对比看导出文件里的最快轮数值。
        // 本机离线实测（纳秒/次，每进程三轮取最小）：命中 155~305、空闲 Tick 46~60、满载驱逐 1180~2150。
        private const double HitBudgetNs = 1_200d;
        private const double IdleTickBudgetNs = 350d;
        private const double EvictBudgetNs = 6_000d;

        /// <summary>已加载条目的重复取用：播放路径每帧都会走这一步。</summary>
        [Test]
        public void CacheHit_PreloadWithinBudget()
        {
            const string address = "Audio/Sfx/Bench";
            using var fixture = new AudioCacheTestSupport(capacity: FullEntries, ttl: 600f);
            Assert.IsTrue(fixture.Cache.Preload(address, EAudioCachePolicy.Ttl));

            double ns = Measure(() => fixture.Cache.Preload(address, EAudioCachePolicy.Ttl), HitCalls);
            Report(nameof(CacheHit_PreloadWithinBudget), ns, HitBudgetNs, HitCalls);

            Assert.AreEqual(1, fixture.Cache.Count, "重复取用同地址不得扩表");
            Assert.LessOrEqual(ns, HitBudgetNs, "已加载条目重复取用的单次耗时超预算");
        }

        /// <summary>空闲 Tick：无可驱逐时应当是常数开销（只比一下 LRU 头的时间）。</summary>
        [Test]
        public void IdleTick_WithinBudget()
        {
            using var fixture = new AudioCacheTestSupport(capacity: FullEntries, ttl: 600f);
            for (int i = 0; i < FullEntries; i++)
            {
                Assert.IsTrue(fixture.Cache.Preload("Audio/Sfx/Bench" + i, EAudioCachePolicy.Ttl));
            }

            double ns = Measure(fixture.Cache.Tick, IdleTickCalls);
            Report(nameof(IdleTick_WithinBudget), ns, IdleTickBudgetNs, IdleTickCalls);

            Assert.AreEqual(FullEntries, fixture.Cache.Count, "TTL 未到期的空闲 Tick 不该驱逐任何条目");
            Assert.LessOrEqual(ns, IdleTickBudgetNs, "空闲 Tick 的单次耗时超预算");
        }

        /// <summary>满载驱逐：新地址挤掉 LRU 头 + 条目复用 + 同步取租约的整条链路。</summary>
        [Test]
        public void EvictionAtCapacity_WithinBudget()
        {
            using var fixture = new AudioCacheTestSupport(capacity: EvictCapacity, ttl: 600f)
            {
                RecycleLeases = true
            };
            fixture.PrepareLeases(EvictAddressPool);

            // 地址字符串预建在计时窗之外：拼接本身是分配 + 哈希，会把基准测成夹具的开销
            var addresses = new string[EvictAddressPool];
            for (int i = 0; i < addresses.Length; i++) addresses[i] = "Audio/Sfx/Evict" + i;

            // 轮转的地址数远大于容量：填满之后每一发都必然挤掉一个 LRU 头
            for (int i = 0; i < EvictAddressPool; i++)
            {
                Assert.IsTrue(fixture.Cache.Preload(addresses[i], EAudioCachePolicy.Ttl),
                    $"第 {i} 发预载应成功；走到满载拒绝说明测的是拒绝路径而不是驱逐链路");
            }

            Assert.AreEqual(EvictCapacity, fixture.Cache.Count, "填满后驻留数应等于容量");

            int slot = 0;
            double ns = Measure(() => fixture.Cache.Preload(addresses[slot++ % EvictAddressPool], EAudioCachePolicy.Ttl),
                EvictCalls);
            Report(nameof(EvictionAtCapacity_WithinBudget), ns, EvictBudgetNs, EvictCalls);

            Assert.LessOrEqual(fixture.LiveHandles, EvictCapacity, $"租约台账应贴着容量波动（实测 {fixture.LiveHandles}）");
            Assert.LessOrEqual(ns, EvictBudgetNs, "满载驱逐链路的单次耗时超预算");
        }

        /// <summary>预热后重复计时 <see cref="RepeatWindows"/> 轮，取最快一轮：抖动只会让某一轮变慢，取最小值才可比。</summary>
        private static double Measure(Action body, int calls)
        {
            for (int i = 0; i < WarmupCalls; i++) body();

            double best = double.PositiveInfinity;
            for (int round = 0; round < RepeatWindows; round++)
            {
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < calls; i++) body();
                clock.Stop();

                double ns = clock.Elapsed.TotalSeconds * 1_000_000_000d / calls;
                if (ns < best) best = ns;
            }

            return best;
        }

        private static void Report(string caseName, double nsPerCall, double budgetNs, long calls)
        {
            // 与 PlayMode 的 CPU 回归同一行前缀，便于 grep；数值同时进统一 XML 报告
            string line = string.Format("CPU,{0},{1},{2:F1}ns,limit={3:F0}", caseName, calls, nsPerCall, budgetNs);
            Debug.Log(line);

            s_Report.Add(new BenchmarkCaseResult
            {
                Name = caseName,
                Category = "AudioCache",
                Iterations = (int)calls,
                Trials = RepeatWindows,
                NsPerOp = nsPerCall,
            }.Metric("budgetNs", budgetNs.ToString("F0")));
        }

        /// <summary>一轮基准结束后把 XML 报告写到统一文件夹（域重载会重建 fixture，所以报告是 static）。</summary>
        [OneTimeSetUp]
        public static void SetUpReport()
        {
            s_Report = new BenchmarkReport("AudioCache");
        }

        [OneTimeTearDown]
        public static void ExportXml()
        {
            s_Report?.WriteXml(s_Report.ResolveXmlPath());
        }
    }
}
