#if ZLOGGER_INSTALLED
#nullable enable
using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using ZLogger;
using ZLogger.Unity;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Moirai.Atropos
{
    /// <summary>
    /// ZLogger 的 Unity 控制台输出通道（绕过全局拦截器版本）。
    /// <para>ZLogger 内置的 <c>AddZLoggerUnityDebug</c> 经由 <c>UnityEngine.Debug.Log</c> 输出，
    /// 会落入被 <see cref="UnityLogInterceptor"/> 劫持的 <c>Debug.unityLogger</c>，使框架自身输出
    /// 再次进入日志管线。本实现与内置 <c>UnityDebugLogProcessor</c> 行为一致，但直写
    /// <see cref="LogUtility.GetBypassUnityHandler"/> 返回的原始 handler。</para>
    /// <para>与内置实现的行为差异：不做 PrettyStacktrace 预处理——直写 handler 时 Unity 捕获的是
    /// 真实调用栈（管线帧已 [HideInCallstack]），无需清洗。</para>
    /// </summary>
    internal sealed class ZLoggerBypassUnityDebugLoggerProvider : ILoggerProvider, IAsyncDisposable
    {
        private readonly ZLoggerOptions _options;
        private readonly ZLoggerBypassUnityDebugProcessor _processor;

        public ZLoggerBypassUnityDebugLoggerProvider(ZLoggerOptions options)
        {
            _options = options;
            _processor = new ZLoggerBypassUnityDebugProcessor(options);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ZLoggerLogger(categoryName, _processor, _options, null);
        }

        public void Dispose()
        {
            _processor.DisposeAsync().AsTask().Wait();
        }

        public ValueTask DisposeAsync()
        {
            return _processor.DisposeAsync();
        }
    }

    /// <summary>
    /// 与 <c>ZLogger.Unity.UnityDebugLogProcessor</c> 一致的同步 processor，输出目标为绕过拦截器的 Unity logger。
    /// </summary>
    internal sealed class ZLoggerBypassUnityDebugProcessor : IAsyncLogProcessor
    {
        [ThreadStatic] private static ArrayBufferWriter<byte>? s_BufferWriter;

        private readonly IZLoggerFormatter _formatter;
        private readonly UnityEngine.ILogger _unityLogger;

        public ZLoggerBypassUnityDebugProcessor(ZLoggerOptions options)
        {
            _formatter = options.CreateFormatter();
            _unityLogger = new UnityEngine.Logger(LogUtility.GetBypassUnityHandler());
        }

        public ValueTask DisposeAsync() => default;

        [HideInCallstack]
        public void Post(IZLoggerEntry log)
        {
            try
            {
                var context = log.LogInfo.Context as UnityEngine.Object;
                // 显式 object 类型避免 (LogType, object, Object) 与 (LogType, string tag, object) 的二义性
                object message = FormatToString(log, _formatter);
                var unityLogType = log.LogInfo.AsUnityLogType();

                // 仅使用 UnityEngine.Logger 已公开的 Log(LogType, ...) / LogException 重载，
                // 与内置 processor 的 Debug.Log* 调用行为一致。
                if (unityLogType == LogType.Exception)
                {
                    // AsUnityLogType 仅在 Exception 非空时返回 LogType.Exception
                    if (context != null) _unityLogger.LogException(log.LogInfo.Exception!, context);
                    else _unityLogger.LogException(log.LogInfo.Exception!);
                }
                else if (context != null)
                {
                    _unityLogger.Log(unityLogType, message, context);
                }
                else
                {
                    _unityLogger.Log(unityLogType, message);
                }
            }
            finally
            {
                log.Return();
            }
        }

        private static string FormatToString(IZLoggerEntry entry, IZLoggerFormatter formatter)
        {
            s_BufferWriter ??= new ArrayBufferWriter<byte>();
            s_BufferWriter.Clear();

            formatter.FormatLogEntry(s_BufferWriter, entry);
            return Encoding.UTF8.GetString(s_BufferWriter.WrittenSpan);
        }
    }
}
#endif
