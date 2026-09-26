using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Policy
{
    /// <summary>
    /// 测试反射策略守卫：把「哪些测试文件允许用 <c>BindingFlags.NonPublic</c>」钉成白名单。
    /// <para>《测试规范》规定测试不得用反射读写字段——反射把字段名变成测试依赖，改名不报编译错、
    /// 只在运行期 <c>GetField</c> 返回 null 后 NRE；需要触达的成员应开 <c>internal</c>（编译器把关）。
    /// 规范若只写在文档里，下一次"顺手反射一下"没人拦得住，所以这里用一格用例把它变成可执行约束。</para>
    /// <para>白名单只保留三类正当用途：① 契约形状守卫（遍历 API 形状/读标注，只能反射）；
    /// ② 唤起 Unity 生命周期回调（Awake/OnEnable/OnInit/OnValidate，EditMode 不自动跑）；
    /// ③ 产码字段探针。另有两个基础设施桥需要探 Unity/UTF 的内部成员。</para>
    /// <para>白名单双向断言：未登记的文件不得出现该模式；已登记的文件必须仍然存在且仍然命中——
    /// 否则名单会腐烂成一张没人维护的清单。</para>
    /// </summary>
    [TestFixture]
    public sealed class ReflectionPolicyGuardTests
    {
        /// <summary>被禁止的反射模式。</summary>
        private const string FORBIDDEN = "BindingFlags.NonPublic";

        /// <summary>
        /// 守卫自身不参与扫描——它的常量与失败提示文案里本来就含该模式字符串。
        /// </summary>
        private const string SELF_FILE_NAME = "ReflectionPolicyGuardTests.cs";

        /// <summary>
        /// 允许使用非公开反射的文件（相对 <c>Tests/</c>，正斜杠分隔）及其归类。
        /// </summary>
        private static readonly Dictionary<string, string> Allowlist = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── 基础设施桥（探 Unity / Unity Test Framework 内部成员，非测试夹具） ──
            ["EditorMode/EditorStateBridge.cs"] = "桥：探 ConsoleWindow.GetCountsByType 与 TestRunnerApi.IsRunActive",
            ["EditorMode/TestRequestRunner.cs"] = "桥：探 TestRunnerApi.IsRunning / IsRunActive",

            // ── ① 契约形状守卫 ──
            ["EditorMode/Core/GameApp/PlayerLoopDriverTests.cs"] = "形状守卫：读私有静态入口的 [RuntimeInitializeOnLoadMethod] 属性",
            ["EditorMode/Service/Resource/AddressableHandlerFailFastTests.cs"] = "形状守卫：遍历方法集断言 fail-fast 面",
            ["EditorMode/Service/Resource/ResourceSeamShapeGuardTests.cs"] = "形状守卫：统计抽象成员 / internal abstract / [Obsolete]",
            ["EditorMode/Service/Resource/ResourceMethodSetContractTests.cs"] = "形状守卫：连非公开成员一起遍历方法集，断言返回 IResourceOperation 的名单",
            ["EditorMode/Service/Resource/YooAssetHandlerSmokeTests.cs"] = "形状守卫：断言运行期数组字段带 [NonSerialized]",

            // ── ② 唤起 Unity 生命周期回调（EditMode 不自动执行） ──
            ["EditorMode/Core/Singleton/SingletonMonoTests.cs"] = "生命周期：Awake / OnDestroy",
            ["EditorMode/Service/Input/PreventInputOnEnableTests.cs"] = "生命周期：OnEnable / OnDisable",
            ["EditorMode/Service/Save/SaveAssetCatalogTests.cs"] = "生命周期：OnValidate",
            ["Player/Service/Audio/AudioPerformanceTests.cs"] = "生命周期：OnInit",
            ["PlayMode/Service/Audio/AudioCpuRegressionTests.cs"] = "生命周期：OnInit",
            ["PlayMode/Service/Audio/AudioLeakAcceptanceTests.cs"] = "生命周期：OnInit",
            ["PlayMode/Service/Audio/AudioMiddlewareBackendFailurePlayModeTests.cs"] = "测试替身的私有桥方法唤起",
            ["PlayMode/Service/Audio/AudioMiddlewareMixPlayModeTests.cs"] = "生命周期：OnInit",
            ["PlayMode/Service/Audio/AudioOwnershipTests.cs"] = "生命周期：OnInit",
            ["PlayMode/Service/Audio/AudioPausePlayModeTests.cs"] = "生命周期：OnInit / OnShutdown",
            ["PlayMode/Service/Audio/AudioServicePlayModeTests.cs"] = "生命周期：OnInit",
            ["PlayMode/Service/Audio/AudioServiceStressPlayModeTests.cs"] = "生命周期：OnInit",
            ["PlayMode/Service/Audio/AudioVoiceDuckingE2ETests.cs"] = "生命周期：OnInit",

            // ── ③ 产码字段探针 ──
            ["EditorMode/Core/MemoryPool/MemoryPoolFixtureTests.cs"] = "产码字段探针：读 s_PageCapacity 一类生成字段",
        };

        /// <summary>
        /// 未登记的文件不得出现非公开反射。
        /// </summary>
        [Test]
        public void NonPublicReflection_OnlyInAllowlistedFiles()
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

                if (File.ReadAllText(file).Contains(FORBIDDEN, StringComparison.Ordinal))
                {
                    offenders.Add(relative);
                }
            }

            Assert.IsEmpty(offenders,
                "以下测试文件新增了非公开反射，但《测试规范》禁止测试用反射读写字段。\n" +
                "正确做法：把需要触达的成员从 private 放宽到 internal（Runtime/AssemblyInfo.cs 已对三个测试程序集开 InternalsVisibleTo），\n" +
                "已有窄接缝的成员走生成的 Internal_PeekHandler()/Internal_UseHandler(next) 或新增 Internal_* 接缝。\n" +
                "若确属白名单三类（契约形状守卫 / 生命周期唤起 / 产码字段探针），把它登记进 ReflectionPolicyGuardTests.Allowlist 并写明归类。\n" +
                "命中文件：\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// 白名单不得腐烂：每一项都必须存在且仍然命中该模式。
        /// </summary>
        [Test]
        public void Allowlist_EntriesStillExistAndStillMatch()
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

                if (!File.ReadAllText(full).Contains(FORBIDDEN, StringComparison.Ordinal))
                {
                    stale.Add(entry.Key + "（已不再使用非公开反射——请从名单移除，别留着占位）");
                }
            }

            Assert.IsEmpty(stale,
                "反射白名单有腐烂条目，请同步 ReflectionPolicyGuardTests.Allowlist：\n  " + string.Join("\n  ", stale));
        }

        /// <summary>
        /// 解析被测包根目录下的 <c>Tests/</c>。经 <see cref="PackageInfo"/> 定位，避免依赖当前工作目录。
        /// </summary>
        private static string ResolveTestsRoot()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(Moirai.Atropos.GameApp).Assembly);
            Assert.IsNotNull(package, "无法定位 Moirai.Atropos 所属包，反射守卫无法解析测试源码根目录。");

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
