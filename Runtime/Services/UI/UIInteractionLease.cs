namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 模态动画期间全局 UI 交互压制位的归属仲裁。
    /// </summary>
    /// <remarks>
    /// <c>InputService.PreventInteractionUI</c> 是无持有者语义的全局布尔，谁写 false 都会清掉别人的压制位。<br />
    /// 本类型记录当前持有者：交还只由持有者完成，非持有者交还要不回清除权。<br />
    /// 线程契约：仅主线程。
    /// </remarks>
    internal sealed class UIInteractionLease
    {
        private UIWindow _holder;

        /// <summary>
        /// 申请交互压制。
        /// </summary>
        /// <remarks>
        /// 非模态窗口不参与归属，直接回 false。<br />
        /// 模态窗口后到者接管：旧持有者再交还时判不匹配，不会去清全局压制位。
        /// </remarks>
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
        /// 清空归属：用于与窗口堆栈一同归零的场合（处理器重入初始化、关闭）。
        /// </summary>
        /// <returns>丢弃了仍持有压制的归属时为真，调用方须同事务清掉全局压制位；无归属可丢弃时为假，不得借机清别人的压制。</returns>
        internal bool Reset()
        {
            bool hadHolder = _holder != null;
            _holder = null;
            return hadHolder;
        }
    }
}
