using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Moirai.Atropos.Tests.EditorMode
{
    /// <summary>
    /// 请求式测试驱动：让开着 Unity 编辑器的工作机（或本仓库的 Agent 会话）不抢锁、不重启就能跑 Test Runner。
    /// <para>batchmode 打不开同一个工程（<c>Temp/UnityLockfile</c> 被占），而「改完代码要证据」这件事又不该
    /// 每次都等人去点 Test Runner。这里在测试程序集里挂一个 <c>EditorApplication.update</c> 轮询（调试桥）：
    /// 工程 <c>Temp/</c> 下出现请求文件就按里面的过滤器执行一轮，把结果与逐格进度回写到指定路径。</para>
    /// <para><b>跨域重载</b>：PlayMode 进出场各触发一次域重载，静态字段会归零。本驱动把「请求 + 计数 + 失败详情」
    /// 落盘到 <c>Temp/MoiraiTestRunState.json</c>，每次域加载时恢复并重新注册回调，并在
    /// <c>AssemblyReloadEvents.beforeAssemblyReload</c> 兜底落盘一次；Test Framework 侧
    /// <c>TestJobDataHolder.ResumeRunningJobs</c> 会续跑作业，派发时按注册表找回调，因此 PlayMode 全程可收齐结果。</para>
    /// <para>协议（都是 <c>Temp/</c> 下的普通文件，调用方只轮询、不双向通信）：</para>
    /// <list type="bullet">
    ///   <item><description>请求 <c>Temp/MoiraiTestRequest.json</c>：<c>{id, mode(EditMode|PlayMode), output, assemblies[], tests[], timeoutSeconds}</c>。读到即删除，避免重复执行。</description></item>
    ///   <item><description>进度 <c>{output}.progress</c>：先写 <c>STARTED</c>，随后是正在跑的用例全名；收口时删除。</description></item>
    ///   <item><description>结果 <c>{output}</c>：计数 + 逐格失败详情；写完再落 <c>{output}.done</c>，内容为请求里的 <c>id</c>。</description></item>
    /// </list>
    /// <para>调用方必须自带唯一 <c>id</c> 并只认配对的 <c>.done</c>，否则会把上一轮留下的旧报告当成这次的结论。
    /// <c>timeoutSeconds</c> 为可选墙钟上限（0 或缺省不限时；编译、导入与域重载的等待计入），超时按 ABORTED 收口，
    /// Test Runner 作业本身不可取消、可能仍在后台跑完，其结果不再计入报告；<c>assemblies</c> 与 <c>tests</c>
    /// 均为空的请求会被直接拒绝收口——空过滤器会让 Test Runner 重跑上一次的选择集。</para>
    /// <para>驱动住在测试程序集（<c>UNITY_INCLUDE_TESTS</c> 门控的调试桥），不进玩家包；它是编辑器内的便利设施，
    /// 不替代发布流程里的自动化测试。</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class TestRequestRunner
    {
        #region 协议常量 [PROTOCOL CONSTANTS]

        private const string REQUEST_PATH = "Temp/MoiraiTestRequest.json";
        private const string CONSUMED_PATH = "Temp/MoiraiTestRequest.consuming.json";
        private const string STATE_PATH = "Temp/MoiraiTestRunState.json";
        private const string MARKER = "MOIRAI-TEST-RUN";

        /// <summary>
        /// 域重载后给 <c>ResumeRunningJobs</c> 留下的认领窗口；超时仍无在途作业则判孤儿单。
        /// </summary>
        private const double ORPHAN_GRACE_SECONDS = 2.0;

        #endregion

        #region 请求与运行态 [REQUEST & RUN STATE]

        [Serializable]
        private sealed class Request
        {
            public string id;
            public string mode;
            public string output;
            public string[] assemblies;
            public string[] tests;
            public int timeoutSeconds;
        }

        /// <summary>
        /// 可序列化运行态：跨域重载的唯一真相源（<see cref="STATE_PATH"/>）。
        /// </summary>
        [Serializable]
        private sealed class RunState
        {
            public string id;
            public string mode;
            public string output;
            public string[] assemblies = Array.Empty<string>();
            public string[] tests = Array.Empty<string>();
            public double deadline;
            public int pass;
            public int fail;
            public int skip;
            public float duration;
            public string[] failures = Array.Empty<string>();
        }

        private static RunState s_State;
        private static double s_RestoredAt = -1d;
        private static bool s_CallbacksRegistered;

        /// <summary>
        /// 缓存的 <c>TestRunnerApi.IsRunActive</c> 探针：孤儿单判活按帧轮询，禁止每帧反射查方法。
        /// </summary>
        private static readonly Func<bool> IsRunActiveProbe = CreateIsRunActiveProbe();

        #endregion

        #region 域生命周期 [DOMAIN LIFECYCLE]

        static TestRequestRunner()
        {
            RegisterCallbacks();
            TryDelete(CONSUMED_PATH);   // 上次会话若在「改名后、读取前」退出，会残留半消费请求
            RestoreState();
            EditorApplication.update += Poll;
            AssemblyReloadEvents.beforeAssemblyReload += PersistState;
        }

        /// <summary>
        /// 注册 ICallbacks。必须每次域加载都做：UTF 的 <c>CallbacksHolder</c> 列表不序列化，
        /// 重载后为空；派发时才查表，故续跑作业能收到本次新注册的实例。
        /// </summary>
        private static void RegisterCallbacks()
        {
            if (s_CallbacksRegistered) return;
            TestRunnerApi.RegisterTestCallback(new Callbacks(), 0);
            s_CallbacksRegistered = true;
        }

        private static void RestoreState()
        {
            if (!File.Exists(STATE_PATH)) return;

            try
            {
                s_State = UnityEngine.JsonUtility.FromJson<RunState>(File.ReadAllText(STATE_PATH));
                if (s_State != null && string.IsNullOrEmpty(s_State.output))
                {
                    s_State = null;
                    TryDelete(STATE_PATH);
                    return;
                }

                s_RestoredAt = EditorApplication.timeSinceStartup;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestRequestRunner] 恢复运行态失败，按孤儿单收口：{exception.Message}");
                s_State = null;
                TryDelete(STATE_PATH);
            }
        }

        private static void PersistState()
        {
            if (s_State == null) return;
            WriteRaw(STATE_PATH, UnityEngine.JsonUtility.ToJson(s_State));
        }

        #endregion

        #region 请求轮询 [REQUEST POLLING]

        private static void Poll()
        {
            // 在途单不接新单；编译/导入中不接；PlayMode 进出场的执行域状态不属于本轮判据
            if (s_State != null)
            {
                TryTimeoutRun();
                TryAbortOrphanedRun();
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(REQUEST_PATH))
            {
                return;
            }

            Request request = TryConsumeRequest();
            if (request == null) return;

            if (string.IsNullOrEmpty(request.output))
            {
                Debug.LogError($"[TestRequestRunner] 请求缺少 output，无处回写结果，已丢弃（id={request.id}）");
                return;
            }

            Start(request);
        }

        /// <summary>
        /// 原子消费请求：先改名再读取。若「读完再删」，删除一旦失败就会留下完整请求文件，
        /// 下一帧把同一单重复执行；改名本身就是消费标记，改名失败则原文件原封不动、下一帧重试。
        /// </summary>
        private static Request TryConsumeRequest()
        {
            if (!File.Exists(REQUEST_PATH)) return null;

            try
            {
                File.Move(REQUEST_PATH, CONSUMED_PATH);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestRequestRunner] 请求文件改名失败，留待下一帧重试：{exception.Message}");
                return null;
            }

            Request request = null;
            try
            {
                request = UnityEngine.JsonUtility.FromJson<Request>(File.ReadAllText(CONSUMED_PATH));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestRequestRunner] 请求 JSON 不合法，已丢弃：{exception.Message}");
            }

            TryDelete(CONSUMED_PATH);
            return request;
        }

        private static void Start(Request request)
        {
            s_State = new RunState
            {
                id = request.id ?? string.Empty,
                mode = request.mode ?? "EditMode",
                output = request.output,
                assemblies = request.assemblies ?? Array.Empty<string>(),
                tests = request.tests ?? Array.Empty<string>(),
                deadline = request.timeoutSeconds > 0
                    ? EditorApplication.timeSinceStartup + request.timeoutSeconds
                    : 0d,
            };
            s_RestoredAt = -1d;
            PersistState();

            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(request.output));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception exception)
            {
                Complete($"无法准备输出路径：{exception.Message}");
                return;
            }

            if (s_State.assemblies.Length == 0 && s_State.tests.Length == 0)
            {
                // 空过滤器会让 Test Runner 重跑「上一次在窗口里选择」的用例集——看似成功，实则文不对题
                Complete("assemblies 与 tests 均为空：空过滤器会重跑 Test Runner 上一次的选择集，已拒绝执行");
                return;
            }

            Write(s_State.output + ".progress", s_State.id, "STARTED");

            try
            {
                // 静态注册的回调与本实例无关；Execute 之后进 PlayMode 触发域重载，状态在磁盘、作业由 UTF 续跑
                ScriptableObject.CreateInstance<TestRunnerApi>().Execute(new ExecutionSettings(new Filter
                {
                    testMode = string.Equals(s_State.mode, "PlayMode", StringComparison.OrdinalIgnoreCase)
                        ? TestMode.PlayMode
                        : TestMode.EditMode,
                    assemblyNames = s_State.assemblies,
                    testNames = s_State.tests,
                }));
            }
            catch (Exception exception)
            {
                Write(s_State.output + ".error", exception.ToString());
                Complete($"执行失败：{exception.Message}");
            }
        }

        #endregion

        #region 收口 [COMPLETION]

        /// <summary>
        /// 墙钟超时收口：请求可带 <c>timeoutSeconds</c>（0 不限时）。编译、导入与 PlayMode 切换期间
        /// 暂缓判定（与孤儿单同组守卫），墙钟计时包含这些等待——与调试桥作业超时同语义。
        /// </summary>
        private static void TryTimeoutRun()
        {
            if (s_State == null || s_State.deadline <= 0d) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup >= s_State.deadline)
            {
                Complete("超时收口：运行超出请求的墙钟上限（timeoutSeconds）");
            }
        }

        /// <summary>
        /// 域重载后作业若已不在（编辑器崩溃、强制退出 Play），不能把调用方永远卡在「无 .done」。
        /// 宽限期内给 UTF 的 <c>ResumeRunningJobs</c> 认领；过后仍无在途作业则写 ABORTED 收口。
        /// </summary>
        private static void TryAbortOrphanedRun()
        {
            if (s_State == null || s_RestoredAt < 0d) return;
            if (EditorApplication.timeSinceStartup - s_RestoredAt < ORPHAN_GRACE_SECONDS) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (IsRunActive()) return;

            Complete("域重载后未发现仍在执行的 TestRunner 作业（孤儿单收口）");
        }

        private static bool IsRunActive()
        {
            // 反射不可用时宁可放行：让作业自己收口，而不是误杀在途 PlayMode
            return IsRunActiveProbe == null || IsRunActiveProbe();
        }

        private static Func<bool> CreateIsRunActiveProbe()
        {
            try
            {
                MethodInfo method = typeof(TestRunnerApi).GetMethod(
                    "IsRunActive", BindingFlags.NonPublic | BindingFlags.Static);
                return method == null
                    ? null
                    : (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Complete(string error)
        {
            RunState state = s_State;
            if (state == null) return;

            s_State = null;
            s_RestoredAt = -1d;
            TryDelete(STATE_PATH);

            StringBuilder report = new StringBuilder();
            report.Append("run ").Append(state.id ?? string.Empty);
            report.Append(error == null
                ? $" | passed {state.pass} | failed {state.fail} | skipped {state.skip} | {state.duration.ToString("F2", CultureInfo.InvariantCulture)}s"
                : $" | ABORTED: {error}");
            report.Append('\n');
            string[] failures = state.failures ?? Array.Empty<string>();
            for (int i = 0; i < failures.Length; i++)
            {
                report.Append('\n').Append(failures[i]).Append('\n');
            }

            Write(state.output, report.ToString());
            Write(state.output + ".done", state.id ?? string.Empty);
            TryDelete(state.output + ".progress");
        }

        #endregion

        #region 结果回调 [RESULT CALLBACKS]

        /// <summary>
        /// Test Runner 结果回调：随域加载静态重注册（见 <see cref="RegisterCallbacks"/>），
        /// <c>s_State</c> 为空（无本驱动的在途单）时全部空转，不干扰窗口里手动发起的测试。
        /// </summary>
        private sealed class Callbacks : ICallbacks, IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void TestStarted(ITestAdaptor test)
            {
                if (s_State != null)
                {
                    Write(s_State.output + ".progress", s_State.id, test.FullName);
                }
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                // 夹具/套件节点也会回调，只统计叶子用例，否则计数翻倍
                if (s_State == null || result == null || result.HasChildren) return;

                string state = result.ResultState ?? string.Empty;
                // 失败态不保证是官方 "Failed"（Error/Cancelled 等变体皆有）——按通过/跳过白名单归类，其余一律计失败
                if (state.StartsWith("Passed", StringComparison.Ordinal))
                {
                    s_State.pass++;
                }
                else if (state.StartsWith("Skipped", StringComparison.Ordinal) ||
                         state.StartsWith("Inconclusive", StringComparison.Ordinal) ||
                         state.StartsWith("Cancel", StringComparison.Ordinal))
                {
                    s_State.skip++;
                }
                else
                {
                    s_State.fail++;
                    Array.Resize(ref s_State.failures, s_State.failures.Length + 1);
                    s_State.failures[s_State.failures.Length - 1] =
                        $"{result.FullName} [{state}]\n{Indent(FirstLines(result.Message, 6))}\n{Indent(FirstLines(result.StackTrace, 8))}";
                }

                // 每格落盘：PlayMode 进出场再各来一次域重载，计数与失败详情不能只活在内存里
                PersistState();
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                if (s_State != null && result != null)
                {
                    s_State.duration = (float)result.Duration;
                    PersistState();
                }

                Complete(null);
            }

            public void OnError(string message)
            {
                Complete($"TestRunner 报错：{message}");
            }
        }

        #endregion

        #region 报告 [REPORTING]

        private static string FirstLines(string text, int count)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int take = Math.Min(count, lines.Length);
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < take; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(lines[i]);
            }

            if (lines.Length > take)
            {
                builder.Append($"\n    … 另有 {lines.Length - take} 行");
            }

            return builder.ToString();
        }

        private static string Indent(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "    (空)";
            }

            return "    " + text.Replace("\n", "\n    ");
        }

        #endregion

        #region 文件工具 [FILE UTILITIES]

        private static void Write(string path, string id, string content)
        {
            Write(path, id.Length == 0 ? content : $"{MARKER} {id} {content}");
        }

        private static void Write(string path, string content) => WriteRaw(path, content);

        private static void WriteRaw(string path, string content)
        {
            try
            {
                File.WriteAllText(Path.GetFullPath(path), content);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestRequestRunner] 写 {path} 失败：{exception.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TestRequestRunner] 删 {path} 失败：{exception.Message}");
            }
        }

        #endregion
    }
}
