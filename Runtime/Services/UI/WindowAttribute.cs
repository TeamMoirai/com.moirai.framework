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

    [AttributeUsage(AttributeTargets.Class)]
    public class WindowAttribute : Attribute
    {
        /// <summary>
        /// 窗口层级
        /// </summary>
        public readonly int windowLayer;

        /// <summary>
        /// 资源定位地址。
        /// </summary>
        public readonly string location;

        /// <summary>
        /// 全屏窗口标记。
        /// </summary>
        /// <remarks>隐藏其他同 UILayer 的弹窗</remarks>
        public readonly bool fullScreen;

        /// <summary>
        /// 是内部资源无需AB加载。
        /// </summary>
        public readonly bool fromResources;

        /// <summary>
        /// 隐藏，等几秒后关闭窗口。
        /// </summary>
        public readonly int hideTimeToClose;
        
        /// <summary>
        /// 缓存实例，关闭时不销毁。
        /// </summary>
        public readonly bool cacheInstance;

        public WindowAttribute(int windowLayer, string location = "", bool fullScreen = false, int hideTimeToClose = 10, bool cacheInstance = false)
        {
            this.windowLayer = windowLayer;
            this.location = location;
            this.fullScreen = fullScreen;
            this.hideTimeToClose = hideTimeToClose;
            this.cacheInstance = cacheInstance;
        }

        public WindowAttribute(UILayer windowLayer, string location = "", bool fullScreen = false, int hideTimeToClose = 10, bool cacheInstance = false)
        {
            this.windowLayer = (int)windowLayer;
            this.location = location;
            this.fullScreen = fullScreen;
            this.hideTimeToClose = hideTimeToClose;
            this.cacheInstance = cacheInstance;
        }

        public WindowAttribute(UILayer windowLayer, bool fromResources, bool fullScreen = false, int hideTimeToClose = 10, bool cacheInstance = false)
        {
            this.windowLayer = (int)windowLayer;
            this.fromResources = fromResources;
            this.fullScreen = fullScreen;
            this.hideTimeToClose = hideTimeToClose;
            this.cacheInstance = cacheInstance;
        }

        public WindowAttribute(UILayer windowLayer, bool fromResources, string location, bool fullScreen = false, int hideTimeToClose = 10, bool cacheInstance = false)
        {
            this.windowLayer = (int)windowLayer;
            this.fromResources = fromResources;
            this.location = location;
            this.fullScreen = fullScreen;
            this.hideTimeToClose = hideTimeToClose;
            this.cacheInstance = cacheInstance;
        }
    }
}