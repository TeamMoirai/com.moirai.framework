#if SERILOG_INSTALLED
using System;
using Moirai.Atropos.Serilog;
using Serilog;
using Serilog.Events;
using UnityEngine;
using ILogger = Serilog.ILogger;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于 Serilog 的日志辅助器。
    /// </summary>
    /// <remarks>
    /// 需经 NuGetForUnity 等方式引入 Serilog 程序集，并手动定义 <c>SERILOG_INSTALLED</c> 脚本宏。
    /// sink 由本类初始化时自建，写入绕过全局拦截器的 Unity 控制台。
    /// </remarks>
    [Serializable]
    internal sealed class SerilogHandler : LogHandler
    {
        [NonSerialized] private ILogger _logger;

        /// <inheritdoc/>
        protected override void OnInit()
        {
            base.OnInit();

            // sink 必须绕过全局拦截器：Unity3D() 默认写 Debug.unityLogger，而 base.OnInit() 已把其
            // logHandler 替换为 UnityLogInterceptor，框架自身输出会被再次捕获重新走管线，
            // 导致 outputTemplate 的 [{Level:u3}] 前缀叠加（[INF] [INF] ...）。
            _logger = new LoggerConfiguration()
                .MinimumLevel.Is(ToSerilogLevel(MinimumLevel))
                .WriteTo.Unity3D(unityLogger: new UnityEngine.Logger(LogUtility.GetBypassUnityHandler()))
                .CreateLogger();
        }

        /// <inheritdoc/>
        protected override void OnShutdown()
        {
            base.OnShutdown();

            _logger = null;
        }

        /// <inheritdoc/>
        [HideInCallstack]
        internal override void Log(ELogLevel logLevel, string message, Exception exception, UObject context = null)
        {
            if (_logger == null) return;

            message ??= string.Empty;

            // 时间戳由 Serilog outputTemplate 的 {Timestamp} 占位符控制（在启动阶段配置 sink 时设定）；
            // 若 outputTemplate 未包含 {Timestamp}，则 TimestampPrefix 作为消息前缀补充。
            string formatted = TimestampPrefix != null
                ? StringUtility.GetString(sb => sb.Append(TimestampPrefix).Append(message))
                : message;

            // 仅在带上下文时派生 logger：WithUnityObject 每次都会新建包装器，热路径不必白配。
            var logger = context != null ? _logger.WithUnityObject(context) : _logger;
            logger.Write(ToSerilogLevel(logLevel), exception, "{Message}", formatted);        }

        private static LogEventLevel ToSerilogLevel(ELogLevel logLevel)
        {
            return logLevel switch
            {
                ELogLevel.Verbose => LogEventLevel.Verbose,
                ELogLevel.Debug => LogEventLevel.Debug,
                ELogLevel.Info => LogEventLevel.Information,
                ELogLevel.Warning => LogEventLevel.Warning,
                ELogLevel.Error => LogEventLevel.Error,
                ELogLevel.Fatal => LogEventLevel.Fatal,
                _ => LogEventLevel.Fatal
            };
        }
    }
}
#endif
