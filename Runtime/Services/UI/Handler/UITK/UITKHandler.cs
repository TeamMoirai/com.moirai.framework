using System;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI Toolkit 轨的驱动者：本轨开窗、关隐、查询那一圈编排的落点。
    /// </summary>
    /// <remarks>
    /// 与 uGUI 轨各持一份 handler，但只有一条栈：栈、停放表与交互租约都住在 <see cref="UIService.SharedLedger"/>，关·隐·查询不分轨。<br />
    /// 面板本体住在 <see cref="UITKWindow"/>：壳物体与 <c>UIDocument</c> 一窗一枚，<c>PanelSettings</c> 一窗一档。<br />
    /// 开窗腿给了 <see cref="UITKWindow.PanelSettingsOverride"/> 就用它，没带回 <see cref="UITKWindow.SharedPanelSettings"/>。<br />
    /// 关·隐那一族横 call 与 <see cref="UIServiceHandler.IsModal"/>、<see cref="UIServiceHandler.CurrentModal"/> 一律不覆写，<br />
    /// 那些 call 的接收者是非虚的共享持有者，覆写虚槽会让门面与账本各答一套结果。<br />
    /// 线程契约：仅主线程。
    /// </remarks>
    [ProviderDisplay(title: "UI Toolkit 轨", description: "UIDocument 壳 + 窗口级 PanelSettings，与 uGUI 轨同栈并存")]
    [Serializable]
    // ReSharper disable once InconsistentNaming
    internal sealed class UITKHandler : UIServiceHandler
    {
        /// <summary>本轨没有 Canvas 根节点，面板挂在壳物体上：这一轨就位时门面答不出 UI 根，回 null。</summary>
        /// <remarks>壳的父级由 <see cref="UITKWindow"/> 取 uGUI 轨那枚 UI 根，不得由本轨代答那一枚资源。</remarks>
        public override Transform UIRoot => null;

        /// <summary>同上：本轨面板由 <c>PanelSettings</c> 驱动，没有专用摄像机，回 null。</summary>
        public override Camera UICamera => null;

        /// <summary>
        /// 安全区落到本轨的面板：当前什么都不做。
        /// </summary>
        /// <remarks>
        /// 门面的安全区入口按支驱动、两支各叫一次，这一轨的落点此刻没有要落的东西。<br />
        /// 换算两轨共用 <see cref="UIServiceHandler.ComputeIPhoneXNotchSafeRect"/>；真要落地时形状与 uGUI 的 <c>CanvasScaler</c> 不同。
        /// </remarks>
        /// <param name="safeRect">安全区域。</param>
        public override void ApplyScreenSafeRect(Rect safeRect)
        {
        }

        /// <summary>
        /// 本轨只认 UI Toolkit 轨的窗：一次关停里 uGUI 那一轨的窗留在共享栈上，由它自己那一枚驱动者去收。
        /// </summary>
        /// <param name="window">栈上待判的那一只。</param>
        /// <returns>这一只以 <see cref="UITKWindow"/> 为基类时为真。</returns>
        protected override bool IsWindowOnOwnTrack(UIWindow window) => window is UITKWindow;

        /// <summary>把这一枚注册进 UI Toolkit 那一轨的门面槽——归属由本类自述。</summary>
        internal override void Internal_Register() => UIService.Internal_ClaimUITKTrack(this);

        /// <summary>
        /// 处理器关闭：关掉本轨那半边的窗，壳物体与文档组件随窗口自己的销毁链收走。
        /// </summary>
        /// <remarks>
        /// 本轨没有要释放的后端句柄：错误日志与 UI 根都长在 uGUI 那一轨上。<br />
        /// 本轨的壳挂在 uGUI 轨那枚 UI 根下，因此门面的 <see cref="UIService.OnShutdown"/> 先叫这一枚、再叫那一枚销毁根，<br />
        /// 次序倒了就是拆还在用的面板。
        /// </remarks>
        protected override void OnShutdown()
        {
            CloseOwnTrackWindows(true);

            base.OnShutdown();
        }
    }
}
