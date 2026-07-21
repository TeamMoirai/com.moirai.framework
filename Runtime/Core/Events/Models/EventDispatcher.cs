using System;
using System.Collections.Generic;
using System.Diagnostics;
using Moirai.Atropos.Pool;
using Debug = UnityEngine.Debug;

namespace Moirai.Atropos.Events
{
    public enum DispatchMode
    {
        /// <summary>
        /// 默认 => Queued
        /// </summary>
        Default = Queued,
        
        /// <summary>
        /// 队列
        /// </summary>
        Queued = 1,
        
        /// <summary>
        /// 立即
        /// </summary>
        Immediate = 2,
    }
    
    /// <summary>
    /// 门（Gate）控制调度程序何时处理事件
    /// </summary>
    public readonly struct EventDispatcherGate : IDisposable, IEquatable<EventDispatcherGate>
    {
        private readonly EventDispatcher m_Dispatcher;
        
        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="d">由此门控制的调度程序。</param>
        public EventDispatcherGate(EventDispatcher d)
        {
            m_Dispatcher = d ?? throw new ArgumentNullException(nameof(d));
            m_Dispatcher.CloseGate();
        }

        /// <summary>
        /// IDisposable.Dispose 的实现。打开门。如果所有入口都打开，则处理队列中的
        /// </summary>
        public void Dispose()
        {
            m_Dispatcher.OpenGate();
        }

        public bool Equals(EventDispatcherGate other)
        {
            return Equals(m_Dispatcher, other.m_Dispatcher);
        }
        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            return obj is EventDispatcherGate gate && Equals(gate);
        }
        public override int GetHashCode()
        {
            return m_Dispatcher != null ? m_Dispatcher.GetHashCode() : 0;
        }

