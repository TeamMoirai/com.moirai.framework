using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityPlayerLoop = UnityEngine.LowLevel.PlayerLoop;

namespace Moirai.Atropos.Editor.PlayerLoopDebug
{
    /// <summary>
    /// PlayerLoop 结构可视化（Odin 实现）：注入状态、三阶段订阅统计，以及可折叠 / 可过滤的循环树。
    /// <para>树上分色标出本框架的三个标记，与"挂着委托却不是 Moirai"的第三方 Pump（UniTask 等）——
    /// 关闭流程只逐项摘自己、正是为了保住后者，所以它是排查停摆时第一眼要看的东西。</para>
    /// <para>菜单：Window → PlayerLoop Debugger</para>
    /// </summary>
    public sealed class PlayerLoopDebuggerWindow : OdinEditorWindow
    {
        #region 常量 [CONSTANTS]

        private const string SESSION_AUTO_REFRESH = "Moirai.PlayerLoopDebugger.AutoRefresh";
        private const string SESSION_AUTO_INTERVAL = "Moirai.PlayerLoopDebugger.AutoInterval";

        /// <summary>自动刷新间隔下限（秒）。</summary>
        private const double MIN_AUTO_INTERVAL = 0.1d;

        /// <summary>首刷展开到的深度：根（0）与顶层相位（1）可见，更深起手折叠。</summary>
        private const int DEFAULT_EXPAND_DEPTH = 1;

        private const float INDENT = 12f;
        private const float FOLDOUT_WIDTH = 15f;
        private const float BADGE_WIDTH = 48f;

        private static readonly Color s_MoiraiColor = new Color(0.32f, 0.72f, 0.42f);
        private static readonly Color s_PumpColor = new Color(0.30f, 0.55f, 0.92f);
        private static readonly Color s_MissingColor = new Color(0.92f, 0.60f, 0.22f);
        private static readonly Color s_NullTypeColor = new Color(0.58f, 0.58f, 0.58f);
        private static readonly Color s_BadgeColor = new Color(0.60f, 0.60f, 0.60f);

        #endregion

        #region 数据模型 [MODELS]

        /// <summary>
        /// 循环树节点。整树在每次刷新时重建，展开状态按 <see cref="Path"/> 跨刷新保留。
        /// </summary>
        private sealed class Node
        {
            public Node Parent;
            public string Name;
            public GUIContent Content;
            public string Path;
            public int Depth;
            public int Index;
            public bool HasDelegate;
            public bool IsLoop;
            public bool IsMoirai;
            public bool Matched;
            public bool Visible;
            public bool Expanded;
            public List<Node> Children;

            public bool HasChildren => Children != null && Children.Count > 0;

            /// <summary>挂着委托却不是 Moirai 标记 —— 第三方注入的 Pump。</summary>
            public bool IsThirdPartyPump => HasDelegate && !IsMoirai;
        }

        /// <summary>阶段统计表行（列头取成员名，勿加 LabelText——Odin 4 会渲染成行内前缀标签导致列错位）。</summary>
        private sealed class StageRow
        {
            [TableColumnWidth(96, false)]
            public string Stage;

            [TableColumnWidth(250, false)]
            public string Phase;

            [TableColumnWidth(80, false)]
            public string Handlers;

            [TableColumnWidth(80, false)]
            public string Callbacks;

            [TableColumnWidth(96, false), GUIColor(nameof(PresentColor))]
            public string Present;

            [HideInTables]
            public bool PresentIsSet;

            private Color PresentColor => PresentIsSet ? s_MoiraiColor : s_MissingColor;
        }

        #endregion

        #region 字段 [FIELDS]

        private static GUIStyle s_RowStyle;
        private static GUIStyle s_MatchedRowStyle;

        private Vector2 _scroll;
        private Node _root;
        private Node _updateMarker;
        private Node _fixedMarker;
        private Node _lateMarker;

        private readonly Dictionary<string, bool> _expandedByPath = new Dictionary<string, bool>();
        private readonly List<StageRow> _stages = new List<StageRow>();

