#if ZLOGGER_INSTALLED
using System;
using Microsoft.Extensions.Logging;
using UnityEngine;
using ZLogger;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Object = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于 ZLogger（com.cysharp.zlogger）的日志辅助器。
    /// <para>由 asmdef 版本定义 ZLOGGER_INSTALLED 自动启用；默认创建输出到 Unity Console 的 logger 工厂。</para>
    /// </summary>
    [Serializable]
    public sealed class ZLoggerHandler : LogHandler
    {
        [NonSerialized] private ILoggerFactory _factory;
        [NonSerialized] private ILogger _logger;

        /// <inheritdoc/>
        protected override void OnInit()
        {
            base.OnInit();

            if (_factory == null)
            {
                _factory = LoggerFactory.Create(builder =>
                {
                    // builder.SetMinimumLevel(ToZLoggerLevel(MinimumLevel));
                    // 不使用 AddZLoggerUnityDebug：其 processor 硬编码 UnityEngine.Debug.Log 输出，
                    // 会落入被全局拦截器劫持的 Debug.unityLogger，使框架自身输出二次进入日志管线。
                    builder.AddProvider(new ZLoggerBypassUnityDebugLoggerProvider(CreateUnityConsoleOptions()));
                });
                _logger = _factory.CreateLogger("Moirai");
            }
        }

        /// <summary>
        /// 创建输出到 Unity 控制台的 ZLogger 配置。
        /// <para>严重程度前缀由 formatter 模板提供：<c>{LogLevel:short}</c> 三字符记法（INF/WRN/ERR/CRI...），
        /// 与 SerilogHandler outputTemplate 的 <c>[{Level:u3}]</c> 输出一致。</para>
        /// </summary>
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
        internal override void Log(ELogLevel logLevel, string message, Exception exception, Object context = null)
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
