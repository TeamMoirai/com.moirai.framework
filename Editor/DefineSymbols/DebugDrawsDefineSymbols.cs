using UnityEditor;

namespace Moirai.Atropos
{
    /// <summary>
    /// <see cref="DebugDrawHelper"/> 宏定义
    /// </summary>
    public static class DebugDrawsDefineSymbols
    {
        private const string MENU_ITEM_PATH = "Tools/Draws Define/";
        private const int MENU_ITEM_PRIORITY = 50;

        /// <summary>
        /// 添加用于启用显示调试绘制的菜单项
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Enable Debug Draws", false, MENU_ITEM_PRIORITY)]
        private static void EnableDebugDraws()
        {
            DebugDrawHelper.DebugDrawEnabled = true;
        }
        
        /// <summary>
        /// 判断[Enable Debug Draws]是否应显示为灰色的条件方法
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Enable Debug Draws", true)]
        private static bool EnableDebugDrawsValidation()
        {
            return !DebugDrawHelper.DebugDrawEnabled;
        }
        
        /// <summary>
        /// 添加用于禁用显示调试绘制的菜单项
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Disable Debug Draws", false, MENU_ITEM_PRIORITY + 1)]
        private static void DisableDebugDraws()
        {
            DebugDrawHelper.DebugDrawEnabled = false;
        }
        
        /// <summary>
        /// 判断[Disable Debug Draws]是否应显示为灰色的条件方法
        /// </summary>
        [MenuItem(MENU_ITEM_PATH + "Disable Debug Draws", true)]
        private static bool DisableDebugDrawsValidation()
        {
            return DebugDrawHelper.DebugDrawEnabled;
        }
    }
}