        private int _totalSystems;
        private int _pumpCount;
        private int _visibleCount;
        private double _nextAutoRefreshTime;

        [BoxGroup("过滤"), ShowInInspector, HideLabel, Delayed,
            PropertyTooltip("按类型名子串过滤，留空显示全树"), OnValueChanged(nameof(RecomputeVisibility))]
        private string _filter = string.Empty;

        [BoxGroup("过滤"), ToggleLeft, LabelText("仅显示注入点（Moirai 与第三方 Pump）"), OnValueChanged(nameof(RecomputeVisibility))]
        private bool _markersOnly;

        [BoxGroup("刷新"), ToggleLeft, LabelText("自动刷新"), OnValueChanged(nameof(SaveSessionState))]
        private bool _autoRefresh;

        [BoxGroup("刷新"), ShowIf(nameof(_autoRefresh)), Delayed, LabelText("间隔（秒）"), OnValueChanged(nameof(SaveSessionState))]
        private double _autoInterval = 0.5d;

        #endregion

        #region 打开与生命周期 [LIFECYCLE]

        [MenuItem("Window/PlayerLoop Debugger")]
        public static void OpenWindow()
        {
            GetWindow<PlayerLoopDebuggerWindow>("PlayerLoop Debugger").Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            titleContent = new GUIContent("PlayerLoop Debugger");
            minSize = new Vector2(640f, 420f);

            _autoRefresh = SessionState.GetBool(SESSION_AUTO_REFRESH, false);
            _autoInterval = Math.Max(MIN_AUTO_INTERVAL, SessionState.GetFloat(SESSION_AUTO_INTERVAL, 0.5f));

            Refresh();
            EditorApplication.update += OnEditorUpdate;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= OnEditorUpdate;
            SaveSessionState();
        }

        private void OnEditorUpdate()
        {
            if (!_autoRefresh) return;

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextAutoRefreshTime) return;

            _nextAutoRefreshTime = now + Math.Max(MIN_AUTO_INTERVAL, _autoInterval);
            Refresh();
            Repaint();
        }

        private void SaveSessionState()
        {
            SessionState.SetBool(SESSION_AUTO_REFRESH, _autoRefresh);
            SessionState.SetFloat(SESSION_AUTO_INTERVAL, (float)_autoInterval);
        }

        #endregion

        #region Odin 面板 [ODIN PANEL]

        [BoxGroup("状态"), InfoBox("$StatusHint", InfoMessageType.Warning, nameof(HasStatusHint)),
            ShowInInspector, ReadOnly, LabelText("注入 [Injected]")]
        private string InjectionLabel =>
            $"{MarkerCount}/3 标记在位 · IsInjected={PlayerLoopInjector.IsInjected}";

        [BoxGroup("状态"), ShowInInspector, ReadOnly, LabelText("驱动 [Driver]"), GUIColor(nameof(DriverColor))]
        private string DriverLabel => PlayerLoopDriver.IsShutdown ? "Shutdown" : "Driving";

        [BoxGroup("状态"), ShowInInspector, ReadOnly, LabelText("模式 [Mode]")]
        private string ModeLabel => EditorApplication.isPlaying ? "Play" : "Edit";

        [BoxGroup("状态"), ShowInInspector, ReadOnly, LabelText("帧时钟 [GameTime]")]
        private string GameClockLabel =>
            $"frame {GameTime.frameCount} · Δ {GameTime.deltaTime:0.000} · time {GameTime.time:0.0}";

        [BoxGroup("状态"), ShowInInspector, ReadOnly, LabelText("引擎时钟 [Time]")]
        private string EngineClockLabel => $"frame {Time.frameCount} · Δ {Time.deltaTime:0.000}";

        [BoxGroup("状态"), ShowInInspector, ReadOnly, LabelText("循环规模 [Systems]")]
        private string SystemCountLabel => $"{_totalSystems} 个系统 · 第三方 Pump {_pumpCount} 个";

