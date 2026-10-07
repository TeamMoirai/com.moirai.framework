#if UNITY_LOGGING_INSTALLED
using System;
using UnityEngine;
using Unity.Logging;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于 Unity 官方 Logging 包（com.unity.logging）的日志辅助器，由 <c>UNITY_LOGGING_INSTALLED</c> 自动启用。
    /// </summary>
    /// <remarks>
    /// sink、输出模板等细节由包自身的 <c>LogSettings</c> / <c>Logger</c> 接管。 <br />
    /// 时间戳由包的 <c>outputTemplate</c> 中 <c>{Timestamp}</c> 占位符控制； <br />
    /// <see cref="LogHandler.TimestampEnabled"/> / <see cref="LogHandler.TimestampFormat"/> 仅作配置记录， <br />
    /// 实际生效需在 <c>LogSettings</c> 中设置。
    /// </remarks>
    [ProviderDisplay(title: "Unity Logging", description: "官方 com.unity.logging 包接管 sink 与输出模板")]
    [Serializable]
    internal sealed class UnityLoggingHandler : LogHandler
    {
        /// <inheritdoc/>
        protected override void OnInit()
        {
            base.OnInit();

            // 每次 domain reload 前其 CleanupFunction 会 DeleteAllLoggers。
            // 此处幂等提前创建默认 logger（LoggerManager.Logger 已存在时不重复创建，尊重用户自定义配置），保证处理器生效后首条日志即可输出。
            DefaultSettings.CreateDefaultLogger();
        }

        /// <inheritdoc/>
        [HideInCallstack]
        internal override void Log(ELogLevel logLevel, string message, Exception exception, UObject context = null)
        {
            // 指定日志等级是否启用
            if (logLevel < MinimumLevel) return;

            message ??= string.Empty;

            // 手动拼 [级别] 前缀对齐其他 Handler 的输出（默认 sink 的 outputTemplate 不含 {Level} 占位符）；
            // 时间戳仍由后端 outputTemplate 的 {Timestamp} 占位符控制（见类注释）。
            string formatted = StringUtility.GetString(sb => sb.Append('[').Append(GetLevelTag(logLevel)).Append("] ").Append(message));

            // 必须写全名：源生成器按调用点文本识别 Log.* 重载，短名会绑定到本类同名方法组（CS0119）、
            // using 别名不被识别——两者都会静默落到 32 字节兜底重载，超长消息在调用点抛 Truncation 异常。
            switch (logLevel)
            {
                case ELogLevel.Verbose:
                    Unity.Logging.Log.Verbose(formatted);
                    break;

                case ELogLevel.Debug:
                    Unity.Logging.Log.Debug(formatted);
                    break;

                case ELogLevel.Info:
                    Unity.Logging.Log.Info(formatted);
                    break;

                case ELogLevel.Warning:
                    Unity.Logging.Log.Warning(formatted);
                    break;

                case ELogLevel.Error:
                    Unity.Logging.Log.Error(formatted);
                    break;

                case ELogLevel.Fatal:
                    Unity.Logging.Log.Fatal(formatted);
                    break;

                default:
                    // 静默降级：未知等级按 Fatal 处理
                    Unity.Logging.Log.Fatal(formatted);
                    break;
            }
        }

        /// <summary>
        /// 获取日志等级对应的三字符类型标签（VRB/DBG/INF/WRN/ERR/FAT），记法与 <see cref="DefaultLogHandler"/> 一致。
        /// </summary>
        /// <param name="logLevel">游戏框架日志等级。</param>
        /// <returns>类型标签文本。</returns>
        private static string GetLevelTag(ELogLevel logLevel)
        {
            return logLevel switch
            {
                ELogLevel.Verbose => "VRB",
                ELogLevel.Debug => "DBG",
                ELogLevel.Info => "INF",
                ELogLevel.Warning => "WRN",
                ELogLevel.Error => "ERR",
                ELogLevel.Fatal => "FAT",
                _ => "FAT"
            };
        }
    }
}
#endif
