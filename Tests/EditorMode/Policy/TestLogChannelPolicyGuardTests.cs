using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Policy
{
    /// <summary>
    /// 测试日志通道策略守卫：测试代码自身的日志发射一律走 <c>UnityEngine.Debug.Log*</c>， <br />
    /// 不得经 <c>LogUtility</c> 的发射方法（Verbose/Debug/Info/Warning/Error/Fatal/Assert）。
    /// </summary>
    /// <remarks>
    /// <c>LogUtility</c> 是带分类过滤与 Handler 管道的运行时基础设施，测试诊断走它会让「这条日志算不算失败」取决于测试域恰好激活的 Handler 配置； <br />
    /// <c>Debug.Log*</c> 对 UTF 的可见性则是确定的。
    /// 断言通道不受此守卫约束：<see cref="LogUtility.OnMessageLogged"/> 订阅是捕获运行时日志的唯一稳定通道， <br />
    /// <see cref="UtfLogExpect"/> 是消除未处理日志的统一入口（读 Handler 状态做可见性判定，不是发射）。
    /// 白名单两类正当用途：① 被测本体（LogUtility 自身的语义回归必须发射 LogUtility）；② 替身复刻（fake loader 复现生产侧错误发射，错误路径断言依赖该可观察行为）。 <br />
    /// 白名单双向断言：未登记的不得出现发射模式，已登记的必须仍存在且仍命中，否则名单腐烂。结构与 <see cref="ReflectionPolicyGuardTests"/> 同构。
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
                "· 断言运行时日志内容 → LogUtility.OnMessageLogged 事件捕获；\n" +
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
