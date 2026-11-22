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

namespace Moirai.Atropos.SourceGenerators.Probe;

/// <summary>
/// ProviderHandlerHost 发射形状自检：服务契约只有一条装配路径、类契约仍走直驱、初始化监视器唯一、MA1005 已退役。
/// </summary>
/// <remarks>
/// 通过标准：全部断言为真且进程退出码 0。断言含正反两面——重新引入运行期判型分派、把清槽登记挪回分支里、
/// 或再发出第二把同名初始化锁，都会在此判红，不等 Unity 编译。
/// </remarks>
internal static class Program
{
    private const string ResetterCall = "RegisterHandlerResetterIfNeeded();";
    private const string ResetterDefinition = "private static void RegisterHandlerResetterIfNeeded()";
    private const string SharedLock = "global::Moirai.Atropos.HandlerBase.InitializationLock";
    private const string LegacyLock = "global::Moirai.Atropos.FrameworkHandler.s_InitializationLock";
    private const string RegisterCall = "GameServices.RegisterService(";
    private const string UnregisterCall = "GameServices.UnregisterHandler(";
    private const string InitCall = "Internal_Init();";
    private const string ShutdownCall = "Internal_Shutdown();";
    private const string BackendGuardCall = "RequireServiceBackend(";
    private const string BackendGuardThrow = "backend must derive from ServiceBase, got ";

    /// <summary>契约桩：只提供分析器与契约校验要绑定的类型与特性，不含生成产物。</summary>
    private const string ContractStubSource = @"
using System;

namespace Moirai.Atropos
{
    public class ProviderHandlerHostAttribute : Attribute
    {
        public ProviderHandlerHostAttribute(Type handlerType) { }
        public ProviderHandlerHostAttribute(Type handlerType, Type defaultHandlerType) { }
    }

    public abstract class HandlerBase { }
    public abstract class FrameworkHandler : HandlerBase { }
    public interface IService { }
    public abstract class ServiceBase : HandlerBase, IService { }

    public interface IProbeService { }
    public sealed class ProbeServiceBackend : ServiceBase, IProbeService { }
    public sealed class ProbeLegacyBackend : FrameworkHandler, IProbeService { }
    public sealed class ProbeUtilityBackend : FrameworkHandler { }

    [ProviderHandlerHost(typeof(IProbeService), typeof(ProbeLegacyBackend))]
    public static partial class LegacyHost { }

    [ProviderHandlerHost(typeof(IProbeService), typeof(ProbeServiceBackend))]
    public static partial class ServiceHost { }

    [ProviderHandlerHost(typeof(FrameworkHandler), typeof(ProbeUtilityBackend))]
    public static partial class UtilityHost { }
}
";

