namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 窗口元数据描述符：注册期一次解析好的 <c>[Window]</c> 特性全量取值，开窗时零反射直取。
    /// </summary>
    /// <remarks>
    /// 由 <c>UIWindowCodegen</c> 在编译期从特性实参解析、模块初始化期随注册表登记。 <br />
    /// 本描述符<b>既不带面板地址也不带窗口标识</b>：两者都来自开窗时传入的 windowId。 <br />
    /// 值语义（纯数值，无引用字段），注册与开窗路径直传。
    /// </remarks>
    public readonly struct UIWindowDescriptor
    {
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

        /// <summary>停放档：0 = 不缓存（关闭即销毁），&gt;0 = 停放并在这么多秒后销毁，&lt;0 = 停放永久。</summary>
        public readonly float CacheTimeToDestroy;

        /// <summary>
        /// 构造一份描述符。
        /// </summary>
        /// <param name="windowLayer">窗口层级。</param>
        /// <param name="fromResources">是否为内置资源。</param>
        /// <param name="fullScreen">是否为全屏窗口。</param>
        /// <param name="modal">模态档三态原值。</param>
        /// <param name="hideTimeToClose">隐藏后转关闭的秒数。</param>
        /// <param name="cacheTimeToDestroy">停放档；0 = 不缓存，&gt;0 = 停放转销毁的秒数，&lt;0 = 停放永久。</param>
        public UIWindowDescriptor(int windowLayer, bool fromResources,
            bool fullScreen, byte modal, int hideTimeToClose, float cacheTimeToDestroy)
        {
            WindowLayer = windowLayer;
            FromResources = fromResources;
            FullScreen = fullScreen;
            Modal = modal;
            HideTimeToClose = hideTimeToClose;
            CacheTimeToDestroy = cacheTimeToDestroy;
        }
    }
}
