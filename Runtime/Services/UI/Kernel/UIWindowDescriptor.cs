namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 窗口元数据描述符：注册期一次解析好的 <c>[Window]</c> 特性全量取值，开窗时零反射直取。
    /// </summary>
    /// <remarks>
    /// 由 <c>UIWindowCodegen</c> 在编译期从特性实参解析、模块初始化期随注册表登记。 <br />
    /// <see cref="FullName"/> 是反射全名（嵌套类带 <c>+</c>），作缺省窗口名；<see cref="Location"/> 是已按「特性为空回落类型名」解析好的面板地址。 <br />
    /// 值语义（≤ 一引用两字符串以内），注册与开窗路径直传。
    /// </remarks>
    public readonly struct UIWindowDescriptor
    {
        /// <summary>类型反射全名（嵌套类带 <c>+</c>）：缺省窗口名与取窗判名用它。</summary>
        public readonly string FullName;

        /// <summary>面板地址：特性写了 location 用它，为空时已回落成类型名。</summary>
        public readonly string Location;

        /// <summary>窗口层级。</summary>
        public readonly int WindowLayer;

        /// <summary>是否为内置资源（不走资源包加载）。</summary>
        public readonly bool FromResources;

        /// <summary>是否为全屏窗口。</summary>
        public readonly bool FullScreen;

        /// <summary>模态档（<see cref="EUIModal"/> 三态原值，继承档由窗口按自身层级结算）。</summary>
        public readonly byte Modal;

        /// <summary>隐藏后转关闭的秒数。</summary>
        public readonly int HideTimeToClose;

        /// <summary>是否缓存实例（关闭时不销毁）。</summary>
        public readonly bool CacheInstance;

        /// <summary>
        /// 构造一份描述符。
        /// </summary>
        /// <param name="fullName">类型反射全名。</param>
        /// <param name="location">面板地址（空特性已回落类型名）。</param>
        /// <param name="windowLayer">窗口层级。</param>
        /// <param name="fromResources">是否为内置资源。</param>
        /// <param name="fullScreen">是否为全屏窗口。</param>
        /// <param name="modal">模态档三态原值。</param>
        /// <param name="hideTimeToClose">隐藏后转关闭的秒数。</param>
        /// <param name="cacheInstance">是否缓存实例。</param>
        public UIWindowDescriptor(string fullName, string location, int windowLayer, bool fromResources,
            bool fullScreen, byte modal, int hideTimeToClose, bool cacheInstance)
        {
            FullName = fullName;
            Location = location;
            WindowLayer = windowLayer;
            FromResources = fromResources;
            FullScreen = fullScreen;
            Modal = modal;
            HideTimeToClose = hideTimeToClose;
            CacheInstance = cacheInstance;
        }
    }
}
