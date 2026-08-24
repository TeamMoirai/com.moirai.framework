using System;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

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
    [HandlerHost(typeof(LogHandler))]
    public static partial class LogUtility
    {
        #region 处理器 [HANDLER]

        /// <summary>
        /// 按编译期可用的最优后端创建默认处理器。
        /// </summary>
        /// <returns>默认日志处理器实例。</returns>
        private static LogHandler CreateDefaultHandler()
        {
#if ZLOGGER_INSTALLED
            return new ZLoggerHandler();
#elif SERILOG_INSTALLED
            return new SerilogHandler();
#elif UNITY_LOGGING_INSTALLED
            return new UnityLoggingHandler();
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
        public static event Action<ELogLevel, string, Exception> OnMessageLogged;

        /// <summary>
        /// 触发日志事件回调。由 T4 生成的方法在记录日志后调用。
        /// </summary>
        /// <param name="logLevel">日志等级。</param>
        /// <param name="message">已格式化的日志内容。</param>
        /// <param name="exception">关联异常，无异常时为 null。</param>
        internal static void RaiseMessageLogged(ELogLevel logLevel, string message, Exception exception)
        {
            OnMessageLogged?.Invoke(logLevel, message, exception);
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
        /// <param name="context">日志关联对象（Console 点击可定位）。</param>
        [HideInCallstack]
        public static void Error(Exception exception, Object context = null)
        {
            var handler = Handler;
            if (!handler.IsEnabled(ELogLevel.Error))
            {
                return;
            }

            var msg = exception?.ToString() ?? string.Empty;
            handler.Log(ELogLevel.Error, msg, exception, context);
            RaiseMessageLogged(ELogLevel.Error, msg, exception);
        }

        /// <summary>
        /// 打印严重错误级别日志，输出异常信息与堆栈。
        /// </summary>
        /// <param name="exception">日志异常。</param>
        /// <param name="context">日志关联对象（Console 点击可定位）。</param>
        [HideInCallstack]
        public static void Fatal(Exception exception, Object context = null)
        {
            var handler = Handler;
            if (!handler.IsEnabled(ELogLevel.Fatal))
            {
                return;
            }

            var msg = exception?.ToString() ?? string.Empty;
            handler.Log(ELogLevel.Fatal, msg, exception, context);
            RaiseMessageLogged(ELogLevel.Fatal, msg, exception);
        }

        #endregion

        #region 重置 [RESET]

        /// <summary>
        /// 重置所有静态状态（事件回调）。主要用于测试隔离。
        /// </summary>
        public static void ResetStatics()
        {
            DisableGlobalInterception();
            OnMessageLogged = null;
        }

        #endregion

        #region 全局拦截 [GLOBAL INTERCEPTION]

        private static ILogHandler s_OriginalUnityHandler;
        private static UnityLogInterceptor s_Interceptor;

        /// <summary>
        /// 当前全局拦截是否已启用。
        /// </summary>
        public static bool IsGlobalInterceptionEnabled => s_Interceptor != null;

        /// <summary>
        /// 启用全局日志拦截：替换 <c>UnityEngine.Debug.unityLogger.logHandler</c>，使所有 Unity 日志（含第三方插件）
        /// 经过框架日志管线（级别过滤、格式化前缀、事件回调）。
        /// <para>
        /// 启用后，第三方插件的 <c>Debug.Log</c> 调用将被转发到当前 <see cref="Handler"/>。
        /// </para>
        /// </summary>
        [HideInCallstack]
        public static void EnableGlobalInterception()
        {
            if (s_Interceptor != null) return;

            // 确保 Handler 已初始化
            _ = Handler;

            var current = UnityEngine.Debug.unityLogger.logHandler;
            if (current is UnityLogInterceptor) return;

            s_OriginalUnityHandler = current;
            s_Interceptor = new UnityLogInterceptor(s_OriginalUnityHandler);
            UnityEngine.Debug.unityLogger.logHandler = s_Interceptor;
        }

        /// <summary>
        /// 禁用全局日志拦截，恢复原始 Unity logHandler。
        /// </summary>
        [HideInCallstack]
        public static void DisableGlobalInterception()
        {
            if (s_Interceptor == null) return;

            UnityEngine.Debug.unityLogger.logHandler = s_OriginalUnityHandler;
            s_Interceptor = null;
            s_OriginalUnityHandler = null;
        }

        #endregion
    }
}
