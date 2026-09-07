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
    /// 循环防护（双保险）：各 <see cref="LogHandler"/> 实现的后端输出经由
    /// <see cref="LogUtility.GetBypassUnityHandler"/> 获取的原始通道直写控制台，不回到本拦截器；
    /// 重入守卫（<c>s_Reentering</c>）兜底——后端若误用被拦截的通道输出，重入调用直达原始 handler，
    /// 不会再次进入日志管线（但消息会带上已渲染的前缀，因此后端不应依赖兜底路径）。
    /// </para>
    /// </summary>
    internal sealed class UnityLogInterceptor : ILogHandler
    {
        private readonly ILogHandler _originalHandler;

        // 重入守卫（兜底）：后端误用被拦截的 Debug.unityLogger 输出时会回到本拦截器，
        // 通过此标志在重入时直接走 _originalHandler，避免无限循环（无法避免前缀叠加，后端应走 GetBypassUnityHandler）。
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
            // 重入守卫（兜底）：后端误用被拦截通道输出时会回到本拦截器
            if (s_Reentering)
            {
                _originalHandler.LogFormat(logType, context, format, args);
                return;
            }

            // 异常隔离——日志管线出错不能传播给第三方调用方
            try
            {
                ELogLevel level = ToLogLevel(logType);

                var handler = LogUtility.Handler;

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

                string message = exception != null ? exception.ToString() : string.Empty;
                s_Reentering = true;
                try
                {
                    handler.Log(ELogLevel.Fatal, message, exception, context);
                    LogUtility.RaiseMessageLogged(ELogLevel.Fatal, message, exception);
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
        private static ELogLevel ToLogLevel(LogType logType)
            => logType switch
            {
                LogType.Error => ELogLevel.Error,
                LogType.Assert => ELogLevel.Error,
                LogType.Warning => ELogLevel.Warning,
                LogType.Exception => ELogLevel.Fatal,
                _ => ELogLevel.Info
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
