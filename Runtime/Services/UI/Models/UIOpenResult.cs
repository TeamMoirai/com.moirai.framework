namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 开窗/取窗等待的终态。
    /// </summary>
    public enum EUIOpenStatus : byte
    {
        /// <summary>面板已装载就绪。</summary>
        Opened = 0,

        /// <summary>装载失败或装载中被关闭：窗口已从栈上回滚作废。</summary>
        Failed = 1,

        /// <summary>按标识取窗时栈上没有该标识（不带标识时按类型扫栈也无该型窗），或命中的窗口不是该类型。</summary>
        Missing = 2,

        /// <summary>等待超时：面板仍未就绪，窗口可能还在装载。</summary>
        Timeout = 3,

        /// <summary>调用方取消：等待被自己的 CancellationToken 撤销，在飞装载随之回滚。</summary>
        Cancelled = 4,
    }

    /// <summary>
    /// 开窗/取窗等待的结果：状态与窗口实例成对交回，不再以 null 与超时混言成败。
    /// </summary>
    /// <remarks>
    /// <see cref="Status"/> 为 <see cref="EUIOpenStatus.Opened"/> 时 <see cref="Window"/> 已就绪可用。<br />
    /// 为 <see cref="EUIOpenStatus.Failed"/> 时窗口已回滚作废，只作诊断，不得再开、不得复用。<br />
    /// 为 <see cref="EUIOpenStatus.Timeout"/> 时窗口可能仍在装载，是否继续等由调用方决定，不得当就绪窗用。<br />
    /// 为 <see cref="EUIOpenStatus.Cancelled"/> 时调用方的令牌撤销了等待，本次等待以取消落定；装载是否续跑取决于其余等待者（无人在等则回滚），不得当就绪窗用。<br />
    /// 为 <see cref="EUIOpenStatus.Missing"/> 时 <see cref="Window"/> 恒为 null。<br />
    /// 隐式布尔只答「就绪成功」一档，状态细判读 <see cref="Status"/>。
    /// </remarks>
    public readonly struct UIOpenResult
    {
        /// <summary>结果状态。</summary>
        public EUIOpenStatus Status { get; }

        /// <summary>与状态配对的窗口实例；Missing 档为 null。</summary>
        public UIWindow Window { get; }

        /// <summary>是否装载就绪。</summary>
        public bool Success => Status == EUIOpenStatus.Opened;

        /// <summary>
        /// 构造一档结果。
        /// </summary>
        /// <param name="status">结果状态。</param>
        /// <param name="window">与状态配对的窗口实例。</param>
        internal UIOpenResult(EUIOpenStatus status, UIWindow window)
        {
            Status = status;
            Window = window;
        }

        /// <summary>是否装载就绪：隐式布尔只答这一档，超时与失败都当假。</summary>
        /// <param name="result">待判的结果。</param>
        public static implicit operator bool(UIOpenResult result) => result.Success;
    }
}
