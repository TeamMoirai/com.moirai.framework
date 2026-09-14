namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 应用焦点与输入 Enabled 的联动守卫。
    /// <para>失焦时记录当前 Enabled 并强制关闭，回焦时还原；重复的同向焦点事件被忽略，
    /// 避免连续两次失焦把「失焦前状态」覆盖为 false，导致回焦后输入永久关闭。</para>
    /// <para>纯逻辑单元，便于单测；由 <see cref="InputService"/> 持有静态实例。</para>
    /// </summary>
    internal sealed class FocusInputGuard
    {
        private bool _hasFocus = true;
        private bool _enabledBeforeFocusLoss = true;

        /// <summary>
        /// 根据焦点变化计算应写入 handler.Enabled 的值。
        /// <para>焦点未变化（重复事件）时返回 <c>null</c>，调用方不应写入。</para>
        /// </summary>
        /// <param name="hasFocus">本事件表示的焦点状态。</param>
        /// <param name="currentEnabled">handler 当前的 Enabled。</param>
        public bool? Evaluate(bool hasFocus, bool currentEnabled)
        {
            if (hasFocus == _hasFocus) return null;

            if (!hasFocus)
            {
                _hasFocus = false;
                _enabledBeforeFocusLoss = currentEnabled;
                return false;
            }

            _hasFocus = true;
            return _enabledBeforeFocusLoss;
        }

        /// <summary>重置为「有焦点、启用」的初始态（禁用 Domain Reload 的 Enter Play Mode 下跨会话不残留）。</summary>
        public void Reset()
        {
            _hasFocus = true;
            _enabledBeforeFocusLoss = true;
        }
    }
}