    public static int Main()
    {
        var failures = new List<string>();

        var serviceText = ProviderHandlerHostGenerator.GenerateCodeForTesting(ServiceHostInfo);
        var utilityText = ProviderHandlerHostGenerator.GenerateCodeForTesting(UtilityHostInfo);
        var serviceResolve = Between(serviceText,
            "IAudioService ResolveHandler(bool preInitAllowed)", "private static void AssignHandler");
        var serviceAssign = Between(serviceText, "void AssignHandler(IAudioService value)", ResetterDefinition);

        // —— 服务契约：一条装配路径，零运行期判型 ——
        Record(failures, Count(serviceText, "(global::Moirai.Atropos.FrameworkHandler)created") == 0,
            "服务路径重新出现了 FrameworkHandler)created 过渡分支");
        Record(failures, Count(serviceText, "FrameworkHandler") == 0,
            "服务路径文本里不应再出现 FrameworkHandler，实际 " + Count(serviceText, "FrameworkHandler") + " 处");
        Record(failures, !serviceText.Contains("is global::Moirai.Atropos.ServiceBase", StringComparison.Ordinal)
                && !serviceText.Contains("as global::Moirai.Atropos.ServiceBase", StringComparison.Ordinal),
            "服务路径不得按实例类型运行期分派（is/as ServiceBase 已退役，非法声明由 MA1003 编译期拦截）");
        Record(failures, Count(serviceResolve, ResetterCall) == 1,
            "ResolveHandler 应恰有 1 处清槽登记，实际 " + Count(serviceResolve, ResetterCall) + " 处");
        Record(failures, Count(serviceAssign, ResetterCall) == 1,
            "AssignHandler 应恰有 1 处清槽登记，实际 " + Count(serviceAssign, ResetterCall) + " 处");
        Record(failures, Count(serviceText, ResetterCall) == 2,
            "服务宿主全类应只有两条装配路径各 1 处清槽登记，实际 " + Count(serviceText, ResetterCall) + " 处");
        Record(failures, Count(serviceText, ResetterDefinition) == 1,
            "清槽登记方法本身应只发射一份（不得被复制进分支）");
        Record(failures, Count(serviceText, RegisterCall) == 2 && Count(serviceText, UnregisterCall) == 1,
            "服务宿主应为 2 次注册 + 1 次注销，实际 "
            + Count(serviceText, RegisterCall) + "/" + Count(serviceText, UnregisterCall));
        Record(failures, Count(serviceText, BackendGuardCall) == 4
                && serviceText.Contains(BackendGuardThrow, StringComparison.Ordinal),
            "三处服务基座转换应各走同一条带宿主名与契约名的判错守卫（1 定义 + 3 调用），实际 "
            + Count(serviceText, BackendGuardCall) + " 处");
        Record(failures, Count(utilityText, BackendGuardCall) == 0,
            "类契约路径不发射服务基座守卫");
        Record(failures, Ordered(serviceResolve, RegisterCall, ResetterCall, "registered;"),
            "ResolveHandler 的发射次序必须是：注册进容器 → 登记清槽 → 回填槽位");
        Record(failures, Ordered(serviceAssign, UnregisterCall, RegisterCall, ResetterCall),
            "AssignHandler 的发射次序必须是：收旧 → 注册新进 → 登记清槽");

        // —— 唯一的初始化监视器 ——
        Record(failures, Count(serviceText, LegacyLock) == 0 && Count(utilityText, LegacyLock) == 0,
            "生成代码不得再引用 FrameworkHandler 上的旧锁字段");
        Record(failures, Count(serviceText, SharedLock) == 2 && Count(utilityText, SharedLock) == 2,
            "两族宿主各应引用 HandlerBase.InitializationLock 两次，实际 "
            + Count(serviceText, SharedLock) + "/" + Count(utilityText, SharedLock));

        // —— 类契约：直驱生命周期保持原样，不进容器也不登记世界清槽 ——
        Record(failures, Count(utilityText, InitCall) == 2 && Count(utilityText, ShutdownCall) == 1,
            "类契约宿主应为 2 次直驱装配 + 1 次直驱关停，实际 "
            + Count(utilityText, InitCall) + "/" + Count(utilityText, ShutdownCall));
        Record(failures, !utilityText.Contains("RegisterService", StringComparison.Ordinal)
                && !utilityText.Contains(ResetterDefinition, StringComparison.Ordinal),
            "类契约（工具宿主）不进容器，也不发射世界清槽");
        Record(failures, Count(utilityText, ResetterCall) == 0,
            "类契约宿主不得调用世界清槽登记，实际 " + Count(utilityText, ResetterCall) + " 处");
        Record(failures, Count(serviceText, InitCall) == 0 && Count(serviceText, ShutdownCall) == 0,
            "服务契约宿主不得再发射直驱生命周期调用");
        Record(failures, serviceText.Contains("WarmHandler", StringComparison.Ordinal)
                && !utilityText.Contains("WarmHandler", StringComparison.Ordinal),
            "WarmHandler 预热入口只发给静态服务宿主");

        // —— 发射文本必须语法可解析：产物由字符串拼装，括号平衡不靠肉眼 ——
        Record(failures, SyntaxClean(serviceText), "服务宿主发射文本语法不合法：" + SyntaxErrors(serviceText));
        Record(failures, SyntaxClean(utilityText), "类契约宿主发射文本语法不合法：" + SyntaxErrors(utilityText));

        // —— MA1005 退役 + 接口契约的 FrameworkHandler 派生改判非法 ——
        var analyzerIds = new ProviderHandlerHostAnalyzer().SupportedDiagnostics
            .Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Record(failures, analyzerIds.Count == 4 && !analyzerIds.Contains("MA1005", StringComparer.Ordinal),
            "分析器支持集应为 MA1001–MA1004，实际：" + string.Join(",", analyzerIds));

        var stubFailures = new List<string>();
        var compilation = BuildContractCompilation(stubFailures);
        failures.AddRange(stubFailures);
        if (compilation != null)
        {
            CheckContractRules(failures, compilation);
            CheckReportedDiagnostics(failures, compilation);
        }

        if (failures.Count > 0)
        {
            Console.Error.WriteLine("==== ProviderHandlerHost.Probe 失败 ====");
            foreach (var failure in failures) Console.Error.WriteLine(" - " + failure);
            return 1;
        }

        Console.WriteLine("==== ProviderHandlerHost.Probe 全部通过 ====");
        Console.WriteLine("服务宿主：清槽登记 Resolve=" + Count(serviceResolve, ResetterCall)
            + " Assign=" + Count(serviceAssign, ResetterCall)
            + "，注册/注销=" + Count(serviceText, RegisterCall) + "/" + Count(serviceText, UnregisterCall)
            + "，FrameworkHandler 出现=" + Count(serviceText, "FrameworkHandler")
            + "，共享锁引用=" + Count(serviceText, SharedLock));
        Console.WriteLine("类契约宿主：直驱装配/关停=" + Count(utilityText, InitCall) + "/" + Count(utilityText, ShutdownCall)
            + "，清槽登记=" + Count(utilityText, ResetterCall) + "，共享锁引用=" + Count(utilityText, SharedLock));
        Console.WriteLine("分析器支持集：" + string.Join(",", analyzerIds) + "（MA1005 已退役，不复用）");
        Console.WriteLine("—— 服务契约装配段实发文本（ResolveHandler 的 try 体）——");
        Console.WriteLine(Between(serviceText, "s_InitializingHandler = true;", "return s_Handler;").TrimEnd());
        Console.WriteLine("—— 服务契约换入段实发文本（AssignHandler 的 try 体）——");
        Console.WriteLine(Between(serviceAssign, "s_Handler = null;", "                finally").TrimEnd());
        return 0;
    }