        [BoxGroup("阶段"), ShowInInspector, HideLabel, TableList(
            AlwaysExpanded = true, IsReadOnly = true, ShowIndexLabels = false,
            MinScrollViewHeight = 82, MaxScrollViewHeight = 150)]
        private List<StageRow> Stages => _stages;

        private int MarkerCount =>
            (_updateMarker != null ? 1 : 0) + (_fixedMarker != null ? 1 : 0) + (_lateMarker != null ? 1 : 0);

        private bool HasStatusHint => !PlayerLoopInjector.IsInjected || MarkerCount < 3;

        private Color DriverColor => PlayerLoopDriver.IsShutdown ? s_MissingColor : s_MoiraiColor;

        private string StatusHint
        {
            get
            {
                int found = MarkerCount;
                if (found == 0) return "当前循环里没有 Moirai 标记：框架未启动，或第三方基于默认循环重建了 PlayerLoop。";
                if (found < 3) return "Moirai 标记不完整：某一阶段被抹掉，按下表定位后 Reinject。";
                if (!PlayerLoopInjector.IsInjected) return "三个标记都在位，但驱动未标记为已注入——通常是框架已 Shutdown。";
                return string.Empty;
            }
        }

        [Button("Refresh"), ButtonGroup("注入操作"), PropertyOrder(1)]
        private void RefreshClicked()
        {
            Refresh();
            Repaint();
        }

        [Button("Ensure Injected"), ButtonGroup("注入操作"), GUIColor(0.36f, 0.68f, 1.00f)]
        private void EnsureInjectedClicked()
        {
            PlayerLoopInjector.EnsureInjected();
            Refresh();
        }

        [Button("Reinject"), ButtonGroup("注入操作"), GUIColor(0.36f, 0.68f, 1.00f)]
        private void ReinjectClicked()
        {
            PlayerLoopInjector.Reinject();
            Refresh();
        }

        [Button("Restore Default"), ButtonGroup("注入操作"), GUIColor(0.92f, 0.60f, 0.22f)]
        private void RestoreDefaultClicked()
        {
            PlayerLoopInjector.RestoreDefault();
            Refresh();
        }

        [Button("Expand All"), ButtonGroup("树操作"), PropertyOrder(2)]
        private void ExpandAll() => SetAllExpanded(true);

        [Button("Collapse All"), ButtonGroup("树操作")]
        private void CollapseAll() => SetAllExpanded(false);

        [Button("Locate Moirai"), ButtonGroup("树操作"), GUIColor(0.32f, 0.72f, 0.42f)]
        private void LocateMoirai()
        {
            _filter = string.Empty;
            _markersOnly = false;
            RecomputeVisibility();
            ApplyExpanded(_root, false);

            ExpandAncestorsOf(_updateMarker);
            ExpandAncestorsOf(_fixedMarker);
            ExpandAncestorsOf(_lateMarker);
            Repaint();
        }

        #endregion

        #region 刷新 [REFRESH]

        /// <summary>重建循环树、可见性标记与阶段统计；展开状态按路径保留。</summary>
        private void Refresh()
        {
            _totalSystems = 0;
            _pumpCount = 0;
            _updateMarker = null;
            _fixedMarker = null;
            _lateMarker = null;

            _root = Build(null, 0, 0, UnityPlayerLoop.GetCurrentPlayerLoop());
            RecomputeVisibility();
            BuildStages();
        }

