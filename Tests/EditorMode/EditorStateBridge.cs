using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEditorInternal;
using UnityEngine;

namespace Moirai.Atropos.Tests.EditorMode
{
    /// <summary>
    /// 编辑器状态桥：把「编辑器此刻在干什么」持续落盘，并接收 <c>Temp/</c> 下的动作请求。
    /// <para>跑回归的前置判据（编辑器是否活着、是否在编译、域重载过没有、程序集新不新）此前只能靠调用方
    /// 自己 <c>stat</c> 若干 dll 再猜，而关掉 Auto Refresh 的工程里连「让它重编」都要人手按 <c>Ctrl+R</c>。
    /// 这里在测试程序集里挂一个 <c>EditorApplication.update</c> 轮询：状态每 ~1s 覆写
    /// <c>Temp/MoiraiEditorState.json</c>，动作请求从 <c>Temp/MoiraiEditorCommand.json</c> 进、结果从
    /// <c>Temp/MoiraiEditorCommand.result.json</c> 出。调用方只读文件、只发命令，不再需要人。</para>
    /// <para><b>心跳即判活</b>：静态构造每次域加载都跑一遍，所以 <c>domainSeq</c> 递增就是「新域已经起来」的硬证据
    /// （<c>SessionState</c> 跨域重载保留、随编辑器退出清空，配合 <c>pid</c> 可区分重载与重启）。
    /// 心跳停止推进只说明主线程没在跑 <c>update</c>：导入中、域重载中、被原生模态框挡住，
    /// 或者 Interaction Mode 不是 No Throttling（那时它会走得慢而不是不动）——具体原因去 <c>Editor.log</c> 取。</para>
    /// <para><b>编译失败不在报告范围内</b>：编译报错时 Unity 保留旧域继续跑，桥照常心跳、<c>isCompiling</c> 归 false，
    /// 判据是 <c>assemblies[].unix</c> 没越过自己的改动时刻、<c>consoleErrors</c> 涨了。
    /// 本桥不代存场景：<c>dirtyScenes</c> 大于 0 时刷新/重编译可能撞上原生「保存场景？」对话框，
    /// 那会把主线程连同心跳一起停在那里。</para>
    /// <para>桥住在测试程序集（<c>UNITY_INCLUDE_TESTS</c> 门控的调试桥，与 <see cref="TestRequestRunner"/> 同处），
    /// 关掉 Test Tools 包就没有心跳；它是编辑器内的便利设施，不参与发布。</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorStateBridge
    {
        #region 协议常量 [PROTOCOL CONSTANTS]

        private const string STATE_PATH = "Temp/MoiraiEditorState.json";
        private const string COMMAND_PATH = "Temp/MoiraiEditorCommand.json";
        private const string COMMAND_TAKEN_PATH = "Temp/MoiraiEditorCommand.taken.json";
        private const string RESULT_PATH = "Temp/MoiraiEditorCommand.result.json";
        private const string RESULT_DONE_PATH = RESULT_PATH + ".done";
        private const string TEST_REQUEST_PATH = "Temp/MoiraiTestRequest.json";
        private const string ASSEMBLY_DIR = "Library/ScriptAssemblies/";
        private const string MARKER = "MOIRAI-EDITOR-STATE";
        private const int SCHEMA = 1;

        /// <summary>采样节流：每帧取状态会把 dll 的 mtime 查询放大成每帧六次文件系统访问。</summary>
        private const double SAMPLE_SECONDS = 0.25;

        /// <summary>心跳上限：状态没变化时也至少隔这么久覆写一次，调用方据此区分「编辑器空闲」与「桥停了」。</summary>
        private const double HEARTBEAT_SECONDS = 1.0;

        /// <summary>域重载计数器在 <c>SessionState</c> 里的键：跨域重载保留，随编辑器退出清空。</summary>
        private const string DOMAIN_SEQ_KEY = "Moirai.EditorStateBridge.DomainSeq";

        /// <summary>调用方按「源树 → 归属程序集」比新鲜度，所以逐份给出，而不是只给一个最新值。</summary>
        private static readonly string[] TrackedAssemblies =
        {
            "Moirai.Atropos",
            "Moirai.Atropos.Editor",
            "Moirai.Atropos.Tests.EditorMode",
            "Moirai.Atropos.Tests.PlayMode",
        };

        #endregion

        #region 状态载荷 [STATE PAYLOAD]

        [Serializable]
        private sealed class EditorState
        {
            public string marker;
            public int schema;
            public long pid;

            /// <summary>域重载序号：本程序集每被重新加载一次 +1，判「跑的是不是新域」的硬依据。</summary>
            public int domainSeq;

            /// <summary>本次心跳的 UTC 秒（与 <c>stat -c %Y</c> 同量纲，调用方直接相减判陈旧）。</summary>
            public long unix;
            public string utc;
            public double uptime;
            public string unityVersion;
            public string projectPath;
            public bool isCompiling;
            public bool isUpdating;
            public bool isPlaying;
            public bool isPaused;
            public bool isChangingPlayMode;
            public bool isFocused;

            /// <summary>编辑器此刻是不是系统前台窗口（<c>isFocused</c> 只答「键盘焦点在不在编辑器内」）。</summary>
            public bool isActive;
            public string activeScenePath;

            /// <summary>未保存的场景数：大于 0 时刷新/重编译可能触发原生保存对话框并把主线程挡在那里。</summary>
            public int dirtyScenes;

            /// <summary>
            /// Console 当前的错误条数（-1 为取不到）。实测一轮重编译会把它清归零，所以刷新之后读到的
            /// 大致就是这一轮的输出；跨轮仍按增量用，别把它当成"本次编译的失败数"。
            /// </summary>
            public int consoleErrors;
            public int consoleWarnings;
            public bool testRequestPending;

            /// <summary>
            /// Test Runner 有无在跑的 run：-1 探针不可用、0 空闲、1 在跑。
            /// 不用 <c>Temp/MoiraiTestRunState.json</c> 在不在判——实测有一单超时收口（报告与 <c>.done</c> 都已落盘、
            /// 状态文件也已删）之后，旧域拆走时又把运行态写回了磁盘，残留文件会把空闲报成在跑。
            /// </summary>
            public int testRunActive;
            public AssemblyStamp[] assemblies;
        }

        [Serializable]
        private sealed class AssemblyStamp
        {
            public string name;

            /// <summary>已编译产物 <c>Library/ScriptAssemblies/&lt;name&gt;.dll</c> 的 UTC 秒；文件不存在为 0。</summary>
            public long unix;
        }

        /// <summary>命令载荷：<c>{"id":"<唯一串>","action":"focus|refresh|recompile"}</c>。</summary>
        [Serializable]
        private sealed class EditorCommand
        {
            public string id;
            public string action;
        }

        [Serializable]
        private sealed class CommandResult
        {
            public string marker;
            public string id;
            public string action;

            /// <summary>命令是否被执行。执行之后的真实效果看状态文件，不在这个回执里。</summary>
            public bool ok;
            public string message;
            public int domainSeq;
            public bool isCompiling;
            public bool isUpdating;
        }

        #endregion

        #region 桥内状态 [BRIDGE STATE]

        private static readonly long s_Pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        private static readonly int s_DomainSeq = NextDomainSeq();
        private static string s_LastSignature;
        private static double s_LastSampleAt = -1d;
        private static double s_LastWriteAt = -1d;

        #endregion

        #region 域生命周期 [DOMAIN LIFECYCLE]

        static EditorStateBridge()
        {
            EditorApplication.update += Tick;
            TryDelete(COMMAND_TAKEN_PATH);   // 上次会话若在「改名后、读取前」退出，会残留半消费命令
        }

        private static int NextDomainSeq()
        {
            int seq = SessionState.GetInt(DOMAIN_SEQ_KEY, 0) + 1;
            SessionState.SetInt(DOMAIN_SEQ_KEY, seq);
            return seq;
        }

        private static void Tick()
        {
            TryConsumeCommand();

            double now = EditorApplication.timeSinceStartup;
            if (now - s_LastSampleAt < SAMPLE_SECONDS) return;
            s_LastSampleAt = now;

            EditorState state = Capture();
            string signature = Signature(state);
            if (signature != s_LastSignature || now - s_LastWriteAt >= HEARTBEAT_SECONDS)
            {
                s_LastSignature = signature;
                s_LastWriteAt = now;
                WriteRaw(STATE_PATH, UnityEngine.JsonUtility.ToJson(state));
            }
        }

        #endregion

        #region 状态采集 [STATE SAMPLING]

        private static EditorState Capture()
        {
            int[] counts = ReadConsoleCounts();
            return new EditorState
            {
                marker = MARKER,
                schema = SCHEMA,
                pid = s_Pid,
                domainSeq = s_DomainSeq,
                unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                uptime = EditorApplication.timeSinceStartup,
                unityVersion = Application.unityVersion,
                projectPath = Directory.GetCurrentDirectory(),
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isChangingPlayMode = EditorApplication.isPlayingOrWillChangePlaymode,
                isFocused = EditorApplication.isFocused,
                isActive = InternalEditorUtility.isApplicationActive,
                activeScenePath = EditorSceneManager.GetActiveScene().path,
                dirtyScenes = CountDirtyScenes(),
                consoleErrors = counts == null ? -1 : counts[0],
                consoleWarnings = counts == null ? -1 : counts[1],
                testRequestPending = File.Exists(TEST_REQUEST_PATH),
                testRunActive = ProbeTestRunActive(),
                assemblies = SampleAssemblies(),
            };
        }

        private static int CountDirtyScenes()
        {
            int dirty = 0;
            int count = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isDirty) dirty++;
            }

