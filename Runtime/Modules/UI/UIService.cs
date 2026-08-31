using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Timer;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI服务外观（Facade）。
    /// <para>统一的静态 UI 访问入口，通过替换 <see cref="Handler"/> 即可在不同 UI 后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="UIServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(UIServiceHandler))]
    [ServiceDependency(typeof(ResourceService), typeof(TimerService))]
    public sealed partial class UIService : ServiceBase, IServiceTickable
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="UIServiceSettings"/> 创建默认 UI 处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认 UI 处理器实例。</returns>
        private static UIServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<UIService>();
            return UIServiceSettings.UIServiceHandlerConfig.CreateHandler();
        }

        /// <summary>
        /// 初始化 UI 服务。由容器在构建期调用。
        /// <para>确保 <c>UIService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
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
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

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

        /// <summary>
        /// UI资源加载器。
        /// </summary>
        public static IUIResourceLoader Resource => s_Handler?.Resource;

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