        private Node Build(Node parent, int depth, int index, PlayerLoopSystem system)
        {
            _totalSystems++;

            string name = system.type != null ? system.type.FullName ?? system.type.Name : "(null)";
            bool isMoirai = system.type == typeof(PlayerLoopInjector.MoiraiUpdate)
                            || system.type == typeof(PlayerLoopInjector.MoiraiFixedUpdate)
                            || system.type == typeof(PlayerLoopInjector.MoiraiLateUpdate);
            bool hasDelegate = system.updateDelegate != null;

            if (hasDelegate && !isMoirai) _pumpCount++;

            var node = new Node
            {
                Parent = parent,
                Name = name,
                Content = new GUIContent(ShortName(name), name),
                Path = parent == null ? name : parent.Path + "/" + index + ":" + name,
                Depth = depth,
                Index = index,
                HasDelegate = hasDelegate,
                IsLoop = system.loopConditionFunction != null,
                IsMoirai = isMoirai,
            };
            node.Expanded = _expandedByPath.TryGetValue(node.Path, out bool saved)
                ? saved
                : depth <= DEFAULT_EXPAND_DEPTH;

            if (isMoirai)
            {
                if (system.type == typeof(PlayerLoopInjector.MoiraiUpdate)) _updateMarker = node;
                else if (system.type == typeof(PlayerLoopInjector.MoiraiFixedUpdate)) _fixedMarker = node;
                else _lateMarker = node;
            }

            PlayerLoopSystem[] children = system.subSystemList;
            if (children != null && children.Length > 0)
            {
                node.Children = new List<Node>(children.Length);
                for (int i = 0; i < children.Length; i++)
                {
                    node.Children.Add(Build(node, depth + 1, i, children[i]));
                }
            }

            return node;
        }

        /// <summary>按循环实况重建三个阶段行：标记在位与否、其在上游相位列表中的下标，以及注册表计数。</summary>
        private void BuildStages()
        {
            _stages.Clear();
            AddStage("Update", "UnityEngine.PlayerLoop.Update", _updateMarker,
                PlayerLoopDriver.UpdateHandlerCount, PlayerLoopDriver.UpdateCallbackCount);
            AddStage("FixedUpdate", "UnityEngine.PlayerLoop.FixedUpdate", _fixedMarker,
                PlayerLoopDriver.FixedUpdateHandlerCount, PlayerLoopDriver.FixedUpdateCallbackCount);
            AddStage("LateUpdate", "UnityEngine.PlayerLoop.PreLateUpdate", _lateMarker,
                PlayerLoopDriver.LateUpdateHandlerCount, PlayerLoopDriver.LateUpdateCallbackCount);
        }

        private void AddStage(
            string stage, string phase, Node marker, int handlers, int callbacks)
        {
            _stages.Add(new StageRow
            {
                Stage = stage,
                Phase = phase,
                Handlers = handlers.ToString(),
                Callbacks = callbacks.ToString(),
                Present = marker == null ? "缺失" : $"在位 #{marker.Index}",
                PresentIsSet = marker != null,
            });
        }

        private static string ShortName(string fullName)
        {
            int last = fullName.LastIndexOf('.');
            return last >= 0 && last + 1 < fullName.Length ? fullName.Substring(last + 1) : fullName;
        }

        #endregion

        #region 过滤与展开 [FILTER & EXPANSION]

        /// <summary>自底向上标记子树可见性；过滤词命中时顺带展开命中项的祖先。</summary>
        private void RecomputeVisibility()
        {
            _visibleCount = 0;
            MarkVisible(_root);

            if (string.IsNullOrEmpty(_filter)) return;

            ExpandMatched(_root);
        }

