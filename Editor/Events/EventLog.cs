using System.Collections.Generic;

namespace Moirai.Atropos.Events.Editor
{
    /// <summary>
    /// 事件日志，按顺序保存事件调试器记录的 <see cref="EventLogLine"/> 行。
    /// </summary>
    class EventLog
    {
        /// <summary>
        /// 获取日志行列表。
        /// </summary>
        public List<EventLogLine> lines { get; } = new List<EventLogLine>();

        /// <summary>
        /// 使用给定的日志行创建事件日志。
        /// </summary>
        /// <param name="eventLogLines">初始日志行。</param>
        public EventLog(params EventLogLine[] eventLogLines)
        {
            lines.AddRange(eventLogLines);
        }

        /// <summary>
        /// 向日志追加一行。
        /// </summary>
        /// <param name="eventLogLine">要追加的日志行。</param>
        public void AddLine(EventLogLine eventLogLine)
        {
            lines.Add(eventLogLine);
        }

        /// <summary>
        /// 清空全部日志行。
        /// </summary>
        public void Clear()
        {
            lines.Clear();
        }
    }
}
