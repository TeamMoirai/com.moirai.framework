using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using Debug = UnityEngine.Debug;

namespace Service.Audio
{
    /// <summary>
    /// Clip 缓存热路径的 CPU 预算基准（<c>[Explicit]</c>：不进常规回归，只在需要时按名执行）。
    /// <para>口径：预热固定轮数后按<b>固定调用次数</b>计时，重复三轮取最快一轮，换算单次纳秒。
    /// 用固定次数而不是固定时长，是为了让不同机器的样本量一致——时长窗口会让慢机器只跑到很少的次数，
    /// 机器抖动直接进结论；取最小值则让"抖动"只表现为轮与轮的差异，而不污染跨改动的对比。</para>
    /// <para>预算取"松到不被抖动判红、紧到能抓住数量级退化"的量级。实测值两条通道：逐条经 <c>Debug.Log</c>
    /// 报出（与 PlayMode CPU 回归同前缀，便于 grep），一轮跑完再整批追加到导出文件
    /// （<c>MOIRAI_AUDIO_BENCH_FILE</c> 覆盖路径，缺省落系统临时目录）——跨改动对比取文件里的数。
    /// 离线跑只能看量级（时钟桩与 JIT 都跟 Unity 不同），真机数值以编辑器内按名运行为准。
    /// 这里是纯托管路径的缓存层基准，端到端（声部/混音）的预算由 PlayMode 的
    /// <c>AudioCpuRegressionTests</c> 把。</para>
    /// </summary>
    [TestFixture]
    [Explicit]
    public class AudioCacheBenchmark
    {
        /// <summary>导出路径的环境变量覆盖；不设则落系统临时目录。</summary>
        private const string ENV_EXPORT_PATH = "MOIRAI_AUDIO_BENCH_FILE";

        // NUnit 每个 [Test] 新建 fixture 实例，跨用例累积只能走 static
        private static readonly List<string> s_Lines = new List<string>();

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
            // 与 PlayMode 的 CPU 回归同一行前缀，便于 grep；两边都留一份：编辑器日志与导出文件
            string line = string.Format("CPU,{0},{1},{2:F1}ns,limit={3:F0}", caseName, calls, nsPerCall, budgetNs);
            Debug.Log(line);

            lock (s_Lines)
            {
                s_Lines.Add(line);
            }
        }

        /// <summary>一轮基准结束后把全部数值追加到导出文件（域重载会重建 fixture，所以累积表是 static）。</summary>
        [OneTimeTearDown]
        public void Export()
        {
            string[] lines;
            lock (s_Lines)
            {
                lines = s_Lines.ToArray();
                s_Lines.Clear();
            }

            if (lines.Length == 0) return;

            // env 覆盖优先：CI 可以把数值导到构建产物目录，不指定就落系统临时目录
            string target = Environment.GetEnvironmentVariable(ENV_EXPORT_PATH);
            if (string.IsNullOrEmpty(target))
            {
                target = Path.Combine(Path.GetTempPath(), "AudioCacheBenchmark.txt");
            }

            try
            {
                var report = new StringBuilder();
                for (int i = 0; i < lines.Length; i++)
                {
                    report.Append(DateTime.Now.ToString("u")).Append("  ").Append(lines[i]).Append(Environment.NewLine);
                }

                File.AppendAllText(target, report.ToString());
                Debug.Log(string.Format("[AudioCacheBenchmark] 数值已追加到 {0}", target));
            }
            catch (Exception e)
            {
                // 报告写不出去不影响判定：数值已经在日志里
                Debug.LogWarning(string.Format("[AudioCacheBenchmark] 写不出导出文件（{0}）：{1}", target, e.Message));
            }
        }
    }
}
