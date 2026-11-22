using System;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 页面级导航：history 去重回溯——Back 关当前顶窗并恢复上一页，BackTo 回溯到指定页。
    /// </summary>
    /// <remarks>
    /// 基于窗口开启序历史（<see cref="UIWindowLedger"/> 的 history 栈，复用/重取自动挪到最新——天然去重）。 <br />
    /// Back 语义：关闭当前顶窗（走既有关闭政策），恢复 history 中前一个"当前不在开启态"的窗口（复用停放实例）。 <br />
    /// 多窗并存：Back 会跳过 history 中仍然开启的窗口，只恢复已停放者。 <br />
    /// 返回 false 的情形：无 history、顶窗政策拒关（模态确认未通过）、BackTo 目标不在历史中。
    /// </remarks>
    public partial class UIService
    {
        /// <summary>
        /// 返回上一页：关闭当前顶窗，并恢复开启序历史中前一个已停放的窗口。
        /// </summary>
        /// <returns>true = 导航成功；false = 无历史或顶窗拒关。</returns>
        public static bool NavigateBack()
        {
            var top = GetTopWindow();
            if (top == null) return false;
            // 目标先行判定：history 中顶窗之前的第一个已停放窗
            var previous = SharedLedger.PeekPreviousClosedHistoryWindow(top);
            if (previous == null) return false;
            // 关当前顶窗（走既有关闭政策——模态确认等）
            if (!TryCloseTopWindow()) return false;
            // 恢复目标（复用停放实例；若目标实际仍在开启态则无需重开）
            if (!SharedLedger.IsWindowOpen(previous))
            {
                ShowUI(previous.GetType(), previous.WindowId);
            }
            return true;
        }

        /// <summary>
        /// 回溯到指定窗口：逐次 Back 直到目标成为顶窗（次数上限 = 导航深度，防环）。
        /// </summary>
        /// <param name="windowId">目标窗口 ID。</param>
        /// <returns>true = 已回溯到目标；false = 目标不在历史中或中途被拒关。</returns>
        public static bool NavigateBackTo(string windowId)
        {
            int guard = NavigationDepth + 1;
            while (guard-- > 0)
            {
                var top = GetTopWindow();
                if (top == null) return false;
                if (string.Equals(top.WindowId, windowId, StringComparison.Ordinal)) return true;
                if (!NavigateBack()) return false;
            }
            return false;
        }
    }
}
