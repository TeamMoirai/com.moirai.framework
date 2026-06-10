using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Debugger;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI模块。
    /// </summary>
    public sealed partial class UIModule : Singleton<UIModule>, IUpdate
    {
        // 核心字段
        private static Transform s_InstanceRoot = null; // UI根节点变换组件
        private bool _enableErrorLog = true; // 是否启用错误日志
        private Camera _uiCamera = null; // UI专用摄像机
        private readonly List<UIWindow> _uiStack = new List<UIWindow>(128); // 窗口堆栈
        private readonly Dictionary<string, UIWindow> _cache = new Dictionary<string, UIWindow>(128);
        private ErrorLogger _errorLogger; // 错误日志记录器

        public const int LAYER_DEEP = 2000;
        public const int WINDOW_DEEP = 100;
        public const int WINDOW_HIDE_LAYER = 2; // Ignore Raycast
        public const int WINDOW_SHOW_LAYER = 5; // UI

        // 资源加载接口
        public static IUIResourceLoader Resource;
        
        /// <summary>
        /// UI根节点。
        /// </summary>
        public static Transform UIRoot => s_InstanceRoot;

        /// <summary>
        /// UI根节点。
        /// </summary>
        public Camera UICamera => _uiCamera;

        /// <summary>
        /// 是否有模态遮挡
        /// </summary>
        public UIWindow CurrentModal => _uiStack.LastOrDefault(IsModal);
        
        internal bool IsModal(UIWindow window) => window.WindowLayer == (int)UILayer.UI ||
                                                  window.WindowLayer == (int)UILayer.Popup ||
                                                  window.WindowLayer == (int)UILayer.System;

        /// <summary>
        /// 模块初始化（自动调用）。
        /// 1. 查找场景中的UIRoot
        /// 2. 初始化资源加载器
        /// 3. 配置错误日志系统
        /// </summary>
        protected override void OnInit()
        {
            var uiRoot = GameObject.Find("UIRoot");
            if (uiRoot != null)
            {
                var canvas = uiRoot.GetComponentInChildren<Canvas>();
                if (canvas == null)
                {
                    Log.Fatal("Can't find any Canvas under UIRoot! Please add a Canvas first.");
                    return;
                }

                s_InstanceRoot = canvas.transform;
                _uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            }
            else
            {
                Log.Fatal("UIRoot not found!");
                return;
            }
            
            Resource = new UIResourceLoader();

            UnityEngine.Object.DontDestroyOnLoad(s_InstanceRoot.parent != null ? s_InstanceRoot.parent : s_InstanceRoot);

            s_InstanceRoot.gameObject.layer = LayerMask.NameToLayer("UI");

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
                    _errorLogger = new ErrorLogger(this);
                }
            }
        }
        
        /// <summary>
        /// 模块释放（自动调用）。
        /// 1. 清理错误日志系统
        /// 2. 关闭所有窗口
        /// 3. 销毁UI根节点
        /// </summary>
        protected override void Shutdown()
        {
            if (_errorLogger != null)
            {
                _errorLogger.Dispose();
                _errorLogger = null;
            }
            CloseAll(isShutDown:true);
            if (s_InstanceRoot != null && s_InstanceRoot.parent != null)
            {
                UnityEngine.Object.Destroy(s_InstanceRoot.parent.gameObject);
            }
        }

        #region 设置安全区域

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）。
        /// </summary>
        /// <param name="safeRect">安全区域</param>
        public static void ApplyScreenSafeRect(Rect safeRect)
        {
            CanvasScaler scaler = UIRoot.GetComponentInParent<CanvasScaler>();
            if (scaler == null)
            {
                Log.Error($"Not found {nameof(CanvasScaler)} !");
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
        public static void SimulateIPhoneXNotchScreen()
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
        
        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        public UIWindow GetTopWindow()
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
        public string GetTopWindowName(int layer)
        {
            UIWindow lastOne = GetTopWindow(layer);
            
            return lastOne == null ? string.Empty : lastOne.WindowName;
        }
        
        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        public UIWindow GetTopWindow(int layer)
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
        public bool IsAnyLoading()
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
        public bool HasWindow<T>(string windowName = null)
        {
            return HasWindow(typeof(T), windowName);
        }

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <param name="windowName">窗口名称</param>
        /// <returns>是否存在。</returns>
        public bool HasWindow(Type type, string windowName = null)
        {
            return IsContains(windowName ?? type.FullName);
        }

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUIAsync<T>(string windowName = null, string assetName = null, bool fromResources = false, params System.Object[] userData) where T : UIWindow , new()
        {
            ShowUIImp(typeof(T), true , windowName, assetName, fromResources, userData);
        }

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称</param>
        /// <param name="assetName">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUI<T>(string windowName = null, string assetName = null, bool fromResources = false, params System.Object[] userData) where T : UIWindow , new()
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
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUIAsync(Type type, string windowName = null, string assetName = null, bool fromResources = false, params System.Object[] userData)
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
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUI(Type type, string windowName = null, string assetName = null, bool fromResources = false, params System.Object[] userData)
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
        public async UniTask<UIWindow> ShowUIAsyncAwait<T>(string windowName = null, string assetName = null, bool fromResources = false, params System.Object[] userData) where T : UIWindow , new()
        {
            return await ShowUIAwaitImp(typeof(T), true, windowName, assetName, fromResources, userData);
        }

        private void ShowUIImp(Type type, bool isAsync, string windowName, string assetName, bool fromResources, params System.Object[] userData)
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
        
        private bool TryGetWindow(string windowName,out UIWindow window, params System.Object[] userData)
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
        
        private async UniTask<UIWindow> ShowUIAwaitImp(Type type, bool isAsync, string windowName, string assetName, bool fromResources, params System.Object[] userData)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;
            
            if (TryGetWindow(windowName, out UIWindow window, userData))
            {
                return window;
            }
            else
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
                
                float time = 0f;
                while (!window.IsLoadDone)
                {
                    time += UnityEngine.Time.unscaledDeltaTime;
                    if (time > 60f)
                    {
                        break;
                    }
                    await UniTask.Yield();
                }
                
                return window;
            }
        }

        /// <summary>
        /// 关闭窗口
        /// </summary>
        public void CloseUI<T>(string windowName = null) where T : UIWindow
        {
            CloseUI(typeof(T), windowName);
        }

        public void CloseUI(Type type, string windowName = null)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;
            UIWindow window = GetWindow(windowName);
           
            if (window == null) return;

            if (window.CacheInstance)
            {
                _cache.Add(windowName, window);
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
        
        public void HideUI<T>(string windowName = null) where T : UIWindow
        {
            HideUI(typeof(T), windowName);
        }

        public void HideUI(Type type, string windowName = null)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;
            UIWindow window = GetWindow(windowName);
            if (window == null)
            {
                return;
            }

            if (window.HideTimeToClose <= 0)
            {
                CloseUI(type);
                return;
            }
            
            window.CancelHideToCloseTimer();
            window.Visible = false;
            window.IsHide = true;
            window.HideTimerId = GameModule.Timer.AddTimer((arg) =>
            {
                CloseUI(type);
            } ,window.HideTimeToClose);
            
            if (window.FullScreen)
            {
                OnSetWindowVisible();
            }
        }

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        public void CloseAll(bool isShutDown = false)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (!isShutDown && window.CacheInstance)
                {
                    _cache.Add(window.WindowName, window);
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
        /// 关闭所有窗口除了。
        /// </summary>
        public void CloseAllWithOut(UIWindow withOut)
        {
            for (int i = _uiStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = _uiStack[i];
                if (window == withOut)
                {
                    continue;
                }

                if (window.CacheInstance)
                {
                    _cache.Add(window.WindowName, window);
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

        /// <summary>
        /// 关闭所有窗口除了。
        /// </summary>
        public void CloseAllWithOut<T>() where T : UIWindow
        {
            for (int i = _uiStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = _uiStack[i];
                if (window.GetType() == typeof(T))
                {
                    continue;
                }

                if (window.CacheInstance)
                {
                    _cache.Add(window.WindowName, window);
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

        /// <summary>
        /// 关闭所有窗口除了。
        /// </summary>
        public void CloseAllWithOut(UILayer withOut)
        {
            for (int i = _uiStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = _uiStack[i];
                if (window.WindowLayer == (int)withOut)
                {
                    continue;
                }

                if (window.CacheInstance)
                {
                    _cache.Add(window.WindowName, window);
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
        
        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <returns>打开窗口操作句柄。</returns>
        public async UniTask<T> GetUIAsyncAwait<T>() where T : UIWindow
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

            float time = 0f;
            while (!ret.IsLoadDone)
            {
                time += UnityEngine.Time.unscaledDeltaTime;
                if (time > 60f)
                {
                    break;
                }
                await UniTask.Yield();
            }
            return ret;
        }

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <param name="callback">回调。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void GetUIAsync<T>(Action<T> callback) where T : UIWindow
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
                float time = 0f;
                while (!ret.IsLoadDone)
                {
                    time += UnityEngine.Time.unscaledDeltaTime;
                    if (time > 60f)
                    {
                        break;
                    }
                    await UniTask.Yield();
                }
                ctx?.Invoke(ret);
            }
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

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        /// <param name="windowName"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetWindow<T>(string windowName) where T : UIWindow
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
        public bool IsBlockedByModal(GameObject obj)
        {
            GameObject curModal = CurrentModal?.gameObject;
            
            if (curModal == null) return false;
            if (curModal == obj || obj.IsChildOf(curModal)) return false;
            
            return true;
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

            // 最后插入到堆栈
            _uiStack.Insert(insertIndex, window);
            UIModuleEvent.Shown(window);
        }

        private void Pop(UIWindow window)
        {
            // 从堆栈里移除
            _uiStack.Remove(window);
            UIModuleEvent.Closed(window);
        }
        
        public void OnUpdate()
        {
            if (_uiStack == null)
            {
                return;
            }

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
    }
}