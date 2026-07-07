using System;
using System.Diagnostics;
using System.Text;
using Debug = UnityEngine.Debug;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认游戏框架日志辅助。
    /// </summary>
    public class DefaultLogHelper : LogUtility.ILogHelper
    {
        private enum ELogType
        {
            Info,
            Debug,
            Assert,
            Warning,
            Error,
            Exception,
        }

        private const ELogType FILTER_LEVEL = ELogType.Info;
        private static readonly StringBuilder s_StringBuilder = new StringBuilder(1024);

        /// <summary>
        /// 打印游戏日志。
        /// </summary>
        /// <param name="level">游戏框架日志等级。</param>
        /// <param name="message">日志信息。</param>
        /// <exception cref="GameException">游戏框架异常类。</exception>
        public void Log(LogLevel level, object message)
        {
            switch (level)
            {
                case LogLevel.Debug:
                    LogImp(ELogType.Debug, TextUtility.Format("<color=#888888>{0}</color>", message));
                    break;

                case LogLevel.Info:
                    LogImp(ELogType.Info, message.ToString());
                    break;

                case LogLevel.Warning:
                    LogImp(ELogType.Warning, message.ToString());
                    break;

                case LogLevel.Error:
                    LogImp(ELogType.Error, message.ToString());
                    break;

                case LogLevel.Fatal:
                    LogImp(ELogType.Exception, message.ToString());
                    break;

                default:
                    throw new GameException(message.ToString());
            }
        }

        /// <summary>
        /// 获取日志格式。
        /// </summary>
        /// <param name="logType">日志级别。</param>
        /// <param name="logString">日志字符。</param>
        /// <param name="bColor">是否使用颜色。</param>
        /// <returns>StringBuilder。</returns>
        private static StringBuilder GetFormatString(ELogType logType, string logString, bool bColor)
        {
            s_StringBuilder.Clear();
            switch (logType)
            {
                case ELogType.Debug:
                    s_StringBuilder.AppendFormat(
                        bColor
                            ? "<color=#CFCFCF><b>[Debug]</b></color> - <color=#00FF18>{0}</color>"
                            : "<color=#00FF18><b>[Debug]</b></color> - {0}",
                        logString);
                    break;
                case ELogType.Info:
                    s_StringBuilder.AppendFormat(
                        bColor
                            ? "<color=#CFCFCF><b>[INFO]</b></color> - <color=#CFCFCF>{0}</color>"
                            : "<color=#CFCFCF><b>[INFO]</b></color> - {0}",
                        logString);
                    break;
                case ELogType.Assert:
                    s_StringBuilder.AppendFormat(
                        bColor
                            ? "<color=#FF00BD><b>[ASSERT]</b></color> - <color=green>{0}</color>"
                            : "<color=#FF00BD><b>[ASSERT]</b></color> - {0}",
                        logString);
                    break;
                case ELogType.Warning:
                    s_StringBuilder.AppendFormat(
                        bColor
                            ? "<color=#FF9400><b>[WARNING]</b></color> - <color=yellow>{0}</color>"
                            : "<color=#FF9400><b>[WARNING]</b></color> - {0}",
                        logString);
                    break;
                case ELogType.Error:
                    s_StringBuilder.AppendFormat(
                        bColor
                            ? "<color=red><b>[ERROR]</b></color> - <color=red>{0}</color>"
                            : "<color=red><b>[ERROR]</b></color> - {0}",
                        logString);
                    break;
                case ELogType.Exception:
                    s_StringBuilder.AppendFormat(
                        bColor
                            ? "<color=red><b>[EXCEPTION]</b></color> - <color=red>{0}</color>"
                            : "<color=red><b>[EXCEPTION]</b></color> - {0}",
                        logString);
                    break;
            }

            return s_StringBuilder;
        }

        private static void LogImp(ELogType type, string logString)
        {
            if (type < FILTER_LEVEL)
            {
                return;
            }

            StringBuilder infoBuilder = GetFormatString(type, logString, true);
            string logStr = infoBuilder.ToString();

            // 获取C#堆栈,Warning以上级别日志才获取堆栈
            if (type == ELogType.Error || type == ELogType.Warning || type == ELogType.Exception)
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
                case ELogType.Info:
                case ELogType.Debug:
                    Debug.Log(logStr);
                    break;
                case ELogType.Warning:
                    Debug.LogWarning(logStr);
                    break;
                case ELogType.Assert:
                    Debug.LogAssertion(logStr);
                    break;
                case ELogType.Error:
                    Debug.LogError(logStr);
                    break;
                case ELogType.Exception:
                    throw new Exception(logStr);
            }
        }
    }
}