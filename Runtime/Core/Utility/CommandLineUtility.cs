using System;
using System.Linq;

namespace Moirai.Atropos
{
    /// <summary>
    /// 命令行功能的 Helper 类
    /// </summary>
    /// <remarks>
    /// 命令行参数的格式为：
    /// <code>
    /// -key1 value1 -key2 -key3 value3
    /// </code>
    /// 详情请参阅：<a href="https://docs.unity.cn/cn/2023.2/Manual/PlayerCommandLineArguments.html">Unity 命令行参数文档</a>
    /// </remarks>
    /// <example>
    /// <para>全屏 => -screen-fullscreen 1</para>
    /// <para>分辨率 => -screen-width 1920 -screen-height 1080</para>
    /// </example>
    public static partial class CommandLineUtility
    {
        private static string[] s_Arguments;
        private static bool s_HasLog;

        /// <summary>
        /// 返回在应用程序的初始化调用中传入的 argument
        /// </summary>
        /// <remarks>
        /// <para>
        /// 对于 WebGL 和 Android 平台，这将读取应用程序的绝对 URL 并解析 URL 样式变量，例如 
        /// <code>example.com?arg1=value1&amp;arg2&amp;arg3=77</code>
        /// 产生一个数组 { arg1, value1, arg2, arg3, 77 }
        /// </para>
        /// <para>
        /// 对于所有其他平台，假设为 Environment.GetCommandLineArgs，请查看 System.Environment.GetCommandLineArgs() 的 C# 文档，了解完整详细信息。
        /// </para>
        /// </remarks>
        /// <returns>返回一个字符串数组，其中第一个元素是可执行文件的路径，其余元素是传递给程序的命令行参数。</returns>
        public static string[] CommandLineArgs
        {
            get
            {
                if (s_Arguments == null || s_Arguments.Length == 0)
                {
#if (UNITY_WEBGL || UNITY_ANDROID)
                    string parameters = UnityEngine.Application.absoluteURL.Substring(UnityEngine.Application.absoluteURL.IndexOf("?") + 1);
                    s_Arguments = parameters.Split(new char[] { '&', '=' });
#else
                    s_Arguments = Environment.GetCommandLineArgs();
#endif
                }

                if (!s_HasLog)
                {
                    Log.Info($"CommandLineArgs : {string.Join(" | ", s_Arguments)}");
                    s_HasLog = true;
                }

                return s_Arguments;
            }
        }
      
        private static string s_ArgumentLine;
        /// <summary>
        /// 返回完整的命令行
        /// </summary>
        /// <returns>返回一个字符串，包含完整的命令行，包括可执行文件的路径和所有命令行参数。</returns>
        public static string GetArgumentLine
        {
            get
            {
                if (string.IsNullOrEmpty(s_ArgumentLine))
                {
#if (UNITY_WEBGL || UNITY_ANDROID)
                    s_ArgumentLine = UnityEngine.Application.absoluteURL;
#else
                    s_ArgumentLine = Environment.CommandLine;
#endif
                }

                return s_ArgumentLine;
            }
        }

        // ------------------------------ 自定义参数 ------------------------------
        
        /// <summary>
        /// 常见的参数
        /// </summary>
        public static partial class CommonCommands
        {
            /// <summary>
            /// 强制系统语言
            /// </summary>
            public const string MODULE_FORCE_LANGUAGE = "-force-language";

            /// <summary>
            /// 显示调试信息
            /// </summary>
            public const string MODULE_SHOW_DEBUGGER = "-show-debugger";
        }
        
        /// <summary>
        /// 检查命令行中的 <see cref="CommonCommands.MODULE_FORCE_LANGUAGE"/> 参数，如果找到，则返回该语言
        /// </summary>
        /// <remarks>传入 Language Name 或 Code</remarks>
        /// <returns>如果存在，则会优先使用参数的语言</returns>
        /// <example>强制语言为汉语 => -force-language zh-Hans</example>
        public static string GetForceLanguage()
        {
            string value = "";
            for (int i = 0; i < CommandLineArgs.Length; i++)
            {
                if (CommandLineArgs[i] == CommonCommands.MODULE_FORCE_LANGUAGE && i + 1 < CommandLineArgs.Length)
                {
                    value = CommandLineArgs[i + 1];
                    return value;
                }
            }
            
            return value;
        }

        /// <summary>
        /// 如果传入了 <see cref="CommonCommands.MODULE_SHOW_DEBUGGER"/> 参数，则返回 <c>true</c>
        /// </summary>
        /// <returns></returns>
        public static bool GetShowDebugger()
        {
            return CommandLineArgs.Any(t => t == CommonCommands.MODULE_SHOW_DEBUGGER);
        }
    }
}