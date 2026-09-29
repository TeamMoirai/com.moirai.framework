using System.Text.RegularExpressions;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Testing
{
    /// <summary>
    /// UTF 日志预期声明助手（PlayMode 程序集本地副本）：把「当前日志处理器对 Unity Test Framework 是否可见」的判定收在这里。
    /// </summary>
    /// <remarks>
    /// 与 EditorMode 同名支撑同构——asmdef 拓扑使跨程序集共享不可行，双副本属可接受形态；用例侧不写处理器判定、不自带 <c>#if</c>。
    /// 可见性取决于运行期生效的处理器（非编译期常量）：<c>DefaultLogHandler</c> / <c>ZLoggerHandler</c> / <c>SerilogHandler</c> 经 <c>Debug</c>
    /// 通路、UTF 可见；<c>UnityLoggingHandler</c> 直写控制台窗口、绕开该通路，不可见。
    /// <see cref="LogAssert"/> 是双向契约：可见时漏声明会因「未处理的错误日志」判红，不可见时声明会反报 "Expected log did not appear"。
    /// 只承担「消除未处理日志」——正则固定 <c>.*</c>，不耦合处理器渲染前缀。
    /// 断言日志内容请走 <see cref="LogUtility.OnMessageLogged"/>，那条通道与处理器无关。
    /// </remarks>
    internal static class UtfLogExpect
    {
        /// <summary>
        /// 为随后一条 Error 日志声明 UTF 预期；当前处理器对 UTF 不可见时不声明。
        /// </summary>
        public static void Error()
        {
#if UNITY_LOGGING_INSTALLED
            if (LogUtility.Handler is UnityLoggingHandler)
            {
                return;
            }
#endif
            LogAssert.Expect(LogType.Error, new Regex(".*"));
        }

        /// <summary>
        /// 为随后一条 Warning 日志声明 UTF 预期；当前处理器对 UTF 不可见时不声明。
        /// </summary>
        public static void Warning()
        {
#if UNITY_LOGGING_INSTALLED
            if (LogUtility.Handler is UnityLoggingHandler)
            {
                return;
            }
#endif
            LogAssert.Expect(LogType.Warning, new Regex(".*"));
        }

        /// <summary>
        /// 为随后一条 Exception 日志声明 UTF 预期；当前处理器对 UTF 不可见时不声明。
        /// </summary>
        public static void Exception()
        {
#if UNITY_LOGGING_INSTALLED
            if (LogUtility.Handler is UnityLoggingHandler)
            {
                return;
            }
#endif
            LogAssert.Expect(LogType.Exception, new Regex(".*"));
        }
    }
}
