#if ZLOGGER_INSTALLED
using System;
using Microsoft.Extensions.Logging;
using UnityEngine;
using ZLogger.Unity;
using ILogger = Microsoft.Extensions.Logging.ILogger;

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

        private ILoggerFactory _factory;
        private ILogger _logger;

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
            if (_factory == null)
            {
                // ZLogger 2.x：UnityLoggerFactory 已移除，统一走 Microsoft.Extensions.Logging 标准工厂，
                // AddZLoggerUnityDebug 扩展（ZLogger.Unity 命名空间）将输出接管到 Unity Console。
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
        public override void Log(LogUtility.ELogLevel logLevel, string message, Exception exception)
        {
            var logger = _logger;
            if (logger == null || !logger.IsEnabled(ToZLoggerLevel(logLevel)))
            {
                return;
            }

            // 走 Microsoft.Extensions.Logging 标准 Log<TState> 通道：
            // 预格式化文本作为状态直传，异常由 ZLogger 的 Exception 通道输出。
            logger.Log(ToZLoggerLevel(logLevel), default, message ?? string.Empty, exception,
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
