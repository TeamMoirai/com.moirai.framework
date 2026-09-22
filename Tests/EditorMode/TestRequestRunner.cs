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
    /// 工程 <c>Temp/</c> 下出现请求文件就按里面的过滤器执行一轮，把结果与逐格进度回写到指定路径。
    /// 作业模型对齐调试桥：每单持有 TestRunner 作业 guid，判活/取消/超时都按 guid 精确到本单，
    /// 而不是「编辑器里有任意 run 在跑」的粗粒度猜测。</para>
    /// <para><b>跨域重载</b>：PlayMode 进出场各触发一次域重载，静态字段会归零。本驱动把「请求 + 计数 + 失败详情 +
    /// 作业 guid」落盘到 <c>Temp/MoiraiTestRunState.json</c>，每次域加载时恢复并重新注册回调，并在
    /// <c>AssemblyReloadEvents.beforeAssemblyReload</c> 兜底落盘一次；Test Framework 侧
    /// <c>TestJobDataHolder.ResumeRunningJobs</c> 会续跑作业，派发时按注册表找回调，因此 PlayMode 全程可收齐结果。</para>
    /// <para>协议（都是 <c>Temp/</c> 下的普通文件，调用方只轮询、不双向通信）：</para>
    /// <list type="bullet">
    ///   <item><description>请求 <c>Temp/MoiraiTestRequest.json</c>：<c>{id, mode(EditMode|PlayMode), output, assemblies[], tests[], timeoutSeconds}</c>。读到即删除，避免重复执行。</description></item>
    ///   <item><description>取消 <c>Temp/MoiraiTestRequest.cancel.json</c>：内容为要取消的请求 <c>id</c>（裸文本或 <c>{"id":"..."}</c> 均可）。
    ///   匹配在途单即删除文件并经 <c>TestRunnerApi.CancelTestRun</c> 取消作业；UTF 取消后不再送达 RunFinished，
    ///   受理即由驱动收口（ABORTED 格式，附已收集计数）；拒绝受理才等 RunFinished 自然收口。</description></item>
    ///   <item><description>进度 <c>{output}.progress</c>：先写 <c>STARTED</c>，随后是正在跑的用例全名；收口时删除。</description></item>
    ///   <item><description>结果 <c>{output}</c>：计数 + 逐格失败详情；写完再落 <c>{output}.done</c>，内容为请求里的 <c>id</c>。</description></item>
    /// </list>
    /// <para>调用方必须自带唯一 <c>id</c> 并只认配对的 <c>.done</c>，否则会把上一轮留下的旧报告当成这次的结论。
    /// <c>timeoutSeconds</c> 为可选墙钟上限（0 或缺省不限时；编译、导入与域重载的等待计入），超时按 ABORTED 收口
    /// 并尽力取消 Test Runner 作业（作业可能已在收尾，取消未必受理）；ABORTED 报告附带已收集到的
    /// <c>collected passed/failed/skipped</c> 与墙钟时长，已跑完的格子不白跑。<c>assemblies</c> 与 <c>tests</c>
    /// 均为空的请求会被直接拒绝收口——空过滤器会让 Test Runner 重跑上一次的选择集。</para>
    /// <para><b>接单门</b>：正在编译、正在导入、正在切换 PlayMode、或编辑器里有任意 run 在跑（含窗口手动发起）时
    /// 不接新单——ICallbacks 无法归因到具体 run，与本驱动并发会把结果串进同一份账。</para>
    /// <para><b>孤儿单</b>：域重载后先给 UTF 认领窗口，随后按作业 guid 判活（探针缺失时降级为「任意 run 在跑」，
    /// 判活完全不可用则再给扩展宽限），仍无在途作业即 ABORTED 强制收口——调用方永不会等不到 <c>.done</c>。</para>
    /// <para>驱动住在测试程序集（<c>UNITY_INCLUDE_TESTS</c> 门控的调试桥），不进玩家包；它是编辑器内的便利设施，
    /// 不替代发布流程里的自动化测试。</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class TestRequestRunner
    {
        #region 协议常量 [PROTOCOL CONSTANTS]

        private const string REQUEST_PATH = "Temp/MoiraiTestRequest.json";
        private const string CONSUMED_PATH = "Temp/MoiraiTestRequest.consuming.json";
        private const string CANCEL_PATH = "Temp/MoiraiTestRequest.cancel.json";
        private const string STATE_PATH = "Temp/MoiraiTestRunState.json";
        private const string MARKER = "MOIRAI-TEST-RUN";

        /// <summary>
        /// 域重载后给 <c>ResumeRunningJobs</c> 留下的认领窗口；超时仍无在途作业则判孤儿单。
        /// </summary>
        private const double ORPHAN_GRACE_SECONDS = 2.0;

        /// <summary>
        /// 判活探针完全不可用时的扩展宽限：宁可多等也不误杀，但宽限后强制收口，绝不把调用方永远挂在「无 .done」上。
        /// </summary>
        private const double UNVERIFIED_ORPHAN_GRACE_SECONDS = 30.0;

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
        /// 取消文件载荷：<c>{"id":"..."}</c>；裸 id 文本走 <see cref="ExtractCancelId"/> 的回退分支。
        /// </summary>
        [Serializable]
        private sealed class CancelRequest
        {
            public string id;
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
            public string guid;
            public string[] assemblies = Array.Empty<string>();
            public string[] tests = Array.Empty<string>();
            public double deadline;
            public double startedAt;
            public int pass;
            public int fail;
            public int skip;
            public float duration;
            public string[] failures = Array.Empty<string>();
        }

        private static RunState s_State;
        private static double s_RestoredAt = -1d;
        private static bool s_CallbacksRegistered;

        #endregion

        #region 判活探针 [LIVENESS PROBES]

        /// <summary>
        /// <c>TestRunnerApi.IsRunning(guid)</c> 反射探针：只问我们这一单是否仍在跑——窗口手动跑不干扰孤儿单判定。
        /// </summary>
        private static readonly Func<string, bool> IsRunningProbe =
            CreateProbe<Func<string, bool>>("IsRunning", typeof(string));

        /// <summary>
        /// <c>TestRunnerApi.IsRunActive()</c> 反射探针：任意 run 在跑即真；仅作 <see cref="IsRunningProbe"/> 不可用时的降级。
        /// </summary>
        private static readonly Func<bool> IsRunActiveProbe = CreateProbe<Func<bool>>("IsRunActive", null);

        /// <summary>
        /// 反射建探针委托，类型加载时执行一次；成员缺失或签名不符返回 null，由调用侧降级，绝不让静态构造失败。
        /// </summary>
        private static TProbe CreateProbe<TProbe>(string methodName, Type parameterType) where TProbe : class
        {
            try
            {
                MethodInfo method = parameterType == null
                    ? typeof(TestRunnerApi).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
                    : typeof(TestRunnerApi).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static,
                        null, new[] { parameterType }, null);
                return method == null ? null : (TProbe)(object)Delegate.CreateDelegate(typeof(TProbe), method);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 三态判活：Running/Idle 有结论，Unknown 表示探针缺失或抛错，由孤儿单策略决定等多久。
        /// </summary>
        private enum ELiveness
        {
            Running,
            Idle,
            Unknown,
        }

        /// <summary>
        /// 探针抛错视同 Unknown（保守等待），guid 为空的旧态运行文件直接降级到「任意 run 在跑」语义。
        /// </summary>
        private static ELiveness ProbeRunLiveness()
        {
            if (s_State != null && !string.IsNullOrEmpty(s_State.guid) && IsRunningProbe != null)
            {
                try
                {
                    return IsRunningProbe(s_State.guid) ? ELiveness.Running : ELiveness.Idle;
                }
                catch (Exception)
                {
                    return ELiveness.Unknown;
                }
            }

            if (IsRunActiveProbe != null)
            {
                try
                {
                    return IsRunActiveProbe() ? ELiveness.Running : ELiveness.Idle;
                }
                catch (Exception)
                {
                    return ELiveness.Unknown;
                }
            }

            return ELiveness.Unknown;
        }

        /// <summary>
        /// 接单门用：编辑器里有任意 run 在跑即 true（含窗口手动发起）；探针不可用时放行，不因基础设施故障卡死接单。
        /// </summary>
        private static bool IsForeignRunActive()
        {
            if (IsRunActiveProbe == null) return false;

            try
            {
                return IsRunActiveProbe();
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 域生命周期 [DOMAIN LIFECYCLE]

        static TestRequestRunner()
        {
            RegisterCallbacks();
            TryDelete(CONSUMED_PATH);   // 上次会话若在「改名后、读取前」退出，会残留半消费请求
            // 取消文件不清理：Poll 在编译/域重载期间停摆，此时投的取消会在重载后才被消费；
            // 请求 id 唯一，陈旧取消永不误匹配新单，残留无害
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
            // 在途单不接新单：驱动取消/超时/孤儿单判定；PlayMode 进出场的执行域状态不属于本轮判据
            if (s_State != null)
            {
                TryCancelRun();
                TryTimeoutRun();
                TryAbortOrphanedRun();
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || IsForeignRunActive() || !File.Exists(REQUEST_PATH))
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

        #endregion

        #region 执行派发 [RUN DISPATCH]

        private static void Start(Request request)
        {
            s_State = new RunState
            {
                id = request.id ?? string.Empty,
                mode = request.mode ?? "EditMode",
                output = request.output,
                guid = string.Empty,
                assemblies = request.assemblies ?? Array.Empty<string>(),
                tests = request.tests ?? Array.Empty<string>(),
                deadline = request.timeoutSeconds > 0
                    ? EditorApplication.timeSinceStartup + request.timeoutSeconds
                    : 0d,
                startedAt = EditorApplication.timeSinceStartup,
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

            // 上一轮同输出路径的产物必须清掉：陈旧 .done 会被只认配对 id 的调用方误读，.error 会张冠李戴
            TryDelete(s_State.output + ".done");
            TryDelete(s_State.output + ".error");
            Write(s_State.output + ".progress", s_State.id, "STARTED");

            string runGuid;
            try
            {
                // 静态注册的回调与本实例无关；Execute 之后进 PlayMode 触发域重载，状态在磁盘、作业由 UTF 续跑
                runGuid = ScriptableObject.CreateInstance<TestRunnerApi>().Execute(new ExecutionSettings(new Filter
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
                return;
            }

            if (string.IsNullOrEmpty(runGuid))
            {
                Complete("Execute 未返回作业标识，运行未启动");
                return;
            }

            s_State.guid = runGuid;
            PersistState();
        }

        #endregion

        #region 收口 [COMPLETION]

        /// <summary>
        /// 墙钟超时收口：请求可带 <c>timeoutSeconds</c>（0 不限时）。编译、导入与 PlayMode 切换期间
        /// 暂缓判定（与孤儿单同组守卫），墙钟计时包含这些等待——与调试桥作业超时同语义。
        /// 收口同时尽力取消 Test Runner 作业（此前作业会继续在后台跑完白烧 CPU）。
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
                TryCancelActiveJob();
                Complete("超时收口：运行超出请求的墙钟上限（timeoutSeconds）");
            }
        }

        /// <summary>
        /// 域重载后作业若已不在（编辑器崩溃、强制退出 Play、续跑失败），不能把调用方永远卡在「无 .done」。
        /// 宽限期内给 UTF 的 <c>ResumeRunningJobs</c> 认领；判活按作业 guid 精确到本单，探针缺失或抛错
        /// 逐级降级，扩展宽限后仍无法证实在跑即 ABORTED 强制收口。
        /// </summary>
        private static void TryAbortOrphanedRun()
        {
            if (s_State == null || s_RestoredAt < 0d) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - s_RestoredAt < ORPHAN_GRACE_SECONDS) return;

            switch (ProbeRunLiveness())
            {
                case ELiveness.Running:
                    return;
                case ELiveness.Idle:
                    break;
                case ELiveness.Unknown:
                    if (EditorApplication.timeSinceStartup - s_RestoredAt < UNVERIFIED_ORPHAN_GRACE_SECONDS) return;
                    break;
            }

            Complete("域重载后未发现仍在执行的 TestRunner 作业（孤儿单收口）");
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
            if (error == null)
            {
                report.Append($" | passed {state.pass} | failed {state.fail} | skipped {state.skip} | {FormatSeconds(state.duration)}s");
            }
            else
            {
                // 超时/孤儿单不丢账：已收集的计数与墙钟时长随 ABORTED 报告交付，已跑完的格子不白跑
                double wall = state.startedAt > 0d
                    ? Math.Max(0d, EditorApplication.timeSinceStartup - state.startedAt)
                    : 0d;
                report.Append($" | ABORTED: {error}");
                report.Append($" | collected passed {state.pass} | failed {state.fail} | skipped {state.skip} | wall {FormatSeconds(wall)}s");
            }
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

        #region 取消 [CANCELLATION]

        /// <summary>
        /// 取消通道：取消文件内容为请求 id（裸文本或 <c>{"id":"..."}</c>）。匹配在途单即删除文件并经
        /// <c>CancelTestRun</c> 取消作业。UTF 取消受理后会清空任务管线、<b>不再送达 RunFinished</b>
        /// （RunFinishedInvocationEvent 被 Canceled 模式跳过），故受理即由本驱动收口，已收集计数随取消报告交付；
        /// 拒绝受理（作业已在收尾/已取消中/找不到 runner）则继续等 RunFinished 自然收口。不匹配的取消请求
        /// 直接清掉（它指向的单已不存在）。收口后的在途单为 null，后续回调自然空转。
        /// </summary>
        private static void TryCancelRun()
        {
            if (s_State == null || !File.Exists(CANCEL_PATH)) return;

            string payload;
            try
            {
                payload = File.ReadAllText(CANCEL_PATH);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TestRequestRunner] 读取取消文件失败，留待下一帧重试：{exception.Message}");
                return;
            }

            TryDelete(CANCEL_PATH);

            string requestedId = ExtractCancelId(payload);
            if (!string.Equals(requestedId, s_State.id, StringComparison.Ordinal)) return;

            if (string.IsNullOrEmpty(s_State.guid))
            {
                Complete("收到取消请求，但作业标识缺失，无法定向取消");
                return;
            }

            if (!TryCancelActiveJob())
            {
                // CancelTestRun 拒绝受理（作业已在收尾/已取消中/找不到 runner）——等 RunFinished 自然收口即可
                Debug.Log($"[TestRequestRunner] 取消请求未受理（作业可能已在收尾），等待自然收口：{s_State.id}");
                return;
            }

            Complete("取消收口：调用方经取消文件请求取消，作业已取消");
        }

        /// <summary>
        /// 尽力取消在途作业：受理返回 true；作业不在注册表/已在收尾时 CancelTestRun 返回 false，不视为失败。
        /// </summary>
        private static bool TryCancelActiveJob()
        {
            if (s_State == null || string.IsNullOrEmpty(s_State.guid)) return false;

            try
            {
                return TestRunnerApi.CancelTestRun(s_State.guid);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TestRequestRunner] 取消作业失败：{exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解析取消 id：JSON 载荷取 <c>id</c> 字段，其余按裸 id 文本处理（容忍引号与 BOM）。
        /// </summary>
        private static string ExtractCancelId(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return null;

            string trimmed = payload.Trim().TrimStart('\uFEFF');
            if (trimmed.Length == 0) return null;

            if (trimmed[0] == '{')
            {
                try
                {
                    CancelRequest request = UnityEngine.JsonUtility.FromJson<CancelRequest>(trimmed);
                    if (request != null && !string.IsNullOrEmpty(request.id))
                    {
                        return request.id;
                    }
                }
                catch (Exception)
                {
                    // 解析失败按裸文本回退
                }
            }

            return trimmed.Trim('"');
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
                // 套件/程序集节点不落进度，只记用例级开始——进度文件语义是「正在跑的用例全名」
                if (s_State != null && test != null && !test.IsSuite)
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

        private static string FormatSeconds(double seconds)
        {
            return seconds.ToString("F2", CultureInfo.InvariantCulture);
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
