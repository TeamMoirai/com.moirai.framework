using System;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 日志记录结点（经 <see cref="MemoryPool"/> 池化，环形缓冲内循环复用）。
    /// </summary>
    public sealed class LogNode : MemoryObject
    {
        #region 字段 [FIELDS]

        private DateTime _logTime;
        private int _logFrameCount;
        private LogType _logType;
        private string _logMessage;
        private string _stackTrack;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化日志记录结点的新实例。
        /// </summary>
        public LogNode()
        {
            _logTime = default;
            _logFrameCount = 0;
            _logType = LogType.Error;
            _logMessage = null;
            _stackTrack = null;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取日志时间（UTC）。
        /// </summary>
        public DateTime LogTime
        {
            get
            {
                return _logTime;
            }
        }

        /// <summary>
        /// 获取日志帧计数。
        /// </summary>
        public int LogFrameCount
        {
            get
            {
                return _logFrameCount;
            }
        }

        /// <summary>
        /// 获取日志类型。
        /// </summary>
        public LogType LogType
        {
            get
            {
                return _logType;
            }
        }

        /// <summary>
        /// 获取日志内容。
        /// </summary>
        public string LogMessage
        {
            get
            {
                return _logMessage;
            }
        }

        /// <summary>
        /// 获取日志堆栈信息。
        /// </summary>
        public string StackTrack
        {
            get
            {
                return _stackTrack;
            }
        }

        #endregion

        #region 工厂 [FACTORY]

        /// <summary>
        /// 创建日志记录结点（经内存池获取并填充；仅限主线程调用——内部读取 <see cref="Time.frameCount"/>）。
        /// </summary>
        /// <param name="logType">日志类型。</param>
        /// <param name="logMessage">日志内容。</param>
        /// <param name="stackTrack">日志堆栈信息。</param>
        /// <returns>创建的日志记录结点。</returns>
        public static LogNode Create(LogType logType, string logMessage, string stackTrack)
        {
            LogNode logNode = MemoryPool.Acquire<LogNode>();
            logNode._logTime = DateTime.UtcNow;
            logNode._logFrameCount = Time.frameCount;
            logNode._logType = logType;
            logNode._logMessage = logMessage;
            logNode._stackTrack = stackTrack;
            return logNode;
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 清理日志记录结点（归还内存池前调用）。
        /// </summary>
        public override void Clear()
        {
            _logTime = default;
            _logFrameCount = 0;
            _logType = LogType.Error;
            _logMessage = null;
            _stackTrack = null;
        }

        #endregion
    }
}
