namespace Moirai.Atropos.Events
{
    internal class DebuggerEventDispatchingStrategy : IEventDispatchingStrategy
    {

        /// <summary>
        /// 获取指定事件是否可由本策略拦截处理；仅编辑器构建下返回 true。
        /// </summary>
        /// <param name="evt">待分发的事件。</param>
        /// <returns>编辑器构建下返回 true，否则返回 false。</returns>
        public bool CanDispatchEvent(EventBase evt)
        {
#if UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// 编辑器构建下尝试拦截事件：命中调试拦截条件时停止传播并阻止默认行为。
        /// </summary>
        /// <param name="evt">待分发的事件。</param>
        /// <param name="coordinator">事件协调器。</param>
        public void DispatchEvent(EventBase evt, IEventCoordinator coordinator)
        {
#if UNITY_EDITOR
            var panelDebug = coordinator as MonoEventCoordinator;
            if (panelDebug == null) return;
            if (panelDebug.InterceptEvent(evt))
            {
                evt.StopPropagation();
                evt.PreventDefault();
                evt.StopDispatch = true;
            }
#endif
        }

        /// <summary>
        /// 编辑器构建下将事件交给调试器执行后处理流程。
        /// </summary>
        /// <param name="evt">已分发的事件。</param>
        /// <param name="coordinator">事件协调器。</param>
        public void PostDispatch(EventBase evt, IEventCoordinator coordinator)
        {
#if UNITY_EDITOR
            if (coordinator is MonoEventCoordinator monoEventCoordinator)
            {
                monoEventCoordinator.PostProcessEvent(evt);
            }
#endif
        }
    }
}
