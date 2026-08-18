using System;
using JetBrains.Annotations;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架日志处理器基类（策略模式抽象策略）。
    /// <para>框架内置四种实现，可在 Unity Debug、Unity Logging Package、Serilog、ZLogger 等日志系统之间零成本切换：</para>
    /// <list type="bullet">
    /// <item><description><see cref="DefaultLogHandler"/>：UnityEngine.Debug，始终可用（默认）。</description></item>
    /// <item><description><see cref="UnityLoggingHandler"/>：需安装 com.unity.logging（自动定义 UNITY_LOGGING_INSTALLED）。</description></item>
    /// <item><description><see cref="ZLoggerHandler"/>：需安装 com.cysharp.zlogger（自动定义 ZLOGGER_INSTALLED）。</description></item>
    /// <item><description><see cref="SerilogHandler"/>：需引入 Serilog 程序集并手动定义 SERILOG_INSTALLED。</description></item>
    /// </list>
    /// </summary>
    [Serializable]
    public abstract class LogHandler
    {
        private bool _initialized;

        /// <summary>
        /// 初始化日志处理器，重复调用幂等。
        /// </summary>
        internal void Internal_Init()
        {
            if (_initialized) return;
            _initialized = true;

            OnInit();
        }

        /// <summary>
        /// 关闭日志处理器，重复调用幂等。
        /// </summary>
        internal void Internal_Shutdown()
        {
            if (!_initialized) return;
            _initialized = false;

            OnShutdown();
        }

        /// <summary>
        /// 初始化回调，用于接管后端资源（logger 工厂、sink 等）。
        /// </summary>
        protected virtual void OnInit()
        {
        }

        /// <summary>
        /// 关闭回调，用于释放后端资源。仅在处理器被替换或应用退出时调用。
        /// </summary>
        protected virtual void OnShutdown()
        {
        }

        /// <summary>
        /// 获取指定日志等级是否启用。
        /// <para>门面（<see cref="LogUtility"/>）会在格式化消息之前调用此方法做前置过滤，
        /// 被过滤的日志不会产生任何字符串格式化开销。</para>
        /// </summary>
        /// <param name="logLevel">游戏框架日志等级。</param>
        /// <returns>等级启用返回 true，否则返回 false。</returns>
        public abstract bool IsEnabled(LogUtility.ELogLevel logLevel);

        /// <summary>
        /// 记录一条已格式化的日志。
        /// </summary>
        /// <param name="logLevel">游戏框架日志等级。</param>
        /// <param name="message">已格式化的日志内容，不为 null。</param>
        /// <param name="exception">关联异常，无异常时为 null，由各实现决定是否输出异常堆栈。</param>
        public abstract void Log(LogUtility.ELogLevel logLevel, string message, [CanBeNull] Exception exception);
    }
}
