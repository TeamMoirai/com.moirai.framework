namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 模态动画期间全局 UI 交互压制位的归属仲裁。
    /// <para><c>InputService.PreventInteractionUI</c> 是单个全局布尔、自身无持有者语义，
    /// 谁写 false 都会清掉别人刚置的压制位；这里记录最后一次申请方，使交还只由持有者完成。</para>
    /// <para>线程契约：仅主线程。</para>
    /// </summary>
    internal sealed class UIInteractionLease
    {
        private UIWindow _holder;

        /// <summary>
        /// 申请交互压制。非模态窗口不参与归属；模态窗口后到者接管（压制以最后一次动画接管方为准，
        /// 被接管的旧续体已由代次门控作废，不会再尝试交还）。
        /// </summary>
        /// <returns>调用方应当置位全局压制时返回 true。</returns>
        internal bool Acquire(UIWindow window, bool isModal)
        {
            if (!isModal) return false;

            _holder = window;
            return true;
        }

        /// <summary>
        /// 交还交互压制。
        /// </summary>
        /// <returns>调用方就是当前持有者、可以清除全局压制位时返回 true；否则说明压制归别人持有。</returns>
        internal bool Release(UIWindow window)
        {
            if (!ReferenceEquals(_holder, window)) return false;

            _holder = null;
            return true;
        }

        /// <summary>
        /// 清空归属。与窗口堆栈一同归零的场合使用（处理器重入初始化、关闭）。
        /// </summary>
        internal void Reset()
        {
            _holder = null;
        }
    }
}
