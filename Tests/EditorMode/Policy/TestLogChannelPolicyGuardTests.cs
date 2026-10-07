using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Policy
{
    /// <summary>
    /// 测试日志通道策略守卫：测试代码自身的日志发射一律走 <c>UnityEngine.Debug.Log*</c>（不得经 <c>LogUtility</c> 的发射方法）， <br />
    /// UTF 日志预期的声明/开关/计数 API 不得在用例侧直用（一律经 <c>UtfLogExpect</c>，Debug 直发场景白名单豁免）。
    /// </summary>
    /// <remarks>
    /// <c>LogUtility</c> 是带分类过滤与 Handler 管道的运行时基础设施，测试诊断走它会让「这条日志算不算失败」取决于测试域恰好激活的 Handler 配置； <br />
    /// <c>Debug.Log*</c> 对 UTF 的可见性则是确定的。 <br />
    /// 断言通道不受此守卫约束：<see cref="Moirai.Atropos.LogUtility.onMessageLogged"/> 订阅是捕获运行时日志的唯一稳定通道， <br />
    /// <c>UtfLogExpect</c> 是消除未处理日志的统一入口（读 Handler 状态做可见性判定，不是发射）。 <br />
    /// LogAssert 直用禁令的两条豁免：两份 <c>UtfLogExpect</c> 支撑副本（实现处本身），与「被测走 Debug 直发、不经 LogUtility」的场景（该链路不涉处理器判定，LogAssert 是唯一通道）。 <br />
    /// 发射禁令白名单两类正当用途：① 被测本体（LogUtility 自身的语义回归必须发射 LogUtility）；② 替身复刻（fake loader 复现生产侧错误发射，错误路径断言依赖该可观察行为）。 <br />
    /// 两条名单都双向断言：未登记的不得出现该模式，已登记的必须仍存在且仍命中，否则名单腐烂。结构与 <see cref="ReflectionPolicyGuardTests"/> 同构。
    /// </remarks>
    [TestFixture]
    public sealed class TestLogChannelPolicyGuardTests
    {
        /// <summary>被禁止的发射方法调用字面（verbatim、无前缀空格，代码与注释同判）。
        /// 注意：这不是语法级识别——注释/文档里写出完整字面同样命中，提及时措辞用「LogUtility 的 X」规避。</summary>
        private static readonly string[] FORBIDDEN_EMISSIONS =
        {
            "LogUtility.Verbose(",
            "LogUtility.Debug(",
            "LogUtility.Info(",
            "LogUtility.Warning(",
            "LogUtility.Error(",
            "LogUtility.Fatal(",
            "LogUtility.Assert(",
        };

        /// <summary>守卫自身不参与扫描——失败提示文案与白名单理由里本来就含这些模式字符串。</summary>
        private const string SELF_FILE_NAME = "TestLogChannelPolicyGuardTests.cs";

        /// <summary>允许发射 LogUtility 的文件（相对 <c>Tests/</c>，正斜杠分隔）及其归类。</summary>
        private static readonly Dictionary<string, string> Allowlist = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── ① 被测本体 ──
            ["EditorMode/Utility/LogUtilityTests.cs"] = "被测本体：LogUtility 门面的语义回归必须发射 LogUtility",

            // ── ② 替身复刻：fake loader 复现生产错误发射，错误路径断言依赖该可观察行为 ──
            ["EditorMode/Service/Save/SaveEntityPersistenceTests.cs"] = "替身复刻：fake loader 复现「Prefab key 未注册」的生产错误发射",
            ["EditorMode/Service/Save/SaveEntityIncrementalTests.cs"] = "替身复刻：fake loader 复现「Prefab key 未注册」的生产错误发射",
        };

        /// <summary>被禁止在用例侧直用的 UTF 日志预期 API 字面（守卫自身除外）。</summary>
        /// <remarks>声明/开关/计数判负三种形态都在内——它们把处理器可见性判定散落进用例，正是 UtfLogExpect 要收拢的。</remarks>
        private static readonly string[] ForbiddenLogAssertApis =
        {
            "LogAssert.Expect(",
            "LogAssert.ignoreFailingMessages",
            "LogAssert.NoUnexpectedReceived(",
        };

        /// <summary>允许直用 LogAssert 的文件（相对 <c>Tests/</c>）及其归类。</summary>
        /// <remarks>白名单两类：UtfLogExpect 的两份程序集本地副本（统一入口的实现处）； <br />
        /// 以及「被测走 <c>Debug.Log*</c> 直发、不经 LogUtility」的场景——那条链路 UTF 恒可见，声明预期不涉及处理器判定，LogAssert 是唯一正确通道。</remarks>
        private static readonly Dictionary<string, string> LogAssertAllowlist = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EditorMode/Support/UtfLogExpect.cs"] = "统一入口：处理器可见性判定、ScopedIgnore 窗口与 ErrorWithException 级别判定的唯一实现处",
            ["PlayMode/Support/UtfLogExpect.cs"] = "统一入口：PlayMode 程序集本地副本（asmdef 拓扑不可跨程序集共享）",
            ["EditorMode/Service/Debugger/DebuggerLogCaptureTests.cs"] = "被测是 Unity 控制台日志捕获器，用例自发射 Debug.LogError 直发、不经 LogUtility——LogAssert 是唯一正确通道",
        };

        /// <summary>
        /// 未登记的文件不得发射 LogUtility。
        /// </summary>
        [Test]
        public void LogUtilityEmissions_OnlyInAllowlistedFiles()
        {
            string testsRoot = ResolveTestsRoot();
            List<string> offenders = new List<string>();

            foreach (string file in EnumerateTestSources(testsRoot))
            {
                string relative = ToRelative(testsRoot, file);
                if (Allowlist.ContainsKey(relative))
                {
                    continue;
                }

                if (ContainsAnyEmission(File.ReadAllText(file)))
                {
                    offenders.Add(relative);
                }
            }

            Assert.IsEmpty(offenders,
                "以下测试文件经 LogUtility 发射日志，但《测试规范》要求测试自身的日志输出统一走 Debug.Log*：\n" +
                "· 测试诊断输出 → Debug.Log / LogWarning / LogError（对 UTF 的可见性是确定的）；\n" +
                "· 断言运行时日志内容 → LogUtility.onMessageLogged 事件捕获；\n" +
                "· 消除未处理日志 → UtfLogExpect.Error()/Warning()；\n" +
                "· 替身复刻生产侧发射（断言依赖该可观察行为）→ 登记进本守卫 Allowlist 并写明归类。\n" +
                "命中文件：\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// 白名单不得腐烂：每一项都必须存在且仍然命中发射模式。
        /// </summary>
        [Test]
        public void Allowlist_EntriesStillExistAndStillEmit()
        {
            string testsRoot = ResolveTestsRoot();
            List<string> stale = new List<string>();

            foreach (KeyValuePair<string, string> entry in Allowlist)
            {
                string full = Path.Combine(testsRoot, entry.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    stale.Add(entry.Key + "（文件不存在——改名或删除后要同步名单）");
                    continue;
                }

                if (!ContainsAnyEmission(File.ReadAllText(full)))
                {
                    stale.Add(entry.Key + "（已不再发射 LogUtility——请从名单移除，别留着占位）");
                }
            }

            Assert.IsEmpty(stale,
                "日志通道白名单有腐烂条目，请同步 TestLogChannelPolicyGuardTests.Allowlist：\n  " +
                string.Join("\n  ", stale));
        }

        /// <summary>
        /// 未登记的文件不得在用例侧直用 LogAssert 的声明/开关/计数 API。
        /// </summary>
        [Test]
        public void LogAssertDirectUsage_OnlyInAllowlistedFiles()
        {
            string testsRoot = ResolveTestsRoot();
            List<string> offenders = new List<string>();

            foreach (string file in EnumerateTestSources(testsRoot))
            {
                string relative = ToRelative(testsRoot, file);
                if (LogAssertAllowlist.ContainsKey(relative))
                {
                    continue;
                }

                if (ContainsAnyForbiddenLogAssertApi(File.ReadAllText(file)))
                {
                    offenders.Add(relative);
                }
            }

            Assert.IsEmpty(offenders,
                "以下测试文件直用了 LogAssert，但《测试规范》要求经 UtfLogExpect 统一声明：\n" +
                "· LogUtility 发射的日志 → UtfLogExpect.Error()/Warning()/Exception()（处理器可见性判定收在那一处，正则固定 .*）；\n" +
                "· 带异常对象的 Error → UtfLogExpect.ErrorWithException(fragment)（级别判定同收）；\n" +
                "· 错误集不可枚举的故障注入 → using (UtfLogExpect.ScopedIgnore())（快照还原由 using 保证）；\n" +
                "· 内容断言 → LogUtility.onMessageLogged 捕获（与处理器无关）；\n" +
                "· 被测走 Debug.Log 直发、不经 LogUtility → LogAssert 是唯一正确通道，登记 LogAssertAllowlist 并写明归类。\n" +
                "命中文件：\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// LogAssert 白名单不得腐烂：每一项都必须存在且仍然直用。
        /// </summary>
        [Test]
        public void LogAssertAllowlist_EntriesStillExistAndStillUse()
        {
            string testsRoot = ResolveTestsRoot();
            List<string> stale = new List<string>();

            foreach (KeyValuePair<string, string> entry in LogAssertAllowlist)
            {
                string full = Path.Combine(testsRoot, entry.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    stale.Add(entry.Key + "（文件不存在——改名或删除后要同步名单）");
                    continue;
                }

                if (!ContainsAnyForbiddenLogAssertApi(File.ReadAllText(full)))
                {
                    stale.Add(entry.Key + "（已不再直用 LogAssert——请从名单移除，别留着占位）");
                }
            }

            Assert.IsEmpty(stale,
                "LogAssert 白名单有腐烂条目，请同步 TestLogChannelPolicyGuardTests.LogAssertAllowlist：\n  " +
                string.Join("\n  ", stale));
        }

        private static bool ContainsAnyEmission(string source)
        {
            for (int i = 0; i < FORBIDDEN_EMISSIONS.Length; i++)
            {
                if (source.Contains(FORBIDDEN_EMISSIONS[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAnyForbiddenLogAssertApi(string source)
        {
            for (int i = 0; i < ForbiddenLogAssertApis.Length; i++)
            {
                if (source.Contains(ForbiddenLogAssertApis[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 解析被测包根目录下的 <c>Tests/</c>。经 <see cref="PackageInfo"/> 定位，避免依赖当前工作目录。
        /// </summary>
        private static string ResolveTestsRoot()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(Moirai.Atropos.GameApp).Assembly);
            Assert.IsNotNull(package, "无法定位 Moirai.Atropos 所属包，日志通道守卫无法解析测试源码根目录。");

            string testsRoot = Path.Combine(package.resolvedPath, "Tests");
            Assert.IsTrue(Directory.Exists(testsRoot), $"测试源码目录不存在：{testsRoot}");

            return testsRoot;
        }

        /// <summary>
        /// 枚举测试源码（跳过守卫自身与 obj/bin 等构建产物）。
        /// </summary>
        private static IEnumerable<string> EnumerateTestSources(string testsRoot)
        {
            return Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !string.Equals(Path.GetFileName(path), SELF_FILE_NAME, StringComparison.Ordinal))
                .Where(path => path.IndexOf($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) < 0)
                .Where(path => path.IndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) < 0);
        }

        private static string ToRelative(string testsRoot, string file)
        {
            return Path.GetRelativePath(testsRoot, file).Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
