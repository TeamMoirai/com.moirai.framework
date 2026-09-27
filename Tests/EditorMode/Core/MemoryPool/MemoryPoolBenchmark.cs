using Moirai.Atropos.Debugger;
using NUnit.Framework;

namespace Core.MemoryPool
{
    /// <summary>
    /// 内存池性能基准（<c>[Explicit]</c>——不参与常规回归，按名手动执行）。
    /// <para>Tests 侧薄壳：矩阵本体在运行时的 <see cref="MemoryPoolBenchmarkRunner"/>——
    /// Debugger 的 MemoryPool 窗口（Run Benchmark 按钮）与这里共用同一驱动器，
    /// 保证两个入口测的是同一份代码；Tests 入口跑完把 XML 报告写到统一文件夹
    /// &lt;工程根&gt;/Benchmarks/memorypool-benchmark.xml。</para>
    /// <para>不变量校验在驱动器内为软校验（只累加 failures 计数并 LogWarning）——
    /// 正确性回归由 MemoryPoolMaintenanceTests / MemoryPoolOwnershipTests 负责。</para>
    /// </summary>
    [TestFixture]
    [Explicit]
    public sealed class MemoryPoolBenchmark
    {
        [Test]
        public void RunFullMatrix_MeasuresAndExportsXml()
        {
            BenchmarkReport report = MemoryPoolBenchmarkRunner.Run();
            report.WriteXml(report.ResolveXmlPath());
        }
    }
}
