#if SERILOG_INSTALLED
#nullable enable
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using System;
using System.IO;
using UnityEngine;

namespace Moirai.Atropos.Serilog
{
    /// <summary>
    /// 将 Serilog 日志事件转发到 Unity 原生日志系统（<see cref="UnityEngine.ILogger"/>）的接收器。
    /// </summary>
    public sealed class Unity3DLogEventSink : ILogEventSink
    {
        /// <summary>
        /// 日志事件文本格式化器。
        /// </summary>
        private readonly ITextFormatter _formatter;

        /// <summary>
        /// Unity 原生日志器（最终输出到 Unity Console 的通道）。
        /// </summary>
        private readonly UnityEngine.ILogger _unityLogger;

        /// <summary>
        /// 创建 Unity 日志接收器。
        /// </summary>
        /// <param name="formatter">用于渲染日志事件的文本格式化器。</param>
        /// <param name="unityLogger">接收输出的 Unity 原生日志器。</param>
        public Unity3DLogEventSink(ITextFormatter formatter, UnityEngine.ILogger unityLogger)
        {
            _formatter = formatter;
            _unityLogger = unityLogger;
        }

        /// <summary>
        /// 输出一条日志事件：格式化为文本后按级别映射为 Unity 日志类型，并按上下文对象/标签组合选择最合适的日志重载写入。
        /// </summary>
        /// <param name="logEvent">要输出的日志事件。</param>
        [HideInCallstack]
        public void Emit(LogEvent logEvent)
        {
            using var buffer = new StringWriter();

            _formatter.Format(logEvent, buffer);
            // 将 Serilog 日志级别映射为 Unity 日志类型（Verbose/Debug/Information 归并为 Log）
            var logType = logEvent.Level switch
            {
                LogEventLevel.Verbose or LogEventLevel.Debug or LogEventLevel.Information => LogType.Log,
                LogEventLevel.Warning => LogType.Warning,
                LogEventLevel.Error or LogEventLevel.Fatal => LogType.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(logEvent.Level), "Unknown log level"),
            };

            // 去除格式化文本首尾空白，避免 Unity Console 出现多余空行
            object message = buffer.ToString().Trim();

            // 提取 UnityObjectEnricher 附加的上下文对象（若有）
            UnityEngine.Object? unityContext = null;
            if (logEvent.Properties.TryGetValue(UnityObjectEnricher.UNITY_CONTEXT_KEY, out var contextPropertyValue) && contextPropertyValue is ScalarValue contextScalarValue)
            {
                unityContext = contextScalarValue.Value as UnityEngine.Object;
            }

            // 提取 UnityTagEnricher 附加的标签（若有）
            string? unityTag = null;
            if (logEvent.Properties.TryGetValue(UnityTagEnricher.UNITY_TAG_KEY, out var tagPropertyValue) && tagPropertyValue is ScalarValue tagScalarValue)
            {
                unityTag = tagScalarValue.Value as string;
            }


            // 按上下文对象/标签的组合选择最匹配的日志重载（携带上下文时 Unity Console 可点击定位到该对象）
            if (unityContext != null)
            {
                if (unityTag != null)
                {
                    _unityLogger.Log(logType, unityTag, message, unityContext);
                }
                else
                {
                    _unityLogger.Log(logType, message, unityContext);
                }
            }
            else if (unityTag != null)
            {
                _unityLogger.Log(logType, unityTag, message);
            }
            else
            {
                _unityLogger.Log(logType, message);
            }
        }
    }
}
#endif
