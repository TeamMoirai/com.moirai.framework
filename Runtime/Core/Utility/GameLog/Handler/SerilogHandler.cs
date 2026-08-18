#if SERILOG_INSTALLED
using System;
using Serilog;
using Serilog.Events;

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
        private ILogger _logger;

        /// <summary>
        /// 获取或设置 Serilog 日志实例，null 时回退到全局 <see cref="Serilog.Log.Logger"/>。
        /// </summary>
        public ILogger Logger
        {
            get => _logger ?? Log.Logger;
            set => _logger = value;
        }

        /// <inheritdoc/>
        protected override void OnInit()
        {
            // 捕获全局 Logger 引用；未配置时使用静默实例，保证日志调用永不抛错。
            _logger = Log.Logger ?? Serilog.Core.Logger.None;
        }

        /// <inheritdoc/>
        protected override void OnShutdown()
        {
            _logger = null;
        }

        /// <inheritdoc/>
        public override bool IsEnabled(LogUtility.ELogLevel logLevel)
        {
            return Logger.IsEnabled(ToSerilogLevel(logLevel));
        }

        /// <inheritdoc/>
        public override void Log(LogUtility.ELogLevel logLevel, string message, Exception exception)
        {
            var logger = Logger;
            var level = ToSerilogLevel(logLevel);
            if (!logger.IsEnabled(level))
            {
                return;
            }

            // 使用固定模板 "{Message}" 传递已格式化文本，
            // 避免消息内容中的大括号被 Serilog 解析为属性占位符。
            logger.Write(level, exception, "{Message}", message ?? string.Empty);
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
