using System.Text.RegularExpressions;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Moirai.Atropos.Tests.EditorMode
{
    /// <summary>
    /// UTF 日志预期声明助手：把「当前日志处理器对 Unity Test Framework 是否可见」这唯一一处判定收在这里。
    ///
    /// <para><b>为什么需要判定</b>：<see cref="LogAssert"/> 是双向契约，两边都会红——
    /// 当前处理器对 UTF 可见时不声明，会因「未处理的错误日志」判红；不可见时声明，又会反报
    /// "Expected log did not appear"。而「是否可见」取决于<b>运行期</b>生效的处理器
    /// （<c>GameAppSettings.asset</c> 的 <c>m_LogHandler</c>），不是编译期常量：
    /// <c>DefaultLogHandler</c> / <c>ZLoggerHandler</c> / <c>SerilogHandler</c> 都经 <c>Debug</c> 通路，
    /// UTF 看得到；<c>UnityLoggingHandler</c> 直写控制台窗口、绕开 <c>Debug.unityLogger</c>，UTF 看不到。</para>
    ///
    /// <para><b>为什么这里有 #if</b>：<c>UnityLoggingHandler</c> 整文件包在 <c>#if UNITY_LOGGING_INSTALLED</c> 内，
    /// 未安装 com.unity.logging 的工程里该类型根本不存在，编译期提到它就会 CS0103。
    /// 判定收在本文件一处，用例侧只写一行调用，不再各自携带 <c>#if</c>。</para>
    ///
    /// <para><b>职责边界</b>：只承担「消除未处理日志」——正则固定 <c>.*</c>，不耦合处理器的渲染前缀
    /// （各后端的前缀格式不同，耦合它会成批假红）。断言日志<b>内容</b>请走
    /// <see cref="LogUtility.OnMessageLogged"/>，那条通道与处理器无关。</para>
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
    }
}
