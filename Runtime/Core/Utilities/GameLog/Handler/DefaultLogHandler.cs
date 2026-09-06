using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认游戏框架日志辅助器，基于 <see cref="UnityEngine.Debug"/> 输出到 Unity 控制台。
    /// <para>日志仅记录、不抛异常：Fatal 等级以错误形式输出，流程是否中断由调用方自行决定。</para>
    /// </summary>
    [Serializable]
    public sealed class DefaultLogHandler : LogHandler
    {
        /// <summary>
        /// 打印游戏日志，异常对象由 Unity Console 的 Exception 通道输出。
        /// </summary>
        /// <param name="logLevel">游戏框架日志等级。</param>
        /// <param name="message">已格式化的日志内容。</param>
        /// <param name="exception">关联异常。</param>
        /// <param name="context">日志关联对象（Console 点击可定位）。</param>
        [HideInCallstack]
        public override void Log(LogUtility.ELogLevel logLevel, string message, Exception exception, Object context = null)
        {
            message ??= string.Empty;

            // 经 <see cref="LogUtility.GetBypassUnityHandler"/> 绕过全局拦截器，避免
            // Log → Debug.Log → Interceptor.LogFormat → Log 的回环与前缀叠加。
            var handler = LogUtility.GetBypassUnityHandler();

            switch (logLevel)
            {
                case LogUtility.ELogLevel.Debug:
                    handler.LogFormat(LogType.Log, context, "{0}", GetFormatString("DEBUG", message, "#CFCFCF", "#00FF18", wrapBody: true));
                    break;

                case LogUtility.ELogLevel.Info:
                    handler.LogFormat(LogType.Log, context, "{0}", GetFormatString("INFO", message, "#CFCFCF", null, wrapBody: false));
                    break;

                case LogUtility.ELogLevel.Warning:
                    handler.LogFormat(LogType.Warning, context, "{0}", GetFormatString("WARNING", message, "#FF9400", "yellow", wrapBody: true));
                    break;

                case LogUtility.ELogLevel.Error:
                    handler.LogFormat(LogType.Error, context, "{0}", GetFormatString("ERROR", message, "red", "red", wrapBody: true));
                    break;

                case LogUtility.ELogLevel.Fatal:
                    if (exception != null)
                        handler.LogException(exception, context);
                    else
                        handler.LogFormat(LogType.Error, context, "{0}", GetFormatString("FATAL", message, "red", "red", wrapBody: true));
                    break;

                default:
                    handler.LogFormat(LogType.Error, context, "{0}", GetFormatString("FATAL", message, "red", "red", wrapBody: true));
                    break;
            }
        }

        /// <summary>
        /// 获取带级别前缀着色的日志格式。
        /// </summary>
        /// <param name="tag">级别标签。</param>
        /// <param name="message">日志正文。</param>
        /// <param name="tagColor">标签颜色。</param>
        /// <param name="bodyColor">正文颜色，null 时不着色。</param>
        /// <param name="wrapBody">是否对正文逐行包裹颜色标签。</param>
        /// <returns>格式化后的日志文本。</returns>
        private string GetFormatString(string tag, string message, string tagColor, string bodyColor, bool wrapBody)
        {
            string body = wrapBody && bodyColor != null ? ColorizePerLine(message, bodyColor) : message;
            return StringUtility.Format("{3}<color={0}><b>[{1}] ► </b></color> - {2}", tagColor, tag, body, TimestampPrefix);
        }

        /// <summary>
        /// 对多行日志逐行包裹颜色标签。
        /// 单次整体包裹 <color></color> 时,Unity Console 只会为第一行着色,后续行不应用颜色,表现为乱码;
        /// 逐行包裹可让每一行都正确着色。同时兼容 \r\n 换行符。
        /// </summary>
        /// <param name="logStr">原始日志文本。</param>
        /// <param name="color">颜色字符串。</param>
        /// <returns>逐行着色后的文本。</returns>
        private static string ColorizePerLine(string logStr, string color)
        {
            if (string.IsNullOrEmpty(logStr))
            {
                return logStr;
            }

            if (logStr.IndexOf('\n') < 0)
            {
                return StringUtility.Format("<color={0}>{1}</color>", color, logStr);
            }

            return StringUtility.GetString(sb =>
            {
                int start = 0;

                for (int i = 0; i < logStr.Length; i++)
                {
                    if (logStr[i] != '\n')
                    {
                        continue;
                    }

                    // 兼容 \r\n:把回车符留在颜色标签之外,避免乱码
                    int lineEnd = i > start && logStr[i - 1] == '\r' ? i - 1 : i;
                    sb.Append("<color=").Append(color).Append('>')
                        .Append(logStr, start, lineEnd - start)
                        .Append("</color>")
                        .Append(logStr, lineEnd, i - lineEnd + 1);
                    start = i + 1;
                }

                if (start < logStr.Length)
                {
                    sb.Append("<color=").Append(color).Append('>')
                        .Append(logStr, start, logStr.Length - start)
                        .Append("</color>");
                }
            });
        }
    }
}
