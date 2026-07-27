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
            /// 强制系统语言
            /// </summary>
            public const string MODULE_FORCE_LANGUAGE = "-force-language";
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
    }
}