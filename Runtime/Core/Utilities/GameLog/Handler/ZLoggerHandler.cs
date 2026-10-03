#if ZLOGGER_INSTALLED
using System;
using Microsoft.Extensions.Logging;
using UnityEngine;
using ZLogger;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于 ZLogger（com.cysharp.zlogger）的日志辅助器，由 <c>ZLOGGER_INSTALLED</c> 自动启用，默认创建输出到 Unity Console 的 logger 工厂。
    /// </summary>
    [Serializable]
    internal sealed class ZLoggerHandler : LogHandler
    {
        [NonSerialized] private ILoggerFactory _factory;
        [NonSerialized] private ILogger _logger;

        /// <inheritdoc/>
        /// <remarks>旁路 processor 只要条目带异常就调 Logger.LogException，级别随之升成 <c>LogType.Exception</c>。</remarks>
        public override bool ErrorWithExceptionUsesExceptionChannel => true;

        /// <inheritdoc/>
        protected override void OnInit()
        {
            base.OnInit();

            if (_factory == null)
            {
                _factory = LoggerFactory.Create(builder =>
                {
                    builder.SetMinimumLevel(ToZLoggerLevel(MinimumLevel));
                    // 不使用 AddZLoggerUnityDebug：其 processor 硬编码 UnityEngine.Debug.Log 输出，
                    // 会落入被全局拦截器劫持的 Debug.unityLogger，使框架自身输出二次进入日志管线。
                    builder.AddProvider(new ZLoggerBypassUnityDebugLoggerProvider(CreateUnityConsoleOptions()));
                });
                _logger = _factory.CreateLogger("Moirai");
            }
        }

        /// <summary>
        /// 创建输出到 Unity 控制台的 ZLogger 配置。
        /// </summary>
        /// <remarks>严重程度前缀由 formatter 模板的 <c>{LogLevel:short}</c> 提供，三字符记法与 <see cref="SerilogHandler"/> 的 <c>[{Level:u3}]</c> 一致。 <br />
        /// </remarks>
        private static ZLoggerOptions CreateUnityConsoleOptions()
        {
            var options = new ZLoggerOptions();
            options.UsePlainTextFormatter(formatter =>
            {
                // ZLogger 官方用法是 C#10 插值字符串 $"[{0:short}] "（自定义 handler 语法），
                // 本工程 LangVersion 9.0 不支持，手动驱动 MessageTemplateHandler 等价复现编译器生成的调用序列。
                var handler = new MessageTemplateHandler(literalLength: 3, formattedCount: 1);
                handler.AppendLiteral("[");
                handler.AppendFormatted(0, 0, "short");
                handler.AppendLiteral("] ");
                formatter.SetPrefixFormatter(handler,
                    (in MessageTemplate template, in LogInfo info) => template.Format(info.LogLevel));
            });
            return options;
        }

        /// <inheritdoc/>
        protected override void OnShutdown()
        {
            base.OnShutdown();

            _factory?.Dispose();
            _factory = null;
            _logger = null;
        }

        /// <inheritdoc/>
        [HideInCallstack]
        internal override void Log(ELogLevel logLevel, string message, Exception exception, UObject context = null)
        {
            if (_logger == null) return;

            message ??= string.Empty;

            // 时间戳前缀：直接在消息前拼接（与 SerilogHandler 同策略）；
            // 严重程度前缀由 formatter 模板的 {LogLevel:short} 提供（见 CreateUnityConsoleOptions）。
            string formatted = TimestampPrefix != null
                ? StringUtility.GetString(sb => sb.Append(TimestampPrefix).Append(message))
                : message;

            _logger.Log(ToZLoggerLevel(logLevel), default, formatted, exception, static (state, _) => state);
        }

        private static LogLevel ToZLoggerLevel(ELogLevel logLevel)
        {
            return logLevel switch
            {
                ELogLevel.Verbose => LogLevel.Trace,
                ELogLevel.Debug => LogLevel.Debug,
                ELogLevel.Info => LogLevel.Information,
                ELogLevel.Warning => LogLevel.Warning,
                ELogLevel.Error => LogLevel.Error,
                ELogLevel.Fatal => LogLevel.Critical,
                _ => LogLevel.Critical
            };
        }
    }
}
#endif