            return dirty;
        }

        private static AssemblyStamp[] SampleAssemblies()
        {
            AssemblyStamp[] stamps = new AssemblyStamp[TrackedAssemblies.Length];
            for (int i = 0; i < stamps.Length; i++)
            {
                string path = ASSEMBLY_DIR + TrackedAssemblies[i] + ".dll";
                long unix = 0;
                try
                {
                    if (File.Exists(path))
                    {
                        unix = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
                    }
                }
                catch (IOException)
                {
                    // 编译中途 dll 正被替换：留 0，下一拍再取
                }

                stamps[i] = new AssemblyStamp { name = TrackedAssemblies[i], unix = unix };
            }

            return stamps;
        }

        /// <summary>
        /// 变化判据用签名，不含 <c>unix</c>/<c>utc</c>/<c>uptime</c> 这些每拍必动的字段——
        /// 否则「无变化」永远不成立，心跳会退化成每采样写一次盘。
        /// </summary>
        private static string Signature(EditorState state)
        {
            int flags = 0;
            if (state.isCompiling) flags |= 1 << 0;
            if (state.isUpdating) flags |= 1 << 1;
            if (state.isPlaying) flags |= 1 << 2;
            if (state.isPaused) flags |= 1 << 3;
            if (state.isChangingPlayMode) flags |= 1 << 4;
            if (state.isFocused) flags |= 1 << 5;
            if (state.isActive) flags |= 1 << 6;
            if (state.testRequestPending) flags |= 1 << 7;
            if (state.testRunActive != 0) flags |= 1 << 8;

            string assemblies = string.Empty;
            AssemblyStamp[] stamps = state.assemblies;
            for (int i = 0; i < stamps.Length; i++)
            {
                assemblies += stamps[i].unix.ToString(CultureInfo.InvariantCulture) + ";";
            }

            return $"{state.domainSeq}|{flags}|{state.activeScenePath}|{state.dirtyScenes}|" +
                   $"{state.consoleErrors}|{state.consoleWarnings}|{assemblies}";
        }

        #endregion

        #region 命令轮询与派发 [COMMAND POLLING & DISPATCH]

        /// <summary>
        /// 原子消费命令：先改名再读取（改名即消费）。改名失败就原封不动、下一帧重试——
        /// 「读完再删」一旦删除失败会把同一条命令重复执行。
        /// </summary>
        private static void TryConsumeCommand()
        {
            if (!File.Exists(COMMAND_PATH)) return;

            try
            {
                File.Move(COMMAND_PATH, COMMAND_TAKEN_PATH);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[EditorStateBridge] 命令文件改名失败，留待下一帧重试：{exception.Message}");
                return;
            }

            EditorCommand command = null;
            try
            {
                command = UnityEngine.JsonUtility.FromJson<EditorCommand>(File.ReadAllText(COMMAND_TAKEN_PATH));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[EditorStateBridge] 命令 JSON 不合法，已丢弃：{exception.Message}");
            }

            TryDelete(COMMAND_TAKEN_PATH);
            if (command == null) return;

            TryDelete(RESULT_DONE_PATH);
            Dispatch(command);
        }

        private static void Dispatch(EditorCommand command)
        {
            string action = (command.action ?? string.Empty).Trim().ToLowerInvariant();
            switch (action)
            {
                case "focus":
                    // 不受忙碌门限制：越是在编译导入，越可能正需要人来看一眼
                    Reply(command, action, true, BringToFront());
                    break;

                case "refresh":
                {
                    if (Busy(out string reason))
                    {
                        Reply(command, action, false, reason);
                        break;
                    }

                    // Refresh 自己会起编译（磁盘上有新增/改动的 .cs 时）。此时绝不再叠一次
                    // RequestScriptCompilation——那会重入编译管线、和 Bee 抢同一份在途构建。
                    AssetDatabase.Refresh();
                    Reply(command, action, true, EditorApplication.isCompiling
                        ? "已执行 AssetDatabase.Refresh()（等价 Ctrl+R），编译已随之开始，等状态文件 isCompiling 归 false"
                        : "已执行 AssetDatabase.Refresh()（等价 Ctrl+R），未检测到需要编译的改动；要强制重编发 recompile");
                    break;
                }

                case "recompile":
                {
                    if (Busy(out string recompileReason))
                    {
                        Reply(command, action, false, recompileReason);
                        break;
                    }

                    if (EditorApplication.isPlaying || EditorApplication.isPaused)
                    {
                        Reply(command, action, false, "正在 Play/Paused，未执行：播放期请求重编译会赶在域重载路上，先 stop");
                        break;
                    }

                    CompilationPipeline.RequestScriptCompilation();
                    Reply(command, action, true,
                        "已请求强制重编译（CompilationPipeline.RequestScriptCompilation）。编译期间本桥停摆、" +
                        "状态文件会变陈旧，新域起来后 domainSeq 会 +1");
                    break;
                }

                default:
                    Reply(command, action, false, $"未知 action：{command.action}（可用：focus / refresh / recompile）");
                    break;
            }
        }

        /// <summary>
        /// 刷新与重编译只在编辑器空闲时受理：正在编译或正在导入时再叠一次导入，只会把状态搅成糊。
        /// 被拒的命令已经消费掉了，调用方按回执里的原因稍后重投。
        /// </summary>
        private static bool Busy(out string reason)
        {
            if (EditorApplication.isCompiling)
            {
                reason = "正在编译脚本，未执行：等状态文件 isCompiling 归 false 后重投";
                return true;
            }

            if (EditorApplication.isUpdating)
            {
                reason = "正在导入资产，未执行：等状态文件 isUpdating 归 false 后重投";
                return true;
            }

            reason = null;
            return false;
        }

        private static void Reply(EditorCommand command, string action, bool ok, string message)
        {
            CommandResult result = new CommandResult
            {
                marker = MARKER,
                id = command.id ?? string.Empty,
                action = action,
                ok = ok,
                message = message,
                domainSeq = s_DomainSeq,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
            };

            // 回执要能被调用方当成「这一单的结论」整体读走：先落内容再落 .done，半截 JSON 不会被误判成完成
            WriteAtomic(RESULT_PATH, UnityEngine.JsonUtility.ToJson(result));
            WriteRaw(RESULT_DONE_PATH, command.id ?? string.Empty);
        }

        #endregion

        #region 前台激活 [FOREGROUND]

