using System;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI层级枚举。
    /// </summary>
    public enum EUILayer : int
    {
        /// <summary>背景 UI（HUD），非模态</summary>
        Bottom = 0,
        
        /// <summary>常规 UI（全屏），模态</summary>
        UI = 1,
        
        /// <summary>常规弹窗（非全屏），模态</summary>
        Popup = 2,
        
        /// <summary>顶级提示（Tooltip），非模态</summary>
        Tips = 3,
        
        /// <summary>系统级提示，模态</summary>
        System = 4,
    }

    /// <summary>
    /// 窗口模态三态：显式声明模态档，缺省按层级继承。
    /// </summary>
    /// <remarks>
    /// attribute 实参禁 nullable（CS0655），三态由此枚举表达。
    /// </remarks>
    public enum EUIModal : byte
    {
        /// <summary>按层级继承：模态层级（UI/Popup/System）即模态。</summary>
        Inherit = 0,

        /// <summary>强制模态（非模态层级也可压下层交互位、占全局压制位）。</summary>
        Modal = 1,

        /// <summary>强制非模态（模态层级也可只显示不压制）。</summary>
        NonModal = 2,
    }

    /// <summary>
    /// 窗口特性：声明层级、取法档、全屏、模态、停放档与隐转关延迟；窗口类必标。
    /// </summary>
    /// <remarks>
    /// 由 <c>UIWindowCodegen</c> 编译期解码并登记进 <see cref="UIWindowRegistry"/>：未标注的窗口类不可开。 <br />
    /// 构造器已收敛单一形状：层级强类型 <see cref="EUILayer"/>，其余按名可选。 <br />
    /// 本特性<b>不声明面板地址</b>：地址只由开窗时传入的窗口标识换算而来。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public class WindowAttribute : Attribute
    {
        /// <summary>窗口层级。</summary>
        public readonly int WindowLayer;

        /// <summary>全屏窗口标记。</summary>
        /// <remarks>隐藏其他同 EUILayer 的弹窗。</remarks>
        public readonly bool FullScreen;

        /// <summary>是内部资源无需AB加载。</summary>
        public readonly bool FromResources;

        /// <summary>隐藏后转关闭的秒数；≤0 表示隐藏即关。</summary>
        public readonly int HideTimeToClose;

        /// <summary>模态档：缺省 <see cref="EUIModal.Inherit"/> 按层级继承。</summary>
        public readonly byte Modal;

        /// <summary>停放档：0 = 不缓存（关闭时直接销毁），&gt;0 = 停放并在这么多秒后销毁，&lt;0 = 停放永久。</summary>
        public readonly float CacheTimeToDestroy;

        /// <summary>
        /// 构造窗口特性。
        /// </summary>
        /// <param name="windowLayer">窗口层级。</param>
        /// <param name="fromResources">是内部资源无需AB加载。</param>
        /// <param name="fullScreen">全屏窗口标记。</param>
        /// <param name="hideTimeToClose">隐藏后转关闭的秒数；≤0 表示隐藏即关。</param>
        /// <param name="modal">模态档；缺省按层级继承（模态层级 UI/Popup/System 即模态）。</param>
        /// <param name="cacheTimeToDestroy">停放档；0 = 不缓存，&gt;0 = 停放转销毁的秒数，&lt;0 = 停放永久。</param>
        public WindowAttribute(EUILayer windowLayer, bool fromResources = false,
            bool fullScreen = false, int hideTimeToClose = 10, EUIModal modal = EUIModal.Inherit,
            float cacheTimeToDestroy = 0f)
        {
            WindowLayer = (int)windowLayer;
            FromResources = fromResources;
            FullScreen = fullScreen;
            HideTimeToClose = hideTimeToClose;
            Modal = (byte)modal;
            CacheTimeToDestroy = cacheTimeToDestroy;
        }
    }
}
