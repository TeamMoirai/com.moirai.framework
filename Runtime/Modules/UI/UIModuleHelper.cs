using UnityEngine;

namespace Moirai.Atropos.UI
{
    public static class UIModuleHelper
    {
        /// <summary>
        /// 交互是否被模态遮挡。是否存在模态遮挡（导致交互被阻止）
        /// </summary>
        /// <remarks>一般用于[非UI组件]但想与UI交互的前置判断</remarks>
        public static bool IsInteractionBlockedByModal =>
            (GameModule.Input != null && GameModule.Input.PreventInteractionUI)
            || (GameModule.UI != null && GameModule.UI.CurrentModal != null);

        /// <summary>
        /// 判断指定UI对象是否可交互
        /// </summary>
        /// <param name="uiObject">UI 所在的游戏对象</param>
        /// <param name="ignoreModal">是否忽略模态框遮挡的影响</param>
        /// <returns>true=可交互，false=不可交互</returns>
        /// <remarks>一般用于[UI组件]交互触发前的判断</remarks>
        public static bool IsUIObjectInteractable(GameObject uiObject, bool ignoreModal = false)
        {
            if (GameModule.Input == null || GameModule.Input.PreventInteractionUI) return false;
            if (GameModule.UI == null || (!ignoreModal && GameModule.UI.IsBlockedByModal(uiObject))) return false;

            return true;
        }
    }
}