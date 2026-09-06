#if SERILOG_INSTALLED
using System;
using Moirai.Atropos.Serilog;
using Serilog;
using Serilog.Events;
using UnityEngine;
using ILogger = Serilog.ILogger;
using Object = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于 Serilog 的日志辅助器。
    /// <para>需通过 NuGetForUnity 等方式引入 Serilog 程序集，并手动添加 SERILOG_INSTALLED 脚本宏；
    /// 默认使用全局 <see cref="Serilog.Log.Logger"/>，请在启动阶段自行配置 sink。</para>
    /// </summary>
    [Serializable]
    public sealed class SerilogHandler : LogHandler
    {
        [NonSerialized] private ILogger _logger;

        /// <inheritdoc/>
        protected override void OnInit()
        {
            base.OnInit();

            _logger = new LoggerConfiguration()
                .WriteTo.Unity3D()
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
        public override void Log(LogUtility.ELogLevel logLevel, string message, Exception exception, Object context = null)
        {
            if (_logger == null) return;

            message ??= string.Empty;

            // 时间戳由 Serilog outputTemplate 的 {Timestamp} 占位符控制（在启动阶段配置 sink 时设定）；
            // 若 outputTemplate 未包含 {Timestamp}，则 TimestampPrefix 作为消息前缀补充。
            string formatted = TimestampPrefix != null
                ? StringUtility.GetString(sb => sb.Append(TimestampPrefix).Append(message))
                : message;

            _logger.Write(ToSerilogLevel(logLevel), exception, "{Message}", formatted);
        }

        private static LogEventLevel ToSerilogLevel(LogUtility.ELogLevel logLevel)
        {
            return logLevel switch
            {
                LogUtility.ELogLevel.Debug => LogEventLevel.Debug,
                LogUtility.ELogLevel.Info => LogEventLevel.Information,
                LogUtility.ELogLevel.Warning => LogEventLevel.Warning,
                LogUtility.ELogLevel.Error => LogEventLevel.Error,
                LogUtility.ELogLevel.Fatal => LogEventLevel.Fatal,
                _ => LogEventLevel.Fatal
            };
        }
    }
}
#endif
