using Moirai.Atropos.Debugger;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器服务调试视图（原生 UI Toolkit，经 <see cref="TimerService.OnInit"/> 注册进游戏内调试器 "Profiler/Timer"）。
    /// <para>展示计时器运行时统计（活跃/容量/峰值/占用率）、活跃计时器采样与"僵尸"一次性计时器检测；按 0.5s 节流重建。</para>
    /// </summary>
    public sealed class TimerServiceDebuggerWindow : PollingDebuggerWindowBase
    {
        #region 常量 [CONSTANTS]

        private const int DISPLAY_COUNT = 32;

        #endregion

        #region 字段 [FIELDS]

        private readonly TimerDebugInfo[] _timerBuffer = new TimerDebugInfo[DISPLAY_COUNT];
        private readonly TimerDebugInfo[] _staleBuffer = new TimerDebugInfo[DISPLAY_COUNT];

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化计时器调试视图的新实例。
        /// </summary>
        public TimerServiceDebuggerWindow() : base(0.5f)
        {
        }

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            if (!TimerService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("Timer Service"));
                root.Add(DebuggerUI.CreateHintLabel("Timer service not ready (enter Play Mode and finish initialization)."));
                return;
            }

            BuildRuntimeStatistics(root);
            BuildActiveTimerSample(root);
#if UNITY_EDITOR
            BuildStaleOneShotTimers(root);
#endif
        }

        #endregion

        #region 分区 [SECTIONS]

        private void BuildRuntimeStatistics(VisualElement root)
        {
            VisualElement card = AddSection(root, "RUNTIME STATISTICS");

            TimerService.GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount);

            AddRow(card, "Active", activeCount.ToString());
            AddRow(card, "Pool Capacity", poolCapacity.ToString());
            AddRow(card, "Peak Active", peakActiveCount.ToString());
            AddRow(card, "Free Slots", freeCount.ToString());

            float activeRatio = poolCapacity > 0 ? Mathf.Clamp01((float)activeCount / poolCapacity) : 0f;
            float peakRatio = poolCapacity > 0 ? Mathf.Clamp01((float)peakActiveCount / poolCapacity) : 0f;
            card.Add(BuildUsageBar("Active Usage", activeRatio));
            card.Add(BuildUsageBar("Peak Usage", peakRatio));
        }

        private void BuildActiveTimerSample(VisualElement root)
        {
            VisualElement card = AddSection(root, "ACTIVE TIMER SAMPLE");

            TimerService.GetStatistics(out int activeCount, out _, out _, out _);
            if (activeCount <= 0)
            {
                card.Add(DebuggerUI.CreateHintLabel("No active timers."));
                return;
            }

            int timerCount = TimerService.GetAllTimers(_timerBuffer);
            if (activeCount > DISPLAY_COUNT)
            {
                card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("Showing first {0} of {1}.", timerCount, activeCount)));
            }

            for (int i = 0; i < timerCount; i++)
            {
                AddTimerRow(card, ref _timerBuffer[i]);
            }
        }

#if UNITY_EDITOR
        private void BuildStaleOneShotTimers(VisualElement root)
        {
            int staleCount = TimerService.GetStaleOneShotTimers(_staleBuffer);
            if (staleCount <= 0)
            {
                return;
            }

            VisualElement card = AddSection(root, "STALE ONE-SHOT TIMERS");
            VisualElement hint = DebuggerUI.CreateHintLabel("Long-lived one-shot timers detected (alive for more than 300s) — likely leaked by logic errors.");
            hint.AddToClassList("dbg-text--warning");
            card.Add(hint);

            for (int i = 0; i < staleCount; i++)
            {
                ref TimerDebugInfo info = ref _staleBuffer[i];
                AddRow(card, StringUtility.Format("ID {0}", info.TimerHandle),
                    StringUtility.Format("Alive {0:F1}s | Remaining {1:F2}s", info.Age, info.LeftTime));
            }
        }
#endif

        #endregion

        #region 私有 [PRIVATE]

        private static void AddTimerRow(VisualElement card, ref TimerDebugInfo info)
        {
            byte flags = info.Flags;
            string mode = (flags & TimerDebugFlags.LOOP) != 0 ? "Loop" : "One-shot";
            string scale = (flags & TimerDebugFlags.UNSCALED) != 0 ? "Unscaled" : "Scaled";
            bool running = (flags & TimerDebugFlags.RUNNING) != 0;
            string state = running ? "Running" : "Paused";
            string title = StringUtility.Format("ID {0} | {1} | {2}", info.TimerHandle, mode, scale);
            string value = StringUtility.Format("{0} | Remaining {1:F2}s | Duration {2:F2}s", state, info.LeftTime, info.Duration);
            AddRow(card, title, value);
        }

        private static VisualElement BuildUsageBar(string label, float ratio)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dbg-slider-row");
            row.style.marginBottom = 4f;

            Label titleLabel = new Label(label);
            titleLabel.AddToClassList("dbg-slider-row__title");

            VisualElement track = new VisualElement();
            track.AddToClassList("dbg-meter");
            track.style.overflow = Overflow.Hidden;

            VisualElement fill = new VisualElement();
            fill.AddToClassList("dbg-meter__fill");
            if (ratio > 0.9f)
            {
                fill.AddToClassList("dbg-meter__fill--danger");
            }

            fill.style.width = Length.Percent(Mathf.Clamp01(ratio) * 100f);
            track.Add(fill);

            Label valueLabel = new Label(StringUtility.Format("{0:P1}", ratio));
            valueLabel.AddToClassList("dbg-slider-value");
            valueLabel.style.minWidth = 54f;
            valueLabel.style.marginLeft = 8f;

            row.Add(titleLabel);
            row.Add(track);
            row.Add(valueLabel);
            return row;
        }

        #endregion
    }
}
