using System;
using System.Diagnostics;
using Moirai.Atropos;
using NUnit.Framework;
using Moirai.Atropos.Debugger;

namespace Service.Kernel
{
    /// <summary>内核性能基准（<c>[Explicit]</c>，不参与常规回归，优化对比时手动运行）。</summary>
    /// <remarks>
    /// 测量项：服务注册、跨作用域查找、Tick 分派（含 GC 采样）。
    /// 编辑器 Mono 基准噪声 ±2×，只做同轮内相对比较；跑完经 <see cref="BenchmarkReport"/> 落统一文件夹 &lt;工程根&gt;/Benchmarks/kernelbenchmark-benchmark.xml。
    /// </remarks>
    [Explicit]
    [TestFixture]
    public sealed class KernelBenchmark
    {
        private const int SERVICE_COUNT = 64;
        private const int LOOKUP_ITERATIONS = 100_000;
        private const int TICK_ITERATIONS = 10_000;

        private sealed class BenchService : ServiceBase, IServiceTickable
        {
            public override void OnInit() { }
            public override void OnShutdown() { }
            public void Tick(float elapseSeconds, float realElapseSeconds) { }
        }

        private ServiceWorld _world;

        // NUnit 每个 [Test] 新建 fixture 实例，跨用例累积走 static（与 AudioCacheBenchmark 同口径）
        private static BenchmarkReport s_Report;
        private static long s_WallStart;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            s_Report = new BenchmarkReport("KernelBenchmark");
            s_WallStart = Stopwatch.GetTimestamp();
        }

        [OneTimeTearDown]
        public void ExportXml()
        {
            s_Report.TotalMs = (Stopwatch.GetTimestamp() - s_WallStart) * 1000.0 / Stopwatch.Frequency;
            s_Report.WriteXml(s_Report.ResolveXmlPath());
        }

        [SetUp]
        public void SetUp() => _world = new ServiceWorld();

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
            _world = null;
        }

        [Test]
        public void Register_64Services_MeasuresThroughput()
        {
            var services = new BenchService[SERVICE_COUNT];
            for (int i = 0; i < SERVICE_COUNT; i++) services[i] = new BenchService();

            // 预热 JIT
            _world.Register(EServiceScopeKind.App, services[0]);
            _world.Initialize();
            _world.Dispose();
            _world = new ServiceWorld();

            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < SERVICE_COUNT; i++)
                _world.Register(EServiceScopeKind.App, services[i]);
            _world.Initialize();
            double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            s_Report.Add(new BenchmarkCaseResult
            {
                Name = "Register+Initialize x" + SERVICE_COUNT,
                Category = "Kernel",
                Iterations = SERVICE_COUNT,
                Trials = 1,
                MinMs = elapsedMs,
                MeanMs = elapsedMs,
                MaxMs = elapsedMs,
                NsPerOp = elapsedMs * 1_000_000.0 / SERVICE_COUNT,
            });

            UnityEngine.Debug.Log($"[KernelBenchmark] Register+Initialize ×{SERVICE_COUNT}: {elapsedMs:F2}ms total, {elapsedMs * 1000.0 / SERVICE_COUNT:F1}μs/service");
            Assert.Pass();
        }

        [Test]
        public void Lookup_CrossScope_MeasuresNsPerOp()
        {
            var service = new BenchService();
            _world.Register(EServiceScopeKind.App, service);
            _world.Initialize();

            // 预热
            for (int i = 0; i < 1000; i++) _ = _world.TryGetService<BenchService>(out _);

            long start = Stopwatch.GetTimestamp();
            int hits = 0;
            for (int i = 0; i < LOOKUP_ITERATIONS; i++)
            {
                if (_world.TryGetService<BenchService>(out _)) hits++;
            }
            double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            s_Report.Add(new BenchmarkCaseResult
            {
                Name = "TryGetService cross-scope",
                Category = "Kernel",
                Iterations = LOOKUP_ITERATIONS,
                Trials = 1,
                MinMs = elapsedMs,
                MeanMs = elapsedMs,
                MaxMs = elapsedMs,
                NsPerOp = elapsedMs * 1e6 / LOOKUP_ITERATIONS,
            });

            UnityEngine.Debug.Log($"[KernelBenchmark] TryGetService ×{LOOKUP_ITERATIONS}: {elapsedMs:F2}ms total, {elapsedMs * 1e6 / LOOKUP_ITERATIONS:F0}ns/op, hits={hits}");
            Assert.AreEqual(LOOKUP_ITERATIONS, hits);
        }

        [Test]
        public void TickDispatch_64Services_MeasuresNsPerFrame_WithGcSample()
        {
            for (int i = 0; i < SERVICE_COUNT; i++)
                _world.Register(EServiceScopeKind.App, new BenchService());
            _world.Initialize();

            // 预热（含 lazy-sort 收敛）
            for (int i = 0; i < 100; i++) _world.Tick(0.016f, 0.016f);

            long gcBefore = GC.CollectionCount(0);
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < TICK_ITERATIONS; i++)
                _world.Tick(0.016f, 0.016f);
            double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            long gcAfter = GC.CollectionCount(0);

            double nsPerFrame = elapsedMs * 1e6 / TICK_ITERATIONS;
            s_Report.Add(new BenchmarkCaseResult
            {
                Name = "Tick dispatch x" + TICK_ITERATIONS,
                Category = "Kernel",
                Iterations = TICK_ITERATIONS,
                Trials = 1,
                MinMs = elapsedMs,
                MeanMs = elapsedMs,
                MaxMs = elapsedMs,
                NsPerOp = nsPerFrame,
            }.Metric("services", SERVICE_COUNT).Metric("gen0Gc", gcAfter - gcBefore));
            UnityEngine.Debug.Log($"[KernelBenchmark] Tick ×{TICK_ITERATIONS} frames ({SERVICE_COUNT} services): " +
                $"{elapsedMs:F2}ms total, {nsPerFrame:F0}ns/frame ({nsPerFrame / SERVICE_COUNT:F1}ns/service/frame), Gen0 GC={gcAfter - gcBefore}");

            // 0-Alloc 门禁：热路径 Tick 分派不得触发 Gen0 GC
            Assert.AreEqual(gcBefore, gcAfter, "Tick 分派热路径应 0-Alloc（不得触发 Gen0 GC）");
        }
    }
}
