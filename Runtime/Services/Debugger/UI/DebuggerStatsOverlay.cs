using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;
using Unity.Profiling;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 常驻统计 HUD（右上角 FPS / 渲染 / 内存摘要）。
    /// <para><see cref="ProfilerRecorder"/> 按需启停（仅可见时运行）；0.25 秒节流刷新 + <see cref="StringBuilder"/> 复用，稳态零分配。</para>
    /// </summary>
    internal sealed class DebuggerStatsOverlay
    {
        #region 常量 [CONSTANTS]

        private const float REFRESH_INTERVAL = 0.25f;
        private const float MARGIN = 12f;
        private const float PANEL_WIDTH = 236f;

        #endregion

        #region 字段 [FIELDS]

        private readonly FpsCounter _fpsCounter;
        private readonly StringBuilder _builder = new StringBuilder(256);
        private VisualElement _root;
        private VisualElement _panel;
        private Label _bodyLabel;
        private bool _visible;
        private float _timeLeft;
        private ProfilerRecorder _trianglesRecorder;
        private ProfilerRecorder _drawCallsRecorder;
        private ProfilerRecorder _batchesRecorder;
        private ProfilerRecorder _setPassRecorder;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化统计 HUD 的新实例。
        /// </summary>
        /// <param name="fpsCounter">帧率采样器（宿主驱动更新）。</param>
        public DebuggerStatsOverlay(FpsCounter fpsCounter)
        {
            _fpsCounter = fpsCounter;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取或设置可见性（可见时启动渲染计数器）。
        /// </summary>
        public bool Visible
        {
            get
            {
                return _visible;
            }
            set
            {
                if (_visible == value)
                {
                    if (value)
                    {
                        RefreshNow();
                    }

                    return;
                }

                _visible = value;
                if (_visible)
                {
                    StartRecorders();
                    RefreshNow();
                }
                else
                {
                    StopRecorders();
                }

                ApplyVisibility();
            }
        }

        #endregion

        #region 挂载 [ATTACH]

        /// <summary>
        /// 挂载到宿主根元素（重复挂载幂等）。
        /// </summary>
        /// <param name="host">宿主根元素。</param>
        public void Attach(VisualElement host)
        {
            if (host == null || (_root != null && _root.parent == host))
            {
                return;
            }

            Detach();

            _root = new VisualElement();
            _root.name = "debugger-stats-overlay";
            _root.pickingMode = PickingMode.Ignore;
            _root.style.position = Position.Absolute;
            _root.style.left = 0f;
            _root.style.top = 0f;
            _root.style.right = 0f;
            _root.style.bottom = 0f;
            _root.style.flexGrow = 1f;

            _panel = new VisualElement();
            _panel.pickingMode = PickingMode.Ignore;
            _panel.AddToClassList("dbg-stats");
            _panel.style.top = MARGIN;
            _panel.style.right = MARGIN;
            _panel.style.width = PANEL_WIDTH;

            Label titleLabel = new Label("STATS");
            titleLabel.pickingMode = PickingMode.Ignore;
            titleLabel.AddToClassList("dbg-stats__title");
            titleLabel.style.marginBottom = 4f;

            _bodyLabel = new Label();
            _bodyLabel.pickingMode = PickingMode.Ignore;
            _bodyLabel.AddToClassList("dbg-stats__body");

            _panel.Add(titleLabel);
            _panel.Add(_bodyLabel);
            _root.Add(_panel);
            host.Add(_root);
            _root.BringToFront();

            ApplyVisibility();
            if (_visible)
            {
                StartRecorders();
                RefreshNow();
            }
        }

        /// <summary>
        /// 从宿主卸载。
        /// </summary>
        public void Detach()
        {
            if (_root == null)
            {
                return;
            }

            _root.RemoveFromHierarchy();
            _root = null;
            _panel = null;
            _bodyLabel = null;
        }

        /// <summary>
        /// 释放资源（停计数器并卸载）。
        /// </summary>
        public void Dispose()
        {
            Visible = false;
            StopRecorders();
            Detach();
        }

        #endregion

        #region 轮询 [TICK]

        /// <summary>
        /// 宿主逐帧驱动（节流刷新）。
        /// </summary>
        /// <param name="unscaledDeltaTime">真实流逝时间（以秒为单位）。</param>
        public void Tick(float unscaledDeltaTime)
        {
            if (!_visible || _bodyLabel == null)
            {
                return;
            }

            _timeLeft -= unscaledDeltaTime;
            if (_timeLeft > 0f)
            {
                return;
            }

            _timeLeft = REFRESH_INTERVAL;
            RefreshNow();
        }

        #endregion

        #region 私有 [PRIVATE]

        private void ApplyVisibility()
        {
            if (_root != null)
            {
                _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void RefreshNow()
        {
            if (_bodyLabel == null)
            {
                return;
            }

            float fps = _fpsCounter != null ? _fpsCounter.CurrentFps : 0f;
            float milliseconds = fps > 0.01f ? 1000f / fps : 0f;

            long triangles = ReadRecorder(_trianglesRecorder);
            long drawCalls = ReadRecorder(_drawCallsRecorder);
            long batches = ReadRecorder(_batchesRecorder);
            long setPass = ReadRecorder(_setPassRecorder);
            long monoUsed = Profiler.GetMonoUsedSizeLong();
            long totalAlloc = Profiler.GetTotalAllocatedMemoryLong();
            long gfxDriver = Profiler.GetAllocatedMemoryForGraphicsDriver();

            _builder.Clear();
            _builder.Append("FPS      ").Append(fps.ToString("F1")).Append("  (").Append(milliseconds.ToString("F1")).Append(" ms)\n");
            _builder.Append("Tris     ").Append(DebuggerUI.GetCompactCountString(triangles)).Append('\n');
            _builder.Append("Batches  ").Append(DebuggerUI.GetCompactCountString(batches)).Append('\n');
            _builder.Append("DrawCall ").Append(DebuggerUI.GetCompactCountString(drawCalls)).Append('\n');
            _builder.Append("SetPass  ").Append(DebuggerUI.GetCompactCountString(setPass)).Append('\n');
            _builder.Append("Mono     ").Append(DebuggerUI.GetCompactByteString(monoUsed)).Append('\n');
            _builder.Append("Alloc    ").Append(DebuggerUI.GetCompactByteString(totalAlloc)).Append('\n');
            _builder.Append("GfxDrv   ").Append(DebuggerUI.GetCompactByteString(gfxDriver));

            _bodyLabel.text = _builder.ToString();
        }

        private void StartRecorders()
        {
            if (_trianglesRecorder.Valid)
            {
                return;
            }

            _trianglesRecorder = CreateRecorder("Triangles Count");
            _drawCallsRecorder = CreateRecorder("Draw Calls Count");
            _batchesRecorder = CreateRecorder("Batches Count");
            _setPassRecorder = CreateRecorder("SetPass Calls Count");
        }

        private void StopRecorders()
        {
            DisposeRecorder(ref _trianglesRecorder);
            DisposeRecorder(ref _drawCallsRecorder);
            DisposeRecorder(ref _batchesRecorder);
            DisposeRecorder(ref _setPassRecorder);
        }

        private static ProfilerRecorder CreateRecorder(string markerName)
        {
            try
            {
                return ProfilerRecorder.StartNew(ProfilerCategory.Render, markerName);
            }
            catch
            {
                // 剥离构建下统计标记可能缺失——HUD 容忍该计数器不可用（显示 n/a），不中断调试器。
                return default;
            }
        }

        private static void DisposeRecorder(ref ProfilerRecorder recorder)
        {
            if (recorder.Valid)
            {
                recorder.Dispose();
            }

            recorder = default;
        }

        private static long ReadRecorder(ProfilerRecorder recorder)
        {
            if (!recorder.Valid || !recorder.IsRunning)
            {
                return -1L;
            }

            return recorder.LastValue < 0L ? 0L : recorder.LastValue;
        }

        #endregion
    }
}
