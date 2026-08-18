#if UNITY_LOGGING_INSTALLED
using System;
using UnityEngine;
using UnityLog = Unity.Logging.Log;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于 Unity 官方 Logging 包（com.unity.logging）的日志辅助器。
    /// <para>由 asmdef 版本定义 UNITY_LOGGING_INSTALLED 自动启用；sink、输出模板等
    /// 细节请通过包自身的 LogSettings / Logger 配置接管。</para>
    /// </summary>
    [Serializable]
    public sealed class UnityLoggingHandler : LogHandler
    {
        [SerializeField] private LogUtility.ELogLevel m_MinimumLevel = LogUtility.ELogLevel.Debug;

        /// <summary>
        /// 获取或设置最小日志等级，低于该等级的日志将由辅助器直接丢弃
        /// （包级别的最低等级仍由 LogSettings 控制）。
        /// </summary>
        public LogUtility.ELogLevel MinimumLevel
        {
            get => m_MinimumLevel;
            set => m_MinimumLevel = value;
        }

        /// <inheritdoc/>
        public override bool IsEnabled(LogUtility.ELogLevel logLevel)
        {
            return logLevel >= m_MinimumLevel;
        }

        /// <inheritdoc/>
        public override void Log(LogUtility.ELogLevel logLevel, string message, Exception exception)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

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
