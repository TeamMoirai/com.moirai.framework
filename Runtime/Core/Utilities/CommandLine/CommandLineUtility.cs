using System;

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
        /// WebGL 与 Android 读应用绝对 URL 并解析 URL 样式参数（<c>example.com?arg1=value1&amp;arg2&amp;arg3=77</c> → { arg1, value1, arg2, arg3, 77 }）；其余平台等同 <c>Environment.GetCommandLineArgs()</c>。
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
                    LogUtility.Info($"CommandLineArgs : {string.Join(" | ", s_Arguments)}");
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
    }
}