using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Editor.Testing
{
    /// <summary>
    /// Test Player Runner：收拢「在 Player 里跑测试」的启动参数并一键发起的编辑器窗口。
    /// </summary>
    /// <remarks>
    /// 菜单 <c>Window → General → Test Player Runner</c>；与 CLI 同一 <c>PlayerLauncher</c> 机制（结果经 PlayerConnection 回传编辑器），
    /// 附等价命令行便于复制进 CI 或本机 batch——batch 需先关 GUI 编辑器（工程锁互斥）。 <br />
    /// 启动条件：正在编译、导入、切 PlayMode 或已有任意 Test Runner 作业在跑时拒绝发起。 <br />
    /// 域重载后按作业 guid 判活（探针缺失逐级降级宽限），证实已死即按 ABORTED 强制收口；失败详情 Message 与 StackTrace 并采、有上限。 <br />
    /// 呈现代码限 UI Toolkit：本窗口住测试程序集，其 asmdef 以 <c>overrideReferences</c> 收窄预编译引用、不含 Odin； <br />
    /// 设置字段走 <c>[SerializeField]</c> 的 EditorWindow 自身序列化，跨域重载保留。
    /// </remarks>
    public sealed class TestPlayerRunnerWindow : EditorWindow
    {
        #region 常量 [CONSTANTS]

        private const string RUN_STATE_PATH = "Temp/MoiraiPlayerTestRun.json";
        private const string DEFAULT_REPORT_DIR = "Library/PlayerTestResults";
        private const int DEFAULT_HEARTBEAT_SECONDS = 60 * 10;
        private const int MIN_HEARTBEAT_SECONDS = 10;

        /// <summary>窗口默认打开尺寸（宽×高），四卡内容无滚动容纳。</summary>
        private static readonly Vector2 DEFAULT_WINDOW_SIZE = new Vector2(720f, 640f);

        /// <summary>窗口最小尺寸：再小四个分区会挤成标题条（内容有 ScrollView 兜底）。</summary>
        private static readonly Vector2 MIN_WINDOW_SIZE = new Vector2(520f, 420f);

        /// <summary>失败详情上限：超出只计数不展开，失败风暴不会把报告写成无底洞。</summary>
        private const int MAX_FAILURES = 200;

        /// <summary>域重载后给 UTF <c>ResumeRunningJobs</c> 认领作业的窗口；超时仍无在途作业则判孤儿单。</summary>
        private const double ORPHAN_GRACE_SECONDS = 2.0;

        /// <summary>判活探针完全不可用时的扩展宽限：宁可多等也不误杀，宽限后仍无法证实在跑即强制收口。</summary>
        private const double UNVERIFIED_ORPHAN_GRACE_SECONDS = 30.0;

        #endregion

        #region 参数 [SETTINGS]

        [SerializeField] private BuildTarget _buildTarget;
        [SerializeField] private string _assemblyNames = string.Empty;
        [SerializeField] private string _testNames = string.Empty;
        [SerializeField] private int _heartbeatTimeout = DEFAULT_HEARTBEAT_SECONDS;
        [SerializeField] private string _reportPath = DEFAULT_REPORT_DIR + "/player-tests.txt";

        private PopupField<BuildTarget> _buildTargetField;
        private TextField _assemblyNamesField;
        private TextField _testNamesField;
        private IntegerField _heartbeatField;
        private TextField _reportPathField;
        private TextField _cliField;

        #endregion

        #region 状态 [STATE]

        private string _status = "未运行";

        private RunState _run;

        /// <summary>本域内 <see cref="RestoreRunState"/> 恢复未完单的时刻（<see cref="EditorApplication.timeSinceStartup"/> 秒）。</summary>
        /// <remarks>
        /// -1 表示本单由本域新发起，孤儿判定不参与——只有跨域重载恢复出的单才可能已成孤儿。
        /// </remarks>
        private double _restoredAt = -1d;

        private RunCallbacks _callbacks;

        private Label _statusLabel;
        private Button _runButton;
        private Button _cancelButton;

        #endregion

        #region UI 构建 [UI BUILD]

        /// <summary>
        /// 打开 Test Player Runner 窗口（菜单 Window → General → Test Player Runner）。
        /// </summary>
        [MenuItem("Window/General/Test Player Runner")]
        public static void OpenWindow()
        {
            TestPlayerRunnerWindow window = GetWindow<TestPlayerRunnerWindow>("Test Player Runner");
            window.minSize = MIN_WINDOW_SIZE;
            if (window.position.width < MIN_WINDOW_SIZE.x || window.position.height < MIN_WINDOW_SIZE.y)
            {
                // 首次打开（或布局里存得比下限还小）时落到显式默认尺寸；用户调整过的大尺寸原样保留
                window.position = new Rect(window.position.position, DEFAULT_WINDOW_SIZE);
            }

            window.Show();
        }

        private void OnEnable()
        {
            if ((int)_buildTarget == 0)
            {
                // 0 不是合法 BuildTarget——仅首次打开时用当前激活平台作默认，不覆盖已持久化的选择
                _buildTarget = EditorUserBuildSettings.activeBuildTarget;
            }

            RestoreRunState();
            RunCallbacks.EnsureRegistered().BindWindow(this);
            EditorApplication.update += PollRunLiveness;
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollRunLiveness;
            RunCallbacks.EnsureRegistered().UnbindWindow(this);
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();

            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1, paddingLeft = 6, paddingRight = 6, paddingTop = 6 } };
            root.Add(scroll);

            BuildTargetSection(scroll);
            FilterSection(scroll);
            RunParametersSection(scroll);
            RunSection(scroll);

            RefreshUi();
        }

        private void BuildTargetSection(VisualElement parent)
        {
            VisualElement card = MakeSection(parent, "目标平台");
            List<BuildTarget> supported = CollectSupportedBuildTargets();
            int index = Math.Max(0, supported.IndexOf(_buildTarget));
            _buildTargetField = new PopupField<BuildTarget>("目标平台", supported, index)
            {
                tooltip = "玩家构建目标；等价 CLI 的 -testPlatform",
            };
            _buildTargetField.RegisterValueChangedCallback(evt =>
            {
                _buildTarget = evt.newValue;
                RefreshUi();
            });
            card.Add(_buildTargetField);
        }

        private void FilterSection(VisualElement parent)
        {
            VisualElement card = MakeSection(parent, "过滤");

            _assemblyNamesField = new TextField("程序集")
            {
                value = _assemblyNames,
                multiline = true,
                tooltip = "按程序集名过滤（不含 .dll），分号或换行分隔；留空跑全部。等价 CLI 的 -assemblyNames",
            };
            _assemblyNamesField.style.minHeight = 40;
            _assemblyNamesField.RegisterValueChangedCallback(evt =>
            {
                _assemblyNames = evt.newValue;
                RefreshUi();
            });
            card.Add(_assemblyNamesField);

            _testNamesField = new TextField("用例")
            {
                value = _testNames,
                multiline = true,
                tooltip = "按用例全名过滤（命名空间.类.方法 或夹具全名），分号或换行分隔；留空不过滤。等价 CLI 的 -testFilter",
            };
            _testNamesField.style.minHeight = 40;
            _testNamesField.RegisterValueChangedCallback(evt =>
            {
                _testNames = evt.newValue;
                RefreshUi();
            });
            card.Add(_testNamesField);
        }

        private void RunParametersSection(VisualElement parent)
        {
            VisualElement card = MakeSection(parent, "运行参数");

            _heartbeatField = new IntegerField("心跳超时（秒）")
            {
                value = _heartbeatTimeout,
                tooltip = "玩家与编辑器之间的 PlayerConnection 心跳超时；玩家卡死或断连时按此收口。等价 CLI 的 -playerHeartbeatTimeout",
            };
            _heartbeatField.RegisterValueChangedCallback(evt =>
            {
                _heartbeatTimeout = Mathf.Max(MIN_HEARTBEAT_SECONDS, evt.newValue);
                if (_heartbeatField.value != _heartbeatTimeout) _heartbeatField.SetValueWithoutNotify(_heartbeatTimeout);
                RefreshUi();
            });
            card.Add(_heartbeatField);

            _reportPathField = new TextField("报告输出")
            {
                value = _reportPath,
                tooltip = "相对工程根。跑完写入文本报告（计数 + 逐格失败详情），完成再落同名 .done",
            };
            _reportPathField.RegisterValueChangedCallback(evt =>
            {
                _reportPath = evt.newValue;
                RefreshUi();
            });
            card.Add(_reportPathField);
        }

        private void RunSection(VisualElement parent)
        {
            VisualElement card = MakeSection(parent, "运行");

            _statusLabel = new Label(_status)
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = 6,
                    paddingLeft = 4,
                    paddingRight = 4,
                    paddingTop = 4,
                    paddingBottom = 4,
                    backgroundColor = new Color(0f, 0f, 0f, 0.15f),
                },
            };
            card.Add(_statusLabel);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _runButton = new Button(OnRunClicked) { text = "Run in Player" };
            _runButton.style.flexGrow = 1;
            _runButton.style.height = 30;
            _runButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _runButton.style.unityBackgroundImageTintColor = new Color(0.4f, 0.8f, 0.5f);
            _cancelButton = new Button(OnCancelClicked) { text = "Cancel" };
            _cancelButton.style.width = 90;
            _cancelButton.style.height = 30;
            var copyButton = new Button(OnCopyCliClicked) { text = "复制 CLI 等价命令" };
            copyButton.style.width = 150;
            copyButton.style.height = 30;
            buttons.Add(_runButton);
            buttons.Add(_cancelButton);
            buttons.Add(copyButton);
            card.Add(buttons);

            _cliField = new TextField
            {
                value = BuildCliCommand(),
                multiline = true,
                isReadOnly = true,
            };
            _cliField.style.minHeight = 80;
            _cliField.style.marginTop = 6;
            card.Add(_cliField);
        }

        #endregion

        #region UI 工具 [UI HELPERS]

        private static VisualElement MakeSection(VisualElement parent, string title)
        {
            var card = new VisualElement
            {
                style =
                {
                    marginBottom = 8,
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 4,
                    paddingBottom = 6,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = new Color(0f, 0f, 0f, 0.3f),
                    borderBottomColor = new Color(0f, 0f, 0f, 0.3f),
                    borderLeftColor = new Color(0f, 0f, 0f, 0.3f),
                    borderRightColor = new Color(0f, 0f, 0f, 0.3f),
                },
            };
            var label = new Label(title)
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 },
            };
            card.Add(label);
            parent.Add(card);
            return card;
        }

        private static List<BuildTarget> CollectSupportedBuildTargets()
        {
            var supported = new List<BuildTarget>();
            foreach (BuildTarget target in Enum.GetValues(typeof(BuildTarget)))
            {
                if (BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                {
                    supported.Add(target);
                }
            }

            return supported;
        }

        /// <summary>
        /// 状态 → UI 单向刷新：状态标签文本、Run/Cancel 使能、CLI 预览。任何状态变更后调用一次。
        /// </summary>
        private void RefreshUi()
        {
            if (_statusLabel != null) _statusLabel.text = _status;
            _runButton?.SetEnabled(_run == null || _run.finished);
            _cancelButton?.SetEnabled(_run != null && !_run.finished);
            if (_cliField != null) _cliField.SetValueWithoutNotify(BuildCliCommand());
        }

        private void OnRunClicked() => RunInPlayer();

        private void OnCancelClicked() => CancelRun();

        private void OnCopyCliClicked()
        {
            EditorGUIUtility.systemCopyBuffer = BuildCliCommand();
            _status = "CLI 等价命令已复制到剪贴板";
            RefreshUi();
        }

        #endregion

        #region 发起与取消 [RUN & CANCEL]

        private void RunInPlayer()
        {
            if (_run != null && !_run.finished)
            {
                Debug.LogWarning("[TestPlayerRunner] 已有 Player 测试在跑，等它收口再发起新单");
                RefreshUi();
                return;
            }

            // 接单门（与测试桥同款）：ICallbacks 无法归因到具体 run，编辑器里有任意作业在跑时并发发起
            // 会把结果串进同一份账；编译/导入/切 PlayMode 期间发起同样收不齐结果
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _status = "编辑器正在编译/导入/切换 PlayMode，稍后再发起";
                RefreshUi();
                return;
            }

            if (IsAnyRunActive())
            {
                _status = "已有 Test Runner 作业在跑（含测试桥/窗口手动发起），等它收口再发起";
                RefreshUi();
                return;
            }

            string reportPath = ResolveReportPath();
            _run = new RunState
            {
                guid = string.Empty,
                reportPath = Path.GetFullPath(reportPath),
                startedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                finished = false,
                failures = Array.Empty<string>(),
            };
            _restoredAt = -1d;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_run.reportPath));
            }
            catch (Exception exception)
            {
                _status = $"报告路径不可用：{exception.Message}";
                _run = null;
                RefreshUi();
                return;
            }

            if (File.Exists(_run.reportPath + ".done")) File.Delete(_run.reportPath + ".done");

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
            _run.guid = guid ?? string.Empty;
            PersistRunState();
            _status = $"运行中：{_buildTarget}（报告将落 {_run.reportPath}）";
            RefreshUi();
        }

        private void CancelRun()
        {
            if (_run == null || _run.finished) return;

            if (string.IsNullOrEmpty(_run.guid))
            {
                FinishAborted("取消收口：作业标识缺失，无法定向取消");
                return;
            }

            bool accepted;
            try
            {
                accepted = TestRunnerApi.CancelTestRun(_run.guid);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TestPlayerRunner] 取消作业失败：{exception.Message}");
                accepted = false;
            }

            // UTF 受理取消后不再送达 RunFinished（RunFinishedInvocationEvent 被 Canceled 模式跳过），
            // 受理即由本窗口收口；拒绝受理（作业已在收尾/已取消中）则等 RunFinished 自然收口
            if (accepted)
            {
                FinishAborted("取消收口：作业已按取消请求终止");
            }
            else
            {
                _status = "取消未受理（作业可能已在收尾），等待自然收口";
                RefreshUi();
            }
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

        #region 判活探针 [LIVENESS PROBES]

        /// <summary><c>TestRunnerApi.IsRunning(guid)</c> 反射探针：只问本窗口这一单是否仍在跑，窗口手动跑/测试桥不干扰判定。</summary>
        private static readonly Func<string, bool> IsRunningProbe =
            CreateProbe<Func<string, bool>>("IsRunning", typeof(string));

        /// <summary><c>TestRunnerApi.IsRunActive()</c> 反射探针：任意 run 在跑即真；仅作接单门与 <see cref="IsRunningProbe"/> 不可用时的降级。</summary>
        private static readonly Func<bool> IsRunActiveProbe = CreateProbe<Func<bool>>("IsRunActive", null);

        private enum ELiveness
        {
            Running,
            Idle,
            Unknown,
        }

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
        /// 三态判活（与测试桥同序）：先按本单 guid 精确判活，guid 缺失或探针抛错才降级「任意 run 在跑」语义。
        /// </summary>
        private ELiveness ProbeRunLiveness()
        {
            if (_run != null && !string.IsNullOrEmpty(_run.guid) && IsRunningProbe != null)
            {
                try
                {
                    return IsRunningProbe(_run.guid) ? ELiveness.Running : ELiveness.Idle;
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
        /// 接单门用：编辑器里有任意 Test Runner 作业在跑即真（含测试桥与本窗口外的手动发起）。
        /// </summary>
        /// <remarks>
        /// 探针不可用时放行，不因基础设施故障卡死接单。
        /// </remarks>
        private static bool IsAnyRunActive()
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
                RunState restored = UnityEngine.JsonUtility.FromJson<RunState>(File.ReadAllText(RUN_STATE_PATH));
                if (restored == null || string.IsNullOrEmpty(restored.reportPath))
                {
                    // 旧格式/残缺的运行文件无处落报告，按孤儿丢弃——留着只会把 Run 按钮钉死在灰态
                    _run = null;
                    TryDeleteRunState();
                    return;
                }

                _run = restored;
                if (!_run.finished)
                {
                    _restoredAt = EditorApplication.timeSinceStartup;
                    _status = $"域重载恢复：运行中（报告将落 {_run.reportPath}）";
                }
                else
                {
                    _status = "上一轮已完成，报告见 " + _run.reportPath;
                }
            }
            catch (Exception)
            {
                _run = null;
            }
        }

        /// <summary>
        /// 由回调宿主驱动（编辑器主线程）：单格通过。
        /// </summary>
        internal void OnTestPassed(string test) => RecordProgress(1, 0, 0, test, null);

        /// <summary>
        /// 由回调宿主驱动（编辑器主线程）：单格跳过（Skip/Inconclusive/Cancel 变体）。
        /// </summary>
        internal void OnTestSkipped(string test) => RecordProgress(0, 0, 1, test, null);

        /// <summary>
        /// 由回调宿主驱动（编辑器主线程）：单格失败，附 Message+StackTrace 详情。
        /// </summary>
        internal void OnTestFailed(string test, string detail) => RecordProgress(0, 1, 0, test, detail);

        private void RecordProgress(int pass, int fail, int skip, string currentTest, string failureDetail)
        {
            if (_run == null) return;
            _run.pass += pass;
            _run.fail += fail;
            _run.skip += skip;

            if (failureDetail != null && _run.failures.Length < MAX_FAILURES)
            {
                Array.Resize(ref _run.failures, _run.failures.Length + 1);
                _run.failures[_run.failures.Length - 1] = failureDetail;
            }

            _status = $"运行中：{currentTest}（已过 {_run.pass} / 败 {_run.fail} / 跳 {_run.skip}）";
            PersistRunState();
            RefreshUi();
        }

        /// <summary>
        /// 由回调宿主驱动（编辑器主线程）：正常收口，计数与失败详情取自跨域真相源（磁盘运行态）。
        /// </summary>
        internal void OnRunFinished(double durationSeconds)
        {
            RunState state = _run;
            if (state == null) return;

            _run = null;
            TryDeleteRunState();

            var report = new StringBuilder();
            report.Append($"player run {state.startedAt} | target {_buildTarget}");
            report.Append($" | passed {state.pass} | failed {state.fail} | skipped {state.skip} | {durationSeconds.ToString("F1", CultureInfo.InvariantCulture)}s");
            report.Append('\n');
            foreach (string failure in state.failures)
            {
                report.Append('\n').Append(failure).Append('\n');
            }

            if (state.fail > state.failures.Length)
            {
                report.Append($"\n（另有 {state.fail - state.failures.Length} 条失败详情超出 {MAX_FAILURES} 上限未展开）\n");
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(state.reportPath));
                File.WriteAllText(state.reportPath, report.ToString());
                // .done 载荷为本单作业 guid——与测试桥「.done=请求 id」同构：本窗口的请求标识即 Execute 返回的 guid
                File.WriteAllText(state.reportPath + ".done", state.guid);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestPlayerRunner] 报告落盘失败：{exception.Message}");
                RefreshUi();
                return;
            }

            _status = state.fail == 0
                ? $"完成：{state.pass} 过 / {state.skip} 跳（报告 {state.reportPath}）"
                : $"完成：{state.fail} 败 / {state.pass} 过 / {state.skip} 跳（报告 {state.reportPath}）";
            RefreshUi();
        }

        /// <summary>
        /// 由回调宿主驱动（编辑器主线程）：UTF 报错（含玩家构建失败）按 ABORTED 收口，不锁死窗口。
        /// </summary>
        internal void OnRunError(string message) => FinishAborted($"TestRunner 报错：{message}");

        /// <summary>
        /// ABORTED 收口：错误回调、取消与孤儿单共用，已收集的计数与失败详情随报告交付，状态文件当场清理。
        /// </summary>
        /// <remarks>
        /// 状态文件清理后 Run 按钮立即解禁，不留「报错即永久灰死」的死结。
        /// </remarks>
        private void FinishAborted(string reason)
        {
            RunState state = _run;
            if (state == null) return;

            _run = null;
            TryDeleteRunState();

            var report = new StringBuilder();
            report.Append($"player run {state.startedAt} | target {_buildTarget} | ABORTED: {reason}");
            report.Append($" | collected passed {state.pass} | failed {state.fail} | skipped {state.skip}");
            report.Append('\n');
            foreach (string failure in state.failures)
            {
                report.Append('\n').Append(failure).Append('\n');
            }

            if (state.fail > state.failures.Length)
            {
                report.Append($"\n（另有 {state.fail - state.failures.Length} 条失败详情超出 {MAX_FAILURES} 上限未展开）\n");
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(state.reportPath));
                File.WriteAllText(state.reportPath, report.ToString());
                File.WriteAllText(state.reportPath + ".done", state.guid);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestPlayerRunner] ABORTED 报告落盘失败：{exception.Message}");
            }

            _status = $"已中止：{reason}（报告 {state.reportPath}）";
            RefreshUi();
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

        /// <summary>
        /// 孤儿单判定（与测试桥同款）：只在跨域重载恢复出的未完单上参与，按作业 guid 判活，扩展宽限后仍无法证实在跑即按 ABORTED 收口。
        /// </summary>
        /// <remarks>
        /// 探针缺失或抛错逐级降级；状态文件不能把 Run 按钮永远钉死在灰态——窗口关闭期间判定不跑，重开窗口的 <c>OnEnable</c> 会接上。
        /// </remarks>
        private void PollRunLiveness()
        {
            if (_run == null || _run.finished || _restoredAt < 0d) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - _restoredAt < ORPHAN_GRACE_SECONDS) return;

            switch (ProbeRunLiveness())
            {
                case ELiveness.Running:
                    return;
                case ELiveness.Idle:
                    FinishAborted("域重载后未发现仍在执行的 TestRunner 作业（孤儿单收口）");
                    return;
                case ELiveness.Unknown:
                    if (EditorApplication.timeSinceStartup - _restoredAt >= UNVERIFIED_ORPHAN_GRACE_SECONDS)
                    {
                        FinishAborted("判活探针不可用且超出宽限，强制收口（孤儿单）");
                    }
                    return;
            }
        }

        #endregion

        #region 运行态与回调宿主 [STATE & CALLBACKS]

        [Serializable]
        private sealed class RunState
        {
            public string guid;
            public string reportPath;
            public string startedAt;
            public int pass;
            public int fail;
            public int skip;
            public bool finished;
            public string[] failures = Array.Empty<string>();
        }

        /// <summary>
        /// <see cref="ICallbacks"/> 宿主（<c>ScriptableObject</c> 存活跨域重载）：只做分类转发，不持有计数与失败详情。
        /// </summary>
        /// <remarks>
        /// UTF 的 <c>CallbacksHolder</c> 列表不序列化，每次域加载都要重注册；计数与失败详情只活在窗口 <c>RunState</c>（磁盘真相源）， <br />
        /// 跨域重载后由 UTF <c>ResumeRunningJobs</c> 续跑作业、窗口从盘上接账。
        /// </remarks>
        private sealed class RunCallbacks : ScriptableObject, ICallbacks, IErrorCallbacks
        {
            private static RunCallbacks s_Instance;

            private TestPlayerRunnerWindow _window;

            private bool OwnsWindowRun => _window != null && _window._run != null && !_window._run.finished;

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
                // 每单的计数在窗口 RunState 构造时清零、跨域从盘上恢复，这里无需也无权重置；
                // 归因护栏见 OwnsWindowRun：本窗口无在途单时对一切回调保持沉默
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result == null || result.HasChildren || !OwnsWindowRun) return;

                string state = result.ResultState ?? string.Empty;
                // 失败态不保证是官方 "Failed"（Error/Cancelled 等变体皆有）——按通过/跳过白名单归类，其余一律计失败
                if (state.StartsWith("Passed", StringComparison.Ordinal))
                {
                    _window.OnTestPassed(result.FullName);
                }
                else if (state.StartsWith("Skipped", StringComparison.Ordinal) ||
                         state.StartsWith("Inconclusive", StringComparison.Ordinal) ||
                         state.StartsWith("Cancel", StringComparison.Ordinal))
                {
                    _window.OnTestSkipped(result.FullName);
                }
                else
                {
                    _window.OnTestFailed(result.FullName,
                        $"{result.FullName} [{state}]\n{Indent(FirstLines(result.Message, 6))}\n{Indent(FirstLines(result.StackTrace, 8))}");
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                if (!OwnsWindowRun) return;
                _window.OnRunFinished(result?.Duration ?? 0d);
            }

            public void OnError(string message)
            {
                // 窗口无在途单（含窗口关闭/别家作业报错）时 FinishAborted 自行空转，不打扰
                _window?.OnRunError(message);
            }

            private static string FirstLines(string text, int count)
            {
                if (string.IsNullOrEmpty(text)) return string.Empty;
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                int take = Math.Min(count, lines.Length);
                var builder = new StringBuilder();
                for (int i = 0; i < take; i++)
                {
                    if (i > 0) builder.Append('\n');
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
                if (string.IsNullOrEmpty(text)) return "    (空)";
                return "    " + text.Replace("\n", "\n    ");
            }
        }

        #endregion
    }
}
