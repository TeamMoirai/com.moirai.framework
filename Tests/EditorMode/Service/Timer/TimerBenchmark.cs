using Moirai.Atropos.Debugger;
using Moirai.Atropos.Timer;
using NUnit.Framework;

namespace Service.Timer
{
    /// <summary>
    /// 计时器性能基准（<c>[Explicit]</c>——不参与常规回归，按名手动执行）。
    /// <para>Tests 侧薄壳：同步矩阵本体在运行时的 <see cref="TimerBenchmarkRunner"/>——
    /// Debugger 的 Timer 调试窗口（Run Benchmark 按钮）与这里共用同一驱动器，两个入口测的是同一份代码；
    /// Tests 入口跑完把 XML 报告写到统一文件夹 &lt;工程根&gt;/Benchmarks/timerservice-benchmark.xml。
    /// 依赖真实帧推进的回调触发/同刻突发用例在 PlayMode 的 <c>TimerFireBenchmarkTests</c>。</para>
    /// <para>软校验在驱动器内只累加 failures 计数——正确性回归由 Timer 测试族负责。</para>
    /// </summary>
    [TestFixture]
    [Explicit]
    public sealed class TimerBenchmark
    {
        [Test]
        public void RunSyncMatrix_MeasuresAndExportsXml()
        {
            BenchmarkReport report = TimerBenchmarkRunner.Run();
            report.WriteXml(report.ResolveXmlPath());
        }
    }
}
