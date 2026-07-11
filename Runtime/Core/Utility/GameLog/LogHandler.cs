using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架日志处理器基类。
    /// </summary>
    [Serializable]
    public abstract class LogHandler
    {
        /// <summary>
        /// 记录日志。
        /// </summary>
        /// <param name="level">游戏框架日志等级。</param>
        /// <param name="message">日志内容。</param>
        public abstract void Log(LogUtility.ELogLevel level, object message);
    }
}
