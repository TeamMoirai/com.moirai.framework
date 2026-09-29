using System;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架日志外观：统一的静态日志入口，后端由 <see cref="Handler"/> 替换。
    /// </summary>
    /// <remarks>
    /// 等级过滤在各 <see cref="LogHandler"/> 的 <see cref="LogHandler.Log"/> 入口按 <see cref="LogHandler.MinimumLevel"/> <br />
    /// 执行（含全局拦截器转发的三方日志）；
    /// <see cref="OnMessageLogged"/> 仅在日志通过过滤后触发。
    /// 未显式设置处理器时按编译期可用的最优后端自动选择（优先级：Unity Logging &gt; ZLogger &gt; Serilog &gt; Unity Debug）。 <br />
    /// 日志方法由 T4 模板生成，见 <c>LogUtility.LogMethods.tt</c>。
    /// </remarks>
    [HandlerHost(typeof(LogHandler))]
    public static partial class LogUtility
    {
        #region 处理器 [HANDLER]

        /// <summary>
        /// 按编译期可用的最优后端创建默认处理器。
        /// </summary>
        /// <returns>默认日志处理器实例。</returns>
        internal static LogHandler CreateDefaultHandler()
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

        private static LogHandler GetHandlerFromSettings() => GameAppSettings.LogHandler;

        #endregion

        #region 事件回调 [EVENTS]

        /// <summary>日志事件回调，每次日志经 <see cref="LogHandler.Log"/> 记录后触发。</summary>
        /// <remarks>
        /// 仅在日志通过 <see cref="LogHandler.MinimumLevel"/> 过滤后触发，被过滤的日志不触发。 <br />
        /// 可用于调试器内嵌控制台、崩溃上报、测试断言等场景。
        /// </remarks>
        internal static event Action<ELogLevel, string, Exception> OnMessageLogged;

        /// <summary>
        /// 触发日志事件回调。由 T4 生成的方法在记录日志后调用。
        /// </summary>
        /// <param name="logLevel">日志等级。</param>
        /// <param name="message">已格式化的日志内容。</param>
        /// <param name="exception">关联异常，无异常时为 null。</param>
        internal static void RaiseMessageLogged(ELogLevel logLevel, string message, Exception exception)
        {
            // 事件契约：与各 Handler 内部过滤同判定——低于 MinimumLevel 的日志不触发事件
            if (Handler.MinimumLevel > logLevel) return;

            OnMessageLogged?.Invoke(logLevel, message, exception);
        }

        #endregion

        #region 异常重载 [EXCEPTION OVERLOADS]

        /// <summary>
        /// 断言严重错误级别日志。
        /// </summary>
        /// <param name="condition">条件。</param>
        [HideInCallstack]
        public static void Assert(bool condition)
        {
            if (!condition)
            {
                Fatal("{0}\n{1}", "Assert Failed", System.Environment.StackTrace);
            }
        }

        /// <summary>
        /// 断言严重错误级别日志。
        /// </summary>
        /// <param name="condition">条件。</param>
        /// <param name="retStr">断言输出字符串。</param>
        [HideInCallstack]
        public static void Assert(bool condition, string retStr)
        {
            if (!condition)
            {
                Fatal("{0}\n{1}", "Assert Failed" + retStr, System.Environment.StackTrace);
            }
        }

        /// <summary>
        /// 打印错误级别日志，输出异常信息与堆栈。
        /// </summary>
        /// <param name="exception">日志异常。</param>
        /// <param name="context">日志关联对象（Console 点击可定位）。</param>
        [HideInCallstack]
        public static void Error(Exception exception, UObject context = null)
        {
            var msg = exception?.ToString() ?? string.Empty;
            Handler.Log(ELogLevel.Error, msg, exception, context);
            RaiseMessageLogged(ELogLevel.Error, msg, exception);
        }

        /// <summary>
        /// 打印严重错误级别日志，输出异常信息与堆栈。
        /// </summary>
        /// <param name="exception">日志异常。</param>
        /// <param name="context">日志关联对象（Console 点击可定位）。</param>
        [HideInCallstack]
        public static void Fatal(Exception exception, UObject context = null)
        {
            var msg = exception?.ToString() ?? string.Empty;
            Handler.Log(ELogLevel.Fatal, msg, exception, context);
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

        /// <summary>当前全局拦截是否已启用。</summary>
        public static bool IsGlobalInterceptionEnabled => s_Interceptor != null;

        /// <summary>
        /// 启用全局日志拦截：替换 <c>UnityEngine.Debug.unityLogger.logHandler</c>，使所有 Unity 日志（含第三方插件）经过框架日志管线。
        /// </summary>
        /// <remarks>启用后第三方插件的 <c>Debug.Log</c> 转发到当前 <see cref="Handler"/>；重复调用幂等。</remarks>
        [HideInCallstack]
        public static void EnableGlobalInterception()
        {
            if (s_Interceptor != null) return;

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

        /// <summary>
        /// 获取可直写 Unity 控制台、绕过全局拦截器的 logHandler。
        /// </summary>
        /// <remarks>
        /// 各 <see cref="LogHandler"/> 的后端输出必须经由本方法获取通道： 直接使用 <c>Debug.unityLogger</c> 会被 <see cref="UnityLogInterceptor"/> <br />
        /// 当作第三方日志重捕，造成级别前缀叠加。 <br />
        /// 与初始化顺序无关：拦截未启用时返回当前 handler，启用后返回拦截器锁定的原始 handler。
        /// </remarks>
        internal static ILogHandler GetBypassUnityHandler()
        {
            // 先读入局部再判空：若 DisableGlobalInterception（如测试的 ResetStatics）在判空与取
            // OriginalHandler 之间把 s_Interceptor 置空，直接链式访问会 NRE。
            var interceptor = s_Interceptor;
            return interceptor != null ? interceptor.OriginalHandler : UnityEngine.Debug.unityLogger.logHandler;
        }

        #endregion
    }
}
