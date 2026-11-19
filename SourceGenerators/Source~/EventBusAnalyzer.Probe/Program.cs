using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Moirai.EventBus.Roslyn.Analyzers;
using Moirai.EventBus.Roslyn.Generators;

namespace Moirai.EventBus.Roslyn.Probe;

/// <summary>
/// EventBusCodegen 自检 Probe：以内存编译验证四条诊断（MIRAI700-0004）与生成胶水代码的正确性。
/// 通过标准：诊断计数精确匹配 + 生成源码可编译零错误。
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

    // 合规宿主：in 参数 + 无参回调（期望生成 SubscribeEvents/UnsubscribeEvents）
    public partial class PlayerSystem
    {
        private int _calls;

        public void OnPingEvent(PingEvent evt) { _calls += evt.Value; }              // MIRAI701：缺 in（生成器跳过订阅）
        public void OnPongEvent() { _calls++; }                                       // 合规（无参，缓存包装桥接）
        public void OnBarEvent(ref BarEvent evt) { _calls += evt.Value; }             // MIRAI701：ref（生成器跳过订阅）
        public void OnQuxEvent(in QuxEvent evt) { _calls += evt.Value; }              // 合规（方法组直连）
        public void SubscribeEvents() { }                                             // 命名冲突 → 生成器应回退 SubscribeBusEvents
    }

    // 匿名函数订阅（MIRAI700 ×2）+ 命名方法订阅（合规）
    public class LegacySystem
    {
        private readonly List<int> _received = new List<int>();

        public void Wire()
        {
            EventBus.Subscribe<PingEvent>((in PingEvent e) => _received.Add(e.Value));   // MIRAI700
            EventBus.Subscribe<PingEvent>(OnPingEvent);                                  // 合规
            EventBus.Unsubscribe<PingEvent>((in PingEvent e) => _received.Add(e.Value)); // MIRAI700
        }

        public void OnPingEvent(in PingEvent evt) { } // 合规
    }

    // 参数类型与方法名后缀不匹配（MIRAI702）
    public partial class MismatchSystem
    {
        public void OnPingEvent(PongEvent evt) { }
    }

    // 非 partial 宿主上的无参回调（MIRAI703）
    public class NonPartialSystem
    {
        public void OnPongEvent() { }
    }
}
";

    public static int Main()
    {
        var failures = new List<string>();

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        var references = BuildReferences(failures);

        var trees = new[]
        {
            SyntaxFactory.ParseSyntaxTree(FrameworkSource, parseOptions, path: "Framework.g.cs"),
            SyntaxFactory.ParseSyntaxTree(UserSource, parseOptions, path: "User.cs"),
        };

        var compilation = CSharpCompilation.Create(
            "Probe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var initialDiagnostics = compilation.GetDiagnostics();
        var compileErrors = initialDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (compileErrors.Count > 0)
        {
            failures.Add("样例源码自身存在编译错误：\n" + string.Join("\n", compileErrors.Select(d => d.ToString())));
        }

        // —— 诊断分析器 ——
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new EventBusAnonymousHandlerAnalyzer(),
            new OnEventCallbackSignatureAnalyzer());

        var analysisDiagnostics = compilation
            .WithAnalyzers(analyzers)
            .GetAllDiagnosticsAsync(CancellationToken.None).Result;

        var moi = analysisDiagnostics
            .Where(d => d.Id.StartsWith("MIRAI", StringComparison.Ordinal))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ThenBy(d => d.Location.SourceSpan.Start)
            .ToList();

        var expected = new Dictionary<string, int>
        {
            ["MIRAI700"] = 2, // LegacySystem lambda Subscribe + Unsubscribe
            ["MIRAI701"] = 2, // PlayerSystem 缺 in + ref 参数
            ["MIRAI702"] = 1, // MismatchSystem 参数类型不匹配
            ["MIRAI703"] = 1, // NonPartialSystem 非 partial 无参回调
        };

        foreach (var id in expected.Keys)
        {
            var actual = moi.Count(d => d.Id == id);
            var check = actual == expected[id];
            Record(failures, check, $"{id} 期望 {expected[id]} 条，实际 {actual} 条"
                + (check ? "" : " → " + string.Join(" | ", moi.Where(d => d.Id == id).Select(d => d.ToString()))));
        }

        // —— 源生成器 ——
        var driver = CSharpGeneratorDriver.Create(
            new[] { new EventBusSubscriptionGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(t => Path.GetFileName(t.FilePath).StartsWith("EventBusSubscriptions_", StringComparison.Ordinal))
            .ToList();

        Record(failures, generatedTrees.Count == 2,
            $"期望生成 2 个宿主胶水文件（PlayerSystem/MismatchSystem），实际 {generatedTrees.Count}（{string.Join(", ", generatedTrees.Select(t => t.FilePath))}）");

        var playerGen = generatedTrees.FirstOrDefault(t => Path.GetFileName(t.FilePath).Contains("PlayerSystem"));
        if (playerGen is not null)
        {
            var text = playerGen.GetText().ToString();
            Record(failures, text.Contains("SubscribeBusEvents"),
                "PlayerSystem 已声明 SubscribeEvents——生成器应回退命名 SubscribeBusEvents");
            Record(failures, text.Contains("??="), "无参回调应有缓存包装委托（??=）");
            Record(failures, text.Contains("global::Moirai.Atropos.EventBus.Subscribe<Game.QuxEvent>(OnQuxEvent);"),
                "in 参数回调应方法组直连 EventBus.Subscribe<T>");
            Record(failures, text.Contains("(in Game.PongEvent _) => OnPongEvent();"),
                "无参回调应桥接为 (in PongEvent _) => OnPongEvent()");
            Record(failures, text.Contains("未按 in 约定声明"),
                "缺 in/ref 处理器应被跳过并留注释");
            Record(failures, !text.Contains("Subscribe<global::Game.PingEvent>"),
                "缺 in 的 OnPingEvent 不应直连订阅");
        }

        // 生成诊断应为空
        var genErrors = generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Record(failures, genErrors.Count == 0,
            "生成器不应报错：" + string.Join(" | ", genErrors.Select(d => d.ToString())));

        // 生成代码可编译（含生成树重新编译，零 Error）
        var recompiled = outputCompilation.WithAnalyzers(analyzers).GetAllDiagnosticsAsync(CancellationToken.None).Result;
        var recompileErrors = recompiled.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Record(failures, recompileErrors.Count == 0,
            "含生成代码的整编译应零错误：\n" + string.Join("\n", recompileErrors.Select(d => d.ToString())));

        // 生成文件应被 <auto-generated> 豁免诊断（PlayerSystem 的 MIRAI701 不应复制进生成代码）
        var genFileDiagnostics = recompiled
            .Where(d => d.Location.SourceTree != null
                && generatedTrees.Contains(d.Location.SourceTree)
                && d.Id.StartsWith("MIRAI", StringComparison.Ordinal))
            .ToList();
        Record(failures, genFileDiagnostics.Count == 0,
            "生成代码应被分析器豁免：" + string.Join(" | ", genFileDiagnostics.Select(d => d.ToString())));

        if (failures.Count > 0)
        {
            Console.Error.WriteLine("==== EventBusAnalyzer.Probe 失败 ====");
            foreach (var failure in failures) Console.Error.WriteLine(" - " + failure);
            return 1;
        }

        Console.WriteLine("==== EventBusAnalyzer.Probe 全部通过 ====");
        Console.WriteLine($"MIRAI 诊断计数：{string.Join(", ", moi.GroupBy(d => d.Id).Select(g => g.Key + "×" + g.Count()))}");
        Console.WriteLine("生成文件：" + string.Join(", ", generatedTrees.Select(t => t.FilePath)));
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