    /// <summary>服务契约宿主的合成输入（settings 优先 + 声明默认兜底的量产形状）。</summary>
    private static ProviderHandlerHostGenerator.ProviderHandlerHostInfo ServiceHostInfo { get; } =
        new("AudioService", "Moirai.Atropos", "IAudioService", "global::Moirai.Atropos.IAudioService",
            isServiceContract: true, hasUserCreateDefaultHandler: false,
            declaredDefaultFqn: "global::Moirai.Atropos.UnityAudioHandler",
            hasGetHandlerFromSettings: true, isStatic: true);

    /// <summary>类契约工具宿主的合成输入（直驱生命周期的 Utility 族形状）。</summary>
    private static ProviderHandlerHostGenerator.ProviderHandlerHostInfo UtilityHostInfo { get; } =
        new("LogUtility", "Moirai.Atropos", "LogHandler", "global::Moirai.Atropos.LogHandler",
            isServiceContract: false, hasUserCreateDefaultHandler: false,
            declaredDefaultFqn: "global::Moirai.Atropos.DefaultLogHandler",
            hasGetHandlerFromSettings: true, isStatic: true);

    /// <summary>默认声明的合法性判据：接口契约只认 ServiceBase 派生，类契约只认 FrameworkHandler 派生。</summary>
    private static void CheckContractRules(List<string> failures, CSharpCompilation compilation)
    {
        var contract = compilation.GetTypeByMetadataName("Moirai.Atropos.IProbeService")!;
        var utilityContract = compilation.GetTypeByMetadataName("Moirai.Atropos.FrameworkHandler")!;
        var serviceBackend = compilation.GetTypeByMetadataName("Moirai.Atropos.ProbeServiceBackend")!;
        var legacyBackend = compilation.GetTypeByMetadataName("Moirai.Atropos.ProbeLegacyBackend")!;
        var utilityBackend = compilation.GetTypeByMetadataName("Moirai.Atropos.ProbeUtilityBackend")!;

        var legacyAccepted = HandlerContract.IsValidDefaultHandler(
            legacyBackend, contract, true, out var legacyReasons);
        Record(failures, !legacyAccepted, "接口契约 + FrameworkHandler 派生须判非法（MA1005 放行已退役）");
        Record(failures, legacyReasons.Contains("ServiceBase", StringComparison.Ordinal),
            "非法理由须指向 ServiceBase，实际：" + legacyReasons);

        var serviceAccepted = HandlerContract.IsValidDefaultHandler(
            serviceBackend, contract, true, out var serviceReasons);
        Record(failures, serviceAccepted, "接口契约 + ServiceBase 派生须判合法，理由：" + serviceReasons);

        var utilityAccepted = HandlerContract.IsValidDefaultHandler(
            utilityBackend, utilityContract, true, out var utilityReasons);
        Record(failures, utilityAccepted, "类契约 + FrameworkHandler 派生须判合法，理由：" + utilityReasons);
    }

