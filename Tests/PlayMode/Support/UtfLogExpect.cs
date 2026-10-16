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
    /// 与 EditorMode 同名支撑同构——asmdef 拓扑使跨程序集共享不可行，双副本属可接受形态；用例侧不写处理器判定、不自带 <c>#if</c>。 <br />
    /// 可见性取决于运行期生效的处理器（非编译期常量）：内置默认后端经 <c>Debug</c> 通路，UTF 可见。 <br />
    /// <c>plugin</c> 分支的外置后端可能绕开 <c>Debug.unityLogger</c> 直写控制台而对 UTF 不可见，但本装配看不见插件类型， <br />
    /// 「按后端类型跳过声明」这层判定已随后端一起出站到插件里。 <br />
    /// <see cref="LogAssert"/> 是双向契约：可见时漏声明会因「未处理的错误日志」判红，不可见时声明会反报 "Expected log did not appear"。
    /// 只承担「消除未处理日志」——正则固定 <c>.*</c>，不耦合处理器渲染前缀。 <br />
    /// 断言日志内容请走 <see cref="LogUtility.onMessageLogged"/>，那条通道与处理器无关。
    /// </remarks>
    internal static class UtfLogExpect
    {
        /// <summary>
        /// 为随后一条 Error 日志声明 UTF 预期。
        /// </summary>
        public static void Error()
        {
            LogAssert.Expect(LogType.Error, new Regex(".*"));
        }

        /// <summary>
        /// 为随后一条 Warning 日志声明 UTF 预期。
        /// </summary>
        public static void Warning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(".*"));
        }

        /// <summary>
        /// 为随后一条 Exception 日志声明 UTF 预期。
        /// </summary>
        public static void Exception()
        {
            LogAssert.Expect(LogType.Exception, new Regex(".*"));
        }
    }
}
