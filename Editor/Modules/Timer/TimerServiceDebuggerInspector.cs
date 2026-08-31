using Moirai.Atropos.Timer;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 计时器服务调试组件 Inspector。
    /// <para>提供计时器后端初始容量配置编辑与运行时调试信息（统计、活跃计时器采样、僵尸一次性计时器检测）。</para>
    /// </summary>
    [CustomEditor(typeof(TimerServiceDebugger))]
    internal sealed class TimerServiceDebuggerInspector : GameFrameworkInspector
    {
        private const double UPDATE_INTERVAL = 0.05d;
        private const int DISPLAY_COUNT = 32;
        private const int MIN_INITIAL_CAPACITY = 256;
        private const int MAX_INITIAL_CAPACITY = 16384;
        private const int CAPACITY_STEP = 256;
        private const float ROW_LABEL_WIDTH = 146f;
        private const float SLIDER_VALUE_WIDTH = 58f;

        private readonly TimerDebugInfo[] _timerBuffer = new TimerDebugInfo[DISPLAY_COUNT];
        private readonly TimerDebugInfo[] _staleBuffer = new TimerDebugInfo[DISPLAY_COUNT];
        private double _lastUpdateTime;
        private Vector2 _timerListScrollPosition;

        /// <summary>
        /// 绘制事件。
        /// </summary>
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space(6f);
            DrawConfiguration();
            DrawRuntimeDebug();

            RequestRuntimeRepaint();
        }

        /// <summary>
        /// 绘制后端初始容量配置（仅编辑期可改，运行期只读）。
        /// </summary>
        private void DrawConfiguration()
        {
            EditorGUILayout.LabelField("配置 [CONFIGURATION]", EditorStyles.boldLabel);

            TimerServiceSettings settings = TimerServiceSettings.Instance;
            if (settings == null)
            {
                EditorGUILayout.HelpBox("未找到 TimerServiceSettings 资产，无法编辑初始容量。", MessageType.Warning);
                return;
            }

            if (TimerServiceSettings.TimerServiceHandlerConfig is not DefaultTimerHandlerConfig config)
            {
                EditorGUILayout.HelpBox("当前计时器后端不含初始容量配置。", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                int capacity = config.InitialCapacity;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("初始容量 [Initial Capacity]", GUILayout.Width(ROW_LABEL_WIDTH));
                int sliderValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(
                    Mathf.Clamp(capacity, MIN_INITIAL_CAPACITY, MAX_INITIAL_CAPACITY),
                    MIN_INITIAL_CAPACITY, MAX_INITIAL_CAPACITY));
                sliderValue = Mathf.Clamp(
                    EditorGUILayout.IntField(sliderValue, GUILayout.Width(SLIDER_VALUE_WIDTH)),
                    MIN_INITIAL_CAPACITY, MAX_INITIAL_CAPACITY);
                EditorGUILayout.EndHorizontal();

                sliderValue = AlignCapacity(sliderValue);
                if (sliderValue != capacity)
                {
                    Undo.RecordObject(settings, "Change Timer Initial Capacity");
                    config.InitialCapacity = sliderValue;
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssetIfDirty(settings);
                }
            }

            EditorGUILayout.HelpBox(
                StringUtility.Format("按 {0} 对齐，仅在计时器服务下次初始化时生效（运行中修改无效）。", CAPACITY_STEP),
                MessageType.None);
        }

        /// <summary>
        /// 绘制运行时调试信息。
        /// </summary>
        private void DrawRuntimeDebug()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("运行时调试 [RUNTIME DEBUG]", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("仅在运行时可用。", MessageType.Info);
                return;
            }

            if (!TimerService.IsValid)
            {
                EditorGUILayout.HelpBox("计时器服务未初始化。", MessageType.Info);
                return;
            }

            TimerService.GetStatistics(out int activeCount, out int poolCapacity,
                out int peakActiveCount, out int freeCount);

            DrawStatisticRow("活跃计时器 [Active]", activeCount);
            DrawStatisticRow("池容量 [Pool Capacity]", poolCapacity);
            DrawStatisticRow("峰值活跃 [Peak Active]", peakActiveCount);
            DrawStatisticRow("空闲槽位 [Free Slots]", freeCount);
            DrawUsageBar("活跃占用 [Active Usage]", activeCount, poolCapacity);
            DrawUsageBar("峰值占用 [Peak Usage]", peakActiveCount, poolCapacity);

            EditorGUILayout.Space(4f);
            DrawTimerList(activeCount);
            DrawStaleTimerList(activeCount);
        }

        /// <summary>
        /// 绘制活跃计时器采样列表。
        /// </summary>
        private void DrawTimerList(int activeCount)
        {
            EditorGUILayout.LabelField("活跃计时器采样 [ACTIVE TIMER SAMPLE]", EditorStyles.boldLabel);

            if (activeCount <= 0)
            {
                EditorGUILayout.HelpBox("无活跃计时器。", MessageType.None);
                return;
            }

            int timerCount = TimerService.GetAllTimers(_timerBuffer);
            if (activeCount > DISPLAY_COUNT)
            {
                EditorGUILayout.HelpBox(
                    StringUtility.Format("仅显示前 {0} 个，共 {1} 个。", timerCount, activeCount),
                    MessageType.None);
            }

            _timerListScrollPosition = EditorGUILayout.BeginScrollView(_timerListScrollPosition, GUILayout.MaxHeight(240f));
            for (int i = 0; i < timerCount; i++)
            {
                DrawTimerInfo(ref _timerBuffer[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 绘制单条计时器信息。
        /// </summary>
        private void DrawTimerInfo(ref TimerDebugInfo info)
        {
            byte flags = info.Flags;
            string mode = (flags & TimerDebugFlags.LOOP) != 0 ? "循环" : "单次";
            string scale = (flags & TimerDebugFlags.UNSCALED) != 0 ? "不缩放" : "缩放";
            bool running = (flags & TimerDebugFlags.RUNNING) != 0;
            string state = running ? "运行" : "暂停";
            string title = StringUtility.Format("ID {0} | {1} | {2}", info.TimerHandle, mode, scale);
            string value = StringUtility.Format("{0} | 剩余 {1:F2}s | 周期 {2:F2}s", state, info.LeftTime, info.Duration);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, GUILayout.Width(ROW_LABEL_WIDTH + 60f));
            EditorGUILayout.LabelField(value, running ? EditorStyles.label : EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制僵尸一次性计时器列表。
        /// </summary>
        private void DrawStaleTimerList(int activeCount)
        {
            if (activeCount <= 0)
            {
                return;
            }

            int staleCount = TimerService.GetStaleOneShotTimers(_staleBuffer);
            if (staleCount <= 0)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("僵尸一次性计时器 [STALE ONE-SHOT TIMERS]", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("检测到长寿命一次性计时器（存活超过 300 秒），可能因逻辑错误未释放。", MessageType.Warning);
            for (int i = 0; i < staleCount; i++)
            {
                TimerDebugInfo info = _staleBuffer[i];
                string title = StringUtility.Format("ID {0}", info.TimerHandle);
                string value = StringUtility.Format("存活 {0:F1}s | 剩余 {1:F2}s", info.Age, info.LeftTime);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(title, GUILayout.Width(ROW_LABEL_WIDTH + 60f));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// 绘制统计行。
        /// </summary>
        private static void DrawStatisticRow(string label, int value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(ROW_LABEL_WIDTH));
            EditorGUILayout.LabelField(value.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制使用率进度条。
        /// </summary>
        private static void DrawUsageBar(string label, int value, int capacity)
        {
            float ratio = capacity > 0 ? Mathf.Clamp01((float)value / capacity) : 0f;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(ROW_LABEL_WIDTH));
            Rect barRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(barRect, ratio, StringUtility.Format("{0:P1}", ratio));
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 将容量按 <see cref="CAPACITY_STEP"/> 向上对齐并钳制在有效区间内。
        /// </summary>
        private static int AlignCapacity(int value)
        {
            int aligned = ((value + CAPACITY_STEP - 1) / CAPACITY_STEP) * CAPACITY_STEP;
            return Mathf.Clamp(aligned, MIN_INITIAL_CAPACITY, MAX_INITIAL_CAPACITY);
        }

        /// <summary>
        /// 运行时按固定间隔请求重绘，避免每帧全量重绘。
        /// </summary>
        private void RequestRuntimeRepaint()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - _lastUpdateTime < UPDATE_INTERVAL)
            {
                return;
            }

            _lastUpdateTime = currentTime;
            Repaint();
        }
    }
}
