namespace Moirai.Atropos.Events.Editor
{
    /// <summary>
    /// 事件日志中的一行，记录单个事件的行号、时间戳、事件名、目标对象及原始事件数据。
    /// </summary>
    class EventLogLine
    {
        /// <summary>
        /// 获取该行在日志中的行号（从 1 开始）。
        /// </summary>
        public int LineNumber { get; }

        /// <summary>
        /// 获取事件的时间戳文本。
        /// </summary>
        public string Timestamp { get; }

        /// <summary>
        /// 获取事件名（事件基类名称）。
        /// </summary>
        public string EventName { get; }

        /// <summary>
        /// 获取事件目标对象的显示名称。
        /// </summary>
        public string Target { get; }

        /// <summary>
        /// 获取关联的原始事件记录，可为 <c>null</c>。
        /// </summary>
        public EventDebuggerEventRecord EventBase { get; }

        /// <summary>
        /// 创建一行事件日志。
        /// </summary>
        /// <param name="lineNumber">日志行号。</param>
        /// <param name="timestamp">时间戳文本，默认为空。</param>
        /// <param name="eventName">事件名，默认为空。</param>
        /// <param name="target">目标对象显示名称，默认为空。</param>
        /// <param name="eventBase">关联的原始事件记录，默认为 <c>null</c>。</param>
        public EventLogLine(int lineNumber, string timestamp = "", string eventName = "", string target = "", EventDebuggerEventRecord eventBase = null)
        {
            LineNumber = lineNumber;
            Timestamp = timestamp;
            EventName = eventName;
            Target = target;
            EventBase = eventBase;
        }
    }
}
