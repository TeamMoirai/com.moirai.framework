#if UNITY_LOGGING_INSTALLED
using System;
using UnityEngine;
using Unity.Logging;
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
        protected override void OnInit()
        {
            base.OnInit();

            // 每次 domain reload 前其 CleanupFunction 会 DeleteAllLoggers。
            // 此处幂等提前创建默认 logger（LoggerManager.Logger 已存在时不重复创建，尊重用户自定义配置），保证处理器生效后首条日志即可输出。
            DefaultSettings.CreateDefaultLogger();
        }

        /// <inheritdoc/>
        [HideInCallstack]
        internal override void Log(ELogLevel logLevel, string message, Exception exception, Object context = null)
        {
            // 指定日志等级是否启用
            if (logLevel < MinimumLevel) return;

            message ??= string.Empty;

            // 日志类型前缀：默认 Editor 控制台 sink 的 outputTemplate 仅为 "{Message}"（不含 {Level} 占位符），
            // 与其他实现类的输出对齐（ZLoggerHandler 的 {LogLevel:short}、SerilogHandler 默认模板的 [{Level:u3}]）；
            // 时间戳仍由后端 outputTemplate 的 {Timestamp} 占位符控制（见类注释）。
            string formatted = StringUtility.GetString(sb => sb.Append('[').Append(GetLevelTag(logLevel)).Append("] ").Append(message));

            // 警示：com.unity.logging 的 Log.Info/Fatal 重载由源生成器按调用点生成，且按接收者文本前缀
            // （"Log." / "Unity.Logging.Log." / 命名空间别名 + ".Log."）识别调用点。本类自身有同名方法 Log，
            // 短名 Log.Info 会绑定到方法组（CS0119），且指向类型的 using 别名（如 using X = Unity.Logging.Log）
            // 不会被生成器识别——两种写法都会让调用点静默落到兜底重载 Info(in FixedString32Bytes)，
            // 超过 32 字节的消息在调用点的隐式转换处抛 Truncation 异常。故此处必须写全名。
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
        /// 获取日志等级对应的三字符类型标签。
        /// <para>记法与 <see cref="DefaultLogHandler"/> 的级别标签一致（VRB/DBG/INF/WRN/ERR/FAT）。</para>
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
