using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架日志门面（Facade）。
    /// <para>统一的静态日志入口，通过替换 <see cref="Handler"/> 即可在
    /// Unity Debug、Unity Logging、Serilog、ZLogger 等日志系统之间零成本切换，调用方代码无需任何改动。</para>
    /// <para>所有带格式化参数的重载都会先做 <see cref="LogHandler.IsEnabled"/> 前置检查，
    /// 被过滤的日志不会产生字符串格式化开销。未显式设置处理器时，按编译期可用的最优后端自动选择
    /// （优先级：Unity Logging &gt; ZLogger &gt; Serilog &gt; Unity Debug）。</para>
    /// <para>日志方法由 T4 模板生成，见 <c>LogUtility.LogMethods.tt</c>。</para>
    /// </summary>
    public static partial class LogUtility
    {
        #region 处理器 [HANDLER]

        private static volatile LogHandler s_Handler;

        /// <summary>
        /// 获取或设置游戏框架日志处理器。线程安全；替换时旧处理器将被关闭。
        /// <para>应在启动阶段（无并发日志时）完成设置。</para>
        /// </summary>
        /// <exception cref="ArgumentNullException">value 为 null。</exception>
        public static LogHandler Handler
        {
            get
            {
                var handler = s_Handler;
                if (handler != null)
                {
                    return handler;
                }

                var created = CreateDefaultHandler();
                if (Interlocked.CompareExchange(ref s_Handler, created, null) == null)
                {
                    created.Internal_Init();
                }

                // 并发竞争失败方直接丢弃 created（尚未初始化，无需释放）。
                return s_Handler;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                value.Internal_Init();
                var previous = Interlocked.Exchange(ref s_Handler, value);
                previous?.Internal_Shutdown();
            }
        }

        /// <summary>
        /// 按编译期可用的最优后端创建默认处理器。
        /// </summary>
        /// <returns>默认日志处理器实例。</returns>
        private static LogHandler CreateDefaultHandler()
        {
#if ZLOGGER_INSTALLED
            return new ZLoggerHandler();
#elif UNITY_LOGGING_INSTALLED
            return new UnityLoggingHandler();
#elif SERILOG_INSTALLED
            return new SerilogHandler();
#else
            return new DefaultLogHandler();
#endif
        }

        #endregion

        #region 事件回调 [EVENTS]

        /// <summary>
        /// 日志事件回调。每次日志被记录后触发（在 <see cref="LogHandler.Log"/> 之后）。
        /// <para>可用于调试器内嵌控制台、崩溃上报、测试断言等场景。</para>
        /// <para>注意：仅在日志通过 <see cref="LogHandler.IsEnabled"/> 前置过滤后才会触发；
        /// 被等级过滤的日志不会触发此事件。</para>
        /// </summary>
        public static event Action<ELogLevel, string, Exception> MessageLogged;

        /// <summary>
        /// 触发日志事件回调。由 T4 生成的方法在记录日志后调用。
        /// </summary>
        /// <param name="logLevel">日志等级。</param>
        /// <param name="message">已格式化的日志内容。</param>
        /// <param name="exception">关联异常，无异常时为 null。</param>
        internal static void RaiseMessageLogged(ELogLevel logLevel, string message, Exception exception)
        {
            MessageLogged?.Invoke(logLevel, message, exception);
        }

        #endregion

        #region 异常重载 [EXCEPTION OVERLOADS]

        /// <summary>
        /// 断言严重错误级别日志。
        /// </summary>
        /// <param name="condition">条件。</param>
        [Conditional("LOG_ALL")]
        [HideInCallstack]
        public static void Assert(bool condition)
        {
            if (!condition)
            {
                Fatal(StringUtility.Format("{0}\n{1}", "Assert Failed", System.Environment.StackTrace));
            }
        }

        /// <summary>
        /// 断言严重错误级别日志。
        /// </summary>
        /// <param name="condition">条件。</param>
        /// <param name="retStr">断言输出字符串。</param>
        [Conditional("LOG_ALL")]
        [HideInCallstack]
        public static void Assert(bool condition, string retStr)
        {
            if (!condition)
            {
                Fatal(StringUtility.Format("{0}\n{1}", "Assert Failed" + retStr, System.Environment.StackTrace));
            }
        }

        /// <summary>
        /// 打印错误级别日志，输出异常信息与堆栈。
        /// </summary>
        /// <param name="exception">日志异常。</param>
        [HideInCallstack]
        public static void Error(Exception exception)
        {
            var handler = Handler;
            if (!handler.IsEnabled(ELogLevel.Error))
            {
                return;
            }

            var msg = exception?.ToString() ?? string.Empty;
            handler.Log(ELogLevel.Error, msg, exception);
            RaiseMessageLogged(ELogLevel.Error, msg, exception);
        }

        /// <summary>
        /// 打印严重错误级别日志，输出异常信息与堆栈。
        /// </summary>
        /// <param name="exception">日志异常。</param>
        [HideInCallstack]
        public static void Fatal(Exception exception)
        {
            var handler = Handler;
            if (!handler.IsEnabled(ELogLevel.Fatal))
            {
                return;
            }

            var msg = exception?.ToString() ?? string.Empty;
            handler.Log(ELogLevel.Fatal, msg, exception);
            RaiseMessageLogged(ELogLevel.Fatal, msg, exception);
        }

        #endregion

        #region 重置 [RESET]

        /// <summary>
        /// 重置所有静态状态（事件回调）。主要用于测试隔离。
        /// </summary>
        public static void ResetStatics()
        {
            MessageLogged = null;
        }

        #endregion
    }
}
