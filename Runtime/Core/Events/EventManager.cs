using UnityEngine;

namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 该类负责事件管理，可用于在整个游戏中广播事件，以通知一个（或多个）类发生了什么事。
    /// </summary>
    /// <example>
    /// <para>要从任意类触发新事件，执行 EventManager.SendEvent(YOUR_PARAMETERS);</para>
    /// 例如：触发名为 Save 的 GameEvent。
    /// <code>
    /// using var evt = GameEvent.GetPooled("Save");
    /// EventManager.SendEvent(evt);
    /// </code>
    /// 
    /// <para>捕获从游戏任意位置发出的所有 GameEvent 类型事件，并执行名为 GameOver 的操作。</para>
    /// 要开始监听其他任意类的 GameEvent 事件，必须：
    /// <code><![CDATA[
    /// 1 - 自定义事件。例如: 
    /// public class GameEvent : EventBase<GameEvent>
    /// {
    ///     public string EventName { get; private set; }
    ///
    ///     protected override void Init()
    ///     {
    ///         base.Init();
    ///         EventName = string.Empty;
    ///     }
    ///     public static GameEvent GetPooled(string eventName)
    ///     {
    ///         var evt = GetPooled();
    ///         evt.EventName = eventName;
    ///         return evt;
    ///     }
    ///     public static void Trigger(string eventName)
    ///     {
    ///         using var evt = GetPooled(eventName);
    ///         EventManager.SendEvent(evt);
    ///     }
    /// }
    /// 
    /// 2 - 在启用和禁用上，分别开始和停止监听事件：
    /// void OnEnable()
    /// {
    ///	    EventManager.RegisterCallback<GameEvent>(HandleEvent);
    /// }
    /// void OnDisable()
    /// {
    ///     EventManager.UnregisterCallback<GameEvent>(HandleEvent);
    /// }
    /// 
    /// 3 - 为该事件实现 HandleEvent。例如：
    /// public void HandleEvent(GameEvent gameEvent)
    /// {
    ///     if (gameEvent.EventName == "GameOver")
    ///	    {
    ///	        // DO SOMETHING
    ///	    }
    /// }
    /// 
    /// 4 - 触发 GameOver 事件。例如：
    /// GameEvent.Trigger("GameOver");
    /// ]]></code>
    /// </example>
    public class EventManager : MonoEventCoordinator
    {
#pragma warning disable IDE1006
        // ReSharper disable once InconsistentNaming
        private sealed class _CallbackEventHandler : CallbackEventHandler, IBehaviourScope
#pragma warning restore IDE1006
        {
            public override bool IsCompositeRoot => true;
            
            private readonly EventManager _eventCoordinator;
            
            public override IEventCoordinator Coordinator => _eventCoordinator;
            
            public MonoBehaviour Behaviour { get; }
            
            public _CallbackEventHandler(EventManager eventCoordinator)
            {
                Behaviour = eventCoordinator;
                _eventCoordinator = eventCoordinator;
            }
            
            public override void SendEvent(EventBase e, DispatchMode dispatchMode = DispatchMode.Default)
            {
                e.Target = this;
                _eventCoordinator.Dispatch(e, dispatchMode, MonoDispatchType.Update);
            }
            
            public void SendMonoEvent(EventBase e, DispatchMode dispatchMode, MonoDispatchType monoDispatchType)
            {
                e.Target = this;
                _eventCoordinator.Dispatch(e, dispatchMode, monoDispatchType);
            }
        }
        
        private static EventManager _instance;
        
        public static EventManager Instance => _instance != null ? _instance : GetInstance();
        
        private CallbackEventHandler _eventHandler;
        
        /// <summary>
        /// 获取事件系统 <see cref="CallbackEventHandler"/>
        /// </summary>
        public static CallbackEventHandler EventHandler => Instance._eventHandler;
        private static EventManager GetInstance()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return null;
#endif
            if (_instance == null)
            {
                GameObject managerObject = new GameObject { name = $"[{nameof(EventManager)}]" };
                _instance = managerObject.AddComponent<EventManager>();
                DontDestroyOnLoad(_instance);
            }
            return _instance;
        }
        
        protected override void Awake()
        {
            base.Awake();
            _eventHandler = new _CallbackEventHandler(this);
        }
        
        /// <summary>
        /// 向实例添加事件处理程序。如果已为同一阶段（TrickleDown 或 BubbleUp）注册了事件处理程序，则此方法无效。
        /// </summary>
        /// <param name="callback">要添加的事件处理程序。</param>
        /// <param name="useTrickleDown">将此参数设置为 <c>true</c> 以从 TrickleDown 阶段删除回调。将此参数设置为 <c>false</c> 以从 BubbleUp 阶段删除回调。</param>
        public static void RegisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            EventHandler.RegisterCallback(callback, useTrickleDown);
        }
        
        /// <summary>
        /// 从实例中删除回调。
        /// </summary>
        /// <param name="callback">要删除的回调。如果此回调从未注册，则不会发生任意情况。</param>
        /// <param name="useTrickleDown">将此参数设置为 <c>true</c> 以从 TrickleDown 阶段删除回调。将此参数设置为 <c>false</c> 以从 BubbleUp 阶段删除回调。</param>
        public static void UnregisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            if (!_instance) return;
            EventHandler.UnregisterCallback(callback, useTrickleDown);
        }
        
        /// <summary>
        /// 将事件发送到事件处理程序。
        /// </summary>
        /// <param name="eventBase">要发送的事件。</param>
        /// <param name="dispatchMode">事件调度模式。</param>
        /// <param name="monoDispatchType"></param>
        public static void SendEvent(EventBase eventBase, DispatchMode dispatchMode = DispatchMode.Default, MonoDispatchType monoDispatchType = MonoDispatchType.Update)
        {
            if (!_instance) return;
            ((_CallbackEventHandler)EventHandler).SendMonoEvent(eventBase, dispatchMode, monoDispatchType);
        }

        public sealed override CallbackEventHandler GetCallbackEventHandler()
        {
            return _eventHandler;
        }
    }
}