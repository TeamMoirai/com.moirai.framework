#if UNITY_LOGGING_INSTALLED
using System;
using UnityEngine;
using UnityLog = Unity.Logging.Log;
using Object = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于 Unity 官方 Logging 包（com.unity.logging）的日志辅助器。
    /// <para>由 asmdef 版本定义 UNITY_LOGGING_INSTALLED 自动启用；sink、输出模板等
    /// 细节请通过包自身的 LogSettings / Logger 配置接管。</para>
    /// <para>时间戳由 Unity Logging 包的 <c>outputTemplate</c>（含 <c>{Timestamp}</c> 占位符）控制，
    /// <see cref="LogHandler.TimestampEnabled"/> / <see cref="LogHandler.TimestampFormat"/>
    /// 仅作为配置记录，实际生效需在 LogSettings 中设置。</para>
    /// </summary>
    [Serializable]
    public sealed class UnityLoggingHandler : LogHandler
    {
        /// <inheritdoc/>
        [HideInCallstack]
        public override void Log(LogUtility.ELogLevel logLevel, string message, Exception exception, Object context = null)
        {
            message ??= string.Empty;

            switch (logLevel)
            {
                case LogUtility.ELogLevel.Debug:
                    UnityLog.Debug(message);
                    break;

                case LogUtility.ELogLevel.Info:
                    UnityLog.Info(message);
                    break;

                case LogUtility.ELogLevel.Warning:
                    UnityLog.Warning(message);
                    break;

                case LogUtility.ELogLevel.Error:
                    UnityLog.Error(message);
                    break;

                case LogUtility.ELogLevel.Fatal:
                    UnityLog.Fatal(message);
                    break;

                default:
                    // 静默降级：未知等级按 Fatal 处理
                    UnityLog.Fatal(message);
                    break;
            }
        }
    }
}
#endif
