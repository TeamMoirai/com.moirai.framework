using System;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI Toolkit 轨的驱动者：本轨开窗、关隐、查询那一圈编排的落点，手里那份窗口栈与 uGUI 轨那枚协调者是同一枚实例。
    /// </summary>
    /// <remarks>
    /// 两支各持一份 handler，但只有一条栈：栈、停放表与交互租约都住在 <see cref="UIService.SharedLedger"/> 那一份共享持有者里， <br />
    /// 本类继承来的转发口交出的都是那一份，因此关·隐·查询不分轨（判据见 <c>UIServiceHandler</c>）。 <br />
    /// 面板本体住在 <see cref="UITKWindow"/> 上：壳物体与 <c>UIDocument</c> 一窗一枚，<c>PanelSettings</c> 一窗一档—— <br />
    /// 开窗腿带着那一枚时由本轨在装载前交给窗口实例（<see cref="UITKWindow.PanelSettingsOverride"/>），没带时回全后端共享的那一份 <br />
    /// （<see cref="UITKWindow.SharedPanelSettings"/>），壳的父级也仍取 uGUI 轨那一枚 UI 根——本类因此答不出 <see cref="UIRoot"/> 与 <see cref="UICamera"/>。 <br />
    /// 派生职责按基类口径写：本类不覆写 <see cref="UIServiceHandler.IsModal"/>、<see cref="UIServiceHandler.CurrentModal"/> 与关·隐那一族横 call， <br />
    /// 那些横 call 的接收者是非虚的共享持有者，覆写虚槽会让门面与账本内部答出两套结果。<br />
    /// 线程契约：仅主线程。
    /// </remarks>
    [Serializable]
    // ReSharper disable once InconsistentNaming
    internal sealed class UITKHandler : UIServiceHandler
    {
        /// <summary>
        /// UI Toolkit 轨没有 Canvas 根节点：本轨的面板挂在壳物体上，壳的父级由 <see cref="UITKWindow"/> 取门面那枚 UI 根。
        /// </summary>
        /// <remarks>因此这一轨就位时门面答不出 UI 根，不得由它代答 uGUI 轨的资源。</remarks>
        public override Transform UIRoot => null;

        /// <summary>同上：UI Toolkit 的面板由 <c>PanelSettings</c> 驱动，没有本轨专用的摄像机。</summary>
        public override Camera UICamera => null;

        /// <summary>
        /// 安全区落到本轨的面板：这一枚目前不做事。
        /// </summary>
        /// <remarks>
        /// 门面的安全区入口按支驱动，两支各叫一次：这一轨的落点此刻被叫到了，只是还没有要落的东西。 <br />
        /// UI Toolkit 那一轨要落到 <c>PanelSettings</c> 的缩放档上，形状与 uGUI 的 <c>CanvasScaler</c> 换算不同， <br />
        /// 换算本身两轨共用 <see cref="UIServiceHandler.ComputeIPhoneXNotchSafeRect"/> 那一份。
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

        /// <summary>把这一枚注册进 UI Toolkit 那一轨的门面槽——归属由本类自述，门面入口不认识任何一枚具体实现。</summary>
        internal override void Internal_Register() => UIService.Internal_ClaimUITKTrack(this);

        /// <summary>
        /// 处理器关闭：关掉本轨那半边的窗，壳物体与文档组件随窗口自己的销毁链收走。
        /// </summary>
        /// <remarks>
        /// 这一枚不接管面板本体之外的资源：错误日志与 UI 根都长在 uGUI 那一轨上，本轨没有要释放的后端句柄。 <br />
        /// 本轨的壳挂在 uGUI 轨那枚 UI 根下（<see cref="UITKWindow"/> 建壳时取的是门面那枚根）， <br />
        /// 因此门面的 <see cref="UIService.OnShutdown"/> 先叫这一枚关停、再叫 uGUI 那一枚销毁那枚根——次序倒了就是拆还在用的面板。
        /// </remarks>
        protected override void OnShutdown()
        {
            CloseOwnTrackWindows(true);

            base.OnShutdown();
        }
    }
}
