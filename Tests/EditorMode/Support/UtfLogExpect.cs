using System.Text.RegularExpressions;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Moirai.Atropos.Tests.EditorMode
{
    /// <summary>
    /// UTF 日志预期声明助手：把「当前日志处理器对 Unity Test Framework 是否可见」这唯一一处判定收在这里。
    /// </summary>
    /// <remarks>
    /// <see cref="LogAssert"/> 是双向契约：处理器可见时不声明会因「未处理的错误日志」判红，不可见时声明会反报 "Expected log did not appear"。
    /// 可见性取决于运行期生效的处理器（<c>GameAppSettings.asset</c> 的 <c>m_LogHandler</c>）： <c>DefaultLogHandler</c> / <br />
    /// <c>ZLoggerHandler</c> / <c>SerilogHandler</c> 经 <c>Debug</c> 通路，UTF 可见； <br />
    /// <c>UnityLoggingHandler</c> 直写控制台窗口、绕开 <c>Debug.unityLogger</c>，UTF 不可见。
    /// <c>UnityLoggingHandler</c> 整文件包在 <c>#if UNITY_LOGGING_INSTALLED</c> 内，未安装 <c>com.unity.logging</c> 时该类型不存在， <br />
    /// 编译期提及即 CS0103——判定收在本文件一处，用例侧只写一行调用。 <br />
    /// 只承担「消除未处理日志」：正则固定 <c>.*</c>，不耦合处理器渲染前缀（各后端前缀格式不同，耦合它会成批假红）；断言日志内容请走 <see cref="LogUtility.OnMessageLogged"/>，那条通道与处理器无关。
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
    }
}
