#if ZLOGGER_INSTALLED
using System;
using Microsoft.Extensions.Logging;
using UnityEngine;
using ZLogger.Unity;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Object = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于 ZLogger（com.cysharp.zlogger）的日志辅助器。
    /// <para>由 asmdef 版本定义 ZLOGGER_INSTALLED 自动启用；默认创建输出到 Unity Console 的
    /// logger 工厂，可通过 <see cref="Configure"/> 替换为文件、滚动文件、JSON 等自定义管线。</para>
    /// </summary>
    [Serializable]
    public sealed class ZLoggerHandler : LogHandler
    {
        [SerializeField] private LogUtility.ELogLevel m_MinimumLevel = LogUtility.ELogLevel.Debug;

        [NonSerialized] private ILoggerFactory _factory;
        [NonSerialized] private ILogger _logger;

        /// <summary>
        /// 获取或设置最小日志等级，低于该等级的日志将被丢弃。
        /// </summary>
        public LogUtility.ELogLevel MinimumLevel
        {
            get => m_MinimumLevel;
            set => m_MinimumLevel = value;
        }

        /// <inheritdoc/>
        protected override void OnInit()
        {
            base.OnInit();

            if (_factory == null)
            {
                _factory = LoggerFactory.Create(builder =>
                {
                    builder.SetMinimumLevel(ToZLoggerLevel(m_MinimumLevel));
                    builder.AddZLoggerUnityDebug();
                });
                _logger = _factory.CreateLogger("Moirai");
            }
        }

        /// <inheritdoc/>
        protected override void OnShutdown()
        {
            base.OnShutdown();

            _factory?.Dispose();
            _factory = null;
            _logger = null;
        }

        /// <summary>
        /// 替换底层 logger 工厂（例如接入文件 / JSON / 异步批处理管线）。
        /// 传入的工厂生命周期由本辅助器接管。
        /// </summary>
        /// <param name="factory">自定义 logger 工厂。</param>
        public void Configure(ILoggerFactory factory)
        {
            if (factory == null)
            {
                throw new GameException("ZLogger factory is invalid.");
            }

            _factory?.Dispose();
            _factory = factory;
            _logger = factory.CreateLogger("Moirai");
        }

        /// <inheritdoc/>
        public override bool IsEnabled(LogUtility.ELogLevel logLevel)
        {
            return _logger != null && _logger.IsEnabled(ToZLoggerLevel(logLevel));
        }

        /// <inheritdoc/>
        public override void Log(LogUtility.ELogLevel logLevel, string message, Exception exception, Object context = null)
        {
            var logger = _logger;
            if (logger == null || !logger.IsEnabled(ToZLoggerLevel(logLevel)))
            {
                return;
            }

            message ??= string.Empty;

            // 时间戳前缀：ZLogger 的 AddZLoggerUnityDebug 不支持 PrefixFormatter，
            // 直接在消息前拼接（与 SerilogHandler 同策略）。
            string formatted = TimestampPrefix != null
                ? StringUtility.GetString(sb => sb.Append(TimestampPrefix).Append(message))
                : message;

            logger.Log(ToZLoggerLevel(logLevel), default, formatted, exception,
                static (state, _) => state);
        }

        private static LogLevel ToZLoggerLevel(LogUtility.ELogLevel logLevel)
        {
            return logLevel switch
            {
                LogUtility.ELogLevel.Debug => LogLevel.Debug,
                LogUtility.ELogLevel.Info => LogLevel.Information,
                LogUtility.ELogLevel.Warning => LogLevel.Warning,
                LogUtility.ELogLevel.Error => LogLevel.Error,
                LogUtility.ELogLevel.Fatal => LogLevel.Critical,
                _ => LogLevel.Critical
            };
        }
    }
}
#endif
