using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 指定事件处理程序在哪些传播阶段执行的枚举。
    /// </summary>
    public enum TrickleDown
    {
        /// <summary>
        /// 事件处理程序在 AtTarget 与 BubbleUp 阶段执行。
        /// </summary>
        NoTrickleDown = 0,
        /// <summary>
        /// 事件处理程序在 AtTarget 与 TrickleDown 阶段执行。
        /// </summary>
        TrickleDown = 1
    }

    internal enum CallbackPhase
    {
        TargetAndBubbleUp = 1 << 0,
        TrickleDownAndTarget = 1 << 1
    }

    internal enum InvokePolicy
    {
        Default = default,
        IncludeDisabled
    }

    internal class EventCallbackListPool
    {
        readonly Stack<EventCallbackList> m_Stack = new Stack<EventCallbackList>();

        /// <summary>
        /// 从池中获取事件回调列表，可选用指定列表的内容初始化。
        /// </summary>
        /// <param name="initializer">用于初始化内容的源列表，可为 null。</param>
        /// <returns>可用的事件回调列表。</returns>
        public EventCallbackList Get(EventCallbackList initializer)
        {
            EventCallbackList element;
            if (m_Stack.Count == 0)
            {
                if (initializer != null)
                    element = new EventCallbackList(initializer);
                else
                    element = new EventCallbackList();
            }
            else
            {
                element = m_Stack.Pop();
                if (initializer != null)
                    element.AddRange(initializer);
            }
            return element;
        }

        /// <summary>
        /// 清空并将事件回调列表归还对象池。
        /// </summary>
        /// <param name="element">要归还的回调列表。</param>
        public void Release(EventCallbackList element)
        {
            element.Clear();
            m_Stack.Push(element);
        }
    }

    internal class EventCallbackList
    {
        private readonly List<EventCallbackFunctorBase> m_List;
        /// <summary>
        /// 获取 TrickleDown（下探）相关阶段回调的数量。
        /// </summary>
        public int TrickleDownCallbackCount { get; private set; }
        /// <summary>
        /// 获取 BubbleUp（冒泡）相关阶段回调的数量。
        /// </summary>
        public int BubbleUpCallbackCount { get; private set; }

        /// <summary>
        /// 初始化空的事件回调列表。
        /// </summary>
        public EventCallbackList()
        {
            m_List = new List<EventCallbackFunctorBase>();
            TrickleDownCallbackCount = 0;
            BubbleUpCallbackCount = 0;
        }

        /// <summary>
        /// 以源列表的回调集合创建新的事件回调列表。
        /// </summary>
        /// <param name="source">作为数据来源的源列表。</param>
        public EventCallbackList(EventCallbackList source)
        {
            m_List = new List<EventCallbackFunctorBase>(source.m_List);
            TrickleDownCallbackCount = 0;
            BubbleUpCallbackCount = 0;
        }

        /// <summary>
        /// 获取是否已包含与指定条件等效的回调。
        /// </summary>
        /// <param name="eventTypeId">事件类型 ID。</param>
        /// <param name="callback">要匹配的回调委托。</param>
        /// <param name="phase">要匹配的回调阶段。</param>
        /// <returns>已包含返回 true，否则返回 false。</returns>
        public bool Contains(long eventTypeId, Delegate callback, CallbackPhase phase)
        {
            return Find(eventTypeId, callback, phase) != null;
        }

        /// <summary>
        /// 查找与指定事件类型、委托和回调阶段等效的回调包装。
        /// </summary>
        /// <param name="eventTypeId">事件类型 ID。</param>
        /// <param name="callback">要匹配的回调委托。</param>
        /// <param name="phase">要匹配的回调阶段。</param>
        /// <returns>匹配的回调包装；未找到时返回 null。</returns>
        public EventCallbackFunctorBase Find(long eventTypeId, Delegate callback, CallbackPhase phase)
        {
            for (int i = 0; i < m_List.Count; i++)
            {
                if (m_List[i].IsEquivalentTo(eventTypeId, callback, phase))
                {
                    return m_List[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 移除与指定条件等效的回调，并同步递减对应阶段的计数。
        /// </summary>
        /// <param name="eventTypeId">事件类型 ID。</param>
        /// <param name="callback">要移除的回调委托。</param>
        /// <param name="phase">要匹配的回调阶段。</param>
        /// <returns>移除成功返回 true，否则返回 false。</returns>
        public bool Remove(long eventTypeId, Delegate callback, CallbackPhase phase)
        {
            for (int i = 0; i < m_List.Count; i++)
            {
                if (m_List[i].IsEquivalentTo(eventTypeId, callback, phase))
                {
                    m_List.RemoveAt(i);

                    if (phase == CallbackPhase.TrickleDownAndTarget)
                    {
                        TrickleDownCallbackCount--;
                    }
                    else if (phase == CallbackPhase.TargetAndBubbleUp)
                    {
                        BubbleUpCallbackCount--;
                    }

                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 添加一个回调包装，并按其阶段递增对应计数。
        /// </summary>
        /// <param name="item">要添加的回调包装。</param>
        public void Add(EventCallbackFunctorBase item)
        {
            m_List.Add(item);

            if (item.Phase == CallbackPhase.TrickleDownAndTarget)
            {
                TrickleDownCallbackCount++;
            }
            else if (item.Phase == CallbackPhase.TargetAndBubbleUp)
            {
                BubbleUpCallbackCount++;
            }
        }

        /// <summary>
        /// 批量添加源列表中的所有回调包装，并按阶段递增对应计数。
        /// </summary>
        /// <param name="list">作为数据来源的源列表。</param>
        public void AddRange(EventCallbackList list)
        {
            m_List.AddRange(list.m_List);

            foreach (var item in list.m_List)
            {
                if (item.Phase == CallbackPhase.TrickleDownAndTarget)
                {
                    TrickleDownCallbackCount++;
                }
                else if (item.Phase == CallbackPhase.TargetAndBubbleUp)
                {
                    BubbleUpCallbackCount++;
                }
            }
        }

        /// <summary>
        /// 获取当前回调数量。
        /// </summary>
        public int Count
        {
            get { return m_List.Count; }
        }

        /// <summary>
        /// 获取或设置指定索引处的回调包装。
        /// </summary>
        /// <param name="i">回调索引。</param>
        public EventCallbackFunctorBase this[int i]
        {
            get { return m_List[i]; }
            set { m_List[i] = value; }
        }

        /// <summary>
        /// 清空所有回调并重置阶段计数。
        /// </summary>
        public void Clear()
        {
            m_List.Clear();
            TrickleDownCallbackCount = 0;
            BubbleUpCallbackCount = 0;
        }
    }

    internal class EventCallbackRegistry
    {
        private static readonly EventCallbackListPool s_ListPool = new EventCallbackListPool();

        private static EventCallbackList GetCallbackList(EventCallbackList initializer = null)
        {
            return s_ListPool.Get(initializer);
        }

        private static void ReleaseCallbackList(EventCallbackList toRelease)
        {
            s_ListPool.Release(toRelease);
        }

        private EventCallbackList m_Callbacks;
        private EventCallbackList m_TemporaryCallbacks;
        private int m_IsInvoking;

        /// <summary>
        /// 初始化事件回调注册表实例。
        /// </summary>
        public EventCallbackRegistry()
        {
            m_IsInvoking = 0;
        }

        private EventCallbackList GetCallbackListForWriting()
        {
            if (m_IsInvoking > 0)
            {
                if (m_TemporaryCallbacks == null)
                {
                    if (m_Callbacks != null)
                    {
                        m_TemporaryCallbacks = GetCallbackList(m_Callbacks);
                    }
                    else
                    {
                        m_TemporaryCallbacks = GetCallbackList();
                    }
                }

                return m_TemporaryCallbacks;
            }
            else
            {
                m_Callbacks ??= GetCallbackList();

                return m_Callbacks;
            }
        }

        private EventCallbackList GetCallbackListForReading()
        {
            if (m_TemporaryCallbacks != null)
            {
                return m_TemporaryCallbacks;
            }

            return m_Callbacks;
        }

        // bool ShouldRegisterCallback(long eventTypeId, Delegate callback, CallbackPhase phase)
        // {
        //     if (callback == null)
        //     {
        //         return false;
        //     }

        //     EventCallbackList callbackList = GetCallbackListForReading();
        //     if (callbackList != null)
        //     {
        //         return !callbackList.Contains(eventTypeId, callback, phase);
        //     }

        //     return true;
        // }

        private bool UnregisterCallback(long eventTypeId, Delegate callback, TrickleDown useTrickleDown)
        {
            if (callback == null)
            {
                return false;
            }

            EventCallbackList callbackList = GetCallbackListForWriting();
            var callbackPhase = useTrickleDown == TrickleDown.TrickleDown ? CallbackPhase.TrickleDownAndTarget : CallbackPhase.TargetAndBubbleUp;
            return callbackList.Remove(eventTypeId, callback, callbackPhase);
        }

        /// <summary>
        /// 注册指定事件类型的回调；重复注册等效回调时忽略本次调用。
        /// </summary>
        /// <param name="callback">要注册的回调。</param>
        /// <param name="useTrickleDown">回调是否在 TrickleDown（下探）阶段触发。</param>
        /// <param name="invokePolicy">回调调用策略。</param>
        public void RegisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown, InvokePolicy invokePolicy = default) where TEventType : EventBase<TEventType>, new()
        {
            if (callback == null)
                throw new ArgumentException("callback parameter is null");

            long eventTypeId = EventBase<TEventType>.TypeId();
            var callbackPhase = useTrickleDown == TrickleDown.TrickleDown ? CallbackPhase.TrickleDownAndTarget : CallbackPhase.TargetAndBubbleUp;

            EventCallbackList callbackList = GetCallbackListForReading();
            if (callbackList == null || callbackList.Contains(eventTypeId, callback, callbackPhase) == false)
            {
                callbackList = GetCallbackListForWriting();
                callbackList.Add(new EventCallbackFunctor<TEventType>(callback, callbackPhase, invokePolicy));
            }
        }

        /// <summary>
        /// 注册带用户参数的回调；若回调已注册，则仅更新其用户参数。
        /// </summary>
        /// <param name="callback">要注册的回调。</param>
        /// <param name="userArgs">注册到回调的用户参数。</param>
        /// <param name="useTrickleDown">回调是否在 TrickleDown（下探）阶段触发。</param>
        /// <param name="invokePolicy">回调调用策略。</param>
        public void RegisterCallback<TEventType, TCallbackArgs>(EventCallback<TEventType, TCallbackArgs> callback, TCallbackArgs userArgs, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown, InvokePolicy invokePolicy = default) where TEventType : EventBase<TEventType>, new()
        {
            if (callback == null)
                throw new ArgumentException("callback parameter is null");

            long eventTypeId = EventBase<TEventType>.TypeId();
            var callbackPhase = useTrickleDown == TrickleDown.TrickleDown ? CallbackPhase.TrickleDownAndTarget : CallbackPhase.TargetAndBubbleUp;

            EventCallbackList callbackList = GetCallbackListForReading();
            if (callbackList != null)
            {
                if (callbackList.Find(eventTypeId, callback, callbackPhase) is EventCallbackFunctor<TEventType, TCallbackArgs> functor)
                {
                    functor.UserArgs = userArgs;
                    return;
                }
            }
            callbackList = GetCallbackListForWriting();
            callbackList.Add(new EventCallbackFunctor<TEventType, TCallbackArgs>(callback, userArgs, callbackPhase, invokePolicy));
        }

        /// <summary>
        /// 注销指定事件类型的回调。
        /// </summary>
        /// <param name="callback">要注销的回调。</param>
        /// <param name="useTrickleDown">注销时匹配的 TrickleDown 选项。</param>
        /// <returns>注销成功返回 true，否则返回 false。</returns>
        public bool UnregisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            long eventTypeId = EventBase<TEventType>.TypeId();
            return UnregisterCallback(eventTypeId, callback, useTrickleDown);
        }

        /// <summary>
        /// 注销带用户参数的回调。
        /// </summary>
        /// <param name="callback">要注销的回调。</param>
        /// <param name="useTrickleDown">注销时匹配的 TrickleDown 选项。</param>
        /// <returns>注销成功返回 true，否则返回 false。</returns>
        public bool UnregisterCallback<TEventType, TCallbackArgs>(EventCallback<TEventType, TCallbackArgs> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            long eventTypeId = EventBase<TEventType>.TypeId();
            return UnregisterCallback(eventTypeId, callback, useTrickleDown);
        }

        internal bool TryGetUserArgs<TEventType, TCallbackArgs>(EventCallback<TEventType, TCallbackArgs> callback, TrickleDown useTrickleDown, out TCallbackArgs userArgs) where TEventType : EventBase<TEventType>, new()
        {
            userArgs = default;

            if (callback == null)
                return false;

            EventCallbackList list = GetCallbackListForReading();
            long eventTypeId = EventBase<TEventType>.TypeId();
            var callbackPhase = useTrickleDown == TrickleDown.TrickleDown ? CallbackPhase.TrickleDownAndTarget : CallbackPhase.TargetAndBubbleUp;

            if (list.Find(eventTypeId, callback, callbackPhase) is not EventCallbackFunctor<TEventType, TCallbackArgs> functor)
                return false;

            userArgs = functor.UserArgs;

            return true;
        }

        /// <summary>
        /// 按注册顺序调用匹配当前传播阶段的回调；事件立即停止传播时中断调用。
        /// </summary>
        /// <param name="evt">当前事件。</param>
        /// <param name="propagationPhase">当前传播阶段。</param>
        public void InvokeCallbacks(EventBase evt, PropagationPhase propagationPhase)
        {
            if (m_Callbacks == null)
            {
                return;
            }

            m_IsInvoking++;
            var requiresIncludeDisabledPolicy = evt.SkipDisabledElements && evt.CurrentTarget is IBehaviourScope ve && !ve.Behaviour.isActiveAndEnabled;
            for (var i = 0; i < m_Callbacks.Count; i++)
            {
                if (evt.IsImmediatePropagationStopped)
                    break;

                if (requiresIncludeDisabledPolicy &&
                    m_Callbacks[i].InvokePolicy != InvokePolicy.IncludeDisabled)
                {
                    continue;
                }

                m_Callbacks[i].Invoke(evt, propagationPhase);
            }

            m_IsInvoking--;

            if (m_IsInvoking == 0)
            {
                // 若回调在调用期间被修改，则在此应用这些修改。
                if (m_TemporaryCallbacks != null)
                {
                    ReleaseCallbackList(m_Callbacks);
                    m_Callbacks = GetCallbackList(m_TemporaryCallbacks);
                    ReleaseCallbackList(m_TemporaryCallbacks);
                    m_TemporaryCallbacks = null;
                }
            }
        }

        /// <summary>
        /// 获取是否注册了 TrickleDown（下探）阶段回调。
        /// </summary>
        /// <returns>已注册返回 true，否则返回 false。</returns>
        public bool HasTrickleDownHandlers()
        {
            return m_Callbacks != null && m_Callbacks.TrickleDownCallbackCount > 0;
        }

        /// <summary>
        /// 获取是否注册了 BubbleUp（冒泡）阶段回调。
        /// </summary>
        /// <returns>已注册返回 true，否则返回 false。</returns>
        public bool HasBubbleHandlers()
        {
            return m_Callbacks != null && m_Callbacks.BubbleUpCallbackCount > 0;
        }
    }
    internal static class GlobalCallbackRegistry
    {
        private static bool s_IsEventDebuggerConnected = false;
        /// <summary>
        /// 获取或设置事件调试器是否已连接；置为 false 时清空全部监听记录。
        /// </summary>
        public static bool IsEventDebuggerConnected
        {
            get { return s_IsEventDebuggerConnected; }
            set
            {
                if (!value)
                    s_Listeners.Clear();

                s_IsEventDebuggerConnected = value;
            }
        }

        internal struct ListenerRecord
        {
            public int hashCode;
            public string name;
            public string fileName;
            public int lineNumber;
        }

        internal static readonly Dictionary<CallbackEventHandler, Dictionary<Type, List<ListenerRecord>>> s_Listeners =
            new Dictionary<CallbackEventHandler, Dictionary<Type, List<ListenerRecord>>>();

        /// <summary>
        /// 清理 Behaviour 已失效（销毁）的监听记录。
        /// </summary>
        public static void CleanListeners()
        {
            var listeners = s_Listeners.ToList();
            foreach (var eventRegistrationListener in listeners)
            {
                var key = eventRegistrationListener.Key as IBehaviourScope; // 发送事件的 Behaviour
                if (key?.Behaviour == null)
                    s_Listeners.Remove(eventRegistrationListener.Key);
            }
        }
        /// <summary>
        /// 事件调试器连接时记录回调注册信息（含回调路径与源码位置）。
        /// </summary>
        /// <typeparam name="TEventType">回调注册的事件类型。</typeparam>
        /// <param name="ceh">注册回调的事件处理器。</param>
        /// <param name="callback">注册的回调委托。</param>
        /// <param name="useTrickleDown">注册时使用的 TrickleDown 选项。</param>
        public static void RegisterListeners<TEventType>(CallbackEventHandler ceh, Delegate callback, TrickleDown useTrickleDown)
        {
            if (!IsEventDebuggerConnected)
                return;
            if (!s_Listeners.TryGetValue(ceh, out Dictionary<Type, List<ListenerRecord>> dict))
            {
                dict = new Dictionary<Type, List<ListenerRecord>>();
                s_Listeners.Add(ceh, dict);
            }

            string itemName = DiagnosticsUtility.GetDelegatePath(callback);

            if (!dict.TryGetValue(typeof(TEventType), out List<ListenerRecord> callbackRecords))
            {
                callbackRecords = new List<ListenerRecord>();
                dict.Add(typeof(TEventType), callbackRecords);
            }

            StackFrame frame = DiagnosticsUtility.GetCurrentStackFrame();

            callbackRecords.Add(new ListenerRecord
            {
                hashCode = callback.GetHashCode(),
                name = itemName,
                fileName = frame.GetFileName(),
                lineNumber = frame.GetFileLineNumber()
            });
        }

        /// <summary>
        /// 事件调试器连接时移除指定回调的监听记录。
        /// </summary>
        /// <typeparam name="TEventType">回调注册的事件类型。</typeparam>
        /// <param name="ceh">注销回调的事件处理器。</param>
        /// <param name="callback">注销的回调委托。</param>
        public static void UnregisterListeners<TEventType>(CallbackEventHandler ceh, Delegate callback)
        {
            if (!IsEventDebuggerConnected)
                return;
            if (!s_Listeners.TryGetValue(ceh, out Dictionary<Type, List<ListenerRecord>> dict))
                return;

            string itemName = DiagnosticsUtility.GetDelegatePath(callback);

            if (!dict.TryGetValue(typeof(TEventType), out List<ListenerRecord> callbackRecords))
                return;

            for (var i = callbackRecords.Count - 1; i >= 0; i--)
            {
                var callbackRecord = callbackRecords[i];
                if (callbackRecord.name == itemName)
                {
                    callbackRecords.RemoveAt(i);
                }
            }
            s_Listeners.Remove(ceh);
        }
    }
}
