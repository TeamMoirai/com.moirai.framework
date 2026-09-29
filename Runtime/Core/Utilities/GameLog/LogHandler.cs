using System;
using JetBrains.Annotations;
using Sirenix.OdinInspector;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架日志处理器基类（策略模式抽象策略），后端经 <see cref="LogUtility.Handler"/> 替换。
    /// </summary>
    /// <remarks>
    /// 内置四种实现及启用条件：<see cref="DefaultLogHandler"/>（<c>UnityEngine.Debug</c>，始终可用，默认）、
    /// <see cref="UnityLoggingHandler"/>（需安装 com.unity.logging，自动定义 <c>UNITY_LOGGING_INSTALLED</c>）、
    /// <see cref="ZLoggerHandler"/>（需安装 com.cysharp.zlogger，自动定义 <c>ZLOGGER_INSTALLED</c>）、
    /// <see cref="SerilogHandler"/>（需引入 Serilog 程序集并手动定义 <c>SERILOG_INSTALLED</c>）。
    /// </remarks>
    [Serializable]
    public abstract class LogHandler : FrameworkHandler
    {
        [Tooltip("最小日志等级，低于该等级的日志将被丢弃。")]
        [SerializeField] private ELogLevel m_MinimumLevel = ELogLevel.Verbose;
        /// <summary>
        /// 获取或设置最小日志等级，低于该等级的日志将被丢弃。
        /// </summary>
        internal ELogLevel MinimumLevel
        {
            get
            {
#if !UNITY_EDITOR
                if (m_MinimumLevel < ELogLevel.Info) return ELogLevel.Info;
#endif
                return m_MinimumLevel;
            }
            set => m_MinimumLevel = value;
        }

        [Space]
        [SerializeField] private bool m_TimestampEnabled;
        [ShowIf(nameof(m_TimestampEnabled))]
        [SerializeField] private string m_TimestampFormat = "HH:mm:ss.fff";

        /// <summary>
        /// 获取或设置是否在日志输出中包含时间戳。
        /// </summary>
        /// <remarks>
        /// 各实现经后端自身的模板/格式化系统应用：<see cref="DefaultLogHandler"/> 在消息前缀拼接 <c>[HH:mm:ss.fff]</c>；
        /// <see cref="ZLoggerHandler"/> 经 <c>PrefixFormatter</c>； <br />
        /// <see cref="UnityLoggingHandler"/> 与 <see cref="SerilogHandler"/> 由后端 outputTemplate 的 <c>{Timestamp}</c> 占位符控制。
        /// </remarks>
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

        /// <inheritdoc/>
        protected override void OnInit()
        {
            base.OnInit();

            LogUtility.EnableGlobalInterception();
        }

        /// <summary>
        /// 记录一条已格式化的日志。
        /// </summary>
        /// <param name="logLevel">游戏框架日志等级。</param>
        /// <param name="message">已格式化的日志内容，不为 null。</param>
        /// <param name="exception">关联异常，无异常时为 null，由各实现决定是否输出异常堆栈。</param>
        /// <param name="context">日志关联对象（可选，Console 点击可定位到该对象）。</param>
        internal abstract void Log(ELogLevel logLevel, string message, [CanBeNull] Exception exception, UObject context = null);
    }
}
