using System;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI层级枚举。
    /// </summary>
    public enum UILayer : int
    {
        Bottom = 0, // 背景 UI（HUD），非模态
        UI = 1,     // 常规 UI（全屏），模态
        Popup = 2,  // 常规弹窗（非全屏），模态
        Tips = 3,   // 顶级提示（Tooltip），非模态
        System = 4, // 系统级提示，模态
    }

    /// <summary>
    /// 窗口特性：声明层级、面板地址、全屏、缓存与隐转关延迟；窗口类必标。
    /// </summary>
    /// <remarks>
    /// 由 <c>UIWindowCodegen</c> 编译期解码并登记进 <see cref="UIWindowRegistry"/>：未标注的窗口类不可开。 <br />
    /// 构造器已收敛单一形状：层级强类型 <see cref="UILayer"/>，其余按名可选。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public class WindowAttribute : Attribute
    {
        /// <summary>窗口层级。</summary>
        public readonly int WindowLayer;

        /// <summary>资源定位地址（空缺省回落类型名）。</summary>
        public readonly string Location;

        /// <summary>全屏窗口标记。</summary>
        /// <remarks>隐藏其他同 UILayer 的弹窗。</remarks>
        public readonly bool FullScreen;

        /// <summary>是内部资源无需AB加载。</summary>
        public readonly bool FromResources;

        /// <summary>隐藏后转关闭的秒数；≤0 表示隐藏即关。</summary>
        public readonly int HideTimeToClose;

        /// <summary>缓存实例，关闭时不销毁。</summary>
        public readonly bool CacheInstance;

        /// <summary>
        /// 构造窗口特性。
        /// </summary>
        /// <param name="windowLayer">窗口层级。</param>
        /// <param name="fromResources">是内部资源无需AB加载。</param>
        /// <param name="location">资源定位地址；空缺省回落类型名。</param>
        /// <param name="fullScreen">全屏窗口标记。</param>
        /// <param name="hideTimeToClose">隐藏后转关闭的秒数；≤0 表示隐藏即关。</param>
        /// <param name="cacheInstance">缓存实例，关闭时不销毁。</param>
        public WindowAttribute(UILayer windowLayer, bool fromResources = false, string location = null,
            bool fullScreen = false, int hideTimeToClose = 10, bool cacheInstance = false)
        {
            WindowLayer = (int)windowLayer;
            FromResources = fromResources;
            Location = location ?? string.Empty;
            FullScreen = fullScreen;
            HideTimeToClose = hideTimeToClose;
            CacheInstance = cacheInstance;
        }
    }
}
