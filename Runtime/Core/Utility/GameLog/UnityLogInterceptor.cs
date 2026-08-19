using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// Unity 全局日志拦截器。实现 <see cref="ILogHandler"/>，替换 <c>Debug.unityLogger.logHandler</c> 后
    /// 拦截所有 Unity 日志（含第三方插件），将其转发至 Moirai 日志管线。
    /// <para>
    /// 通过 <see cref="LogUtility.EnableGlobalInterception"/> 启用，
    /// <see cref="LogUtility.DisableGlobalInterception"/> 禁用。
    /// </para>
    /// <para>
    /// 循环防护：各 <see cref="LogHandler"/> 实现直接输出到各自后端（如 <c>UnityEngine.Debug.Log</c>），
    /// 不经过 <c>Debug.unityLogger.logHandler</c>，因此不会形成循环。
    /// </para>
    /// </summary>
    internal sealed class UnityLogInterceptor : ILogHandler
    {
        private readonly ILogHandler _originalHandler;

        // 重入守卫：Handler 后端输出到 Debug.Log 时会回到本拦截器，
        // 通过此标志在重入时直接走 _originalHandler，避免循环。
        [NonSerialized] private static bool s_Reentering;

        /// <summary>
        /// 原始 Unity logHandler（拦截启用前的值）。
        /// </summary>
        public ILogHandler OriginalHandler => _originalHandler;

        internal UnityLogInterceptor(ILogHandler originalHandler)
        {
            _originalHandler = originalHandler ?? throw new ArgumentNullException(nameof(originalHandler));
        }

        [HideInCallstack]
        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            // 重入守卫：Handler 后端（ZLogger/DefaultLogHandler）输出到 Debug.Log 时会回到本拦截器
            if (s_Reentering)
            {
                _originalHandler.LogFormat(logType, context, format, args);
                return;
            }

            // 异常隔离——日志管线出错不能传播给第三方调用方
            try
            {
                LogUtility.ELogLevel level = ToLogLevel(logType);

                var handler = LogUtility.Handler;
                if (!handler.IsEnabled(level))
                    return;

                string message = FormatMessage(format, args);
                s_Reentering = true;
                try
                {
                    handler.Log(level, message, null, context);
                    LogUtility.RaiseMessageLogged(level, message, null);
                }
                finally
                {
                    s_Reentering = false;
                }
            }
            catch (Exception ex)
            {
                FallbackToOriginal(logType, context, format, args, ex);
            }
        }

        [HideInCallstack]
        public void LogException(Exception exception, Object context)
        {
            if (s_Reentering)
            {
                _originalHandler.LogException(exception, context);
                return;
            }

            try
            {
                var handler = LogUtility.Handler;
                if (!handler.IsEnabled(LogUtility.ELogLevel.Fatal))
                    return;

                string message = exception != null ? exception.ToString() : string.Empty;
                s_Reentering = true;
                try
                {
                    handler.Log(LogUtility.ELogLevel.Fatal, message, exception, context);
                    LogUtility.RaiseMessageLogged(LogUtility.ELogLevel.Fatal, message, exception);
                }
                finally
                {
                    s_Reentering = false;
                }
            }
            catch (Exception ex)
            {
                _originalHandler.LogException(ex, context);
            }
        }

        /// <summary>
        /// 将 Unity LogType 转换为框架 ELogLevel。
        /// </summary>
        [HideInCallstack]
        private static LogUtility.ELogLevel ToLogLevel(LogType logType)
            => logType switch
            {
                LogType.Error => LogUtility.ELogLevel.Error,
                LogType.Assert => LogUtility.ELogLevel.Error,
                LogType.Warning => LogUtility.ELogLevel.Warning,
                LogType.Exception => LogUtility.ELogLevel.Fatal,
                _ => LogUtility.ELogLevel.Info
            };

        [HideInCallstack]
        private static string FormatMessage(string format, object[] args)
        {
            if (args == null || args.Length == 0)
                return format ?? string.Empty;

            return string.Format(format, args);
        }

        [HideInCallstack]
        private void FallbackToOriginal(LogType logType, Object context, string format, object[] args, Exception ex)
        {
            try
            {
                _originalHandler.LogFormat(logType, context, format, args);
                _originalHandler.LogFormat(LogType.Warning, context,
                    "Moirai log pipeline error: {0}", ex.Message);
            }
            catch
            {
                // 彻底放弃——日志不应导致崩溃
            }
        }
    }
}
