using UnityEditor;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// <see cref="DebugDrawUtility"/> 宏定义
    /// </summary>
    public static class DebugDrawUtilityDefineSymbols
    {
        private const string MENU_ITEM_PATH = DefineSymbols.MENU_ITEM_PATH + "DebugDraw";
        private const int MENU_ITEM_PRIORITY = DefineSymbols.MENU_ITEM_PRIORITY;

        /// <summary>
        /// 添加用于启用显示调试绘制的菜单项
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Enable Debug Draw", false, MENU_ITEM_PRIORITY)]
        private static void EnableDebugDraw()
        {
            DebugDrawUtility.DebugDrawEnabled = true;
        }
        
        /// <summary>
        /// 判断[<see cref="EnableDebugDraw"/>]是否应显示为灰色的条件方法
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Enable Debug Draw", true)]
        private static bool EnableDebugDrawsValidation()
        {
            return !DebugDrawUtility.DebugDrawEnabled;
        }
        
        /// <summary>
        /// 添加用于禁用显示调试绘制的菜单项
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Disable Debug Draw", false, MENU_ITEM_PRIORITY + 1)]
        private static void DisableDebugDraw()
        {
            DebugDrawUtility.DebugDrawEnabled = false;
        }
        
        /// <summary>
        /// 判断[<see cref="DisableDebugDraw"/>]是否应显示为灰色的条件方法
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Disable Debug Draw", true)]
        private static bool DisableDebugDrawsValidation()
        {
            return DebugDrawUtility.DebugDrawEnabled;
        }
    }
}
