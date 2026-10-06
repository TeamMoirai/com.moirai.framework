using System;
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
    /// 只承担「消除未处理日志」：正则固定 <c>.*</c>，不耦合处理器渲染前缀（各后端前缀格式不同，耦合它会成批假红）；断言日志内容请走 <see cref="LogUtility.OnMessageLogged"/>，那条通道与处理器无关。 <br />
    /// <see cref="ScopedIgnore"/> 是唯一允许触碰 <c>LogAssert.ignoreFailingMessages</c> 的入口（故障注入窗口，语义见其 remarks）。
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
        /// 为随后一条 Exception 级日志声明 UTF 预期；当前处理器对 UTF 不可见时不声明。
        /// </summary>
        /// <remarks>
        /// 覆盖「带异常对象」的 Fatal/LogException 通路（<c>DefaultLogHandler</c> 的 <c>Fatal(Exception)</c> 走 <c>Debug.LogException</c>）。
        /// </remarks>
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

        /// <summary>
        /// 打开「未处理日志不判红」窗口并返回还原句柄；<c>using</c> 结束时还原进入窗口前的开关值。
        /// </summary>
        /// <remarks>
        /// 供错误集不可枚举的故障注入夹具使用：窗口内<b>所有</b>未处理日志（含真缺陷的）都不判红， <br />
        /// 相当于放弃「意外错误也要红」这层信号——只在夹具确实不需要该信号时选用；错误集可枚举的用例逐条 <see cref="Error"/>/<see cref="Warning"/>。 <br />
        /// <see cref="LogAssert.ignoreFailingMessages"/> 是全局静态，<c>using</c> 形态保证还原路径必然执行（等价快照 + try/finally）。
        /// </remarks>
        public static IDisposable ScopedIgnore()
        {
            return new ScopedIgnoreToken();
        }

        /// <summary>窗口句柄：构造时快照并打开开关，<see cref="IDisposable.Dispose"/> 还原快照值。</summary>
        private sealed class ScopedIgnoreToken : IDisposable
        {
            private readonly bool _previous;

            public ScopedIgnoreToken()
            {
                _previous = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;
            }

            public void Dispose()
            {
                LogAssert.ignoreFailingMessages = _previous;
            }
        }

        /// <summary>
        /// 为随后一条「带异常对象的 Error 日志」声明 UTF 预期，级别判定同样收在这唯一一处。
        /// </summary>
        /// <remarks>
        /// LogUtility 的 Error(Exception) 重载在不同处理器下落成不同级别：<c>ZLoggerHandler</c> 的旁路只要条目带异常就转 <c>LogType.Exception</c> <br />
        /// （见 <c>ZLoggerBypassUnityDebugLoggerProvider</c> 的 AsUnityLogType 分支），而 <c>DefaultLogHandler</c> 的 Error 分支仍是 <c>LogType.Error</c>。 <br />
        /// 级别由处理器自述（<c>LogHandler.ErrorWithExceptionUsesExceptionChannel</c>）：本装配拿不到后端的 <c>*_INSTALLED</c> 宏， <br />
        /// 按类型名判定的写法会被预处理裁掉而恒走 Error 分支——实测假红过一次。
        /// </remarks>
        /// <param name="fragment">日志正文需匹配的片段（正则）。</param>
        public static void ErrorWithException(string fragment)
        {
#if UNITY_LOGGING_INSTALLED
            if (LogUtility.Handler is UnityLoggingHandler)
            {
                return;
            }
#endif
            LogType type = LogUtility.Handler != null && LogUtility.Handler.ErrorWithExceptionUsesExceptionChannel
                ? LogType.Exception
                : LogType.Error;
            LogAssert.Expect(type, new Regex(fragment));
        }
    }
}
