namespace Moirai.Atropos.Events
{
    internal class EventDebuggerTrace
    {
        /// <summary>
        /// 获取事件记录信息。
        /// </summary>
        public EventDebuggerEventRecord EventBase { get; }
        /// <summary>
        /// 获取或设置事件处理耗时（毫秒）。
        /// </summary>
        public long Duration { get; set; }
        /// <summary>
        /// 获取关联的事件协调器。
        /// </summary>
        public IEventCoordinator Coordinator { get; }
        /// <summary>
        /// 创建事件处理轨迹记录。
        /// </summary>
        /// <param name="coordinator">关联的事件协调器。</param>
        /// <param name="evt">被记录的事件。</param>
        /// <param name="duration">事件处理耗时（毫秒）。</param>
        public EventDebuggerTrace(IEventCoordinator coordinator, EventBase evt, long duration)
        {
            EventBase = new EventDebuggerEventRecord(evt);
            Coordinator = coordinator;
            Duration = duration;
        }
    }
    internal class EventDebuggerCallTrace : EventDebuggerTrace
    {
        /// <summary>
        /// 获取回调的哈希码。
        /// </summary>
        public int CallbackHashCode { get; }
        /// <summary>
        /// 获取回调的显示名称。
        /// </summary>
        public string CallbackName { get; }
        /// <summary>
        /// 获取回调执行后传播是否已停止。
        /// </summary>
        public bool PropagationHasStopped { get; }
        /// <summary>
        /// 获取回调执行后是否已立即停止同元素上的后续回调。
        /// </summary>
        public bool ImmediatePropagationHasStopped { get; }
        /// <summary>
        /// 获取回调执行后是否已阻止默认行为。
        /// </summary>
        public bool DefaultHasBeenPrevented { get; }

        /// <summary>
        /// 创建回调调用轨迹记录。
        /// </summary>
        /// <param name="coordinator">关联的事件协调器。</param>
        /// <param name="evt">被记录的事件。</param>
        /// <param name="cbHashCode">回调的哈希码。</param>
        /// <param name="cbName">回调的显示名称。</param>
        /// <param name="propagationHasStopped">回调执行后传播是否已停止。</param>
        /// <param name="immediatePropagationHasStopped">回调执行后是否已立即停止同元素上的后续回调。</param>
        /// <param name="defaultHasBeenPrevented">回调执行后是否已阻止默认行为。</param>
        /// <param name="duration">回调耗时（毫秒）。</param>
        public EventDebuggerCallTrace(IEventCoordinator coordinator, EventBase evt, int cbHashCode, string cbName,
                                      bool propagationHasStopped,
                                      bool immediatePropagationHasStopped,
                                      bool defaultHasBeenPrevented,
                                      long duration)
            : base(coordinator, evt, duration)
        {
            CallbackHashCode = cbHashCode;
            CallbackName = cbName;
            PropagationHasStopped = propagationHasStopped;
            ImmediatePropagationHasStopped = immediatePropagationHasStopped;
            DefaultHasBeenPrevented = defaultHasBeenPrevented;
        }
    }

    internal class EventDebuggerDefaultActionTrace : EventDebuggerTrace
    {
        /// <summary>
        /// 获取执行默认行为时所处的传播阶段。
        /// </summary>
        public PropagationPhase Phase { get; }

        /// <summary>
        /// 获取事件目标的完整类型名。
        /// </summary>
        public string TargetName
        {
            get { return EventBase.Target.GetType().FullName; }
        }

        /// <summary>
        /// 创建默认行为执行轨迹记录。
        /// </summary>
        /// <param name="coordinator">关联的事件协调器。</param>
        /// <param name="evt">被记录的事件。</param>
        /// <param name="phase">执行默认行为时所处的传播阶段。</param>
        /// <param name="duration">默认行为执行耗时（毫秒）。</param>
        public EventDebuggerDefaultActionTrace(IEventCoordinator coordinator, EventBase evt, PropagationPhase phase, long duration)
            : base(coordinator, evt, duration)
        {
            Phase = phase;
        }
    }
    class EventDebuggerPathTrace : EventDebuggerTrace
    {
        /// <summary>
        /// 获取事件的传播路径。
        /// </summary>
        public PropagationPaths Paths { get; }

        /// <summary>
        /// 创建传播路径轨迹记录。
        /// </summary>
        /// <param name="coordinator">关联的事件协调器。</param>
        /// <param name="evt">被记录的事件。</param>
        /// <param name="paths">事件的传播路径。</param>
        public EventDebuggerPathTrace(IEventCoordinator coordinator, EventBase evt, PropagationPaths paths)
            : base(coordinator, evt, -1)
        {
            Paths = paths;
        }
    }
}
