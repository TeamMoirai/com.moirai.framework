using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 服务契约（策略模式契约接口）：轨无关的 UI 消费面，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 双轨例外：UIService 不声明 <c>[ProviderHandlerHost]</c>，契约绑定由组合根在注册清单后显式补登；服务互调经 <c>Context.GetRequiredService&lt;IUIService&gt;()</c> 解析到的就是 UIService 实例。 <br />
    /// 轨专有入口不进契约：泛型开窗（<c>ShowUI&lt;T&gt;</c>）按轨基类（UGUIWindow/UITKWindow）形参约束，留在各轨 partial；契约只收 Type 形入口与轨无关的查询/关隐面。 <br />
    /// 事件桥接：onWindowShown/onWindowClosed 的实例事件 add/remove 直接转发门面静态事件，订阅与退订语义同静态订阅。 <br />
    /// 实现约束：UIService 显式实现转发静态门面保持单一语义源，降级语义与门面一致。
    /// </remarks>
    public interface IUIService
    {
        #region 窗口事件 [WINDOW EVENTS]

        /// <summary>窗口入栈后广播，形参是刚入栈的窗口；装载在途也照发（回执说的是栈序，不是面板就绪）。</summary>
        event Action<UIWindow> onWindowShown;

        /// <summary>窗口出栈后广播，形参是刚出栈的窗口：停放与销毁都发，一次出栈恰一次。</summary>
        event Action<UIWindow> onWindowClosed;

        #endregion 窗口事件 [WINDOW EVENTS]

        #region 状态与查询 [STATE & QUERIES]

        /// <summary>目录里任何一轨的驱动者就位即为真；一轨都没就位为假。</summary>
        bool IsValid { get; }

        /// <summary>当前模态遮挡窗口：读的是那条各轨共用的栈，与哪一轨在位无关。</summary>
        UIWindow CurrentModal { get; }

        /// <summary>导航深度：开启序历史的长度。</summary>
        int NavigationDepth { get; }

        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        UIWindow GetTopWindow();

        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        UIWindow GetTopWindow(int layer);

        /// <summary>
        /// 获取指定层级下顶部窗口的标识。
        /// </summary>
        string GetTopWindowId(int layer);

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        bool IsAnyLoading();

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        bool HasWindow<T>(string windowId = null) where T : UIWindow;

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        bool HasWindow(Type type, string windowId = null);

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        T GetWindow<T>(string windowId = null) where T : UIWindow;

        /// <summary>
        /// 判断指定 UI 对象是否被模态窗口遮挡。
        /// </summary>
        bool IsBlockedByModal(GameObject obj);

        /// <summary>
        /// 判断窗口是否为模态窗口。
        /// </summary>
        bool IsModal(UIWindow window);

        #endregion 状态与查询 [STATE & QUERIES]

        #region 开窗 [SHOW WINDOW]

        /// <summary>
        /// 同步打开窗口（WebGL 上交给异步装载）。
        /// </summary>
        void ShowUI(Type type, string windowId, UIPayload payload = default, CancellationToken ct = default);

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        void ShowUIAsync(Type type, string windowId, UIPayload payload = default, CancellationToken ct = default);

        /// <summary>
        /// 异步打开窗口并等待面板就绪（Type 形入口）。
        /// </summary>
        UniTask<UIWindow> ShowUIAsyncAwait(Type type, string windowId, UIPayload payload = default, CancellationToken ct = default);

        #endregion 开窗 [SHOW WINDOW]

        #region 关隐窗口 [CLOSE & HIDE WINDOW]

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        void CloseUI<T>(string windowId = null) where T : UIWindow;

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        void CloseUI(Type type, string windowId = null);

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        void HideUI<T>(string windowId = null) where T : UIWindow;

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        void HideUI(Type type, string windowId = null);

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        void CloseAll(bool isShutDown = false);

        /// <summary>
        /// 关闭所有窗口除了指定窗口。
        /// </summary>
        void CloseAllWithOut(UIWindow withOut);

        /// <summary>
        /// 关闭所有窗口除了指定类型的窗口。
        /// </summary>
        void CloseAllWithOut<T>() where T : UIWindow;

        /// <summary>
        /// 关闭所有窗口除了指定层级的窗口。
        /// </summary>
        void CloseAllWithOut(EUILayer withOut);

        /// <summary>
        /// 关闭最近打开的窗口：走既有 CanClose 政策，无历史/拒关/过渡中回假。
        /// </summary>
        bool TryCloseTopWindow();

        #endregion 关隐窗口 [CLOSE & HIDE WINDOW]

        #region 异步获取窗口 [GET WINDOW ASYNC]

        /// <summary>
        /// 异步获取窗口并等待就绪。
        /// </summary>
        UniTask<T> GetUIAsyncAwait<T>(string windowId = null) where T : UIWindow;

        /// <summary>
        /// 异步获取窗口：栈上没有这一枚时按类型扫栈取栈顶那一只。
        /// </summary>
        void GetUIAsync<T>(string windowId, Action<T> callback) where T : UIWindow;

        /// <summary>
        /// 异步获取窗口并等装载终态：就绪/失败/缺失/超时按 UIOpenResult 交回。
        /// </summary>
        UniTask<UIOpenResult> GetUIAwaitResult<T>(string windowId = null) where T : UIWindow;

        #endregion 异步获取窗口 [GET WINDOW ASYNC]

        #region 安全区 [SAFE AREA]

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）：广播给各轨，各自把这块矩形落到本轨的面板上。
        /// </summary>
        void ApplyScreenSafeRect(Rect safeRect);

        /// <summary>
        /// 模拟 IPhoneX 异形屏：安全区矩形按支算一次、广播给各自的面板落位。
        /// </summary>
        void SimulateIPhoneXNotchScreen();

        #endregion 安全区 [SAFE AREA]

        #region 阶段驱动 [PHASE TICK]

        /// <summary>
        /// 容器 Tick 驱动：每帧把那条共享栈结算一次，再广播各轨本轨专有职责。
        /// </summary>
        void Tick(float elapseSeconds, float realElapseSeconds);

        #endregion 阶段驱动 [PHASE TICK]
    }
}
