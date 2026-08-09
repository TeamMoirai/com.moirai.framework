using System.Linq;

namespace Moirai.Atropos
{
    public static partial class CommandLineUtility
    {
        /// <summary>
        /// 常见的参数
        /// </summary>
        public static partial class CommonCommands
        {
            /// <summary>
            /// 显示调试信息
            /// </summary>
            public const string MODULE_SHOW_DEBUGGER = "-show-debugger";
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