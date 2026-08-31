using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器日志捕获器（线程安全入队 + 主线程排空的环形缓冲）。
    /// <para>订阅 <see cref="Application.logMessageReceivedThreaded"/> 捕获任意线程日志；原始字段经并发队列暂存，主线程 <see cref="Drain"/> 期间完成 <see cref="LogNode"/> 池化分配（内部读取 <see cref="Time.frameCount"/>，仅限主线程）。</para>
    /// <para>环形缓冲满时按先进先出淘汰最旧结点（归还内存池）；各级别计数在增删时增量维护，消费端零遍历。</para>
    /// </summary>
    public sealed class DebuggerLogCapture
    {
        #region 类型 [TYPES]

        /// <summary>
        /// 待排空日志条目（值类型——仅承载三个引用，避免装箱）。
        /// </summary>
        private readonly struct PendingLogEntry
        {
            public readonly LogType LogType;
            public readonly string Message;
            public readonly string StackTrace;

            public PendingLogEntry(LogType logType, string message, string stackTrace)
            {
                LogType = logType;
                Message = message;
                StackTrace = stackTrace;
            }
        }

        #endregion

        #region 字段 [FIELDS]

        private readonly ConcurrentQueue<PendingLogEntry> _pendingEntries = new ConcurrentQueue<PendingLogEntry>();
        private readonly Queue<LogNode> _nodes = new Queue<LogNode>();
        private readonly int _capacity;
        private int _infoCount;
        private int _warningCount;
        private int _errorCount;
        private int _fatalCount;
        private int _version;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化日志捕获器的新实例。
        /// </summary>
        /// <param name="capacity">环形缓冲容量（保留的最近日志条数，须为正）。</param>
        public DebuggerLogCapture(int capacity)
        {
            if (capacity <= 0)
            {
                throw new GameException("Log capture capacity is invalid.");
            }

            _capacity = capacity;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取环形缓冲容量。
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// 获取当前缓冲的日志总数。
        /// </summary>
        public int Count => _nodes.Count;

        /// <summary>
        /// 获取信息级日志计数。
        /// </summary>
        public int InfoCount => _infoCount;

        /// <summary>
        /// 获取警告级日志计数。
        /// </summary>
        public int WarningCount => _warningCount;

        /// <summary>
        /// 获取错误级日志计数。
        /// </summary>
        public int ErrorCount => _errorCount;

        /// <summary>
        /// 获取致命级（异常）日志计数。
        /// </summary>
        public int FatalCount => _fatalCount;

        /// <summary>
        /// 获取内容版本号（新日志入环或清空时递增——消费端据此节流刷新）。
        /// </summary>
        public int Version => _version;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 开始捕获（订阅 Unity 日志回调）。
        /// </summary>
        public void Start()
        {
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
        }

        /// <summary>
        /// 停止捕获（退订回调并清空缓冲）。
        /// </summary>
        public void Stop()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
            Clear();
        }

        #endregion

        #region 排空与检索 [DRAIN AND QUERY]

        /// <summary>
        /// 主线程排空并发队列（将待处理日志转入环形缓冲）。
        /// </summary>
        public void Drain()
        {
            bool contentChanged = false;
            while (_pendingEntries.TryDequeue(out PendingLogEntry entry))
            {
                LogNode node = LogNode.Create(NormalizeLogType(entry.LogType), entry.Message, entry.StackTrace);
                _nodes.Enqueue(node);
                IncrementCount(node.LogType);
                contentChanged = true;

                while (_nodes.Count > _capacity)
                {
                    LogNode evicted = _nodes.Dequeue();
                    DecrementCount(evicted.LogType);
                    MemoryPool.Release(evicted);
                }
            }

            if (contentChanged)
            {
                _version++;
            }
        }

        /// <summary>
        /// 获取缓冲的全部日志（按时间升序）。
        /// </summary>
        /// <param name="results">输出收集列表（调用前自动清空）。</param>
        public void GetRecentLogs(List<LogNode> results)
        {
            if (results == null)
            {
                LogUtility.Error("Results is invalid.");
                return;
            }

            results.Clear();
            foreach (LogNode node in _nodes)
            {
                results.Add(node);
            }
        }

        /// <summary>
        /// 枚举缓冲的全部日志（按时间升序，直接遍历内部环形缓冲——避免调用方分配临时列表）。
        /// </summary>
        /// <returns>日志结点枚举器。</returns>
        public IEnumerable<LogNode> GetLogNodes()
        {
            foreach (LogNode node in _nodes)
            {
                yield return node;
            }
        }

        /// <summary>
        /// 获取缓冲的最近日志（按时间升序，最多 count 条）。
        /// </summary>
        /// <param name="results">输出收集列表（调用前自动清空）。</param>
        /// <param name="count">要获取最近日志的数量。</param>
        public void GetRecentLogs(List<LogNode> results, int count)
        {
            if (results == null)
            {
                LogUtility.Error("Results is invalid.");
                return;
            }

            if (count <= 0)
            {
                LogUtility.Error("Count is invalid.");
                return;
            }

            int position = _nodes.Count - count;
            if (position < 0)
            {
                position = 0;
            }

            int index = 0;
            results.Clear();
            foreach (LogNode node in _nodes)
            {
                if (index++ < position)
                {
                    continue;
                }

                results.Add(node);
            }
        }

        /// <summary>
        /// 清空缓冲（结点归还内存池，计数归零）。
        /// </summary>
        public void Clear()
        {
            while (_nodes.Count > 0)
            {
                MemoryPool.Release(_nodes.Dequeue());
            }

            _infoCount = 0;
            _warningCount = 0;
            _errorCount = 0;
            _fatalCount = 0;
            _version++;
        }

        #endregion

        #region 私有 [PRIVATE]

        private void OnLogMessageReceived(string message, string stackTrace, LogType logType)
        {
            _pendingEntries.Enqueue(new PendingLogEntry(logType, message, stackTrace));
        }

        private static LogType NormalizeLogType(LogType logType)
        {
            return logType == LogType.Assert ? LogType.Error : logType;
        }

        private void IncrementCount(LogType logType)
        {
            switch (logType)
            {
                case LogType.Log:
                    _infoCount++;
                    break;

                case LogType.Warning:
                    _warningCount++;
                    break;

                case LogType.Error:
                    _errorCount++;
                    break;

                case LogType.Exception:
                    _fatalCount++;
                    break;
            }
        }

        private void DecrementCount(LogType logType)
        {
            switch (logType)
            {
                case LogType.Log:
                    _infoCount--;
                    break;

                case LogType.Warning:
                    _warningCount--;
                    break;

                case LogType.Error:
                    _errorCount--;
                    break;

                case LogType.Exception:
                    _fatalCount--;
                    break;
            }
        }

        #endregion
    }
}
