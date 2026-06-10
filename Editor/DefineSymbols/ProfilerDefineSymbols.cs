using UnityEditor;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// Profiler分析器宏定义操作类。
    /// </summary>
    public static class ProfilerDefineSymbols
    {
        private const string ENABLE_PROFILER_ENABLE_SYMBOL = "PROFILER_ENABLE";
        
        private const string MENU_ITEM_PATH = "Tools/Profiler Define/";
        private const int MENU_ITEM_PRIORITY = 51;

#if PROFILER_ENABLE
        /// <summary>
        /// 禁用所有分析器宏定义。
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Disable Profiler", false, MENU_ITEM_PRIORITY)]
        public static void DisableAllProfiler()
        {
            ScriptingDefineSymbols.RemoveScriptingDefineSymbol(ENABLE_PROFILER_ENABLE_SYMBOL);
        }
#else
        /// <summary>
        /// 开启所有分析器宏定义。
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Enable Profiler", false, MENU_ITEM_PRIORITY)]
        public static void EnableAllProfiler()
        {
            if (!ScriptingDefineSymbols.HasScriptingDefineSymbol(ENABLE_PROFILER_ENABLE_SYMBOL))
            {
                ScriptingDefineSymbols.AddScriptingDefineSymbol(ENABLE_PROFILER_ENABLE_SYMBOL);
            }
        }
#endif
    }
}