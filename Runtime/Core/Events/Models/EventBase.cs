using System;
using System.Collections.Generic;
using Moirai.Atropos.Pool;
using Newtonsoft.Json;
using UnityEngine;

namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 该类实现 IDisposable 以确保在必要时从池中正确释放事件和任意非托管资源。
    /// </summary>
    /// <remarks>
    /// 移植自 <see cref="UnityEngine.UIElements.EventBase"/>.
    /// </remarks>
    public abstract class EventBase : IDisposable
    {
        [Flags]
        private enum LifeCycleStatus
        {
            None = 0,
            PropagationStopped = 1,
            ImmediatePropagationStopped = 2,
            DefaultPrevented = 4,
            Dispatching = 8,
            Pooled = 16,
            StopDispatch = 32,
            Dispatched = 64,
            Processed = 128,
        }
        
        /// <summary>
        /// 参考 https://docs.unity3d.com/cn/current/Manual/UIE-Events-Dispatching.html
        /// </summary>
        [Flags]
        public enum EventPropagation
        {
            None = 0,
            /// <summary>冒泡</summary>
            Bubbles = 1,
            /// <summary>涓滴</summary>
            TricklesDown = 2,
            /// <summary>可取消</summary>
            Cancellable = 4,
            /// <summary>跳过禁用的元素</summary>
            SkipDisabledElements = 8,
            /// <summary>忽略复合根节点</summary>
            IgnoreCompositeRoots = 16,
        }
        
        private static long s_LastTypeId = 0;

        /// <summary>
        /// 将事件类注册到事件类型系统。
        /// </summary>
        /// <returns>类型 ID。</returns>
        protected static long RegisterEventType() { return ++s_LastTypeId; }

        /// <summary>
        /// 检索此事件实例的类型 ID。
        /// </summary>
        [JsonIgnore]
        public virtual long EventTypeId => -1;

        private static ulong s_NextEventId = 0;

        // 只读状态（Read-only state）
        /// <summary>
        /// 创建事件的时间（以毫秒为单位）。
        /// </summary>
        /// <remarks>
        /// 此值是相对于当前应用程序的开始时间的。
        /// </remarks>
        [JsonIgnore]
        public long Timestamp { get; private set; }
        
        internal ulong EventId { get; private set; }
        
        internal ulong TriggerEventId { get; private set; }
        
        internal PropagationPaths Path { get; set; }
        
        public EventPropagation Propagation { get; protected set; }
        
        internal void SetTriggerEventId(ulong id)
        {
            TriggerEventId = id;
        }
        
        private LifeCycleStatus Status { get; set; }
        
        /// <summary>
        /// 此事件的当前传播阶段。
        /// </summary>
        [JsonIgnore]
        public PropagationPhase PropagationPhase { get; internal set; }

        /// <summary>
        /// 允许子类（subclasses）在 dispatch 事件之前执行自定义逻辑。
        /// </summary>
        protected internal virtual void PreDispatch(IEventCoordinator coordinator)
        {
        }

        /// <summary>
        /// 允许子类（subclasses）在 dispatch 事件后执行自定义逻辑。
        /// </summary>
        protected internal virtual void PostDispatch(IEventCoordinator coordinator)
        {
            Processed = true;
        }

        /// <summary>
        /// 返回此事件类型是否在事件传播路径中向上冒泡。
        /// </summary>
        [JsonIgnore]
        public bool Bubbles
        {
            get => (Propagation & EventPropagation.Bubbles) != 0;
            protected set
            {
                if (value)
                {
                    Propagation |= EventPropagation.Bubbles;
                }
                else
                {
                    Propagation &= ~EventPropagation.Bubbles;
                }
            }
        }

        /// <summary>
        /// 返回此事件类型是否在事件传播路径中向下涓滴
        /// </summary>
        [JsonIgnore]
        public bool TricklesDown
        {
            get => (Propagation & EventPropagation.TricklesDown) != 0;
            protected set
            {
                if (value)
                {
                    Propagation |= EventPropagation.TricklesDown;
                }
                else
                {
                    Propagation &= ~EventPropagation.TricklesDown;
                }
            }
        }

        internal bool BubblesOrTricklesDown => (Propagation & (EventPropagation.Bubbles | EventPropagation.TricklesDown)) != 0;
        
        private IEventHandler m_Target;

        /// <summary>
        /// 接收此事件的目标处理程序。
        /// 与 currentTarget 不同，当事件发送到传播路径上的其他元素时，此目标不会更改。
        /// </summary>
        [JsonIgnore]
        public IEventHandler Target
        {
            get => m_Target;
            set
            {
                m_Target = value;
                LeafTarget ??= value;
            }
        }
        
        // 原始目标。当传播事件且 target.isCompositeRoot 为 true 时，可能与 target 不同。
        internal IEventHandler LeafTarget { get; private set; }
        
        internal List<IEventHandler> SkipElements { get; } = new List<IEventHandler>();

        internal bool Skip(IEventHandler h)
        {
            return SkipElements.Contains(h);
        }
        
        internal bool SkipDisabledElements
        {
            get => (Propagation & EventPropagation.SkipDisabledElements) != 0;
            set
            {
                if (value)
                {
                    Propagation |= EventPropagation.SkipDisabledElements;
                }
                else
                {
                    Propagation &= ~EventPropagation.SkipDisabledElements;
                }
            }
        }
        
        internal bool IgnoreCompositeRoots
        {
            get { return (Propagation & EventPropagation.IgnoreCompositeRoots) != 0; }
            set
            {
                if (value)
                {
                    Propagation |= EventPropagation.IgnoreCompositeRoots;
                }
                else
                {
                    Propagation &= ~EventPropagation.IgnoreCompositeRoots;
                }
            }
        }
        
        /// <summary>
        /// 是否为此事件调用 StopPropagation()。
        /// </summary>
        [JsonIgnore]
        public bool IsPropagationStopped
        {
            get => (Status & LifeCycleStatus.PropagationStopped) != LifeCycleStatus.None;
            internal set
            {
                if (value)
                {
                    Status |= LifeCycleStatus.PropagationStopped;
                }
                else
                {
                    Status &= ~LifeCycleStatus.PropagationStopped;
                }
            }
        }
        
        /// <summary>
        /// 指示是否阻止对此事件执行默认操作。
        /// </summary>
        public void PreventDefault()
        {
            if ((Propagation & EventPropagation.Cancellable) == EventPropagation.Cancellable)
            {
                IsDefaultPrevented = true;
            }
        }
        
        /// <summary>
        /// 停止传播此事件。该事件不会发送到传播路径上的其他元素。此方法不会阻止其他事件处理程序在当前目标上执行。
        /// </summary>
        public void StopPropagation()
        {
            IsPropagationStopped = true;
        }

        /// <summary>
        /// 指示是否为此事件调用 StopImmediatePropagation()。
        /// </summary>
        [JsonIgnore]
        public bool IsImmediatePropagationStopped
        {
            get => (Status & LifeCycleStatus.ImmediatePropagationStopped) != LifeCycleStatus.None;
            private set
            {
                if (value)
                {
                    Status |= LifeCycleStatus.ImmediatePropagationStopped;
                }
                else
                {
                    Status &= ~LifeCycleStatus.ImmediatePropagationStopped;
                }
            }
        }

        /// <summary>
        /// 立即停止事件的传播。
        /// 该事件不会发送到传播路径上的其他元素。
        /// 此方法可防止其他事件处理程序在当前目标上执行。
        /// </summary>
        public void StopImmediatePropagation()
        {
            IsPropagationStopped = true;
            IsImmediatePropagationStopped = true;
        }

        /// <summary>
        /// 如果不应为此事件执行默认操作，则返回 <c>true</c>。
        /// </summary>
        [JsonIgnore]
        public bool IsDefaultPrevented
        {
            get => (Status & LifeCycleStatus.DefaultPrevented) != LifeCycleStatus.None;
            private set
            {
                if (value)
                {
                    Status |= LifeCycleStatus.DefaultPrevented;
                }
                else
                {
                    Status &= ~LifeCycleStatus.DefaultPrevented;
                }
            }
        }

        private IEventHandler m_CurrentTarget;

        /// <summary>
        /// 事件的当前目标。
        /// 这是传播路径中的 eventHandler，当前正在为其执行事件处理程序。
        /// </summary>
        [JsonIgnore]
        public virtual IEventHandler CurrentTarget
        {
            get => m_CurrentTarget;
            internal set => m_CurrentTarget = value;
        }

        /// <summary>
        /// 指示是否将事件调度给 eventHandler。
        /// 事件在被分派时不能重新分派。如果需要递归 dispatch 事件，建议使用事件的副本。
        /// </summary>
        [JsonIgnore]
        public bool Dispatch
        {
            get => (Status & LifeCycleStatus.Dispatching) != LifeCycleStatus.None;
            internal set
            {
                if (value)
                {
                    Status |= LifeCycleStatus.Dispatching;
                    Dispatched = true;
                }
                else
                {
                    Status &= ~LifeCycleStatus.Dispatching;
                }
            }
        }

        internal void MarkReceivedByDispatcher()
        {
            Debug.Assert(Dispatched == false, "Events cannot be dispatched more than once.");
            Dispatched = true;
        }

        private bool Dispatched
        {
            get => (Status & LifeCycleStatus.Dispatched) != LifeCycleStatus.None;
            set
            {
                if (value)
                {
                    Status |= LifeCycleStatus.Dispatched;
                }
                else
                {
                    Status &= ~LifeCycleStatus.Dispatched;
                }
            }
        }

        internal bool Processed
        {
            get => (Status & LifeCycleStatus.Processed) != LifeCycleStatus.None;
            private set
            {
                if (value)
                {
                    Status |= LifeCycleStatus.Processed;
                }
                else
                {
                    Status &= ~LifeCycleStatus.Processed;
                }
            }
        }


        public bool StopDispatch
        {
            get => (Status & LifeCycleStatus.StopDispatch) != LifeCycleStatus.None;
            set
            {
                if (value)
                {
                    Status |= LifeCycleStatus.StopDispatch;
                }
                else
                {
                    Status &= ~LifeCycleStatus.StopDispatch;
                }
            }
        }

#if UNITY_EDITOR
        internal EventDebugger EventLogger { get; set; }

        internal bool Log => EventLogger != null;
#endif
        
        /// <summary>
        /// 将所有事件成员重置为其初始值。
        /// </summary>
        protected virtual void Init()
        {
            LocalInit();
        }

        private void LocalInit()
        {
            Timestamp = TimeSinceStartupMs();

            TriggerEventId = 0;
            EventId = s_NextEventId++;

            Target = null;
            LeafTarget = null;

            Path?.Release();
            Path = null;
            SkipElements.Clear();
            Propagation = EventPropagation.None;
            PropagationPhase = PropagationPhase.None;
            IsPropagationStopped = false;
            IsImmediatePropagationStopped = false;
            IsDefaultPrevented = false;


            m_CurrentTarget = null;

            Dispatch = false;
            StopDispatch = false;

            Dispatched = false;
            Processed = false;
            Pooled = false;
#if UNITY_EDITOR
            EventLogger = null;
#endif
        }
        
        public static long TimeSinceStartupMs()
        {
            return (long)(Time.realtimeSinceStartup * 1000.0f);
        }
        
        /// <summary>
        /// 构造函数。避免创建新的事件实例。相反，请使用 GetPooled() 从可复用事件实例池中获取实例。
        /// </summary>
        protected EventBase()
        {
            LocalInit();
        }

        /// <summary>
        /// 事件是否从事件池中分配。
        /// </summary>
        protected bool Pooled
        {
            get => (Status & LifeCycleStatus.Pooled) != LifeCycleStatus.None;
            set
            {
                if (value)
                {
                    Status |= LifeCycleStatus.Pooled;
                }
                else
                {
                    Status &= ~LifeCycleStatus.Pooled;
                }
            }
        }

        public abstract void Acquire();
        
        /// <summary>
        /// 实现 <see cref="IDisposable"/>.
        /// </summary>
        public abstract void Dispose();
    }

    /// <summary>
    /// 事件的泛型基类，实现事件池和自动注册到事件类型系统。
    /// </summary>
    public abstract class EventBase<T> : EventBase where T : EventBase<T>, new()
    {
        // ReSharper disable once InconsistentNaming
        private static readonly long s_TypeId = RegisterEventType();
    
        // ReSharper disable once InconsistentNaming
        private static readonly _ObjectPool<T> s_Pool = new _ObjectPool<T>(() => new T());

        internal static void SetCreateFunction(Func<T> createMethod)
        {
            s_Pool.CreateFunc = createMethod;
        }
        private int m_RefCount;

        // ReSharper disable once ConvertConstructorToMemberInitializers
        protected EventBase()
        {
            m_RefCount = 0;
        }

        /// <summary>
        /// 检索此事件实例的类型 ID。
        /// </summary>
        /// <returns>The type ID.</returns>
        public static long TypeId()
        {
            return s_TypeId;
        }

        /// <summary>
        /// 将所有事件成员重置为其初始值。
        /// </summary>
        protected override void Init()
        {
            base.Init();

            if (m_RefCount != 0)
            {
                Debug.LogWarning($"Event improperly released, reference count {m_RefCount}.");
                m_RefCount = 0;
            }
        }

        /// <summary>
        /// 从事件池中获取事件。使用此功能，而不是创建新事件。
        /// 使用此方法获取的事件需要释放回池中。可以使用 Dispose() 来释放它们。
        /// </summary>
        /// <returns>已初始化的事件。</returns>
        protected static T GetPooled()
        {
            T t = s_Pool.Get();
            t.Init();
            t.Pooled = true;
            t.Acquire();
            return t;
        }
        
        internal static T GetPooled(EventBase e)
        {
            T t = GetPooled();
            if (e != null)
            {
                t.SetTriggerEventId(e.EventId);
            }
            return t;
        }

        static void ReleasePooled(T evt)
        {
            if (evt.Pooled)
            {
                // 在池化之前重置事件以避免泄漏
                evt.Init();

                s_Pool.Release(evt);

                // 避免从池中双重释放
                evt.Pooled = false;
            }
        }

        public override void Acquire()
        {
            m_RefCount++;
        }

        /// <summary>
        /// IDispose 的实现。
        /// </summary>
        /// <remarks>
        /// 如果事件是从事件池实例化的，则在调用 Dispose 时释放该事件。
        /// </remarks>
        public sealed override void Dispose()
        {
            if (--m_RefCount == 0)
            {
                ReleasePooled((T)this);
            }
        }

        /// <summary>
        /// 检索此事件实例的类型 ID。
        /// </summary>
        [JsonIgnore]
        public override long EventTypeId => s_TypeId;
    }
}