#if UNITY_EDITOR_WIN
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const int SW_RESTORE = 9;

        /// <summary>
        /// 把编辑器顶到系统前台。Windows 的前台锁只认「当前前台那一伙线程」，
        /// 所以要把自己的输入线程临时挂到前台线程上，<c>SetForegroundWindow</c> 才不是白闪一下任务栏。
        /// </summary>
        private static string BringToFront()
        {
            try
            {
                IntPtr window = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (window == IntPtr.Zero) return "取不到编辑器主窗口句柄";

                if (IsIconic(window)) ShowWindow(window, SW_RESTORE);

                IntPtr foreground = GetForegroundWindow();
                uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
                uint thisThread = GetCurrentThreadId();

                bool attached = foregroundThread != 0 && foregroundThread != thisThread
                    && AttachThreadInput(foregroundThread, thisThread, true);
                bool taken = SetForegroundWindow(window);
                if (attached) AttachThreadInput(foregroundThread, thisThread, false);

                return taken ? "已请求切到前台，结果看状态文件 isActive" : "SetForegroundWindow 未受理，结果看状态文件 isActive";
            }
            catch (Exception exception)
            {
                return $"前台激活调用异常：{exception.Message}";
            }
        }
#else
        private static string BringToFront()
        {
            return "前台激活目前只实现了 Windows 编辑器，结果看状态文件 isActive";
        }