        public static bool operator ==(EventDispatcherGate left, EventDispatcherGate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EventDispatcherGate left, EventDispatcherGate right)
        {
            return !left.Equals(right);
        }
    }
    /// <summary>
    /// 将事件分派给 <see cref="IEventCoordinator"/>.
    /// </summary>
    public sealed class EventDispatcher
    {
        private struct EventRecord
        {
            public EventBase m_Event;
            public IEventCoordinator m_Coordinator;
#if UNITY_EDITOR
            internal StackTrace m_StackTrace;
            internal readonly string StackTrace => m_StackTrace?.ToString() ?? string.Empty;
#endif
        }

        private readonly List<IEventDispatchingStrategy> m_DispatchingStrategies;
        private static readonly _ObjectPool<Queue<EventRecord>> s_EventQueuePool = new _ObjectPool<Queue<EventRecord>>(() => new Queue<EventRecord>());
        private Queue<EventRecord> m_Queue;
        private uint m_GateCount;
        private readonly DebuggerEventDispatchingStrategy m_DebuggerEventDispatchingStrategy;
        private struct DispatchContext
        {
            public uint m_GateCount;
            public Queue<EventRecord> m_Queue;
        }
        private readonly List<IEventDispatchingListener> dispatchingListeners;
        private readonly Stack<DispatchContext> m_DispatchContexts = new Stack<DispatchContext>();
        public EventDispatcher(IList<IEventDispatchingStrategy> strategies)
        {
            dispatchingListeners = new List<IEventDispatchingListener>();
            m_DispatchingStrategies = new List<IEventDispatchingStrategy>();
#if UNITY_EDITOR
            m_DebuggerEventDispatchingStrategy = new DebuggerEventDispatchingStrategy();
            m_DispatchingStrategies.Add(m_DebuggerEventDispatchingStrategy);
#endif
            m_DispatchingStrategies.AddRange(strategies);
            m_Queue = s_EventQueuePool.Get();
        }
        private static readonly IEventDispatchingStrategy[] s_DefaultStrategies =
        {
            new DefaultDispatchingStrategy()
        };
        public static EventDispatcher CreateDefault()
        {
            return new EventDispatcher(s_DefaultStrategies);
        }
        private readonly bool m_Immediate = false;
        private bool DispatchImmediately
        {
            get { return m_Immediate || m_GateCount == 0; }
        }

        public bool ProcessingEvents { get; private set; }

        public void Dispatch(EventBase evt, IEventCoordinator coordinator, DispatchMode dispatchMode)
        {
            evt.MarkReceivedByDispatcher();

            if (DispatchImmediately || (dispatchMode == DispatchMode.Immediate))
            {
                ProcessEvent(evt, coordinator);
            }
            else
            {
                evt.Acquire();
                m_Queue.Enqueue(new EventRecord
                {
                    m_Event = evt,
                    m_Coordinator = coordinator,
#if UNITY_EDITOR
                    m_StackTrace = new StackTrace()
#endif
                });
            }
        }

        public void PushDispatcherContext()
        {
            foreach (var listener in dispatchingListeners)
            {
                listener.OnPushDispatcherContext();
            }

            // 在推送新上下文之前清空事件队列。
            ProcessEventQueue();

            m_DispatchContexts.Push(new DispatchContext() { m_GateCount = m_GateCount, m_Queue = m_Queue });
            m_GateCount = 0;
            m_Queue = s_EventQueuePool.Get();
        }

        public void PopDispatcherContext()
        {
            Debug.Assert(m_GateCount == 0, "All gates should have been opened before popping dispatch context.");
            Debug.Assert(m_Queue.Count == 0, "Queue should be empty when popping dispatch context.");

            s_EventQueuePool.Release(m_Queue);

            m_GateCount = m_DispatchContexts.Peek().m_GateCount;
            m_Queue = m_DispatchContexts.Peek().m_Queue;
            m_DispatchContexts.Pop();

            foreach (var listener in dispatchingListeners)
            {
                listener.OnPopDispatcherContext();
            }
        }

        internal void CloseGate()
        {
            m_GateCount++;
        }

        internal void OpenGate()
        {
            Debug.Assert(m_GateCount > 0);

            if (m_GateCount > 0)
            {
                m_GateCount--;
            }

            if (m_GateCount == 0)
            {
                ProcessEventQueue();
            }
        }

        private void ProcessEventQueue()
        {
            // 在处理当前队列时，需要一个新队列来存储当前队列事件处理期间可能生成的其他事件。
            // 多亏了门机制，放入新队列中的事件将在当前队列中的其余事件之前被处理（但在完成生成它们的事件的处理之后）。

            Queue<EventRecord> queueToProcess = m_Queue;
            m_Queue = s_EventQueuePool.Get();

            try
            {
                ProcessingEvents = true;
                while (queueToProcess.Count > 0)
                {
                    EventRecord eventRecord = queueToProcess.Dequeue();
                    EventBase evt = eventRecord.m_Event;
                    IEventCoordinator coordinator = eventRecord.m_Coordinator;
                    try
                    {
                        ProcessEvent(evt, coordinator);
                    }
                    finally
                    {
                        // 在将事件放入队列时平衡 Acquire。
                        evt.Dispose();
                    }
                }
            }
            finally
            {
                ProcessingEvents = false;
                s_EventQueuePool.Release(queueToProcess);
            }
        }
        private void ProcessEvent(EventBase evt, IEventCoordinator coordinator)
        {
            using (new EventDispatcherGate(this))
            {
                evt.PreDispatch(coordinator);

                if (!evt.StopDispatch && !evt.IsPropagationStopped)
                {
                    ApplyDispatchingStrategies(evt, coordinator);
                }

                // 构建路径的最后机会。某些调度策略（例如 PointerCaptureDispatchingStrategy）不调用 PropagateEvents，
                // 但仍需要在复合根上调用 ExecuteDefaultActions。
                var path = evt.Path;
                if (path == null && evt.BubblesOrTricklesDown && evt.LeafTarget is CallbackEventHandler leafTarget)
                {
                    path = PropagationPaths.Build(leafTarget, evt);
                    evt.Path = path;
                    EventDebugger.LogPropagationPaths(evt, path);
                }

                if (path != null)
                {
                    foreach (var element in path.TargetElements)
                    {
                        if (element.Coordinator == coordinator)
                        {
                            evt.Target = element;
                            EventDispatchUtilities.ExecuteDefaultAction(evt);
                        }
                    }

                    // 将目标重置为叶目标
                    evt.Target = evt.LeafTarget;
                }
                else
                {
                    // 如果没有传播路径，请确保 EventDispatchUtilities.ExecuteDefaultAction 有目标
                    if (evt.Target is not CallbackEventHandler target)
                    {
                        evt.Target = target = coordinator.GetCallbackEventHandler();
                    }

                    if (target?.Coordinator == coordinator)
                    {
                        EventDispatchUtilities.ExecuteDefaultAction(evt);
                    }
                }

#if UNITY_EDITOR
                m_DebuggerEventDispatchingStrategy.PostDispatch(evt, coordinator);
#endif

                evt.PostDispatch(coordinator);
            }
        }

        private void ApplyDispatchingStrategies(EventBase evt, IEventCoordinator coordinator)
        {
            foreach (var strategy in m_DispatchingStrategies)
            {
                if (strategy.CanDispatchEvent(evt))
                {
                    strategy.DispatchEvent(evt, coordinator);

                    if (evt.StopDispatch || evt.IsPropagationStopped)
                        break;
                }
            }
        }
        
        public TListener GetEventDispatchingListener<TListener>() where TListener : IEventDispatchingListener
        {
            foreach (var listener in dispatchingListeners)
            {
                if (listener is TListener dispatchingListener) return dispatchingListener;
            }
            return default;
        }
        
        public void AddEventDispatchingListener(IEventDispatchingListener listener)
        {
            dispatchingListeners.Add(listener);
        }
    }
}