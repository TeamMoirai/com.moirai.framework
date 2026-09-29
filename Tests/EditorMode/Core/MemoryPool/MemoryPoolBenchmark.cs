using Moirai.Atropos.Debugger;
using NUnit.Framework;

namespace Core.MemoryPool
{
    /// <summary>
    /// 内存池性能基准（<c>[Explicit]</c>，按名手动执行）。
    /// </summary>
    /// <remarks>
    /// Tests 侧薄壳：矩阵本体在运行时的 <see cref="MemoryPoolBenchmarkRunner"/>，Debugger 的 MemoryPool 窗口与这里共用同一驱动器，保证两入口测同一份代码； <br />
    /// 跑完把 XML 报告写到 <c>&lt;工程根&gt;/Benchmarks/memorypool-benchmark.xml</c>。 <br />
    /// 不变量校验在驱动器内为软校验（只累加 failures 计数并记告警），正确性回归由 <c>MemoryPoolMaintenanceTests</c> / <c>MemoryPoolOwnershipTests</c> 负责。
    /// </remarks>
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