#endif

        #endregion

        #region 反射探针 [REFLECTION PROBES]

        /// <summary>
        /// <c>LogEntries.GetCountsByType</c> 反射探针：那是 <c>internal static</c>、不在文档 API 面上，
        /// 成员缺失或签名变动时整块退回 <c>null</c>（状态里落 -1），绝不把「读不到」说成「没有错」。
        /// 类型取 <c>typeof(EditorApplication).Assembly</c> 而不是按程序集名拼字符串——
        /// LogEntries 与 EditorApplication 同模块，而模块名在各版本间挪过。
        /// </summary>
        private static readonly Func<int[]> ConsoleCountsProbe = CreateConsoleProbe();

        private static Func<int[]> CreateConsoleProbe()
        {
            try
            {
                Type logEntries = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                MethodInfo method = logEntries == null
                    ? null
                    : logEntries.GetMethod("GetCountsByType",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) return null;

                return () =>
                {
                    object[] args = { 0, 0, 0 };
                    method.Invoke(null, args);
                    return new[] { (int)args[0], (int)args[1] };
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int[] ReadConsoleCounts()
        {
            if (ConsoleCountsProbe == null) return null;

            try
            {
                return ConsoleCountsProbe();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// <c>TestRunnerApi.IsRunActive()</c> 反射探针（<c>internal static</c>，与测试桥的接单门同源）：
        /// 编辑器里有任意 run 在跑即真，含 Test Runner 窗口里手动发起的。
        /// </summary>
        private static readonly Func<bool> RunActiveProbe = CreateRunActiveProbe();

        private static Func<bool> CreateRunActiveProbe()
        {
            try
            {
                MethodInfo method = typeof(TestRunnerApi).GetMethod("IsRunActive",
                    BindingFlags.NonPublic | BindingFlags.Static);
                return method == null ? null : (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int ProbeTestRunActive()
        {
            if (RunActiveProbe == null) return -1;

            try
            {
                return RunActiveProbe() ? 1 : 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        #endregion

        #region 文件工具 [FILE UTILITIES]

        private static void WriteRaw(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[EditorStateBridge] 写 {path} 失败：{exception.Message}");
            }
        }

        private static void WriteAtomic(string path, string content)
        {
            string temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, content);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[EditorStateBridge] 写 {path} 失败：{exception.Message}");
                TryDelete(temp);
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
                Debug.LogWarning($"[EditorStateBridge] 删 {path} 失败：{exception.Message}");
            }
        }

        #endregion
    }
}