    /// <summary>过渡形态在编译期由 MA1005 改判 MA1003：合法的服务/工具宿主声明不得带出任何诊断。</summary>
    private static void CheckReportedDiagnostics(List<string> failures, CSharpCompilation compilation)
    {
        var diagnostics = compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ProviderHandlerHostAnalyzer()))
            .GetAllDiagnosticsAsync(CancellationToken.None).Result
            .Where(d => d.Id.StartsWith("MA", StringComparison.Ordinal))
            .ToList();
        var reported = string.Join(",", diagnostics.Select(d =>
            d.Id + "@L" + d.Location.GetLineSpan().StartLinePosition.Line));

        Record(failures, diagnostics.Count == 1 && diagnostics[0].Id == "MA1003",
            "只应由 MA1003 对过渡形态判红且无其它诊断，实际：" + reported);
        Record(failures, !diagnostics.Any(d => d.Id == "MA1005"),
            "MA1005 不得再被上报，实际：" + reported);
    }

    private static CSharpCompilation? BuildContractCompilation(List<string> failures)
    {
        var references = new List<MetadataReference>();
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var name in new[]
                 { "System.Private.CoreLib.dll", "System.Runtime.dll", "netstandard.dll", "System.Collections.dll" })
        {
            var path = Path.Combine(runtimeDir, name);
            if (File.Exists(path)) references.Add(MetadataReference.CreateFromFile(path));
            else failures.Add("缺少引用程序集：" + path);
        }

        var compilation = CSharpCompilation.Create(
            "ProviderHandlerHostContractStub",
            new[] { SyntaxFactory.ParseSyntaxTree(ContractStubSource) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            failures.Add("契约桩自身存在编译错误：\n" + string.Join("\n", errors.Select(d => d.ToString())));
            return null;
        }

        return compilation;
    }

    /// <summary>取两个标记之间的发射文本片段；起点缺失时返回空串，让调用方的计数断言判红。</summary>
    private static string Between(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;

        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static bool Ordered(string text, params string[] needles)
    {
        var previous = -1;
        foreach (var needle in needles)
        {
            var current = text.IndexOf(needle, StringComparison.Ordinal);
            if (current <= previous) return false;
            previous = current;
        }

        return true;
    }

    private static bool SyntaxClean(string text) => SyntaxErrors(text).Length == 0;

    private static string SyntaxErrors(string text)
    {
        return string.Join(" | ", CSharpSyntaxTree.ParseText(text).GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Location.GetLineSpan().StartLinePosition.Line + ":" + d.GetMessage()));
    }

    private static void Record(List<string> failures, bool pass, string detail)
    {
        if (!pass) failures.Add(detail);
    }
}
