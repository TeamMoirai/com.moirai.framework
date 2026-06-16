using Moirai.Atropos.Editor;
using UnityEditor;

namespace Moirai.Atropos.Schedulers.Editor
{
    /// <summary>
    /// SchedulerDebugger 分析器宏定义操作类。
    /// </summary>
    public static class SchedulerDebuggerDefineSymbols
    {
        private const string STACK_TRACE_SCHEDULER_DISABLE_SYMBOL = "SCHEDULER_STACK_TRACE_DISABLE";
        
        public const string MENU_ITEM_PATH = "Tools/DefineSymbols/Scheduler/";
        private const int MENU_ITEM_PRIORITY = DefineSymbols.MENU_ITEM_PRIORITY;

#if SCHEDULER_STACK_TRACE_DISABLE
        /// <summary>
        /// 开启所有分析器宏定义。
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Enable Scheduler Debugger", false, MENU_ITEM_PRIORITY)]
        public static void EnableSchedulerDebugger()
        {
            ScriptingDefineSymbols.RemoveScriptingDefineSymbol(STACK_TRACE_SCHEDULER_DISABLE_SYMBOL);
        }
#else
        /// <summary>
        /// 禁用所有分析器宏定义。
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Disable Scheduler Debugger", false, MENU_ITEM_PRIORITY)]
        public static void DisableSchedulerDebugger()
        {
            ScriptingDefineSymbols.AddScriptingDefineSymbol(STACK_TRACE_SCHEDULER_DISABLE_SYMBOL);
        }
#endif
    }
}