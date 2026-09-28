using System;

namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 定义可注册到元素上以处理指定事件类型的回调结构。
    /// </summary>
    /// <param name="evt">事件实例。</param>
    /// <typeparam name="TEventType">要注册回调的事件类型。</typeparam>
    public delegate void EventCallback<in TEventType>(TEventType evt);

    /// <summary>
    /// 定义可注册到元素上以处理指定事件类型的回调结构，并附带自定义用户参数。
    /// </summary>
    /// <param name="evt">事件实例。</param>
    /// <param name="userArgs">用户参数实例。</param>
    /// <typeparam name="TEventType">回调注册的事件类型。</typeparam>
    /// <typeparam name="TCallbackArgs">用户参数的类型。</typeparam>
    public delegate void EventCallback<in TEventType, in TCallbackArgs>(TEventType evt, TCallbackArgs userArgs);
    internal abstract class EventCallbackFunctorBase
    {
        public CallbackPhase Phase { get; }
        public InvokePolicy InvokePolicy { get; }

        protected EventCallbackFunctorBase(CallbackPhase phase, InvokePolicy invokePolicy)
        {
            Phase = phase;
            InvokePolicy = invokePolicy;
        }

        public abstract void Invoke(EventBase evt, PropagationPhase propagationPhase);

        public abstract bool IsEquivalentTo(long eventTypeId, Delegate callback, CallbackPhase phase);

        protected bool PhaseMatches(PropagationPhase propagationPhase)
        {
            switch (Phase)
            {
                case CallbackPhase.TrickleDownAndTarget:
                    if (propagationPhase != PropagationPhase.TrickleDown && propagationPhase != PropagationPhase.AtTarget)
                        return false;
                    break;

                case CallbackPhase.TargetAndBubbleUp:
                    if (propagationPhase != PropagationPhase.AtTarget && propagationPhase != PropagationPhase.BubbleUp)
                        return false;
                    break;
            }

            return true;
        }
    }
    internal class EventCallbackFunctor<TEventType> : EventCallbackFunctorBase where TEventType : EventBase<TEventType>, new()
    {
        private readonly EventCallback<TEventType> m_Callback;
        private readonly long m_EventTypeId;

        public EventCallbackFunctor(EventCallback<TEventType> callback, CallbackPhase phase, InvokePolicy invokePolicy = default) : base(phase, invokePolicy)
        {
            m_Callback = callback;
            m_EventTypeId = EventBase<TEventType>.TypeId();
        }

        public override void Invoke(EventBase evt, PropagationPhase propagationPhase)
        {
            if (evt == null)
                throw new ArgumentNullException(nameof(evt));

            if (evt.EventTypeId != m_EventTypeId)
                return;

            if (PhaseMatches(propagationPhase))
            {
                using (new EventDebuggerLogCall(m_Callback, evt))
                {
                    m_Callback(evt as TEventType);
                }
            }
        }

        public override bool IsEquivalentTo(long eventTypeId, Delegate callback, CallbackPhase phase)
        {
            return (m_EventTypeId == eventTypeId) && ((Delegate)m_Callback) == callback && (this.Phase == phase);
        }
    }

    internal class EventCallbackFunctor<TEventType, TCallbackArgs> : EventCallbackFunctorBase where TEventType : EventBase<TEventType>, new()
    {
        private readonly EventCallback<TEventType, TCallbackArgs> m_Callback;
        private readonly long m_EventTypeId;

        internal TCallbackArgs UserArgs { get; set; }

        public EventCallbackFunctor(EventCallback<TEventType, TCallbackArgs> callback, TCallbackArgs userArgs, CallbackPhase phase, InvokePolicy invokePolicy) : base(phase, invokePolicy)
        {
            UserArgs = userArgs;
            m_Callback = callback;
            m_EventTypeId = EventBase<TEventType>.TypeId();
        }

        public override void Invoke(EventBase evt, PropagationPhase propagationPhase)
        {
            if (evt == null)
                throw new ArgumentNullException(nameof(evt));

            if (evt.EventTypeId != m_EventTypeId)
                return;

            if (PhaseMatches(propagationPhase))
            {
                using (new EventDebuggerLogCall(m_Callback, evt))
                {
                    m_Callback(evt as TEventType, UserArgs);
                }
            }
        }

        public override bool IsEquivalentTo(long eventTypeId, Delegate callback, CallbackPhase phase)
        {
            return (m_EventTypeId == eventTypeId) && ((Delegate)m_Callback) == callback && (this.Phase == phase);
        }
    }
}
