using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Input;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Timer;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI服务外观（Facade）。
    /// <para>统一的静态 UI 访问入口，通过替换 <see cref="Handler"/> 即可在不同 UI 后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，懒加载优先经 <c>GetHandlerFromSettings</c> 从 <see cref="UIServiceSettings"/> 解析；settings 未配置则回退 <see cref="CreateDefaultHandler"/>。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(UIServiceHandler))]
    [ServiceDependency(typeof(DebuggerService), typeof(ResourceService), typeof(TimerService), typeof(InputService))]
    public sealed partial class UIService : ServiceBase, IServiceTickable
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 创建默认 UI 处理器（settings 未配置时的代码兜底）。
        /// </summary>
        /// <returns>默认 UI 处理器实例。</returns>
        internal static UIServiceHandler CreateDefaultHandler() => new UGUIHandler();

        /// <summary>
        /// 从 <see cref="UIServiceSettings"/> 解析 UI 处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——懒加载主路径（settings 已配置时 <see cref="CreateDefaultHandler"/> 被短路）首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static UIServiceHandler GetHandlerFromSettings()
        {
            GameServices.EnsureRegistered<UIService>();
            return UIServiceSettings.UIServiceHandler;
        }

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>
        /// 初始化 UI 服务。由容器在构建期调用。
        /// <para>确保 <c>UIService.Handler</c> 已赋值（触发 <c>Handler</c> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭 UI 服务。由容器在关闭期调用。
        /// <para>先摘除 Handler 引用再关闭——窗口销毁链抛异常时（如用户 OnDestroy 回调）不得让
        /// 半关状态的 Handler 残留，后续外观访问应经关闭守卫走显式重建而非复用半关实例。</para>
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器驱动窗口内部更新。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            s_Handler?.Tick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 层级常量 [LAYER CONSTANTS]

        public const int LAYER_DEEP = 2000;
        public const int WINDOW_DEEP = 100;
        public const int WINDOW_HIDE_LAYER = 2; // Ignore Raycast
        public const int WINDOW_SHOW_LAYER = 5; // UI

        #endregion

        #region 属性 [PROPERTIES]
		
        /// <summary>
        /// UI根节点。
        /// </summary>
        public static Transform UIRoot => s_Handler?.UIRoot;

        /// <summary>
        /// UI专用摄像机。
        /// </summary>
        public static Camera UICamera => s_Handler?.UICamera;

        /// <summary>
        /// 当前模态遮挡窗口。
        /// </summary>
        public static UIWindow CurrentModal => s_Handler?.CurrentModal;

        #endregion

        #region 安全区域 [SAFE AREA]

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）。
        /// </summary>
        /// <param name="safeRect">安全区域</param>
        public static void ApplyScreenSafeRect(Rect safeRect) =>
            s_Handler?.ApplyScreenSafeRect(safeRect);

        /// <summary>
        /// 模拟IPhoneX异形屏
        /// </summary>
        public static void SimulateIPhoneXNotchScreen() =>
            s_Handler?.SimulateIPhoneXNotchScreen();

        #endregion

        #region 窗口查询 [WINDOW QUERIES]

        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        public static UIWindow GetTopWindow() =>
            s_Handler?.GetTopWindow();

        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        public static UIWindow GetTopWindow(int layer) =>
            s_Handler?.GetTopWindow(layer);

        /// <summary>
        /// 获取指定层级下顶部的窗口名称。
        /// </summary>
        public static string GetTopWindowName(int layer) =>
            s_Handler?.GetTopWindowName(layer);

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        public static bool IsAnyLoading() =>
            s_Handler?.IsAnyLoading() ?? false;

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <typeparam name="T">界面类型。</typeparam>
        /// <param name="windowName">窗口名称</param>
        /// <returns>是否存在。</returns>
        public static bool HasWindow<T>(string windowName = null) where T : UIWindow =>
            s_Handler?.HasWindow<T>(windowName) ?? false;

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <param name="windowName">窗口名称</param>
        /// <returns>是否存在。</returns>
        public static bool HasWindow(Type type, string windowName = null) =>
            s_Handler?.HasWindow(type, windowName) ?? false;

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <returns>窗口实例。</returns>
        public static T GetWindow<T>(string windowName) where T : UIWindow =>
            s_Handler?.GetWindow<T>(windowName);

        /// <summary>
        /// 判断指定 UI 对象是否被模态窗口遮挡。
        /// </summary>
        public static bool IsBlockedByModal(GameObject obj) =>
            s_Handler?.IsBlockedByModal(obj) ?? false;

        /// <summary>
        /// 判断窗口是否为模态窗口。
        /// </summary>
        public static bool IsModal(UIWindow window) =>
            s_Handler?.IsModal(window) ?? false;

        /// <summary>
        /// 申请模态动画期间的 UI 交互压制。
        /// </summary>
        /// <returns>调用方应当置位压制时返回 true；非模态窗口恒为 false。</returns>
        /// <remarks>压制位本身是无归属的全局布尔，仲裁见 <see cref="UIInteractionLease"/>。</remarks>
        internal static bool AcquireModalInteraction(UIWindow window) =>
            s_Handler != null && s_Handler.InteractionLease.Acquire(window, IsModal(window));

        /// <summary>
        /// 交还模态动画期间的 UI 交互压制。
        /// </summary>
        /// <returns>调用方是当前持有者、可以清除压制位时返回 true；压制归别人持有时返回 false。</returns>
        internal static bool ReleaseModalInteraction(UIWindow window) =>
            s_Handler != null && s_Handler.InteractionLease.Release(window);

        #endregion

        #region 显示窗口 [SHOW WINDOW]

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public static void ShowUIAsync<T>(string windowName = null, string assetName = null, bool fromResources = false, params object[] userData)
            where T : UIWindow, new() =>
            s_Handler?.ShowUIAsync<T>(windowName, assetName, fromResources, userData);

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public static void ShowUI<T>(string windowName = null, string assetName = null, bool fromResources = false, params object[] userData)
            where T : UIWindow, new() =>
            s_Handler?.ShowUI<T>(windowName, assetName, fromResources, userData);

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public static void ShowUIAsync(Type type, string windowName = null, string assetName = null, bool fromResources = false, params object[] userData) =>
            s_Handler?.ShowUIAsync(type, windowName, assetName, fromResources, userData);

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public static void ShowUI(Type type, string windowName = null, string assetName = null, bool fromResources = false, params object[] userData) =>
            s_Handler?.ShowUI(type, windowName, assetName, fromResources, userData);

        /// <summary>
        /// 异步打开窗口并等待加载完成。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public static UniTask<UIWindow> ShowUIAsyncAwait<T>(string windowName = null, string assetName = null, bool fromResources = false, params object[] userData) where T : UIWindow, new() =>
            s_Handler?.ShowUIAsyncAwait<T>(windowName, assetName, fromResources, userData) ?? UniTask.FromResult<UIWindow>(null);

        #endregion

        #region 关闭窗口 [CLOSE WINDOW]

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        public static void CloseUI<T>(string windowName = null) where T : UIWindow =>
            s_Handler?.CloseUI<T>(windowName);

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称。</param>
        public static void CloseUI(Type type, string windowName = null) =>
            s_Handler?.CloseUI(type, windowName);

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        public static void HideUI<T>(string windowName = null) where T : UIWindow =>
            s_Handler?.HideUI<T>(windowName);

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称。</param>
        public static void HideUI(Type type, string windowName = null) =>
            s_Handler?.HideUI(type, windowName);

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        public static void CloseAll(bool isShutDown = false) =>
            s_Handler?.CloseAll(isShutDown);

        /// <summary>
        /// 关闭所有窗口除了指定窗口。
        /// </summary>
        public static void CloseAllWithOut(UIWindow withOut) =>
            s_Handler?.CloseAllWithOut(withOut);

        /// <summary>
        /// 关闭所有窗口除了指定类型的窗口。
        /// </summary>
        public static void CloseAllWithOut<T>() where T : UIWindow =>
            s_Handler?.CloseAllWithOut<T>();

        /// <summary>
        /// 关闭所有窗口除了指定层级的窗口。
        /// </summary>
        public static void CloseAllWithOut(UILayer withOut) =>
            s_Handler?.CloseAllWithOut(withOut);

        #endregion

        #region 异步获取窗口 [GET WINDOW ASYNC]

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>窗口实例。</returns>
        public static UniTask<T> GetUIAsyncAwait<T>() where T : UIWindow =>
            s_Handler?.GetUIAsyncAwait<T>() ?? UniTask.FromResult<T>(null);

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="callback">回调。</param>
        public static void GetUIAsync<T>(Action<T> callback) where T : UIWindow =>
            s_Handler?.GetUIAsync(callback);

        #endregion
    }
}
