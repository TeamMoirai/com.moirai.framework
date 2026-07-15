namespace Moirai.Atropos.Debugger
{
    public static partial class Constant
    {
        /// <summary>
        /// 调试器相关常量。
        /// </summary>
        public static class Debug
        {
            public const string ICON_X = "Debugger.Icon.X";
            public const string ICON_Y = "Debugger.Icon.Y";
            public const string WINDOW_X = "Debugger.Window.X";
            public const string WINDOW_Y = "Debugger.Window.Y";
            public const string WINDOW_WIDTH = "Debugger.Window.Width";
            public const string WINDOW_HEIGHT = "Debugger.Window.Height";
            public const string WINDOW_SCALE = "Debugger.Window.Scale";
        
            /// <summary>
            /// 暂停滚动
            /// </summary>
            public const string LOCK_SCROLL = "Debugger.Console.LockScroll";
            /// <summary>
            /// 筛选以显示[信息]级别日志
            /// </summary>
            public const string INFO_FILTER = "Debugger.Console.InfoFilter";
            /// <summary>
            /// 筛选以显示[警告]级别日志
            /// </summary>
            public const string WARNING_FILTER = "Debugger.Console.WarningFilter";
            /// <summary>
            /// 筛选以显示[错误]级别日志
            /// </summary>
            public const string ERROR_FILTER = "Debugger.Console.ErrorFilter";
            /// <summary>
            /// 筛选以显示[致命错误]级别日志
            /// </summary>
            public const string FATAL_FILTER = "Debugger.Console.FatalFilter";
        }
    }
}