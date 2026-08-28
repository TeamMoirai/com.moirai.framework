using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Timer;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI处理器（后端）。承载窗口堆栈管理、层级排序与资源加载等核心逻辑。
    /// <para>通过 <see cref="UIServiceSettings.UIServiceHandler"/> 序列化配置，可替换为自定义 UI 后端。</para>
    /// </summary>
    [Serializable]
    public sealed class UGUIHandler : UIServiceHandler
    {
        // 核心字段
        private Transform _instanceRoot = null; // UI根节点变换组件
        private bool _enableErrorLog = true; // 是否启用错误日志
        private Camera _uiCamera = null; // UI专用摄像机
        private readonly List<UIWindow> _uiStack = new List<UIWindow>(128); // 窗口堆栈
        private readonly Dictionary<string, UIWindow> _cache = new Dictionary<string, UIWindow>(128);
        private ErrorLogger _errorLogger; // 错误日志记录器
        private bool _uiInitialized;

        /// <summary>
        /// UI根节点。
        /// </summary>
        public override Transform UIRoot => _instanceRoot;

        /// <summary>
        /// UI专用摄像机。
        /// </summary>
        public override Camera UICamera => _uiCamera;

        /// <summary>
        /// 当前模态遮挡窗口。
        /// </summary>
        public override UIWindow CurrentModal => _uiStack.LastOrDefault(IsModal);

        /// <summary>
        /// 资源加载器。
        /// </summary>
        public override IUIResourceLoader Resource { get; set; }

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 处理器初始化。此阶段（AfterAssembliesLoaded）场景尚未加载，初始化延迟到首个 Update tick。
        /// </summary>
        protected override void OnInit()
        {
            MainThreadDispatcher.Post(TryInitializeUIRoot);
        }

        /// <summary>
        /// 处理器关闭。
        /// 1. 清理错误日志系统
        /// 2. 关闭所有窗口
        /// 3. 销毁UI根节点
        /// </summary>
        protected override void OnShutdown()
        {
            if (_errorLogger != null)
            {
                _errorLogger.Dispose();
                _errorLogger = null;
            }
            CloseAll(true);
            if (_instanceRoot != null && _instanceRoot.parent != null)
            {
                UnityEngine.Object.Destroy(_instanceRoot.parent.gameObject);
            }
            _uiInitialized = false;
        }

        /// <summary>
        /// 每帧驱动窗口内部更新。
        /// </summary>
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            if (_uiStack == null) return;

            int count = _uiStack.Count;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (_uiStack.Count != count)
                {
                    break;
                }

                var window = _uiStack[i];
                window.InternalUpdate();
            }
        }

        #endregion

        #region 初始化 [INITIALIZATION]

        private void TryInitializeUIRoot()
        {
            if (_uiInitialized) return;

            var uiRoot = GameObject.Find("UIRoot");
            if (uiRoot == null)
            {
                LogUtility.Fatal("UIRoot not found!");
                return;
            }

            var canvas = uiRoot.GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                LogUtility.Fatal("Can't find any Canvas under UIRoot! Please add a Canvas first.");
                return;
            }

            Resource = new UIResourceLoader();

            _instanceRoot = canvas.transform;
            _uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            UnityEngine.Object.DontDestroyOnLoad(_instanceRoot.parent != null ? _instanceRoot.parent : _instanceRoot);
            _instanceRoot.gameObject.layer = LayerMask.NameToLayer("UI");

            if (DebuggerComp.Instance != null)
            {
                switch (DebuggerComp.Instance.ActiveWindowType)
                {
                    case DebuggerActiveWindowType.AlwaysOpen:
                        _enableErrorLog = true;
                        break;

                    case DebuggerActiveWindowType.OnlyOpenWhenDevelopment:
                        _enableErrorLog = Debug.isDebugBuild;
                        break;

                    case DebuggerActiveWindowType.OnlyOpenInEditor:
                        _enableErrorLog = Application.isEditor;
                        break;

                    default:
                        _enableErrorLog = false;
                        break;
                }
                if (!_enableErrorLog)
                {
                    _errorLogger = new ErrorLogger();
                }
            }

            _uiInitialized = true;
        }

        #endregion

        #region 设置安全区域 [SET SAFE AREA]

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）。
        /// </summary>
        /// <param name="safeRect">安全区域</param>
        public override void ApplyScreenSafeRect(Rect safeRect)
        {
            CanvasScaler scaler = UIRoot.GetComponentInParent<CanvasScaler>();
            if (scaler == null)
            {
                LogUtility.Error($"Not found {nameof(CanvasScaler)} !");
                return;
            }

            // Convert safe area rectangle from absolute pixels to UGUI coordinates
            float rateX = scaler.referenceResolution.x / Screen.width;
            float rateY = scaler.referenceResolution.y / Screen.height;
            float posX = (int)(safeRect.position.x * rateX);
            float posY = (int)(safeRect.position.y * rateY);
            float width = (int)(safeRect.size.x * rateX);
            float height = (int)(safeRect.size.y * rateY);

            float offsetMaxX = scaler.referenceResolution.x - width - posX;
            float offsetMaxY = scaler.referenceResolution.y - height - posY;

            // 注意：安全区坐标系的原点为左下角
            var rectTrans = UIRoot.transform as RectTransform;
            if (rectTrans != null)
            {
                rectTrans.offsetMin = new Vector2(posX, posY); //锚框状态下的屏幕左下角偏移向量
                rectTrans.offsetMax = new Vector2(-offsetMaxX, -offsetMaxY); //锚框状态下的屏幕右上角偏移向量
            }
        }

        /// <summary>
        /// 模拟IPhoneX异形屏
        /// </summary>
        public override void SimulateIPhoneXNotchScreen()
        {
            Rect rect;
            if (Screen.height > Screen.width)
            {
                // 竖屏Portrait
                float deviceWidth = 1125;
                float deviceHeight = 2436;
                rect = new Rect(0f / deviceWidth, 102f / deviceHeight, 1125f / deviceWidth, 2202f / deviceHeight);
            }
            else
            {
                // 横屏Landscape
                float deviceWidth = 2436;
                float deviceHeight = 1125;
                rect = new Rect(132f / deviceWidth, 63f / deviceHeight, 2172f / deviceWidth, 1062f / deviceHeight);
            }

            Rect safeArea = new Rect(Screen.width * rect.x, Screen.height * rect.y, Screen.width * rect.width, Screen.height * rect.height);
            ApplyScreenSafeRect(safeArea);
        }

        #endregion

        #region 窗口查询 [WINDOW QUERIES]

        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        public override UIWindow GetTopWindow()
        {
            if (_uiStack.Count == 0)
            {
                return null;
            }

            UIWindow topWindow = _uiStack[^1];
            return topWindow;
        }

        /// <summary>
        /// 获取指定层级下顶部的窗口名称。
        /// </summary>
        public override string GetTopWindowName(int layer)
        {
            UIWindow lastOne = GetTopWindow(layer);

            return lastOne == null ? string.Empty : lastOne.WindowName;
        }

        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        public override UIWindow GetTopWindow(int layer)
        {
            UIWindow lastOne = null;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (_uiStack[i].WindowLayer == layer)
                    lastOne = _uiStack[i];
            }

            if (lastOne == null)
                return null;

            return lastOne;
        }

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        public override bool IsAnyLoading()
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                var window = _uiStack[i];
                if (window.IsLoadDone == false)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <typeparam name="T">界面类型。</typeparam>
        /// <param name="windowName">窗口名称</param>
        /// <returns>是否存在。</returns>
        public override bool HasWindow<T>(string windowName)
        {
            return HasWindow(typeof(T), windowName);
        }

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <param name="windowName">窗口名称</param>
        /// <returns>是否存在。</returns>
        public override bool HasWindow(Type type, string windowName)
        {
            return IsContains(windowName ?? type.FullName);
        }

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        /// <param name="windowName"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public override T GetWindow<T>(string windowName)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (window is T uiWindow && window.WindowName == windowName)
                {
                    return uiWindow;
                }
            }

            return null;
        }

        /// <summary>
        /// 判断是否被模态窗口遮挡
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool IsBlockedByModal(GameObject obj)
        {
            GameObject curModal = CurrentModal?.gameObject;

            if (curModal == null) return false;
            if (curModal == obj || obj.IsChildOf(curModal)) return false;

            return true;
        }

        private UIWindow GetWindow(string windowName)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (window.WindowName == windowName)
                {
                    return window;
                }
            }

            return null;
        }

        private bool IsContains(string windowName)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (window.WindowName == windowName)
                {
                    return true;
                }
            }

            return false;
        }

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
        public override void ShowUIAsync<T>(string windowName, string assetName, bool fromResources, params object[] userData)
        {
            ShowUIImp(typeof(T), true, windowName, assetName, fromResources, userData);
        }

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public override void ShowUI<T>(string windowName, string assetName, bool fromResources, params object[] userData)
        {
            ShowUIImp(typeof(T),
#if UNITY_WEBGL
                true
#else
                false
#endif
                , windowName, assetName, fromResources, userData);
        }

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="type"></param>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public override void ShowUIAsync(Type type, string windowName, string assetName, bool fromResources, params object[] userData)
        {
            ShowUIImp(type, true, windowName, assetName, fromResources, userData);
        }

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <param name="type"></param>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public override void ShowUI(Type type, string windowName, string assetName, bool fromResources, params object[] userData)
        {
            ShowUIImp(type,
#if UNITY_WEBGL
                true
#else
                false
#endif
                , windowName, assetName, fromResources, userData);
        }

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public override async UniTask<UIWindow> ShowUIAsyncAwait<T>(string windowName, string assetName, bool fromResources, params object[] userData)
        {
            return await ShowUIAwaitImp(typeof(T), true, windowName, assetName, fromResources, userData);
        }

        private void ShowUIImp(Type type, bool isAsync, string windowName, string assetName, bool fromResources, params object[] userData)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;

            if (!TryGetWindow(windowName, out UIWindow window, userData))
            {
                if (!string.IsNullOrEmpty(windowName) && _cache.TryGetValue(windowName, out window))
                {
                    window.gameObject.SetActive(true);
                    _cache.Remove(windowName);
                    Push(window); // 首次压入
                    window.TryInvoke(OnWindowPrepare, userData);
                }
                else
                {
                    window = CreateInstance(type, windowName, assetName, fromResources);
                    Push(window); // 首次压入
                    window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userData).Forget();
                }
            }
        }

        private bool TryGetWindow(string windowName, out UIWindow window, params object[] userData)
        {
            window = null;
            if (IsContains(windowName))
            {
                window = GetWindow(windowName);
                Pop(window); // 弹出窗口
                Push(window); // 重新压入
                window.TryInvoke(OnWindowPrepare, userData);

                return true;
            }
            return false;
        }

        private async UniTask<UIWindow> ShowUIAwaitImp(Type type, bool isAsync, string windowName, string assetName, bool fromResources, params object[] userData)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;

            if (TryGetWindow(windowName, out UIWindow window, userData))
            {
                return window;
            }

            if (!string.IsNullOrEmpty(windowName) && _cache.TryGetValue(windowName, out window))
            {
                window.gameObject.SetActive(true);
                _cache.Remove(windowName);
                Push(window); // 首次压入
                window.TryInvoke(OnWindowPrepare, userData);
            }
            else
            {
                window = CreateInstance(type, windowName, assetName, fromResources);
                Push(window); // 首次压入
                window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userData).Forget();
            }

            // 使用 WaitUntil 替代手动轮询，避免每帧 unscaledDeltaTime 累加；CTS 提供 60s 超时保护
            using (var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(60)))
            {
                try
                {
                    await UniTask.WaitUntil(() => window.IsLoadDone, cancellationToken: cts.Token);
                }
                catch (System.OperationCanceledException)
                {
                    LogUtility.Warning("ShowUIAsyncAwait timed out waiting for window load: {0}", windowName);
                }
            }

            return window;
        }

        private UIWindow CreateInstance(Type type, string windowName, string assetName = null, bool fromResources = false)
        {
            UIWindow window = Activator.CreateInstance(type) as UIWindow;
            WindowAttribute attribute = Attribute.GetCustomAttribute(type, typeof(WindowAttribute)) as WindowAttribute;

            if (window == null)
            {
                throw new GameException($"Window {type.FullName} create instance failed.");
            }

            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;

            if (attribute != null)
            {
                if (string.IsNullOrEmpty(assetName))
                {
                    assetName = string.IsNullOrEmpty(attribute.location) ? type.Name : attribute.location;
                }
                fromResources = fromResources || attribute.fromResources;
                window.Init(windowName, attribute.windowLayer, attribute.fullScreen, assetName, fromResources, attribute.hideTimeToClose, attribute.cacheInstance);
            }
            else
            {
                window.Init(windowName, (int)UILayer.UI, fullScreen: window.FullScreen, assetName: assetName ?? type.Name, fromResources: false, hideTimeToClose: 10, cacheInstance: false);
            }

            return window;
        }

        #endregion

        #region 关闭窗口 [CLOSE WINDOW]

        /// <summary>
        /// 关闭窗口
        /// </summary>
        public override void CloseUI<T>(string windowName)
        {
            CloseUI(typeof(T), windowName);
        }

        public override void CloseUI(Type type, string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;
            UIWindow window = GetWindow(windowName);

            if (window == null) return;

            if (window.CacheInstance)
            {
                _cache[windowName] = window;
                window.InternalClose();
            }
            else
            {
                window.InternalDestroy();
            }
            Pop(window);
            OnSortWindowDepth(window.WindowLayer);
            OnSetWindowVisible();
            if (_uiStack.Count > 0) _uiStack.Last().InternalRefresh(false);
        }

        public override void HideUI<T>(string windowName)
        {
            HideUI(typeof(T), windowName);
        }

        public override void HideUI(Type type, string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;
            UIWindow window = GetWindow(windowName);
            if (window == null)
            {
                return;
            }

            if (window.HideTimeToClose <= 0)
            {
                CloseUI(type, windowName);
                return;
            }

            window.CancelHideToCloseTimer();
            window.Visible = false;
            window.IsHide = true;
            window.HideTimerId = TimerService.AddTimer(() =>
            {
                CloseUI(type, windowName);
            }, window.HideTimeToClose);

            if (window.FullScreen)
            {
                OnSetWindowVisible();
            }
        }

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        public override void CloseAll(bool isShutDown)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (!isShutDown && window.CacheInstance)
                {
                    _cache[window.WindowName] = window;
                    window.InternalClose();
                }
                else
                {
                    window.InternalDestroy(isShutDown);
                }
            }

            _uiStack.Clear();
        }

        /// <summary>
        /// 关闭所有窗口除了指定窗口。
        /// </summary>
        public override void CloseAllWithOut(UIWindow withOut)
        {
            CloseAllWithOutInternal(window => window == withOut);
        }

        /// <summary>
        /// 关闭所有窗口除了指定类型的窗口。
        /// </summary>
        public override void CloseAllWithOut<T>()
        {
            CloseAllWithOutInternal(window => window.GetType() == typeof(T));
        }

        /// <summary>
        /// 关闭所有窗口除了指定层级的窗口。
        /// </summary>
        public override void CloseAllWithOut(UILayer withOut)
        {
            CloseAllWithOutInternal(window => window.WindowLayer == (int)withOut);
        }

        /// <summary>
        /// 关闭所有不匹配跳过条件的窗口（内部统一实现）。
        /// </summary>
        /// <param name="shouldSkip">返回 true 时跳过该窗口（保留不关闭）。</param>
        private void CloseAllWithOutInternal(Func<UIWindow, bool> shouldSkip)
        {
            for (int i = _uiStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = _uiStack[i];
                if (shouldSkip(window))
                {
                    continue;
                }

                if (window.CacheInstance)
                {
                    _cache[window.WindowName] = window;
                    window.InternalClose();
                }
                else
                {
                    window.InternalDestroy();
                }
                _uiStack.RemoveAt(i);
            }
            if (_uiStack.Count > 0) _uiStack.Last().InternalRefresh(false);
        }

        #endregion

        #region 异步获取窗口 [GET WINDOW ASYNC]

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <returns>打开窗口操作句柄。</returns>
        public override async UniTask<T> GetUIAsyncAwait<T>()
        {
            string windowName = typeof(T).FullName;
            var window = GetWindow(windowName);
            if (window == null)
            {
                return null;
            }

            var ret = window as T;

            if (ret == null)
            {
                return null;
            }

            if (ret.IsLoadDone)
            {
                return ret;
            }

            // 使用 WaitUntil 替代手动轮询；CTS 提供 60s 超时保护
            using (var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(60)))
            {
                try
                {
                    await UniTask.WaitUntil(() => ret.IsLoadDone, cancellationToken: cts.Token);
                }
                catch (System.OperationCanceledException)
                {
                    LogUtility.Warning("GetUIAsyncAwait timed out waiting for window load: {0}", typeof(T).FullName);
                }
            }
            return ret;
        }

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <param name="callback">回调。</param>
        public override void GetUIAsync<T>(Action<T> callback)
        {
            string windowName = typeof(T).FullName;
            var window = GetWindow(windowName);
            if (window == null)
            {
                return;
            }

            var ret = window as T;

            if (ret == null)
            {
                return;
            }

            GetUIAsyncImp(callback).Forget();

            async UniTaskVoid GetUIAsyncImp(Action<T> ctx)
            {
                using (var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(60)))
                {
                    try
                    {
                        await UniTask.WaitUntil(() => ret.IsLoadDone, cancellationToken: cts.Token);
                    }
                    catch (System.OperationCanceledException)
                    {
                        LogUtility.Warning("GetUIAsync timed out waiting for window load: {0}", typeof(T).FullName);
                    }
                }
                ctx?.Invoke(ret);
            }
        }

        #endregion

        #region 窗口堆栈 [WINDOW STACK]

        private void OnWindowPrepare(UIWindow window)
        {
            window.InternalCreate();
            OnSortWindowDepth(window.WindowLayer);
            OnSetWindowVisible();
        }

        private void OnSortWindowDepth(int layer)
        {
            int depth = layer * LAYER_DEEP;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (_uiStack[i].WindowLayer == layer)
                {
                    _uiStack[i].Depth = depth;
                    depth += WINDOW_DEEP;
                }
            }
        }

        private void OnSetWindowVisible()
        {
            bool isHideNext = false;
            for (int i = _uiStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = _uiStack[i];
                if (isHideNext == false)
                {
                    if (window.IsHide)
                    {
                        continue;
                    }
                    window.Visible = true;
                    if (window.IsPrepare && window.FullScreen)
                    {
                        isHideNext = true;
                    }
                }
                else
                {
                    window.Visible = false;
                }
            }
        }

        private void Push(UIWindow window)
        {
            // 如果已经存在
            if (IsContains(window.WindowName))
            {
                throw new GameException($"Window {window.WindowName} is exist.");
            }

            // 获取插入到所属层级的位置
            int insertIndex = -1;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (window.WindowLayer == _uiStack[i].WindowLayer)
                {
                    insertIndex = i + 1;
                }
            }

            // 如果没有所属层级，找到相邻层级
            if (insertIndex == -1)
            {
                for (int i = 0; i < _uiStack.Count; i++)
                {
                    if (window.WindowLayer > _uiStack[i].WindowLayer)
                    {
                        insertIndex = i + 1;
                    }
                }
            }

            // 如果是空栈或没有找到插入位置
            if (insertIndex == -1)
            {
                insertIndex = 0;
            }

            // 模态窗口会屏蔽下层的可交互
            if (insertIndex > 0 && IsModal(window)) _uiStack[insertIndex - 1].Interactable = false;

            // 最后插入到堆栈
            _uiStack.Insert(insertIndex, window);
            UIServiceEvent.Shown(window);
        }

        private void Pop(UIWindow window)
        {
            // 从堆栈里移除
            _uiStack.Remove(window);
            UIServiceEvent.Closed(window);
        }

        #endregion
    }
}
