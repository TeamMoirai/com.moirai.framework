using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Timer;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 窗口栈、停放表与交互租约的共享持有者：承载开栈与关·隐·查询的编排本体，各轨处理器把这一份存储经门缝取用。
    /// </summary>
    /// <remarks>
    /// 堆栈里放的是后端中立的 <see cref="UIWindow"/>，一支后端的窗口与另一支的并存在同一份栈上，关·隐·查询因此不分轨。 <br />
    /// 本类型只承载栈序与编排本身，不接管任何后端资源：UI 根、摄像机与面板装载仍住在各轨处理器里。 <br />
    /// 线程契约：仅主线程。
    /// </remarks>
    internal sealed class UIWindowLedger
    {
        /// <summary>窗口加载等待超时（秒）——ShowUIAwaitImp 与门面侧的 GetUIAsyncAwait/GetUIAsync 同判据。</summary>
        private const float LOAD_WAIT_TIMEOUT_SECONDS = 60f;

        private readonly List<UIWindow> _uiStack = new List<UIWindow>(128); // 窗口堆栈
        private readonly Dictionary<string, UIWindow> _cache = new Dictionary<string, UIWindow>(128);

        /// <summary>模态动画期间交互压制的归属仲裁。与窗口堆栈同生命周期。</summary>
        internal UIInteractionLease InteractionLease { get; } = new UIInteractionLease();

        /// <summary>当前模态遮挡窗口。</summary>
        internal UIWindow CurrentModal
        {
            get
            {
                // 反向手写循环：LastOrDefault(IsModal) 会装箱 List 枚举器、并每次新建判定委托——
                // 本属性是交互前置判断（UIServiceHelper）的高频查询入口，保持零分配取末位命中。
                for (int i = _uiStack.Count - 1; i >= 0; i--)
                {
                    var window = _uiStack[i];
                    if (IsModal(window)) return window;
                }

                return null;
            }
        }

        /// <summary>
        /// 判断窗口是否为模态窗口。
        /// </summary>
        internal bool IsModal(UIWindow window) => window.WindowLayer == (int)UILayer.UI ||
                                                   window.WindowLayer == (int)UILayer.Popup ||
                                                   window.WindowLayer == (int)UILayer.System;

        /// <summary>
        /// 把栈与停放表归零：只有门面的那一道归零事务叫它（<see cref="UIService.Internal_ResetSharedLedger"/>，初始化与关停各一次），交互租约的复位由那道事务在同批接办。
        /// </summary>
        internal void ResetStorage()
        {
            _uiStack.Clear();
            _cache.Clear();
        }

        /// <summary>
        /// 每帧驱动栈上窗口的内部更新：栈序在遍历期间被窗口回叫改写时立刻收尾，避免半程索引读到错位窗口。
        /// </summary>
        internal void Tick()
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

        #region 窗口查询 [WINDOW QUERIES]

        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        internal UIWindow GetTopWindow()
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
        internal string GetTopWindowName(int layer)
        {
            UIWindow lastOne = GetTopWindow(layer);

            return lastOne == null ? string.Empty : lastOne.WindowName;
        }

        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        internal UIWindow GetTopWindow(int layer)
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
        internal bool IsAnyLoading()
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
        internal bool HasWindow<T>(string windowName = null) where T : UIWindow
        {
            return HasWindow(typeof(T), windowName);
        }

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        internal bool HasWindow(Type type, string windowName = null)
        {
            return IsContains(windowName ?? type.FullName);
        }

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        internal T GetWindow<T>(string windowName) where T : UIWindow
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
        /// 判断是否被模态窗口遮挡。
        /// </summary>
        internal bool IsBlockedByModal(GameObject obj)
        {
            GameObject curModal = CurrentModal?.gameObject;

            if (curModal == null) return false;
            if (curModal == obj || obj.IsChildOf(curModal)) return false;

            return true;
        }

        /// <summary>
        /// 按窗口名取栈上的窗口：栈上没有同名窗口时回 null。
        /// </summary>
        internal UIWindow GetWindow(string windowName)
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
        /// 查询窗口名称是否已在栈上（<paramref name="windowName"/> 由调用方给全，未命名窗口取类型全名的规则在调用方一侧）。
        /// </summary>
        internal bool IsContains(string windowName)
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
        /// 开栈编排的同步腿：认名→复用栈上那一只 / 取回停放的那一只 / 造一只新的，然后压栈并发起装载。
        /// </summary>
        /// <remarks>
        /// 这一份只落共享栈、不认轨：门面上每一轨自己的开窗实现（<c>UIService.&lt;轨&gt;.cs</c>）都经它把窗口送进同一份栈， <br />
        /// 两轨交给它的差别只在实参取值。 <br />
        /// <paramref name="onInstanceCreated"/> 是<b>本轨自己</b>那一段配置的交接处：只在造出新实例那一档、<c>Push</c> 与 <c>InternalLoad</c> 之前叫一次， <br />
        /// 形参表只写中性的 <see cref="UIWindow"/>，后端类型因此落不进这条链路的签名；交回 null 时一次都不叫。 <br />
        /// 复用与停放重取那两条支路不叫它：那两只窗的面板早已装好，与 <paramref name="assetLocation"/>、<paramref name="fromResources"/> 一样不再吃。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="onInstanceCreated">新实例装载前的交接钩子；不需要交接时为 null。</param>
        /// <param name="userData">用户自定义数据。</param>
        internal void ShowUIImp(Type type, bool isAsync, string windowName, string assetLocation, bool fromResources,
            Action<UIWindow> onInstanceCreated, params object[] userData)
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
                    window = CreateInstance(type, windowName, assetLocation, fromResources);
                    onInstanceCreated?.Invoke(window); // 交在压栈与装载之前：晚一步面板就按没覆盖的那一份装上了
                    Push(window); // 首次压入
                    window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userData).Forget();
                }
            }
        }

        /// <summary>栈上已有同名窗口时把它挪到栈顶并发准备回执。</summary>
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

        /// <summary>
        /// 开栈编排的等待腿：与同步腿同一份栈、同一次压入，另把「面板就绪」等出来再交回窗口。
        /// </summary>
        /// <remarks>
        /// 压栈那一段与 <see cref="ShowUIImp"/> 同一份判据，含 <paramref name="onInstanceCreated"/> 的交接时机与不吃的那两条支路。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="onInstanceCreated">新实例装载前的交接钩子；不需要交接时为 null。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>栈上那一只窗口（面板就绪或等待超时之后交回）。</returns>
        internal async UniTask<UIWindow> ShowUIAwaitImp(Type type, bool isAsync, string windowName, string assetLocation, bool fromResources,
            Action<UIWindow> onInstanceCreated, params object[] userData)
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
                window = CreateInstance(type, windowName, assetLocation, fromResources);
                onInstanceCreated?.Invoke(window); // 同上：交在压栈与装载之前，等出来的面板才带着这一枚覆盖
                Push(window); // 首次压入
                window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userData).Forget();
            }

            // 使用 WaitUntil 替代手动轮询，避免每帧 unscaledDeltaTime 累加；CTS 提供超时保护
            using (var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(LOAD_WAIT_TIMEOUT_SECONDS)))
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

        private UIWindow CreateInstance(Type type, string windowName, string assetLocation = null, bool fromResources = false)
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
                if (string.IsNullOrEmpty(assetLocation))
                {
                    assetLocation = string.IsNullOrEmpty(attribute.location) ? type.Name : attribute.location;
                }
                fromResources = fromResources || attribute.fromResources;
                window.Init(windowName, attribute.windowLayer, attribute.fullScreen, assetLocation, fromResources, attribute.hideTimeToClose, attribute.cacheInstance);
            }
            else
            {
                window.Init(windowName, (int)UILayer.UI, fullScreen: window.FullScreen, assetLocation: assetLocation ?? type.Name, fromResources: false, hideTimeToClose: 10, cacheInstance: false);
            }

            return window;
        }

        #endregion

        #region 异步获取窗口 [GET WINDOW ASYNC]

        /// <summary>
        /// 异步获取窗口：栈上没有这一名、或那一只是别的类型时交回 null，否则把面板就绪等出来。
        /// </summary>
        /// <remarks>
        /// 这一枚问的是那条共享栈，两支后端的窗都在它的射程里：等待的那一半住在这里，各轨处理器只是转发口。
        /// </remarks>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>窗口实例。</returns>
        internal async UniTask<T> GetUIAsyncAwait<T>() where T : UIWindow
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

            // 使用 WaitUntil 替代手动轮询；CTS 提供超时保护
            using (var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(LOAD_WAIT_TIMEOUT_SECONDS)))
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
        /// 异步获取窗口：与等待腿同一份栈、同一条判据，另把结果交回回调。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="callback">回调。</param>
        internal void GetUIAsync<T>(Action<T> callback) where T : UIWindow
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
                using (var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(LOAD_WAIT_TIMEOUT_SECONDS)))
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

        #region 关闭窗口 [CLOSE WINDOW]

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        internal void CloseUI<T>(string windowName = null) where T : UIWindow
        {
            CloseUI(typeof(T), windowName);
        }

        internal void CloseUI(Type type, string windowName = null)
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
            if (_uiStack.Count > 0) _uiStack[_uiStack.Count - 1].InternalRefresh(false);
        }

        internal void HideUI<T>(string windowName = null) where T : UIWindow
        {
            HideUI(typeof(T), windowName);
        }

        internal void HideUI(Type type, string windowName = null)
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
            window.HideTimerId = TimerService.Delay(window.HideTimeToClose, window.Close);

            if (window.FullScreen)
            {
                OnSetWindowVisible();
            }
        }

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        internal void CloseAll(bool isShutDown = false)
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
        /// 关闭栈上被这一轨认得的那些窗口，其余连位置都不动：一支 handler 关停时只交自己那一轨的窗进来。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="CloseAll"/> 同一次序（自下而上）、同一分档（缓存窗交进停放表、非缓存窗直接销毁）， <br />
        /// 只是不抹整条栈——判据没认得的窗口留在原来的栈位上，由它自己那一轨去收。
        /// </remarks>
        /// <param name="isShutDown">关停轮：连缓存窗也一并销毁，不进停放表。</param>
        /// <param name="onTrack">返回真时这一只属于调用方那一轨。</param>
        internal void CloseAllWhere(bool isShutDown, Func<UIWindow, bool> onTrack)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (!onTrack(window))
                {
                    continue;
                }

                _uiStack.RemoveAt(i);
                i--;

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
        }

        /// <summary>
        /// 关闭所有窗口除了指定窗口。
        /// </summary>
        internal void CloseAllWithOut(UIWindow withOut)
        {
            CloseAllWithOutInternal(window => window == withOut);
        }

        /// <summary>
        /// 关闭所有窗口除了指定类型的窗口。
        /// </summary>
        internal void CloseAllWithOut<T>() where T : UIWindow
        {
            CloseAllWithOutInternal(window => window.GetType() == typeof(T));
        }

        /// <summary>
        /// 关闭所有窗口除了指定层级的窗口。
        /// </summary>
        internal void CloseAllWithOut(UILayer withOut)
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
            if (_uiStack.Count > 0) _uiStack[_uiStack.Count - 1].InternalRefresh(false);
        }

        #endregion

        #region 窗口堆栈 [WINDOW STACK]

        /// <summary>
        /// 窗口面板就绪：补建窗口、按层级重排深度、重发显隐回执。
        /// </summary>
        internal void OnWindowPrepare(UIWindow window)
        {
            window.InternalCreate();
            OnSortWindowDepth(window.WindowLayer);
            OnSetWindowVisible();
        }

        /// <summary>
        /// 重排指定层级内各窗口的深度：按栈序从该层基址起逐窗口加一档 <see cref="UIService.WINDOW_DEEP"/>。
        /// </summary>
        internal void OnSortWindowDepth(int layer)
        {
            int depth = layer * UIService.LAYER_DEEP;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (_uiStack[i].WindowLayer == layer)
                {
                    _uiStack[i].Depth = depth;
                    depth += UIService.WINDOW_DEEP;
                }
            }
        }

        /// <summary>
        /// 自栈顶向下发显隐回执：栈顶可见，遇到已准备的全屏窗口后其余一律置为不可见。
        /// </summary>
        internal void OnSetWindowVisible()
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

        /// <summary>
        /// 把窗口压入堆栈：按所属层级定位插入点，模态窗口压掉下层窗口的可交互位，末尾发一次打开回执。
        /// </summary>
        internal void Push(UIWindow window)
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

        /// <summary>
        /// 把窗口移出堆栈并发一次关闭回执。
        /// </summary>
        internal void Pop(UIWindow window)
        {
            // 从堆栈里移除
            _uiStack.Remove(window);
            UIServiceEvent.Closed(window);
        }

        #endregion

        #region 测试接缝 [TEST SEAMS]

        /// <summary>
        /// 栈上窗口的只读视图：栈本体住在持有者里、保持 private，这一道门只给读、不给写。
        /// </summary>
        internal IReadOnlyList<UIWindow> PeekStack() => _uiStack;

        /// <summary>
        /// 停放表里是否有这个名字的窗：缓存实例关闭后落在这里，栈上已无。
        /// </summary>
        internal bool IsParked(string windowName) => _cache.ContainsKey(windowName);

        #endregion
    }
}
