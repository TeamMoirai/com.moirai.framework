using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI处理器（后端）。承载窗口堆栈管理、层级排序与资源加载等核心逻辑。
    /// <para>通过 <see cref="UIServiceSettings.UIServiceHandler"/> 序列化配置，可替换为自定义 UI 后端。</para>
    /// </summary>
    [Serializable]
    public abstract class UIServiceHandler : FrameworkHandler
    {
        /// <summary>
        /// UI根节点。
        /// </summary>
        public abstract Transform UIRoot { get; }

        /// <summary>
        /// UI专用摄像机。
        /// </summary>
        public abstract Camera UICamera { get; }

        /// <summary>
        /// 当前模态遮挡窗口。
        /// </summary>
        public abstract UIWindow CurrentModal { get; }

        /// <summary>
        /// 资源加载器。
        /// </summary>
        public abstract IUIResourceLoader Resource { get; set; }

        /// <summary>
        /// 判断窗口是否为模态窗口。
        /// </summary>
        public virtual bool IsModal(UIWindow window) => window.WindowLayer == (int)UILayer.UI ||
                                                        window.WindowLayer == (int)UILayer.Popup ||
                                                        window.WindowLayer == (int)UILayer.System;

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 处理器初始化。此阶段（AfterAssembliesLoaded）场景尚未加载，初始化延迟到首个 Update tick。
        /// </summary>
        protected override void OnInit()
        {
        }

        /// <summary>
        /// 处理器关闭。
        /// 1. 清理错误日志系统
        /// 2. 关闭所有窗口
        /// 3. 销毁UI根节点
        /// </summary>
        protected override void OnShutdown()
        {
        }

        /// <summary>
        /// 每帧驱动窗口内部更新。
        /// </summary>
        public abstract void Tick(float elapseSeconds, float realElapseSeconds);

        #endregion

        #region 设置安全区域 [SET SAFE AREA]

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）。
        /// </summary>
        /// <param name="safeRect">安全区域</param>
        public abstract void ApplyScreenSafeRect(Rect safeRect);

        /// <summary>
        /// 模拟IPhoneX异形屏
        /// </summary>
        public abstract void SimulateIPhoneXNotchScreen();

        #endregion

        #region 窗口查询 [WINDOW QUERIES]

        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        public abstract UIWindow GetTopWindow();

        /// <summary>
        /// 获取指定层级下顶部的窗口名称。
        /// </summary>
        public abstract string GetTopWindowName(int layer);

        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        public abstract UIWindow GetTopWindow(int layer);

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        public abstract bool IsAnyLoading();

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <typeparam name="T">界面类型。</typeparam>
        /// <param name="windowName">窗口名称</param>
        /// <returns>是否存在。</returns>
        public abstract bool HasWindow<T>(string windowName = null) where T : UIWindow;

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <param name="windowName">窗口名称</param>
        /// <returns>是否存在。</returns>
        public abstract bool HasWindow(Type type, string windowName = null);

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        /// <param name="windowName"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public abstract T GetWindow<T>(string windowName) where T : UIWindow;

        /// <summary>
        /// 判断是否被模态窗口遮挡
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public abstract bool IsBlockedByModal(GameObject obj);

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
        public abstract void ShowUIAsync<T>(string windowName = null, string assetName = null, bool fromResources = false, params object[] userData)
            where T : UIWindow, new();

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public abstract void ShowUI<T>(string windowName = null, string assetName = null, bool fromResources = false, params object[] userData) where T : UIWindow, new();

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="type"></param>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public abstract void ShowUIAsync(Type type, string windowName = null, string assetName = null, bool fromResources = false, params object[] userData);

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <param name="type"></param>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public abstract void ShowUI(Type type, string windowName = null, string assetName = null, bool fromResources = false, params object[] userData);

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public abstract UniTask<UIWindow> ShowUIAsyncAwait<T>(string windowName = null, string assetName = null, bool fromResources = false, params object[] userData) where T : UIWindow, new();

        #endregion

        #region 关闭窗口 [CLOSE WINDOW]

        /// <summary>
        /// 关闭窗口
        /// </summary>
        public abstract void CloseUI<T>(string windowName = null) where T : UIWindow;

        public abstract void CloseUI(Type type, string windowName = null);

        public abstract void HideUI<T>(string windowName = null) where T : UIWindow;

        public abstract void HideUI(Type type, string windowName = null);

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        public abstract void CloseAll(bool isShutDown = false);

        /// <summary>
        /// 关闭所有窗口除了指定窗口。
        /// </summary>
        public abstract void CloseAllWithOut(UIWindow withOut);

        /// <summary>
        /// 关闭所有窗口除了指定类型的窗口。
        /// </summary>
        public abstract void CloseAllWithOut<T>() where T : UIWindow;

        /// <summary>
        /// 关闭所有窗口除了指定层级的窗口。
        /// </summary>
        public abstract void CloseAllWithOut(UILayer withOut);

        #endregion

        #region 异步获取窗口 [GET WINDOW ASYNC]

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <returns>打开窗口操作句柄。</returns>
        public abstract UniTask<T> GetUIAsyncAwait<T>() where T : UIWindow;

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <param name="callback">回调。</param>
        public abstract void GetUIAsync<T>(Action<T> callback) where T : UIWindow;

        #endregion
    }
}
