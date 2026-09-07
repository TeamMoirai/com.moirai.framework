#if SERILOG_INSTALLED
#nullable enable
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using System;
using UnityEngine;

namespace Moirai.Atropos.Serilog
{
    /// <summary>
    /// Serilog 接收器配置扩展：提供将日志事件写入 Unity 原生日志系统的 <c>Unity3D</c> 扩展方法。
    /// </summary>
    public static class UnitySinkExtensions
    {
        /// <summary>
        /// 默认输出模板：级别 + 消息 + 异常（不包含时间戳占位符，Unity Console 自带时间列）。
        /// </summary>
        private const string DEFAULT_DEBUG_OUTPUT_TEMPLATE = "[{Level:u3}] {Message:lj}{NewLine}{Exception}";

        /// <summary>
        /// 将日志事件写入 <see cref="UnityEngine.ILogger"/>，默认使用 <see cref="UnityEngine.Debug.unityLogger"/>。
        /// </summary>
        /// <param name="sinkConfiguration">日志接收器配置。</param>
        /// <param name="restrictedToMinimumLevel">允许通过接收器的最低日志级别；指定 <paramref name="levelSwitch"/> 时忽略此参数。</param>
        /// <param name="outputTemplate">描述接收器输出格式的消息模板，默认值为 <code>"[{Level:u3}] {Message:lj}{NewLine}{Exception}"</code>。</param>
        /// <param name="formatProvider">区域性相关格式化信息，可为 <c>null</c>。</param>
        /// <param name="levelSwitch">允许在运行时动态调整最低通过级别的开关。</param>
        /// <param name="unityLogger">指定 Unity 原生日志器，默认为 <see cref="UnityEngine.Debug.unityLogger"/>。</param>
        /// <returns>支持链式调用的配置对象。</returns>
        [HideInCallstack]
        public static LoggerConfiguration Unity3D(
            this LoggerSinkConfiguration sinkConfiguration,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            string outputTemplate = DEFAULT_DEBUG_OUTPUT_TEMPLATE,
            IFormatProvider? formatProvider = null,
            LoggingLevelSwitch? levelSwitch = null,
            UnityEngine.ILogger? unityLogger = null)
        {
            if (sinkConfiguration == null) throw new ArgumentNullException(nameof(sinkConfiguration));
            if (outputTemplate == null) throw new ArgumentNullException(nameof(outputTemplate));

#pragma warning disable IDE0074 // Use compound assignment
            if (unityLogger == null) unityLogger = UnityEngine.Debug.unityLogger;
#pragma warning restore IDE0074 // Use compound assignment

            var formatter = new MessageTemplateTextFormatter(outputTemplate, formatProvider);
            return sinkConfiguration.Unity3D(formatter, restrictedToMinimumLevel, levelSwitch, unityLogger);
        }

        /// <summary>
        /// 将日志事件写入 <see cref="UnityEngine.ILogger"/>，默认使用 <see cref="UnityEngine.Debug.unityLogger"/>。
        /// </summary>
        /// <param name="sinkConfiguration">日志接收器配置。</param>
        /// <param name="formatter">控制日志事件渲染为文本的方式（例如输出 JSON）；如需控制纯文本格式，请使用接受输出模板的重载。</param>
        /// <param name="restrictedToMinimumLevel">允许通过接收器的最低日志级别；指定 <paramref name="levelSwitch"/> 时忽略此参数。</param>
        /// <param name="levelSwitch">允许在运行时动态调整最低通过级别的开关。</param>
        /// <param name="unityLogger">指定 Unity 原生日志器，默认为 <see cref="UnityEngine.Debug.unityLogger"/>。</param>
        /// <returns>支持链式调用的配置对象。</returns>
        [HideInCallstack]
        public static LoggerConfiguration Unity3D(
            this LoggerSinkConfiguration sinkConfiguration,
            ITextFormatter formatter,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            LoggingLevelSwitch? levelSwitch = null,
            UnityEngine.ILogger? unityLogger = null)
        {
            if (sinkConfiguration == null) throw new ArgumentNullException(nameof(sinkConfiguration));
            if (formatter == null) throw new ArgumentNullException(nameof(formatter));

#pragma warning disable IDE0074 // Use compound assignment
            if (unityLogger == null) unityLogger = UnityEngine.Debug.unityLogger;
#pragma warning restore IDE0074 // Use compound assignment

            return sinkConfiguration.Sink(new Unity3DLogEventSink(formatter, unityLogger), restrictedToMinimumLevel, levelSwitch);
        }
    }
}
#endif
