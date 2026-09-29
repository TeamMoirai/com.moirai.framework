using System;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// Unity 全局日志拦截器：替换 <c>Debug.unityLogger.logHandler</c> 后，把所有 Unity 日志（含第三方插件）转发至 Moirai 日志管线。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="LogUtility.EnableGlobalInterception"/> / <see cref="LogUtility.DisableGlobalInterception"/> 成对启用与禁用。 <br />
    /// 循环防护：后端输出必须经 <see cref="LogUtility.GetBypassUnityHandler"/> 直写控制台；重入守卫兜底误用被拦截通道的后端，此路径会带上已渲染的前缀，后端不应依赖它。
    /// </remarks>
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
        public void LogFormat(LogType logType, UObject context, string format, params object[] args)
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
        public void LogException(Exception exception, UObject context)
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
        private void FallbackToOriginal(LogType logType, UObject context, string format, object[] args, Exception ex)
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
