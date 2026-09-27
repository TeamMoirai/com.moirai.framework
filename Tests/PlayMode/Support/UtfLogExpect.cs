using System.Text.RegularExpressions;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Testing
{
    /// <summary>
    /// UTF 日志预期声明助手（PlayMode 程序集本地副本）：把「当前日志处理器对 Unity Test Framework
    /// 是否可见」这唯一一处判定收在这里。
    /// <para>与 EditorMode 的同名支撑同构——跨程序集不可共享（asmdef 拓扑），双副本属可接受形态。
    /// 「一律经 UtfLogExpect」因此对 PlayMode 可执行：用例侧不写处理器判定、不自带 #if。</para>
    ///
    /// <para><b>为什么需要判定</b>：<see cref="LogAssert"/> 是双向契约，两边都会红——当前处理器对 UTF
    /// 可见时不声明，会因「未处理的错误日志」判红；不可见时声明，又会反报 "Expected log did not
    /// appear"。而「是否可见」取决于<b>运行期</b>生效的处理器（GameAppSettings 的 m_LogHandler），
    /// 不是编译期常量：<c>DefaultLogHandler</c> / <c>ZLoggerHandler</c> / <c>SerilogHandler</c> 都经
    /// <c>Debug</c> 通路，UTF 看得到；<c>UnityLoggingHandler</c> 直写控制台窗口、绕开 Debug 通路，看不到。</para>
    ///
    /// <para><b>职责边界</b>：只承担「消除未处理日志」——正则固定 <c>.*</c>，不耦合处理器的渲染前缀。
    /// 断言日志<b>内容</b>请走 <see cref="LogUtility.OnMessageLogged"/>，那条通道与处理器无关。</para>
    /// </summary>
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