        private bool MarkVisible(Node node)
        {
            if (node == null) return false;

            node.Matched = !string.IsNullOrEmpty(_filter)
                           && node.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;

            bool passes = node.Matched || string.IsNullOrEmpty(_filter);
            if (passes && _markersOnly && !node.HasDelegate) passes = false;

            List<Node> children = node.Children;
            bool anyChild = false;
            if (children != null)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    anyChild |= MarkVisible(children[i]);
                }
            }

            node.Visible = passes || anyChild;
            if (node.Visible) _visibleCount++;
            return node.Visible;
        }

        private bool ExpandMatched(Node node)
        {
            if (node == null) return false;

            bool descendantMatched = false;
            List<Node> children = node.Children;
            if (children != null)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    descendantMatched |= ExpandMatched(children[i]);
                }
            }

            if (descendantMatched) SetExpanded(node, true);
            return node.Matched || descendantMatched;
        }

        private void SetExpanded(Node node, bool expanded)
        {
            node.Expanded = expanded;
            _expandedByPath[node.Path] = expanded;
        }

        private void SetAllExpanded(bool expanded)
        {
            ApplyExpanded(_root, expanded);
            Repaint();
        }

        private void ApplyExpanded(Node node, bool expanded)
        {
            if (node == null) return;

            SetExpanded(node, expanded);
            List<Node> children = node.Children;
            if (children == null) return;

            for (int i = 0; i < children.Count; i++)
            {
                ApplyExpanded(children[i], expanded);
            }
        }

        private void ExpandAncestorsOf(Node node)
        {
            for (Node p = node?.Parent; p != null; p = p.Parent)
            {
                SetExpanded(p, true);
            }

            if (node != null) SetExpanded(node, false);
        }

        #endregion

        #region 循环树绘制 [TREE GUI]

        /// <summary>Odin 成员全部绘制完之后接循环树：表头一行 + 滚动区内的节点树。</summary>
        protected override void OnEndDrawEditors()
        {
            base.OnEndDrawEditors();

            if (_root == null) return;

            InitStyles();
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("当前 PlayerLoop 结构", EditorStyles.boldLabel);
                GUILayout.Label($"匹配 {_visibleCount}/{_totalSystems}", EditorStyles.miniLabel, GUILayout.Width(96));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.miniButtonLeft, GUILayout.Width(58))) Refresh();
                if (GUILayout.Button("Expand", EditorStyles.miniButtonMid, GUILayout.Width(56))) SetAllExpanded(true);
                if (GUILayout.Button("Collapse", EditorStyles.miniButtonRight, GUILayout.Width(62))) SetAllExpanded(false);
            }

            Rect header = GUILayoutUtility.GetLastRect();
            float height = Mathf.Max(120f, position.height - header.yMax - 8f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(height));
            DrawNode(_root);
            EditorGUILayout.EndScrollView();
        }

        private void DrawNode(Node node)
        {
            if (!node.Visible) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(node.Depth * INDENT);

                if (node.HasChildren)
                {
                    Rect toggle = GUILayoutUtility.GetRect(FOLDOUT_WIDTH, EditorGUIUtility.singleLineHeight);
                    if (GUI.Toggle(toggle, node.Expanded, GUIContent.none, EditorStyles.foldout) != node.Expanded)
                    {
                        SetExpanded(node, !node.Expanded);
                    }
                }
                else
                {
                    GUILayout.Space(FOLDOUT_WIDTH);
                }

                DrawLabel(node);
                DrawBadge(node);
                GUILayout.FlexibleSpace();
            }

            if (!node.Expanded || !node.HasChildren) return;

            List<Node> children = node.Children;
            for (int i = 0; i < children.Count; i++)
            {
                DrawNode(children[i]);
            }
        }

        private void DrawLabel(Node node)
        {
            Color color = node.IsMoirai ? s_MoiraiColor
                : node.IsThirdPartyPump ? s_PumpColor
                : node.Name == "(null)" ? s_NullTypeColor
                : GUI.contentColor;

            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(node.Content, node.Matched ? s_MatchedRowStyle : s_RowStyle);
            GUI.contentColor = previous;
        }

        private void DrawBadge(Node node)
        {
            string badge = node.IsMoirai ? "Moirai"
                : node.IsThirdPartyPump ? "Pump"
                : node.IsLoop ? "loop"
                : null;

            if (badge == null) return;

            Color previous = GUI.contentColor;
            GUI.contentColor = node.IsMoirai ? s_MoiraiColor
                : node.IsThirdPartyPump ? s_PumpColor
                : s_BadgeColor;
            GUILayout.Label(badge, EditorStyles.miniLabel, GUILayout.Width(BADGE_WIDTH));
            GUI.contentColor = previous;
        }

        private static void InitStyles()
        {
            if (s_RowStyle != null) return;

            s_RowStyle = new GUIStyle(EditorStyles.label);
            s_MatchedRowStyle = new GUIStyle(EditorStyles.boldLabel);
        }

        #endregion
    }
}
