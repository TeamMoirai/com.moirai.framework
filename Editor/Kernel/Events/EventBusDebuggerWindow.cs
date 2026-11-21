using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 事件总线调试窗（Odin 实现，Window/Event Debugger）：经 <see cref="EventBusDiagnostics.OnPublished"/> 钩子观察事件派发流。
    /// </summary>
    /// <remarks>
    /// 内核级事件总线不设运行时调试接口——诊断钩子与调试窗同生命周期、仅编辑器存在（<c>#if UNITY_EDITOR</c>），
    /// 独立窗口，不挂 Debugger 服务的窗口注册表。
    /// 面板形态对齐 <see cref="Moirai.Atropos.PlayerLoopDebug.PlayerLoopDebuggerWindow"/>：控制条与状态区由 Odin 特性渲染，
    /// 事件流列表与参数详情在 <see cref="OnEndDrawEditors"/> 自绘——行可点选（[R] 标记回放条目并着色），
    /// 参数经 <see cref="PropertyTree.Create(object)"/> 以 Odin 检视器渲染 struct 快照（类型感知、只读）。 <br />
    /// 重播走 <c>EventBus.Publish&lt;T&gt;</c> 反射闭合泛型，走的是真实发布路径（会再次触发订阅与诊断钩子）；
    /// struct 快照在钩子处装箱一次、重播时拆箱还原，编辑器工具开销不进运行时。
    /// </remarks>
    internal sealed class EventBusDebuggerWindow : OdinEditorWindow
    {
        #region 常量 [CONSTANTS]

        private const int MaxFlowEntries = 64;

        /// <summary>列表刷新节流间隔（秒）。</summary>
        private const double RefreshInterval = 0.25;

        /// <!-- 控制 -->
        private const string CONTROL_GROUP = "控制 [Controls]";

        /// <!-- 状态 -->
        private const string STATUS_GROUP = "状态 [Status]";

        private static readonly Color s_ReplayColor = new Color(0.92f, 0.60f, 0.22f);
        private static readonly Color s_SelectedRowColor = new Color(0.36f, 0.68f, 1.00f, 0.35f);

        #endregion

        #region 数据模型 [MODELS]

        /// <summary>
        /// 事件流条目：序号 + 事件类型 + 装箱快照 + 记录时刻 + 是否回放产生。
        /// </summary>
        private readonly struct FlowEntry
        {
            public readonly int Sequence;
            public readonly Type EventType;
            public readonly object Snapshot;
            public readonly double Time;
            public readonly bool IsReplay;

            public FlowEntry(int sequence, Type eventType, object snapshot, double time, bool isReplay)
            {
                Sequence = sequence;
                EventType = eventType;
                Snapshot = snapshot;
                Time = time;
                IsReplay = isReplay;
            }
        }

        #endregion

        #region 字段 [FIELDS]

        private static GUIStyle s_RowStyle;
        private static GUIStyle s_SelectedRowStyle;

        [NonSerialized] private readonly RingBuffer<FlowEntry> _flow = new RingBuffer<FlowEntry>(MaxFlowEntries);
        [NonSerialized] private readonly List<FlowEntry> _replayQueue = new List<FlowEntry>();
        [NonSerialized] private readonly List<FlowEntry> _flowSnapshot = new List<FlowEntry>();
        [NonSerialized] private readonly object _flowLock = new object();
        [NonSerialized] private int _sequence;
        [NonSerialized] private FlowEntry? _selected;
        [NonSerialized] private PropertyTree _detailTree;
        [NonSerialized] private Vector2 _flowScroll;
        [NonSerialized] private Vector2 _detailScroll;
        [NonSerialized] private double _lastRefresh;
        [NonSerialized] private bool _dirty;
        [NonSerialized] private int _replayIndex = -1;
        [NonSerialized] private double _replayNextTime;

        [ShowInInspector, PropertyOrder(0), LabelText("倍速 [Speed]"), ValueDropdown(nameof(SpeedChoices)),
            Tooltip("整段回放倍速：按记录时间差 ÷ 倍速推进，间隔钳制 [0, 2s]")]
        private float _replaySpeed = 1f;

        #endregion

        #region 打开与生命周期 [LIFECYCLE]

        [MenuItem("Window/Event Debugger")]
        public static void Open()
        {
            GetWindow<EventBusDebuggerWindow>("Event Bus Debugger").Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            titleContent = new GUIContent("Event Bus Debugger");
            minSize = new Vector2(620f, 400f);

            // 会话状态一律不跨域保留，统一清零杜绝脏读。
            _flow.Clear();
            _replayQueue.Clear();
            _selected = null;
            _detailTree = null;
            _sequence = 0;
            _replayIndex = -1;
            _replayNextTime = 0d;
            _dirty = true;

            EventBusDiagnostics.OnPublished += OnPublishedHook;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EventBusDiagnostics.OnPublished -= OnPublishedHook;
            StopReplay();
            DisposeDetailTree();
        }

        private void Update()
        {
            // 刷新节流：钩子高频触发时也至多每 0.25s 重绘一次
            if (_dirty && EditorApplication.timeSinceStartup - _lastRefresh >= RefreshInterval)
            {
                _lastRefresh = EditorApplication.timeSinceStartup;
                _dirty = false;
                Repaint();
            }

            if (_replayIndex >= 0)
            {
                PumpReplay();
                _dirty = true;
            }
        }

        #endregion

        #region Odin 面板 [ODIN PANEL]

        [BoxGroup(CONTROL_GROUP), HorizontalGroup(CONTROL_GROUP + "/行"), Button("重播选中"),
            PropertyOrder(1), GUIColor(0.36f, 0.68f, 1.00f), EnableIf(nameof(HasSelection))]
        private void ReplaySelectedClicked() => ReplaySelected();

        [BoxGroup(CONTROL_GROUP), HorizontalGroup(CONTROL_GROUP + "/行"), Button("重播全部"),
            PropertyOrder(2), GUIColor(0.32f, 0.72f, 0.42f), EnableIf(nameof(HasEntries))]
        private void ReplayAllClicked() => ReplayAll();

        [BoxGroup(CONTROL_GROUP), HorizontalGroup(CONTROL_GROUP + "/行"), Button("停止"),
            PropertyOrder(3), GUIColor(0.92f, 0.60f, 0.22f), EnableIf(nameof(IsReplaying))]
        private void StopClicked() => StopReplay();

        [BoxGroup(CONTROL_GROUP), HorizontalGroup(CONTROL_GROUP + "/行"), Button("清空"),
            PropertyOrder(4), GUIColor(0.92f, 0.36f, 0.36f)]
        private void ClearClicked() => ClearAll();

        [BoxGroup(STATUS_GROUP), ShowInInspector, ReadOnly, LabelText("事件流 [Entries]")]
        private string EntriesLabel => $"{_flow.Count} / {MaxFlowEntries}";

        [BoxGroup(STATUS_GROUP), ShowInInspector, ReadOnly, LabelText("回放 [Replay]"),
            GUIColor(nameof(ReplayStatusColor))]
        private string ReplayLabel => _replayIndex < 0 ? "空闲" : $"第 {_replayIndex + 1} / {_replayQueue.Count} 条 …";

        [BoxGroup(STATUS_GROUP), ShowInInspector, ReadOnly, LabelText("选中 [Selected]")]
        private string SelectedLabel =>
            _selected == null ? "（未选中）" : $"#{_selected.Value.Sequence} {_selected.Value.EventType.Name}";

        private bool HasSelection => _selected != null;

        private bool HasEntries => _flow.Count > 0;

        private bool IsReplaying => _replayIndex >= 0;

        private Color ReplayStatusColor => _replayIndex >= 0 ? s_ReplayColor : GUI.contentColor;

        private static IEnumerable SpeedChoices => new[]
        {
            new ValueDropdownItem<float>("0.1x", 0.1f),
            new ValueDropdownItem<float>("0.25x", 0.25f),
            new ValueDropdownItem<float>("0.5x", 0.5f),
            new ValueDropdownItem<float>("1x", 1f),
            new ValueDropdownItem<float>("2x", 2f),
            new ValueDropdownItem<float>("4x", 4f),
        };

        #endregion

        #region 事件流与详情 [FLOW AND DETAIL]

        /// <summary>
        /// Odin 成员全部绘制完之后接事件流区：头行（计数）+ 左列事件行 / 右列参数详情。
        /// </summary>
        protected override void OnEndDrawEditors()
        {
            base.OnEndDrawEditors();

            if (EditorStyles.label == null) return; // 域重载早期样式未就绪，跳过本帧

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("事件流 [Flow]", EditorStyles.boldLabel);
                GUILayout.Label($"{_flow.Count} / {MaxFlowEntries}", EditorStyles.miniLabel, GUILayout.Width(64f));
                GUILayout.FlexibleSpace();
            }

            Rect header = GUILayoutUtility.GetLastRect();
            float height = Mathf.Max(120f, position.height - header.yMax - 10f);
            float listWidth = Mathf.Clamp(position.width * 0.4f, 220f, 340f);

            using (new EditorGUILayout.HorizontalScope())
            {
                _flowScroll = EditorGUILayout.BeginScrollView(
                    _flowScroll, GUILayout.Width(listWidth), GUILayout.Height(height));
                DrawFlowRows();
                EditorGUILayout.EndScrollView();

                GUILayout.Space(8f);

                _detailScroll = EditorGUILayout.BeginScrollView(
                    _detailScroll, GUILayout.ExpandWidth(true), GUILayout.Height(height));
                DrawDetail();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawFlowRows()
        {
            if (_flow.Count == 0)
            {
                GUILayout.Label("尚无事件——发布事件后此处滚动记录。", EditorStyles.miniLabel);
                return;
            }

            InitRowStyles();

            // 发布可能来自非主线程：锁内快照再绘制（复用列表，稳态零分配）
            List<FlowEntry> rows;
            lock (_flowLock)
            {
                _flowSnapshot.Clear();
                for (int i = 0; i < _flow.Count; i++)
                    _flowSnapshot.Add(_flow[i]);
                rows = _flowSnapshot;
            }

            for (int i = rows.Count - 1; i >= 0; i--)
            {
                FlowEntry entry = rows[i];
                if (entry.EventType == null) continue; // 防御：脏条目直接跳过，不让单条脏数据引发逐帧异常刷屏

                bool selected = _selected != null && _selected.Value.Sequence == entry.Sequence;

                Color previousBackground = GUI.backgroundColor;
                Color previousContent = GUI.contentColor;
                if (selected)
                    GUI.backgroundColor = s_SelectedRowColor;
                else if (entry.IsReplay)
                    GUI.contentColor = s_ReplayColor;

                string text = (entry.IsReplay ? "[R] " : string.Empty)
                              + string.Format("#{0}  {1}", entry.Sequence, entry.EventType.Name);
                if (GUILayout.Button(text, selected ? s_SelectedRowStyle : s_RowStyle))
                    SelectEntry(entry);

                GUI.backgroundColor = previousBackground;
                GUI.contentColor = previousContent;
            }
        }

        private void DrawDetail()
        {
            if (_selected == null)
            {
                GUILayout.Label("点选左侧事件行查看参数详情。", EditorStyles.miniLabel);
                return;
            }

            FlowEntry entry = _selected.Value;

            GUILayout.Label(entry.EventType.FullName ?? entry.EventType.Name, EditorStyles.boldLabel);
            GUILayout.Label(
                string.Format("#{0} · t+{1:0.0}s · {2}", entry.Sequence, entry.Time, entry.IsReplay ? "回放条目" : "发布条目"),
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4f);

            if (entry.Snapshot == null)
            {
                GUILayout.Label("（默认 / 空事件）", EditorStyles.miniLabel);
                return;
            }

            if (!HasPublicMembers(entry.Snapshot.GetType()))
            {
                GUILayout.Label("（无公开字段或属性）", EditorStyles.miniLabel);
                return;
            }

            EnsureDetailTree(entry.Snapshot);
            _detailTree?.Draw();
        }

        private void SelectEntry(FlowEntry entry)
        {
            if (_selected != null && _selected.Value.Sequence == entry.Sequence) return;

            _selected = entry;
            DisposeDetailTree();
            Repaint();
        }

        /// <summary>
        /// 以 Odin 检视器建树渲染快照；只读——快照是重播数据源，禁止经检视器改写。
        /// </summary>
        private void EnsureDetailTree(object snapshot)
        {
            if (_detailTree != null) return;

            _detailTree = PropertyTree.Create(snapshot);
            foreach (InspectorProperty property in _detailTree.EnumerateTree())
                property.State.Enabled = false;
        }

        private void DisposeDetailTree()
        {
            if (_detailTree == null) return;

            _detailTree.Dispose();
            _detailTree = null;
        }

        private static bool HasPublicMembers(Type type)
        {
            MemberInfo[] members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is FieldInfo) return true;
                if (members[i] is PropertyInfo property
                    && property.GetIndexParameters().Length == 0 && property.CanRead)
                    return true;
            }

            return false;
        }

        private static void InitRowStyles()
        {
            if (s_RowStyle != null) return;

            s_RowStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Normal,
            };
            s_SelectedRowStyle = new GUIStyle(s_RowStyle) { fontStyle = FontStyle.Bold };
        }

        #endregion

        #region 重播 [REPLAY]

        private void ReplaySelected()
        {
            if (_selected == null) return;
            PublishSnapshot(_selected.Value);
        }

        private void ReplayAll()
        {
            if (_flow.Count == 0) return;

            // 按序号升序重放整段（含环形缓冲内现存条目）
            var ordered = new List<FlowEntry>();
            for (int i = 0; i < _flow.Count; i++)
                ordered.Add(_flow[i]);
            ordered.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

            _replayQueue.Clear();
            _replayQueue.AddRange(ordered);
            _replayIndex = 0;
            _replayNextTime = EditorApplication.timeSinceStartup;
        }

        private void PumpReplay()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _replayNextTime) return;

            // 按记录时间差 × 倍速推进；首条立即、间隔钳制在 [0, 2s]
            double delay = 0;
            if (_replayIndex > 0 && _replayIndex < _replayQueue.Count)
            {
                double span = _replayQueue[_replayIndex].Time - _replayQueue[_replayIndex - 1].Time;
                delay = Math.Max(0, Math.Min(2.0, span / Math.Max(0.01f, _replaySpeed)));
            }

            if (now - _replayNextTime < delay && _replayIndex > 0)
                return;

            PublishSnapshot(_replayQueue[_replayIndex]);

            _replayIndex++;
            if (_replayIndex >= _replayQueue.Count)
            {
                StopReplay();
                return;
            }
            _replayNextTime = now + Math.Max(0, Math.Min(2.0,
                (_replayQueue[_replayIndex].Time - _replayQueue[_replayIndex - 1].Time) / Math.Max(0.01f, _replaySpeed)));
        }

        /// <summary>
        /// 反射闭合 <c>EventBus.Publish&lt;TEvent&gt;(in TEvent, EDispatchMode)</c> 并重放快照。
        /// </summary>
        private static void PublishSnapshot(FlowEntry entry)
        {
            if (entry.Snapshot == null) return;

            MethodInfo publish = null;
            foreach (MethodInfo method in typeof(EventBus).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "Publish" || !method.IsGenericMethodDefinition) continue;
                ParameterInfo[] ps = method.GetParameters();
                // 带参重载：Publish<TEvent>(in TEvent evt, EDispatchMode mode)；无参重载（Publish<T>()）跳过
                if (ps.Length == 2 && ps[0].ParameterType.IsByRef && ps[0].Name == "evt")
                {
                    publish = method;
                    break;
                }
            }

            if (publish == null)
            {
                Debug.LogWarning("[EventBusDebugger] 未找到 EventBus.Publish<TEvent>(in TEvent, EDispatchMode)，重播跳过。");
                return;
            }

            try
            {
                MethodInfo closed = publish.MakeGenericMethod(entry.EventType);
                object[] args = { entry.Snapshot, EDispatchMode.Immediate };
                closed.Invoke(null, args);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EventBusDebugger] 重播 " + entry.EventType.Name + " 失败：" + e.GetType().Name + " " + e.Message);
            }
        }

        private void StopReplay()
        {
            _replayIndex = -1;
            _replayQueue.Clear();
        }

        private void ClearAll()
        {
            StopReplay();
            _flow.Clear();
            _selected = null;
            DisposeDetailTree();
            _dirty = true;
        }

        #endregion

        #region 钩子 [HOOKS]

        private void OnPublishedHook(Type eventType, object snapshot)
        {
            bool isReplay = _replayIndex >= 0;
            lock (_flowLock)
                _flow.Add(new FlowEntry(_sequence++, eventType, snapshot, EditorApplication.timeSinceStartup, isReplay));
            _dirty = true;
        }

        #endregion

        #region 环形缓冲 [RING BUFFER]

        /// <summary>
        /// 定容环形缓冲（增删 O(1)，只读索引遍历）。
        /// </summary>
        private sealed class RingBuffer<T>
        {
            private readonly T[] _items;
            private int _head;
            public int Count { get; private set; }

            public RingBuffer(int capacity)
            {
                _items = new T[capacity];
            }

            public void Add(T item)
            {
                _items[_head] = item;
                _head = (_head + 1) % _items.Length;
                if (Count < _items.Length) Count++;
            }

            public T this[int index] => _items[(index + _items.Length - Count) % _items.Length];

            public void Clear()
            {
                _head = 0;
                Count = 0;
            }
        }

        #endregion
    }
}
