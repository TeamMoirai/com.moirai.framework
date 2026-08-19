using System;
using JetBrains.Annotations;
using Sirenix.OdinInspector;
using UnityEngine;
using Object = UnityEngine.Object;

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
        [SerializeField] private bool m_TimestampEnabled = true;
        [ShowIf(nameof(m_TimestampEnabled))]
        [SerializeField] private string m_TimestampFormat = "HH:mm:ss.fff";

        [NonSerialized] private bool _initialized;

        /// <summary>
        /// 初始化日志处理器，重复调用幂等。
        /// </summary>
        internal void Internal_Init()
        {
            if (_initialized) return;

            OnInit();
            _initialized = true;
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
        /// 获取或设置是否在日志输出中包含时间戳。
        /// <para>各实现通过后端自身的模板/格式化系统应用此配置：</para>
        /// <list type="bullet">
        /// <item><description><see cref="DefaultLogHandler"/>：在消息前缀中拼接 <c>[HH:mm:ss.fff]</c>。</description></item>
        /// <item><description><see cref="ZLoggerHandler"/>：通过 <c>PrefixFormatter</c> 设置。</description></item>
        /// <item><description><see cref="UnityLoggingHandler"/> / <see cref="SerilogHandler"/>：由后端 outputTemplate 的 <c>{Timestamp}</c> 占位符控制。</description></item>
        /// </list>
        /// </summary>
        public bool TimestampEnabled
        {
            get => m_TimestampEnabled;
            set => m_TimestampEnabled = value;
        }

        /// <summary>
        /// 获取或设置时间戳格式字符串（默认 <c>HH:mm:ss.fff</c>）。
        /// </summary>
        public string TimestampFormat
        {
            get => m_TimestampFormat;
            set => m_TimestampFormat = value;
        }

        /// <summary>
        /// 获取当前时间戳前缀字符串（含尾部空格），未启用时返回 null。
        /// </summary>
        protected string TimestampPrefix
            => m_TimestampEnabled ? StringUtility.Format("[{0}] ", DateTime.Now.ToString(m_TimestampFormat)) : null;

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
        /// <param name="context">日志关联对象（可选，Console 点击可定位到该对象）。</param>
        public abstract void Log(LogUtility.ELogLevel logLevel, string message, [CanBeNull] Exception exception, Object context = null);
    }
}
