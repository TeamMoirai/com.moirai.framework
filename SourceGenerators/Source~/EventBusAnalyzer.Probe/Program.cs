using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Moirai.EventBus.Roslyn.Analyzers;

namespace Moirai.EventBus.Roslyn.Probe;

/// <summary>
/// EventBusAnalyzer 自检 Probe：以内存编译验证四条诊断（MA7000–MA7003）。
/// 通过标准：诊断计数精确匹配 + 程序集内不含任何 Source Generator（不再产出胶水代码）。
/// </summary>
internal static class Program
{
    private const string FrameworkSource = @"
namespace Moirai.Atropos
{
    public interface IGameEvent { }

    public delegate void EventDelegate<TEvent>(in TEvent evt) where TEvent : struct, IGameEvent;

    public static class EventBus
    {
        public static void Subscribe<TEvent>(EventDelegate<TEvent> handler) where TEvent : struct, IGameEvent { }
        public static void Unsubscribe<TEvent>(EventDelegate<TEvent> handler) where TEvent : struct, IGameEvent { }
    }
}
";

    private const string UserSource = @"
using Moirai.Atropos;
using System.Collections.Generic;

namespace Game
{
    public readonly struct PingEvent : IGameEvent { public readonly int Value; }
    public readonly struct PongEvent : IGameEvent { public readonly int Value; }
    public readonly struct BarEvent : IGameEvent { public readonly int Value; }
    public readonly struct QuxEvent : IGameEvent { public readonly int Value; }

    // 签名规范用例：订阅胶水不再生成，用户自行控制
    public partial class PlayerSystem
    {
        private int _calls;
        public void OnPingEvent(PingEvent evt) { _calls += evt.Value; }              // MA7001：缺 in
        public void OnPongEvent() { _calls++; }                                       // 无参不再要求 partial
        public void OnBarEvent(ref BarEvent evt) { _calls += evt.Value; }             // MA7001：ref
        public void OnQuxEvent(in QuxEvent evt) { _calls += evt.Value; }              // 合规
    }

    // MA7000 ×2；命名处理器成对退订 → MA7003 0
    public class LegacySystem
    {
        private readonly List<int> _received = new List<int>();
        public void Wire()
        {
            EventBus.Subscribe<PingEvent>((in PingEvent e) => _received.Add(e.Value));   // MA7000
            EventBus.Subscribe<PingEvent>(OnPingEvent);
            EventBus.Unsubscribe<PingEvent>((in PingEvent e) => _received.Add(e.Value)); // MA7000
            EventBus.Unsubscribe<PingEvent>(OnPingEvent);
        }
        public void OnPingEvent(in PingEvent evt) { }
    }

    // MA7002
    public class MismatchSystem
    {
        public void OnPingEvent(PongEvent evt) { }
    }

    // 非 partial 无参回调：新方案不再告警
    public class NonPartialSystem
    {
        public void OnPongEvent() { }
    }

    // 成对订阅 → 不告警
    public class PairedSystem
    {
        public void OnEnable() => EventBus.Subscribe<PongEvent>(OnPongEvent);
        public void OnDisable() => EventBus.Unsubscribe<PongEvent>(OnPongEvent);
        public void OnPongEvent(in PongEvent evt) { }
    }

    // 漏退订 → MA7003 ×1
    public class LeakySystem
    {
        public void Wire() => EventBus.Subscribe<QuxEvent>(OnQuxEvent);
        public void OnQuxEvent(in QuxEvent evt) { }
    }

    // 2 订阅 1 退订 → MA7003 ×1（多出的那一个）
    public class DoubleSubscribeSystem
    {
        public void Wire()
        {
            EventBus.Subscribe<PingEvent>(OnPingEvent);
            EventBus.Subscribe<PingEvent>(OnPingEvent);
        }
        public void TearDown() => EventBus.Unsubscribe<PingEvent>(OnPingEvent);
        public void OnPingEvent(in PingEvent evt) { }
    }

    // 退订用了另一个处理器 → 订阅侧 MA7003 ×1
    public class CrossHandlerSystem
    {
        public void Wire()
        {
            EventBus.Subscribe<PingEvent>(OnPingEvent);
            EventBus.Unsubscribe<PingEvent>(OnOtherPingEvent);
        }
        public void OnPingEvent(in PingEvent evt) { }
        public void OnOtherPingEvent(in PingEvent evt) { }
    }

    // 委托字段订阅成对 → 不告警
    public class DelegateFieldSystem
    {
        private EventDelegate<PingEvent> _handler;
        public DelegateFieldSystem() { _handler = OnPingEvent; }
        public void Wire() => EventBus.Subscribe(_handler);
        public void TearDown() => EventBus.Unsubscribe(_handler);
        private void OnPingEvent(in PingEvent evt) { }
    }
}
";

