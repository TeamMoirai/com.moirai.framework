using System;
using System.Collections.Generic;
using Moirai.Atropos.Debugger;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos
{
    /// <summary>
    /// 事件总线调试窗：事件流监视（最近派发的环形缓冲 + 各事件类型的订阅计数），挂接 <see cref="EventBusDiagnostics.OnPublished"/> 钩子。
    /// </summary>
    /// <remarks>
    /// 注册路径 "Profiler/EventBus"（总线由容器生命周期驱动，事件发布计数按会话累计）。 <br />
    /// 缓冲容量 64 条（环形覆盖旧条目）；OnUpdate 节流刷新（0.25s 间隔，避免每帧重建 UI）。
    /// </remarks>
    public sealed class EventBusDebuggerWindow : ScrollableDebuggerWindowBase
    {
        private const int MaxFlowEntries = 64;
        private const float RefreshInterval = 0.25f;

        private readonly struct FlowEntry
        {
            public readonly Type EventType;
            public readonly float Time;

            public FlowEntry(Type eventType, float time)
            {
                EventType = eventType;
                Time = time;
            }
        }

        private readonly RingBuffer<FlowEntry> _flow = new RingBuffer<FlowEntry>(MaxFlowEntries);
        private readonly Dictionary<Type, int> _publishCounts = new Dictionary<Type, int>();
        private readonly Dictionary<Type, int> _subscriberCounts = new Dictionary<Type, int>();

        private VisualElement _flowCard;
        private VisualElement _statsCard;
        private float _refreshTimer;
        private bool _dirty;

        #region 构建窗口 [BUILD WINDOW]

        /// <summary>构建窗口：事件流卡片 + 类型统计卡片。</summary>
        protected override void BuildWindow(VisualElement root)
        {
            root.Add(DebuggerUI.CreateSectionTitle("Event Bus"));

            VisualElement flowSection = DebuggerUI.CreateSection("EVENT FLOW (LAST " + MaxFlowEntries + ")", out _flowCard);
            flowSection.Add(DebuggerUI.CreateHintLabel("Live publish stream via EventBusDiagnostics hook."));
            root.Add(flowSection);

            VisualElement statsSection = DebuggerUI.CreateSection("EVENT TYPES", out _statsCard);
            statsSection.Add(DebuggerUI.CreateHintLabel("Publish count (session-cumulative) · active subscribers per type."));
            root.Add(statsSection);

            _dirty = true;
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>进窗挂接诊断钩子（OnLeave 摘除——避免窗关闭后继续累积）。</summary>
        public override void OnEnter()
        {
            EventBusDiagnostics.OnPublished += OnPublishedHook;
        }

        /// <summary>离窗摘除钩子。</summary>
        public override void OnLeave()
        {
            EventBusDiagnostics.OnPublished -= OnPublishedHook;
        }

        /// <summary>关停清空缓冲与计数。</summary>
        public override void Shutdown()
        {
            EventBusDiagnostics.OnPublished -= OnPublishedHook;
            _flow.Clear();
            _publishCounts.Clear();
            _subscriberCounts.Clear();
        }

        /// <summary>节流刷新（0.25s）。</summary>
        public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            if (!_dirty) return;

            _refreshTimer += realElapseSeconds;
            if (_refreshTimer < RefreshInterval) return;

            _refreshTimer = 0f;
            _dirty = false;
            RebuildFlow();
            RebuildStats();
        }

        #endregion

        #region 钩子 [HOOKS]

        private void OnPublishedHook(Type eventType)
        {
            _flow.Add(new FlowEntry(eventType, Time.unscaledTime));
            _publishCounts.TryGetValue(eventType, out int count);
            _publishCounts[eventType] = count + 1;
            _dirty = true;
        }

        #endregion

        #region 刷新 [REFRESH]

        private void RebuildFlow()
        {
            _flowCard.Clear();

            if (_flow.Count == 0)
            {
                _flowCard.Add(DebuggerUI.CreateHintLabel("No events published yet (hook active only while this window is open)."));
                return;
            }

            // 新条目在上（逆序遍历环形缓冲）
            for (int i = _flow.Count - 1; i >= 0 && i > _flow.Count - 24; i--)
            {
                FlowEntry entry = _flow[i];
                float age = Time.unscaledTime - entry.Time;
                _flowCard.Add(DebuggerUI.CreateHintLabel(StringUtility.Format(
                    "  {0:0.00}s ago  {1}", age, entry.EventType.Name)));
            }

            if (_flow.Count > 24)
            {
                _flowCard.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("  …{0} older entries hidden", _flow.Count - 24)));
            }
        }

        private void RebuildStats()
        {
            _statsCard.Clear();

            if (_publishCounts.Count == 0)
            {
                _statsCard.Add(DebuggerUI.CreateHintLabel("No event types observed."));
                return;
            }

            foreach (KeyValuePair<Type, int> pair in _publishCounts)
            {
                _statsCard.Add(DebuggerUI.CreateHintLabel(StringUtility.Format(
                    "  {0}  ·  {1} publishes", pair.Key.Name, pair.Value)));
            }
        }

        #endregion

        #region 环形缓冲 [RING BUFFER]

        /// <summary>定容环形缓冲（增删 O(1)，只读索引遍历）。</summary>
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
