namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 默认事件分发策略：目标元素属于当前协调器时执行标准事件传播流程，随后结束本次分发。
    /// </summary>
    public class DefaultDispatchingStrategy : IEventDispatchingStrategy
    {
        /// <summary>
        /// 获取指定事件是否可由本策略分发（始终返回 true）。
        /// </summary>
        /// <param name="evt">待分发的事件。</param>
        /// <returns>始终返回 true。</returns>
        public bool CanDispatchEvent(EventBase evt)
        {
            return true;
        }

        /// <summary>
        /// 在事件目标的协调器与当前协调器一致时执行标准事件传播，最后结束本次分发。
        /// </summary>
        /// <param name="evt">待分发的事件。</param>
        /// <param name="coordinator">事件协调器。</param>
        public void DispatchEvent(EventBase evt, IEventCoordinator coordinator)
        {
            if (evt.Target is CallbackEventHandler ve && ve.Coordinator == coordinator)
            {
                EventDispatchUtilities.PropagateEvent(evt);
            }
            evt.StopDispatch = true;
        }
    }
}