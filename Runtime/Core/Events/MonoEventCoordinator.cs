using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Events
{
    public enum MonoDispatchType
    {
        Update,
        FixedUpdate = 1,
        LateUpdate = 2,
    }

    /// <summary>
    /// 基于 MonoBehaviour 的 EventCoordinator，可以启用和禁用，并且可以由调试器跟踪
    /// </summary>
    public abstract class MonoEventCoordinator : MonoBehaviour, IEventCoordinator
    {
        public virtual EventDispatcher EventDispatcher { get; protected set; }
        
        public MonoDispatchType DispatchStatus { get; private set; }
        
        private readonly HashSet<ICoordinatorDebugger> _debuggers = new HashSet<ICoordinatorDebugger>();
        private readonly Queue<EventBase> _updateQueue = new Queue<EventBase>();
        private readonly Queue<EventBase> _lateUpdateQueue = new Queue<EventBase>();
        private readonly Queue<EventBase> _fixedUpdateQueue = new Queue<EventBase>();
       
        protected virtual void Awake()
        {
            EventDispatcher = EventDispatcher.CreateDefault();
        }
        
        protected virtual void Update()
        {
            DispatchStatus = MonoDispatchType.Update;
            DrainQueue(DispatchStatus);
            EventDispatcher.PushDispatcherContext();
            EventDispatcher.PopDispatcherContext();
        }
        
        protected virtual void FixedUpdate()
        {
            DispatchStatus = MonoDispatchType.FixedUpdate;
            DrainQueue(DispatchStatus);
            EventDispatcher.PushDispatcherContext();
            EventDispatcher.PopDispatcherContext();
        }
        
        protected virtual void LateUpdate()
        {
            DispatchStatus = MonoDispatchType.LateUpdate;
            DrainQueue(DispatchStatus);
            EventDispatcher.PushDispatcherContext();
            EventDispatcher.PopDispatcherContext();
        }
        
        protected virtual void OnDestroy()
        {
            DetachAllDebuggers();
        }
        
        public void Dispatch(EventBase evt, DispatchMode dispatchMode, MonoDispatchType monoDispatchType)
        {
            if (dispatchMode == DispatchMode.Immediate || monoDispatchType == DispatchStatus)
            {
                EventDispatcher.Dispatch(evt, this, dispatchMode);
                Refresh();
            }
            else
            {
                // 获取以确保不发布
                evt.Acquire();
                GetDispatchQueue(monoDispatchType).Enqueue(evt);
            }
        }
        
        private void DrainQueue(MonoDispatchType monoDispatchType)
        {
            var queue = GetDispatchQueue(monoDispatchType);
            foreach (var evt in queue)
            {
                try
                {
                    EventDispatcher.Dispatch(evt, this, DispatchMode.Queued);
                }
                finally
                {
                    // 在将事件放入队列时平衡 Acquire。
                    evt.Dispose();
                }
            }
            queue.Clear();
            Refresh();
        }
        
        private Queue<EventBase> GetDispatchQueue(MonoDispatchType monoDispatchType)
        {
            return monoDispatchType switch
            {
                MonoDispatchType.Update => _updateQueue,
                MonoDispatchType.FixedUpdate => _fixedUpdateQueue,
                MonoDispatchType.LateUpdate => _lateUpdateQueue,
                _ => throw new ArgumentOutOfRangeException(nameof(monoDispatchType)),
            };
        }
        
        internal void AttachDebugger(ICoordinatorDebugger debugger)
        {
            if (debugger != null && _debuggers.Add(debugger))
            {
                debugger.CoordinatorDebug = this;
            }
        }
        
        internal void DetachDebugger(ICoordinatorDebugger debugger)
        {
            if (debugger != null)
            {
                debugger.CoordinatorDebug = null;
                _debuggers.Remove(debugger);
            }
        }
        
        internal void DetachAllDebuggers()
        {
            foreach (var debugger in _debuggers)
            {
                debugger.CoordinatorDebug = null;
                debugger.Disconnect();
            }
        }
        
        internal IEnumerable<ICoordinatorDebugger> GetAttachedDebuggers()
        {
            return _debuggers;
        }
        
        public void Refresh()
        {
            foreach (var debugger in _debuggers)
            {
                debugger.Refresh();
            }
        }
        
        public bool InterceptEvent(EventBase ev)
        {
            bool intercepted = false;
            foreach (var debugger in _debuggers)
            {
                intercepted |= debugger.InterceptEvent(ev);
            }
            return intercepted;
        }

        public void PostProcessEvent(EventBase ev)
        {
            foreach (var debugger in _debuggers)
            {
                debugger.PostProcessEvent(ev);
            }
        }

        public abstract CallbackEventHandler GetCallbackEventHandler();
    }
}