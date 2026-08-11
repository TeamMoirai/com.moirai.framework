using System;
using System.Diagnostics;
using System.Text;
using Debug = UnityEngine.Debug;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认游戏框架日志辅助。
    /// </summary>
    [Serializable]
    public sealed class DefaultLogHandler : LogHandler
    {
        private enum ELogLevel
        {
            Info,
            Debug,
            Assert,
            Warning,
            Error,
            Exception,
        }

        private const ELogLevel FILTER_LEVEL = ELogLevel.Info;
        private static readonly StringBuilder s_StringBuilder = new StringBuilder(1024);

        protected override void OnInit()
        {
        }

        protected override void Shutdown()
        {
        }

        /// <summary>
        /// 打印游戏日志。
        /// </summary>
        /// <param name="logLevel">游戏框架日志等级。</param>
        /// <param name="message">日志信息。</param>
        /// <exception cref="GameException">游戏框架异常类。</exception>
        public override void Log(LogUtility.ELogLevel logLevel, object message)
        {
            switch (logLevel)
            {
                case LogUtility.ELogLevel.Debug:
                    LogImp(ELogLevel.Debug, StringUtility.Format("<color=#888888>{0}</color>", message));
                    break;

                case LogUtility.ELogLevel.Info:
                    LogImp(ELogLevel.Info, message.ToString());
                    break;

                case LogUtility.ELogLevel.Warning:
                    LogImp(ELogLevel.Warning, message.ToString());
                    break;

                case LogUtility.ELogLevel.Error:
                    LogImp(ELogLevel.Error, message.ToString());
                    break;

                case LogUtility.ELogLevel.Fatal:
                    LogImp(ELogLevel.Exception, message.ToString());
                    break;

                default:
                    throw new GameException(message.ToString());
            }
        }

        /// <summary>
        /// 获取日志格式。
        /// </summary>
        /// <param name="logLevel">日志级别。</param>
        /// <param name="logString">日志字符。</param>
        /// <param name="bColor">是否使用颜色。</param>
        /// <returns>StringBuilder。</returns>
        private static StringBuilder GetFormatString(ELogLevel logLevel, string logString, bool bColor)
        {
            s_StringBuilder.Clear();

            // 多行日志需要逐行包裹颜色标签,否则Unity Console中后续行不会应用颜色(显示为秘文)
            string body = bColor ? ColorizePerLine(logString, GetBodyColor(logLevel)) : logString;
            switch (logLevel)
            {
                case ELogLevel.Debug:
                    s_StringBuilder.AppendFormat("<color=#CFCFCF><b>[Debug] ► </b></color> - {0}", body);
                    break;
                case ELogLevel.Info:
                    s_StringBuilder.AppendFormat("<color=#CFCFCF><b>[INFO] ► </b></color> - {0}", body);
                    break;
                case ELogLevel.Assert:
                    s_StringBuilder.AppendFormat("<color=#FF00BD><b>[ASSERT] ► </b></color> - {0}", body);
                    break;
                case ELogLevel.Warning:
                    s_StringBuilder.AppendFormat("<color=#FF9400><b>[WARNING] ► </b></color> - {0}", body);
                    break;
                case ELogLevel.Error:
                    s_StringBuilder.AppendFormat("<color=red><b>[ERROR] ► </b></color>- {0}", body);
                    break;
                case ELogLevel.Exception:
                    s_StringBuilder.AppendFormat("<color=red><b>[EXCEPTION] ► </b></color> - {0}", body);
                    break;
            }

            return s_StringBuilder;
        }

        /// <summary>
        /// 获取日志正文颜色。
        /// </summary>
        /// <param name="logLevel">日志级别。</param>
        /// <returns>颜色字符串。</returns>
        private static string GetBodyColor(ELogLevel logLevel)
            => logLevel switch
            {
                ELogLevel.Debug => "#00FF18",
                ELogLevel.Assert => "green",
                ELogLevel.Warning => "yellow",
                ELogLevel.Error => "red",
                ELogLevel.Exception => "red",
                _ => "#CFCFCF"
            };

        /// <summary>
        /// 对多行日志逐行包裹颜色标签。
        /// 单次整体包裹 <color></color> 时,Unity Console 只会为第一行着色,后续行不应用颜色,表现为秘文;
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

            var sb = new StringBuilder(logStr.Length + 32);
            int start = 0;

            for (int i = 0; i < logStr.Length; i++)
            {
                if (logStr[i] != '\n')
                {
                    continue;
                }

                // 兼容 \r\n:把回车符留在颜色标签之外,避免秘文
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

            return sb.ToString();
        }

        private static void LogImp(ELogLevel type, string logString)
        {
            if (type < FILTER_LEVEL)
            {
                return;
            }

            StringBuilder infoBuilder = GetFormatString(type, logString, true);
            string logStr = infoBuilder.ToString();
            
            // 获取C#堆栈,Warning以上级别日志才获取堆栈
            if (type == ELogLevel.Error || type == ELogLevel.Warning || type == ELogLevel.Exception)
            {
                StackFrame[] stackFrames = new StackTrace().GetFrames();
                // ReSharper disable once PossibleNullReferenceException
                for (int i = 0; i < stackFrames.Length; i++)
                {
                    StackFrame frame = stackFrames[i];
                    // ReSharper disable once PossibleNullReferenceException
                    string declaringTypeName = frame.GetMethod().DeclaringType.FullName;
                    string methodName = stackFrames[i].GetMethod().Name;

                    infoBuilder.AppendFormat("[{0}::{1}\n", declaringTypeName, methodName);
                }
            }
            
            switch (type)
            {
                case ELogLevel.Info:
                case ELogLevel.Debug:
                    Debug.Log(logStr);
                    break;
                case ELogLevel.Warning:
                    Debug.LogWarning(logStr);
                    break;
                case ELogLevel.Assert:
                    Debug.LogAssertion(logStr);
                    break;
                case ELogLevel.Error:
                    Debug.LogError(logStr);
                    break;
                case ELogLevel.Exception:
                    throw new Exception(logStr);
            }
        }
    }
}