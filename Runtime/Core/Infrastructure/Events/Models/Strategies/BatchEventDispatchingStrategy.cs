using System.Collections.Generic;

namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 实现该接口以支持事件批量合并。
    /// </summary>
    public interface IBatchEvent
    {
        /// <summary>
        /// 创建用于承载本次批量结果的新事件实例。
        /// </summary>
        /// <returns>新创建的批量事件。</returns>
        EventBase CreateBatchedEvent();
        
        /// <summary>
        /// 将指定事件合并进批量事件。
        /// </summary>
        /// <param name="eventBase">要合并的事件。</param>
        void Batch(EventBase eventBase);
    }
    
    /// <summary>
    /// 批量事件分发策略：将实现 <see cref="IBatchEvent"/> 的事件暂存合并，在推送分发上下文时统一派发。
    /// </summary>
    public class BatchEventDispatchingStrategy : IEventDispatchingStrategy
    {
        /// <summary>
        /// 获取指定事件是否可由本策略分发（即事件实现了 <see cref="IBatchEvent"/>）。
        /// </summary>
        /// <param name="evt">待分发的事件。</param>
        /// <returns>可分发返回 true，否则返回 false。</returns>
        public bool CanDispatchEvent(EventBase evt)
        {
            return evt is IBatchEvent;
        }

        /// <summary>
        /// 将事件注册为批量事件并阻止其立即传播；已进入批处理的事件被跳过。
        /// </summary>
        /// <param name="evt">待分发的事件。</param>
        /// <param name="coordinator">事件协调器。</param>
        public void DispatchEvent(EventBase evt, IEventCoordinator coordinator)
        {
            var listener = coordinator.EventDispatcher.GetEventDispatchingListener<BatchEventDispatchingListener>();
            // 事件已进入批处理，跳过
            if (listener.ContainsEvent(evt)) return;
            evt.StopPropagation();
            evt.PreventDefault();
            evt.StopDispatch = true;
            listener.RegisterBatchEvent(evt);
        }
    }
    
    /// <summary>
    /// 批量事件分发监听器：缓存批量事件，并在推送分发上下文时统一派发。
    /// </summary>
    public class BatchEventDispatchingListener : IEventDispatchingListener
    {
        /// <summary>
        /// 初始化批量事件分发监听器实例。
        /// </summary>
        /// <param name="coordinator">关联的事件协调器。</param>
        /// <param name="dispatchMode">批量事件使用的分发模式。</param>
        public BatchEventDispatchingListener(IEventCoordinator coordinator, DispatchMode dispatchMode)
        {
            _coordinator = coordinator;
            _dispatchMode = dispatchMode;
        }
        
        private readonly IEventCoordinator _coordinator;
        
        private readonly Dictionary<long, EventBase> _batchEventMap = new Dictionary<long, EventBase>();
        
        private readonly HashSet<long> _dispatchedIds = new HashSet<long>();

        private readonly DispatchMode _dispatchMode;
        
        /// <summary>
        /// 弹出分发上下文时清空已分发事件 ID 集合。
        /// </summary>
        public virtual void OnPopDispatcherContext()
        {
            _dispatchedIds.Clear();
        }
        
        /// <summary>
        /// 推送分发上下文时统一派发并释放所有批量事件，随后清空缓存。
        /// </summary>
        public virtual void OnPushDispatcherContext()
        {
            foreach (var pair in _batchEventMap)
            {
                _dispatchedIds.Add(pair.Key);
                _coordinator.EventDispatcher.Dispatch(pair.Value, _coordinator, _dispatchMode);
                pair.Value.Dispose();
            }
            _batchEventMap.Clear();
        }
        
        internal bool ContainsEvent(EventBase eventBase)
        {
            return _dispatchedIds.Contains(eventBase.EventTypeId);
        }
        
        internal void RegisterBatchEvent(EventBase evt)
        {
            if (_batchEventMap.TryGetValue(evt.EventTypeId, out var batchedEvent))
            {
                ((IBatchEvent)batchedEvent).Batch(evt);
                return;
            }
            batchedEvent = ((IBatchEvent)evt).CreateBatchedEvent();
            batchedEvent.Acquire();
            _batchEventMap.Add(evt.EventTypeId, batchedEvent);
        }
    }
    
}