    // partial 分部甲：订阅
    private const string PartialSourceA = @"
using Moirai.Atropos;

namespace Game
{
    public partial class PartialSystem
    {
        public void Wire() => EventBus.Subscribe<BarEvent>(OnBarEvent);
        public void OnBarEvent(in BarEvent evt) { }
    }
}
";

    // partial 分部乙：退订（跨文件也应配对成功）
    private const string PartialSourceB = @"
using Moirai.Atropos;

namespace Game
{
    public partial class PartialSystem
    {
        public void TearDown() => EventBus.Unsubscribe<BarEvent>(OnBarEvent);
    }
}
";

    public static int Main()
    {
        var failures = new List<string>();

        // —— 程序集形态：纯分析器，零 Source Generator ——
        var analyzerAssembly = typeof(EventBusAnonymousHandlerAnalyzer).Assembly;
        var generatorTypes = analyzerAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IIncrementalGenerator).IsAssignableFrom(t))
            .ToList();
        Record(failures, generatorTypes.Count == 0,
            "EventBusAnalyzer.dll 不应包含 Source Generator（胶水代码生成已移除），实际："
            + string.Join(", ", generatorTypes.Select(t => t.FullName)));

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        var references = BuildReferences(failures);

        var trees = new[]
        {
            SyntaxFactory.ParseSyntaxTree(FrameworkSource, parseOptions, path: "Framework.g.cs"),
            SyntaxFactory.ParseSyntaxTree(UserSource, parseOptions, path: "User.cs"),
            SyntaxFactory.ParseSyntaxTree(PartialSourceA, parseOptions, path: "Partial.A.cs"),
            SyntaxFactory.ParseSyntaxTree(PartialSourceB, parseOptions, path: "Partial.B.cs"),
        };

        var compilation = CSharpCompilation.Create(
            "Probe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (compileErrors.Count > 0)
        {
            failures.Add("样例源码自身存在编译错误：\n" + string.Join("\n", compileErrors.Select(d => d.ToString())));
        }

        // —— 诊断分析器 ——
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new EventBusAnonymousHandlerAnalyzer(),
            new OnEventCallbackSignatureAnalyzer(),
            new EventBusSubscriptionPairAnalyzer(),
            new GameEventAttributeAnalyzer());

        var moi = compilation
            .WithAnalyzers(analyzers)
            .GetAllDiagnosticsAsync(CancellationToken.None).Result
            .Where(d => d.Id.StartsWith("MA", StringComparison.Ordinal))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ThenBy(d => d.Location.SourceSpan.Start)
            .ToList();

        var expected = new Dictionary<string, int>
        {
            ["MA7000"] = 2, // LegacySystem 的 lambda Subscribe + Unsubscribe
            ["MA7001"] = 2, // PlayerSystem 缺 in + ref 参数
            ["MA7002"] = 1, // MismatchSystem 参数类型不匹配
            ["MA7003"] = 3, // LeakySystem 漏退订 + DoubleSubscribe 多一个 + CrossHandler 处理器不同
        };

        foreach (var id in expected.Keys)
        {
            var actual = moi.Count(d => d.Id == id);
            var check = actual == expected[id];
            Record(failures, check, $"{id} 期望 {expected[id]} 条，实际 {actual} 条"
                + (check ? "" : " → " + string.Join(" | ", moi.Where(d => d.Id == id).Select(d => d.ToString()))));
        }

        // MA7003 应落在具体的 Subscribe 调用上（而非 Unsubscribe）
        var unpaired = moi.Where(d => d.Id == "MA7003").ToList();
        Record(failures, unpaired.All(d => d.GetMessage().Contains("EventBus.Subscribe")),
            "MA7003 应报告在订阅点：" + string.Join(" | ", unpaired.Select(d => d.GetMessage())));

        if (failures.Count > 0)
        {
            Console.Error.WriteLine("==== EventBusAnalyzer.Probe 失败 ====");
            foreach (var failure in failures) Console.Error.WriteLine(" - " + failure);
            return 1;
        }

        Console.WriteLine("==== EventBusAnalyzer.Probe 全部通过 ====");
        Console.WriteLine("MA 诊断计数：" + string.Join(", ", moi.GroupBy(d => d.Id).Select(g => g.Key + "×" + g.Count())));
        Console.WriteLine("源生成器数量：0（胶水代码不再生成，订阅由用户自管）");
        return 0;
    }

    private static void Record(List<string> failures, bool pass, string detail)
    {
        if (!pass) failures.Add(detail);
    }

    private static List<MetadataReference> BuildReferences(List<string> failures)
    {
        var references = new List<MetadataReference>();
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var name in new[] { "System.Private.CoreLib.dll", "System.Runtime.dll", "System.Collections.dll" })
        {
            var path = Path.Combine(runtimeDir, name);
            if (File.Exists(path)) references.Add(MetadataReference.CreateFromFile(path));
            else failures.Add("缺少引用程序集：" + path);
        }
        return references;
    }
}
