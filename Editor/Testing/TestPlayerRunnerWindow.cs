using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Moirai.Atropos.Editor.Testing
{
    /// <summary>
    /// Test Player Runner：把「在 Player 里跑测试」的常用启动参数收进一个窗口，一键发起。
    /// <para>背景（2026-09-28 L3 实证）：Player 测试的收录以<b>编辑器可见性</b>为前提、结果经 PlayerConnection
    /// 回传编辑器而非玩家落盘、构建期由 UTF 注入引导场景——GUI 与 CLI 走同一 <c>PlayerLauncher</c> 机制。
    /// 本窗口即 GUI 通道的产品化：目标平台、测试过滤、心跳超时、报告输出路径收拢一处，附 CLI 等价命令
    /// 便于复制进 CI 或本机 batch（batch 需先关 GUI 编辑器——工程锁互斥）。</para>
    /// <para>菜单：Window → General → Test Player Runner。</para>
    /// <para>域重载存活：Player 构建可能触发域重载，回调宿主与计数随 <c>Temp/MoiraiPlayerTestRun.json</c>
    /// 落盘恢复（与测试桥 <c>TestRequestRunner</c> 同一教训：跨域的真相只能住磁盘）。</para>
    /// </summary>
    public sealed class TestPlayerRunnerWindow : OdinEditorWindow
    {
        #region 常量 [CONSTANTS]

        private const string RUN_STATE_PATH = "Temp/MoiraiPlayerTestRun.json";
        private const string DEFAULT_REPORT_DIR = "Library/PlayerTestResults";
        private const int DEFAULT_HEARTBEAT_SECONDS = 60 * 10;

        #endregion

        #region 参数 [SETTINGS]

        [BoxGroup("目标平台"), ShowInInspector, LabelText("目标平台"),
            ValueDropdown(nameof(BuildTargetChoices)), OnValueChanged(nameof(RefreshCliCommand)),
            Tooltip("玩家构建目标；等价 CLI 的 -testPlatform")]
        private BuildTarget _buildTarget;

        [BoxGroup("过滤"), ShowInInspector, LabelText("程序集"), TextArea(2, 3), Delayed,
            Tooltip("按程序集名过滤（不含 .dll），分号或换行分隔；留空跑全部。等价 CLI 的 -assemblyNames")]
        private string _assemblyNames = string.Empty;

        [BoxGroup("过滤"), ShowInInspector, LabelText("用例"), TextArea(2, 3), Delayed,
            Tooltip("按用例全名过滤（命名空间.类.方法 或夹具全名），分号或换行分隔；留空不过滤。等价 CLI 的 -testFilter")]
        private string _testNames = string.Empty;

        [BoxGroup("运行参数"), ShowInInspector, LabelText("心跳超时（秒）"), MinValue(10), Delayed,
            OnValueChanged(nameof(RefreshCliCommand)),
            Tooltip("玩家与编辑器之间的 PlayerConnection 心跳超时；玩家卡死或断连时按此收口。等价 CLI 的 -playerHeartbeatTimeout")]
        private int _heartbeatTimeout = DEFAULT_HEARTBEAT_SECONDS;

        [BoxGroup("运行参数"), ShowInInspector, LabelText("报告输出"), Delayed,
            Tooltip("相对工程根。跑完写入文本报告（计数 + 逐格失败详情），完成再落同名 .done")]
        private string _reportPath = DEFAULT_REPORT_DIR + "/player-tests.txt";

        private ValueDropdownList<BuildTarget> BuildTargetChoices()
        {
            var choices = new ValueDropdownList<BuildTarget>();
            foreach (BuildTarget target in Enum.GetValues(typeof(BuildTarget)))
            {
                if (BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                {
                    choices.Add(target.ToString(), target);
                }
            }

            return choices;
        }

        #endregion

        #region 状态 [STATE]

        [ShowInInspector, HideLabel, ReadOnly, PropertyOrder(-1), MultiLineProperty(4)]
        private string _status = "未运行";

        private RunState _run;

        private RunCallbacks _callbacks;

        [BoxGroup("运行"), Button(ButtonSizes.Large), GUIColor(0.4f, 0.8f, 0.5f), EnableIf(nameof(CanRun))]
        private void RunInPlayer()
        {
            if (_run != null && !_run.Finished)
            {
                Debug.LogWarning("[TestPlayerRunner] 已有 Player 测试在跑，等它收口再发起新单");
                return;
            }

            string reportPath = ResolveReportPath();
            _run = new RunState
            {
                Guid = string.Empty,
                ReportPath = Path.GetFullPath(reportPath),
                StartedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Finished = false,
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_run.ReportPath));
            }
            catch (Exception exception)
            {
                _status = $"报告路径不可用：{exception.Message}";
                return;
            }

            if (File.Exists(_run.ReportPath + ".done")) File.Delete(_run.ReportPath + ".done");

            var filter = new Filter
            {
                testMode = TestMode.PlayMode,
                targetPlatform = _buildTarget,
                assemblyNames = SplitLines(_assemblyNames),
                testNames = SplitLines(_testNames),
            };

            var settings = new ExecutionSettings(filter)
            {
                playerHeartbeatTimeout = _heartbeatTimeout,
            };

            _callbacks = RunCallbacks.EnsureRegistered();
            _callbacks.BindWindow(this);

            string guid = ScriptableObject.CreateInstance<TestRunnerApi>().Execute(settings);
            _run.Guid = guid ?? string.Empty;
            PersistRunState();
            _status = $"运行中：{_buildTarget}（报告将落 {_run.ReportPath}）";
        }

        [BoxGroup("运行"), ShowInInspector, ReadOnly, HideLabel, MultiLineProperty(6), PropertyOrder(1)]
        private string _cliCommand;

        [BoxGroup("运行"), Button, PropertyOrder(2)]
        private void CopyCliCommand()
        {
            EditorGUIUtility.systemCopyBuffer = BuildCliCommand();
            _status = "CLI 等价命令已复制到剪贴板";
        }

        private bool CanRun() => _run == null || _run.Finished;

        private void RefreshCliCommand() => _cliCommand = BuildCliCommand();

        private void OnEnable()
        {
            base.OnEnable();
            _buildTarget = EditorUserBuildSettings.activeBuildTarget;
            _cliCommand = BuildCliCommand();
            RestoreRunState();
            RunCallbacks.EnsureRegistered().BindWindow(this);
        }

        private void OnDisable()
        {
            RunCallbacks.EnsureRegistered().UnbindWindow(this);
            base.OnDisable();
        }

        #endregion

        #region 命令拼装 [CLI EQUIVALENT]

        private string BuildCliCommand()
        {
            var builder = new StringBuilder();
            builder.Append("Unity -batchmode -projectPath <工程根>");
            builder.Append(" -runTests -testPlatform ").Append(_buildTarget);
            builder.Append(" -testResults <绝对路径>/report.xml -logFile <绝对路径>/run.log");

            string[] assemblies = SplitLines(_assemblyNames);
            if (assemblies.Length > 0)
            {
                builder.Append(" -assemblyNames ").Append(string.Join(";", assemblies));
            }

            string[] tests = SplitLines(_testNames);
            if (tests.Length > 0)
            {
                builder.Append(" -testFilter ").Append(string.Join(";", tests));
            }

            if (_heartbeatTimeout != DEFAULT_HEARTBEAT_SECONDS)
            {
                builder.Append(" -playerHeartbeatTimeout ").Append(_heartbeatTimeout.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append('\n');
            builder.Append("# 前提：GUI 编辑器先关（工程锁互斥）；退出码 0=全过 / 2=测试失败 / 3=RunError / 4=平台名错");
            return builder.ToString();
        }

        private static string[] SplitLines(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
            string[] parts = raw.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var trimmed = new string[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                trimmed[i] = parts[i].Trim();
            }

            return trimmed;
        }

        private string ResolveReportPath()
        {
            string path = string.IsNullOrWhiteSpace(_reportPath)
                ? DEFAULT_REPORT_DIR + "/player-tests.txt"
                : _reportPath;
            return path.StartsWith("Library", StringComparison.Ordinal) || Path.IsPathRooted(path)
                ? path
                : Path.Combine(DEFAULT_REPORT_DIR, path);
        }

        #endregion

        #region 收口 [COMPLETION]

        private void PersistRunState()
        {
            try
            {
                File.WriteAllText(RUN_STATE_PATH, UnityEngine.JsonUtility.ToJson(_run));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TestPlayerRunner] 运行态落盘失败：{exception.Message}");
            }
        }

        private void RestoreRunState()
        {
            try
            {
                if (!File.Exists(RUN_STATE_PATH)) return;
                _run = UnityEngine.JsonUtility.FromJson<RunState>(File.ReadAllText(RUN_STATE_PATH));
                _status = _run != null && !_run.Finished
                    ? $"域重载恢复：运行中（报告将落 {_run?.ReportPath}）"
                    : "上一轮已完成，报告见 " + (_run?.ReportPath ?? "(未记录)");
            }
            catch (Exception)
            {
                _run = null;
            }
        }

        /// <summary>由回调宿主驱动（编辑器主线程）。</summary>
        internal void OnTestFinished(int pass, int fail, int skip, string currentTest)
        {
            if (_run == null) return;
            _run.Pass = pass;
            _run.Fail = fail;
            _run.Skip = skip;
            _status = $"运行中：{currentTest}（已过 {pass} / 败 {fail} / 跳 {skip}）";
            PersistRunState();
        }

        /// <summary>由回调宿主驱动（编辑器主线程）。</summary>
        internal void OnRunFinished(int pass, int fail, int skip, double durationSeconds, string[] failures)
        {
            if (_run == null) return;
            _run.Finished = true;
            PersistRunState();
            TryDeleteRunState();

            var report = new StringBuilder();
            report.Append($"player run {_run.StartedAt} | target {_buildTarget}");
            report.Append($" | passed {pass} | failed {fail} | skipped {skip} | {durationSeconds.ToString("F1", CultureInfo.InvariantCulture)}s");
            report.Append('\n');
            foreach (string failure in failures)
            {
                report.Append('\n').Append(failure).Append('\n');
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_run.ReportPath));
                File.WriteAllText(_run.ReportPath, report.ToString());
                File.WriteAllText(_run.ReportPath + ".done", _run.Guid);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestPlayerRunner] 报告落盘失败：{exception.Message}");
                return;
            }

            _status = fail == 0
                ? $"完成：{pass} 过 / {skip} 跳（报告 {_run.ReportPath}）"
                : $"完成：{fail} 败 / {pass} 过 / {skip} 跳（报告 {_run.ReportPath}）";
            _run = null;
        }

        private static void TryDeleteRunState()
        {
            try
            {
                if (File.Exists(RUN_STATE_PATH)) File.Delete(RUN_STATE_PATH);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TestPlayerRunner] 清理运行态失败：{exception.Message}");
            }
        }

        #endregion

        #region 运行态与回调宿主 [STATE & CALLBACKS]

        [Serializable]
        private sealed class RunState
        {
            public string Guid;
            public string ReportPath;
            public string StartedAt;
            public int Pass;
            public int Fail;
            public int Skip;
            public bool Finished;
        }

        /// <summary>
        /// ICallbacks 宿主（ScriptableObject 存活跨域重载——UTF 的 CallbacksHolder 列表不序列化，
        /// 每次域加载都重注册；计数与失败详情经窗口落盘，域重载后由 <see cref="RestoreRunState"/> 恢复）。
        /// </summary>
        private sealed class RunCallbacks : ScriptableObject, ICallbacks
        {
            private static RunCallbacks s_Instance;

            private TestPlayerRunnerWindow _window;
            private int _pass;
            private int _fail;
            private int _skip;
            private readonly List<string> _failures = new List<string>();

            public static RunCallbacks EnsureRegistered()
            {
                if (s_Instance == null)
                {
                    s_Instance = ScriptableObject.CreateInstance<RunCallbacks>();
                    TestRunnerApi.RegisterTestCallback(s_Instance, 0);
                }

                return s_Instance;
            }

            public void BindWindow(TestPlayerRunnerWindow window) => _window = window;

            public void UnbindWindow(TestPlayerRunnerWindow window)
            {
                if (_window == window) _window = null;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                _pass = _fail = _skip = 0;
                _failures.Clear();
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result == null || result.HasChildren) return;

                string state = result.ResultState ?? string.Empty;
                if (state.StartsWith("Passed", StringComparison.Ordinal))
                {
                    _pass++;
                }
                else if (state.StartsWith("Skipped", StringComparison.Ordinal) ||
                         state.StartsWith("Inconclusive", StringComparison.Ordinal) ||
                         state.StartsWith("Cancel", StringComparison.Ordinal))
                {
                    _skip++;
                }
                else
                {
                    _fail++;
                    _failures.Add($"{result.FullName} [{state}]\n    {FirstLines(result.Message)}");
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                double duration = result?.Duration ?? 0d;
                _window?.OnRunFinished(_pass, _fail, _skip, duration, _failures.ToArray());
                _pass = _fail = _skip = 0;
                _failures.Clear();
            }

            private static string FirstLines(string text)
            {
                if (string.IsNullOrEmpty(text)) return "(空)";
                string normalized = text.Replace("\r\n", "\n");
                int limit = Mathf.Min(normalized.Length, 512);
                return limit < normalized.Length ? normalized.Substring(0, limit) + " …" : normalized;
            }
        }

        #endregion

        [MenuItem("Window/General/Test Player Runner")]
        public static void OpenWindow()
        {
            GetWindow<TestPlayerRunnerWindow>("Test Player Runner").Show();
        }
    }
}
