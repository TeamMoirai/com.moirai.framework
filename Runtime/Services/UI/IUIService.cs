using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 服务契约（策略模式契约接口）：UI 后端的实现契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 回摆口径：契约不再由门面实例实现——两支后端 handler（<see cref="UGUIHandler"/> / <see cref="UITKHandler"/>）直接落地本接口； <br />
    /// 全部成员为 DIM 默认实现（转发静态门面 <see cref="UIService"/>，单一语义源），后端零覆写即可整接口落地； <br />
    /// 门面级广播（onWindowShown/onWindowClosed）与阶段驱动（Tick）已移出契约：前者直订门面静态事件，后者由后端的 <c>IServiceTickable</c> 承接。 <br />
    /// 多后端并行无单一契约实例：<see cref="UIService.RegisterEnabledHandlers"/> 把 handler 以各自具体类型注册进容器，IUIService 引用面（多缓存）由门面轨槽持有。
    /// </remarks>
    public interface IUIService
    {
        #region 状态与查询 [STATE & QUERIES]

        /// <summary>目录里任何一轨的驱动者就位即为真；一轨都没就位为假。</summary>
        bool IsValid { get { return UIService.IsValid; } }

        /// <summary>当前模态遮挡窗口：读的是那条各轨共用的栈，与哪一轨在位无关。</summary>
        UIWindow CurrentModal { get { return UIService.CurrentModal; } }

        /// <summary>导航深度：开启序历史的长度。</summary>
        int NavigationDepth { get { return UIService.NavigationDepth; } }

        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        UIWindow GetTopWindow() { return UIService.GetTopWindow(); }

        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        UIWindow GetTopWindow(int layer) { return UIService.GetTopWindow(layer); }

        /// <summary>
        /// 获取指定层级下顶部窗口的标识。
        /// </summary>
        string GetTopWindowId(int layer) { return UIService.GetTopWindowId(layer); }

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        bool IsAnyLoading() { return UIService.IsAnyLoading(); }

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        bool HasWindow<T>(string windowId = null) where T : UIWindow { return UIService.HasWindow<T>(windowId); }

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        bool HasWindow(Type type, string windowId = null) { return UIService.HasWindow(type, windowId); }

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        T GetWindow<T>(string windowId = null) where T : UIWindow { return UIService.GetWindow<T>(windowId); }

        /// <summary>
        /// 判断指定 UI 对象是否被模态窗口遮挡。
        /// </summary>
        bool IsBlockedByModal(GameObject obj) { return UIService.IsBlockedByModal(obj); }

        /// <summary>
        /// 判断窗口是否为模态窗口。
        /// </summary>
        bool IsModal(UIWindow window) { return UIService.IsModal(window); }

        #endregion 状态与查询 [STATE & QUERIES]

        #region 开窗 [SHOW WINDOW]

        /// <summary>
        /// 同步打开窗口（WebGL 上交给异步装载）。
        /// </summary>
        void ShowUI(Type type, string windowId, UIPayload payload = default, CancellationToken ct = default) { UIService.ShowUI(type, windowId, payload, ct); }

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        void ShowUIAsync(Type type, string windowId, UIPayload payload = default, CancellationToken ct = default) { UIService.ShowUIAsync(type, windowId, payload, ct); }

        /// <summary>
        /// 异步打开窗口并等待面板就绪（Type 形入口）。
        /// </summary>
        UniTask<UIWindow> ShowUIAsyncAwait(Type type, string windowId, UIPayload payload = default, CancellationToken ct = default) { return UIService.ShowUIAsyncAwait(type, windowId, payload, ct); }

        #endregion 开窗 [SHOW WINDOW]

        #region 关隐窗口 [CLOSE & HIDE WINDOW]

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        void CloseUI<T>(string windowId = null) where T : UIWindow { UIService.CloseUI<T>(windowId); }

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        void CloseUI(Type type, string windowId = null) { UIService.CloseUI(type, windowId); }

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        void HideUI<T>(string windowId = null) where T : UIWindow { UIService.HideUI<T>(windowId); }

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        void HideUI(Type type, string windowId = null) { UIService.HideUI(type, windowId); }

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        void CloseAll(bool isShutDown = false) { UIService.CloseAll(isShutDown); }

        /// <summary>
        /// 关闭所有窗口除了指定窗口。
        /// </summary>
        void CloseAllWithOut(UIWindow withOut) { UIService.CloseAllWithOut(withOut); }

        /// <summary>
        /// 关闭所有窗口除了指定类型的窗口。
        /// </summary>
        void CloseAllWithOut<T>() where T : UIWindow { UIService.CloseAllWithOut<T>(); }

        /// <summary>
        /// 关闭所有窗口除了指定层级的窗口。
        /// </summary>
        void CloseAllWithOut(EUILayer withOut) { UIService.CloseAllWithOut(withOut); }

        /// <summary>
        /// 关闭最近打开的窗口：走既有 CanClose 政策，无历史/拒关/过渡中回假。
        /// </summary>
        bool TryCloseTopWindow() { return UIService.TryCloseTopWindow(); }

        #endregion 关隐窗口 [CLOSE & HIDE WINDOW]

        #region 异步获取窗口 [GET WINDOW ASYNC]

        /// <summary>
        /// 异步获取窗口：栈上没有这一枚时按类型扫栈取栈顶那一只。
        /// </summary>
        UniTask<T> GetUIAsyncAwait<T>(string windowId) where T : UIWindow { return UIService.GetUIAsyncAwait<T>(windowId); }

        /// <summary>
        /// 异步获取窗口：栈上没有这一枚时按类型扫栈取栈顶那一只。
        /// </summary>
        void GetUIAsync<T>(string windowId, Action<T> callback) where T : UIWindow { UIService.GetUIAsync<T>(windowId, callback); }

        /// <summary>
        /// 异步获取窗口并等装载终态：就绪/失败/缺失/超时按 UIOpenResult 交回。
        /// </summary>
        UniTask<UIOpenResult> GetUIAwaitResult<T>(string windowId = null) where T : UIWindow { return UIService.GetUIAwaitResult<T>(windowId); }

        #endregion 异步获取窗口 [GET WINDOW ASYNC]

        #region 安全区 [SAFE AREA]

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）：广播给各轨，各自把这块矩形落到本轨的面板上。
        /// </summary>
        void ApplyScreenSafeRect(Rect safeRect) { UIService.ApplyScreenSafeRect(safeRect); }

        /// <summary>
        /// 模拟 IPhoneX 异形屏：安全区矩形按支算一次、广播给各自的面板落位。
        /// </summary>
        void SimulateIPhoneXNotchScreen() { UIService.SimulateIPhoneXNotchScreen(); }

        #endregion 安全区 [SAFE AREA]
    }
}
