using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Timer;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 窗口栈、停放表与交互租约的共享持有者：承载开栈与关·隐·查询的编排本体，各轨处理器经门缝取用这一份存储。
    /// </summary>
    /// <remarks>
    /// 栈里放的是后端中立的 <see cref="UIWindow"/>，两支后端的窗并存于同一份栈，关·隐·查询因此不分轨。<br />
    /// 本类型只承载栈序与编排本身，不接管后端资源：UI 根、摄像机与面板装载仍住在各轨处理器里。<br />
    /// 线程契约：仅主线程。
    /// </remarks>
    internal sealed class UIWindowLedger
    {
        /// <summary>窗口加载等待超时（秒）——ShowUIAwaitImp 与门面侧的 GetUIAsyncAwait/GetUIAsync 同判据。</summary>
        private const float LOAD_WAIT_TIMEOUT_SECONDS = 60f;

        private readonly List<UIWindow> _uiStack = new List<UIWindow>(128); // 窗口堆栈
        private readonly Dictionary<string, UIWindow> _cache = new Dictionary<string, UIWindow>(128);
        private List<string> _sweepScratch;
        private readonly List<UIWindow> _history = new List<UIWindow>(32); // 开启序：Push 追加、摘栈移除

        /// <summary>模态动画期间交互压制的归属仲裁。与窗口堆栈同生命周期。</summary>
        internal UIInteractionLease InteractionLease { get; } = new UIInteractionLease();

        /// <summary>当前模态遮挡窗口。</summary>
        internal UIWindow CurrentModal
        {
            get
            {
                // 高频查询入口（交互前置判断），手写倒序循环取末位命中，保持零分配。
                for (int i = _uiStack.Count - 1; i >= 0; i--)
                {
                    var window = _uiStack[i];
                    if (IsModal(window)) return window;
                }

                return null;
            }
        }

        /// <summary>
        /// 判断窗口是否为模态窗口：读窗口初始化时结算的模态位。
        /// </summary>
        /// <remarks>显式档（<c>[Window(modal:…)]</c>）赢过层级档；继承档在窗口侧按 <see cref="IsWindowLayerModal"/> 结算。</remarks>
        internal bool IsModal(UIWindow window) => window.IsModalWindow;

        /// <summary>
        /// 按层级判模态档：模态层级（UI/Popup/System）为模态，其余非模态。
        /// </summary>
        /// <remarks>继承档的结算真源；显式模态档不走这一份。</remarks>
        internal static bool IsWindowLayerModal(int layer) =>
            layer == (int)EUILayer.UI ||
            layer == (int)EUILayer.Popup ||
            layer == (int)EUILayer.System;

        /// <summary>
        /// 把栈与停放表归零。
        /// </summary>
        /// <remarks>
        /// 只由门面的那道归零事务叫（<see cref="UIService.Internal_ResetSharedLedger"/>，初始化与关停各一次）。<br />
        /// 不含交互租约：复位由那道事务在同一次调用里接办。
        /// </remarks>
        internal void ResetStorage()
        {
            _uiStack.Clear();
            _cache.Clear();
            _history.Clear();
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
                try
                {
                    window.Internal_Update();
                }
                catch (System.Exception ex)
                {
                    LogUtility.Error("UI 窗口 '{0}' 的 OnUpdate 抛出异常，本帧其余窗口照常结算：{1}", window.WindowName, ex);
                }
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
        /// 这一份只落共享栈、不认轨：每轨自己的开窗实现都经它把窗口送进同一份栈，两轨的差别只在实参取值。<br />
        /// <paramref name="onInstanceCreated"/> 只在造出新实例那一档叫一次，交在 <c>Push</c> 与 <c>InternalLoad</c> 之前，为 null 时不叫。<br />
        /// 复用栈上窗与停放重取那两条支路不叫它：那两只窗的面板早已装好，<paramref name="windowId"/> 与 <paramref name="fromResources"/> 也不再吃。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="onInstanceCreated">新实例装载前的交接钩子；不需要交接时为 null。</param>
        /// <param name="payload">动态腿擦除后的载荷；无载荷时传 <see cref="UIPayload.Empty"/>。</param>
        /// <param name="callerCt">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        internal void ShowUIImp(Type type, bool isAsync, string windowName, string windowId, bool fromResources,
            Action<UIWindow> onInstanceCreated, UIPayload payload, CancellationToken callerCt = default)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;

            ResolveOrStartLoad(type, isAsync, windowName, windowId, fromResources, onInstanceCreated, payload, out var window);
            JoinInFlight(window, callerCt);
        }

        /// <summary>开栈编排的同步腿（泛型直塞形）：静态腿经此把载荷按 <typeparamref name="TArg"/> 零装箱落进窗口。</summary>
        internal void ShowUIImp<TArg>(Type type, bool isAsync, string windowName, string windowId, bool fromResources,
            Action<UIWindow> onInstanceCreated, in TArg payload, CancellationToken callerCt = default)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;

            ResolveOrStartLoad<TArg>(type, isAsync, windowName, windowId, fromResources, onInstanceCreated, in payload, out var window);
            JoinInFlight(window, callerCt);
        }

        /// <summary>装载在途才登记等待者；复用/停放命中同步完成，CT 不消费（语义诚实）。</summary>
        private static void JoinInFlight(UIWindow window, CancellationToken callerCt)
        {
            if (window.IsLoadDone || window.IsDestroyed)
            {
                return;
            }

            window.Internal_JoinOpenWaiter();
            if (callerCt.CanBeCanceled)
            {
                window.Internal_RegisterOpenCancel(callerCt);
            }
        }

        /// <summary>
        /// 开栈编排的公共前置：认名→复用栈上那一只 / 取回停放的那一只 / 造一只新的压栈并发起装载。
        /// </summary>
        /// <remarks>
        /// 这一份只落共享栈、不认轨：每轨自己的开窗实现都经它把窗口送进同一份栈，两轨的差别只在实参取值。<br />
        /// <paramref name="onInstanceCreated"/> 只在造出新实例那一档叫一次，交在 <c>Push</c> 与装载之前，为 null 时不叫。<br />
        /// 复用栈上窗与停放重取那两条支路不叫它：那两只窗的面板早已装好，寻址两档也不再吃。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称（已给全）。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="onInstanceCreated">新实例装载前的交接钩子；不需要交接时为 null。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="window">进栈的那一只窗口（可能仍在装载）。</param>
        private void ResolveOrStartLoad(Type type, bool isAsync, string windowName, string windowId, bool fromResources,
            Action<UIWindow> onInstanceCreated, UIPayload payload, out UIWindow window)
        {
            if (TryGetWindow(windowName, out window))
            {
                if (!payload.IsEmpty) window.Internal_SetPayload(payload);   // last-wins：在飞/复用都覆盖
                window.TryInvoke(OnWindowPrepare);
                return;
            }

            if (!string.IsNullOrEmpty(windowName) && _cache.TryGetValue(windowName, out window))
            {
                // 载荷校验排在卸停放之前：抬错时那只实例仍留在停放表里取得回，不落既离表又未入栈的悬空态。
                if (!payload.IsEmpty) window.Internal_SetPayload(payload);
                window.CancelCacheTimer();
                window.gameObject.SetActive(true);
                _cache.Remove(windowName);
                Push(window);
                window.TryInvoke(OnWindowPrepare);
                return; // 停放重取：装载早已完成，同步交回不再等待
            }

            window = CreateInstance(type, windowName, windowId, fromResources);
            onInstanceCreated?.Invoke(window);
            if (!payload.IsEmpty) window.Internal_SetPayload(payload);
            Push(window);
            window.InternalLoad(window.AssetLocation, OnWindowPrepare, isAsync).Forget();
        }

        /// <summary>开栈编排的公共前置（泛型直塞形）：与 UIPayload 形同路，只把载荷经 <see cref="IUIPayloadSlot{TArg}"/> 强类型落位、不擦除。</summary>
        private void ResolveOrStartLoad<TArg>(Type type, bool isAsync, string windowName, string windowId, bool fromResources,
            Action<UIWindow> onInstanceCreated, in TArg payload, out UIWindow window)
        {
            if (TryGetWindow(windowName, out window))
            {
                SetPayloadChecked(window, in payload);
                window.TryInvoke(OnWindowPrepare);
                return;
            }

            if (!string.IsNullOrEmpty(windowName) && _cache.TryGetValue(windowName, out window))
            {
                // 载荷校验排在卸停放之前：抬错时那只实例仍留在停放表里取得回，不落既离表又未入栈的悬空态。
                SetPayloadChecked(window, in payload);
                window.CancelCacheTimer();
                window.gameObject.SetActive(true);
                _cache.Remove(windowName);
                Push(window);
                window.TryInvoke(OnWindowPrepare);
                return;
            }

            window = CreateInstance(type, windowName, windowId, fromResources);
            onInstanceCreated?.Invoke(window);
            SetPayloadChecked(window, in payload);
            Push(window);
            window.InternalLoad(window.AssetLocation, OnWindowPrepare, isAsync).Forget();
        }

        /// <summary>泛型直塞通道的落点：槽型不符（含不带槽）当场抬错，不退化为擦除路径。</summary>
        private static void SetPayloadChecked<TArg>(UIWindow window, in TArg payload)
        {
            if (window is IUIPayloadSlot<TArg> slot)
            {
                slot.SetPayload(in payload);
                return;
            }

            throw new GameException(StringUtility.Format(
                "UI 窗口 '{0}' 的载荷槽类型不符：按名命中的是 {1}，这一腿要塞 {2}。",
                window.WindowName, window.GetType().FullName, typeof(TArg).Name));
        }

        /// <summary>栈上已有同名窗口时把它挪到栈顶（Pop/Push 重排）；载荷写入与准备回执交由调用方排在重排之后。</summary>
        private bool TryGetWindow(string windowName, out UIWindow window)
        {
            window = null;
            if (IsContains(windowName))
            {
                window = GetWindow(windowName);
                Pop(window); // 弹出窗口
                Push(window); // 重新压入
                return true;
            }
            return false;
        }

        /// <summary>
        /// 开栈编排的等待腿：与同步腿同一份栈、同一次压入，另把「面板就绪」等出来再交回窗口。
        /// </summary>
        /// <remarks>
        /// 压栈那一段与 <c>ShowUIImp</c> 同一份判据（那一名下有 UIPayload 形与 <c>TArg</c> 形两枚重载，故不写 cref），含 <paramref name="onInstanceCreated"/> 的交接时机与不吃它的那两条支路。<br />
        /// 等面板就绪最长 <see cref="LOAD_WAIT_TIMEOUT_SECONDS"/> 秒；超时只发一条 Warning，仍交回那只窗口。<br />
        /// 装载失败或装载中被关闭的窗不再当结果交回：失败那一刻即交回 null，不再等到超时。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="onInstanceCreated">新实例装载前的交接钩子；不需要交接时为 null。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="callerCt">调用方取消令牌；被它撤销时等待原样上抛 <see cref="System.OperationCanceledException"/>，与超时分档。</param>
        /// <returns>栈上那一只窗口（面板就绪或等待超时之后交回；装载失败交回 null）。</returns>
        internal async UniTask<UIWindow> ShowUIAwaitImp(Type type, bool isAsync, string windowName, string windowId,
            bool fromResources, Action<UIWindow> onInstanceCreated, UIPayload payload, CancellationToken callerCt = default)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;

            ResolveOrStartLoad(type, isAsync, windowName, windowId, fromResources, onInstanceCreated, payload, out var window);
            if (window.IsLoadDone)
            {
                return window; // 复用/停放命中：同步完成
            }

            window.Internal_JoinOpenWaiter(); // 在飞合并：等待者 +1，装载不重启

            var waitLease = MemoryPool.Acquire<UICtsLease>();
            waitLease.Source.CancelAfter(System.TimeSpan.FromSeconds(LOAD_WAIT_TIMEOUT_SECONDS));
            try
            {
                await UniTask.Yield();
                await window.WaitPanelReadyAsync(waitLease.Source.Token, callerCt);
            }
            catch (System.OperationCanceledException)
            {
                if (callerCt.IsCancellationRequested)
                {
                    window.Internal_LeaveOpenWaiter();
                    throw; // 调用方取消：OCE 原样上抛（与超时分档）
                }

                LogUtility.Warning("ShowUIAsyncAwait timed out waiting for window load: {0}", windowName);
            }
            finally
            {
                waitLease.Source.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);
                MemoryPool.Release(waitLease);
            }

            if (window.IsLoadFailed || (window.IsDestroyed && !window.IsLoadDone))
            {
                return null;
            }

            return window;
        }

        /// <summary>开栈编排的等待腿（泛型直塞形）：与 UIPayload 形同路，仅把载荷换进泛型直塞通道。</summary>
        /// <remarks><paramref name="payload"/> 用普通形参而非 <c>in</c>：<c>async</c> 方法禁 <c>in</c> 形参（CS1988）；交给泛型直塞前置时按其 <c>in</c> 形参隐式按值传递。</remarks>
        internal async UniTask<UIWindow> ShowUIAwaitImp<TArg>(Type type, bool isAsync, string windowName, string windowId,
            bool fromResources, Action<UIWindow> onInstanceCreated, TArg payload, CancellationToken callerCt = default)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;

            ResolveOrStartLoad<TArg>(type, isAsync, windowName, windowId, fromResources, onInstanceCreated, payload, out var window);
            if (window.IsLoadDone)
            {
                return window;
            }

            window.Internal_JoinOpenWaiter();

            var waitLease = MemoryPool.Acquire<UICtsLease>();
            waitLease.Source.CancelAfter(System.TimeSpan.FromSeconds(LOAD_WAIT_TIMEOUT_SECONDS));
            try
            {
                await UniTask.Yield();
                await window.WaitPanelReadyAsync(waitLease.Source.Token, callerCt);
            }
            catch (System.OperationCanceledException)
            {
                if (callerCt.IsCancellationRequested)
                {
                    window.Internal_LeaveOpenWaiter();
                    throw;
                }

                LogUtility.Warning("ShowUIAsyncAwait timed out waiting for window load: {0}", windowName);
            }
            finally
            {
                waitLease.Source.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);
                MemoryPool.Release(waitLease);
            }

            if (window.IsLoadFailed || (window.IsDestroyed && !window.IsLoadDone))
            {
                return null;
            }

            return window;
        }

        /// <summary>
        /// 开栈编排的结果腿：与等待腿同一份栈、同一次压入，把「就绪/失败/超时」等成 <see cref="UIOpenResult"/> 交回。
        /// </summary>
        /// <remarks>
        /// 等待经窗口实例方法轮询、无闭包分配；终态先于首帧检查，同步装载与装载当场失败的窗口同帧落定。<br />
        /// 状态语义以 <see cref="UIOpenResult"/> 为准：就绪交回可用窗、失败交回已作废那只、超时交回仍在装载的那只。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="onInstanceCreated">新实例装载前的交接钩子；不需要交接时为 null。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="callerCt">调用方取消令牌；被它撤销即落 <see cref="EUIOpenStatus.Cancelled"/> 档。</param>
        /// <returns>开窗结果。</returns>
        internal async UniTask<UIOpenResult> ShowUIAwaitResultImp(Type type, bool isAsync, string windowName, string windowId,
            bool fromResources, Action<UIWindow> onInstanceCreated, UIPayload payload, CancellationToken callerCt = default)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;

            ResolveOrStartLoad(type, isAsync, windowName, windowId, fromResources, onInstanceCreated, payload, out var window);
            if (window.IsLoadDone)
            {
                return new UIOpenResult(EUIOpenStatus.Opened, window);
            }

            window.Internal_JoinOpenWaiter();
            return await WaitWindowResultAsync(window, windowName, LOAD_WAIT_TIMEOUT_SECONDS, callerCt);
        }

        /// <summary>开栈编排的结果腿（泛型直塞形）：与 UIPayload 形同路，仅把载荷换进泛型直塞通道。</summary>
        /// <remarks><paramref name="payload"/> 用普通形参而非 <c>in</c>：<c>async</c> 方法禁 <c>in</c> 形参（CS1988）；交给泛型直塞前置时按其 <c>in</c> 形参隐式按值传递。</remarks>
        internal async UniTask<UIOpenResult> ShowUIAwaitResultImp<TArg>(Type type, bool isAsync, string windowName, string windowId,
            bool fromResources, Action<UIWindow> onInstanceCreated, TArg payload, CancellationToken callerCt = default)
        {
            if (string.IsNullOrEmpty(windowName)) windowName = type.FullName;

            ResolveOrStartLoad<TArg>(type, isAsync, windowName, windowId, fromResources, onInstanceCreated, payload, out var window);
            if (window.IsLoadDone)
            {
                return new UIOpenResult(EUIOpenStatus.Opened, window);
            }

            window.Internal_JoinOpenWaiter();
            return await WaitWindowResultAsync(window, windowName, LOAD_WAIT_TIMEOUT_SECONDS, callerCt);
        }

        /// <summary>
        /// 装载失败的回滚：把窗口摘出栈、补深度与显隐回执、刷新新栈顶，再把窗口作废。
        /// </summary>
        /// <remarks>
        /// 窗口已被显式关闭收口时（<see cref="UIWindow.IsDestroyed"/> 已置位）不再补第二次关闭回执。<br />
        /// 关停守卫不在这一层：窗口侧按 <see cref="UIService.IsValid"/> 决定走不走这一道。
        /// </remarks>
        /// <param name="window">装载失败的那一只。</param>
        internal void RollbackFailedLoad(UIWindow window)
        {
            if (window.IsDestroyed)
            {
                return;
            }

            Pop(window);
            OnSortWindowDepth(window.WindowLayer);
            OnSetWindowVisible();
            if (_uiStack.Count > 0) _uiStack[_uiStack.Count - 1].InternalRefresh(false);
            window.AbortFailedLoad();
        }

        /// <summary>
        /// 造一只新窗口：查注册表拿编译期工厂与注册期描述符，不再走反射。
        /// </summary>
        /// <remarks>
        /// 标识必填：面板地址只由 <see cref="UIService.ResolveWindowLocation"/> 从 <paramref name="windowId"/> 换算而来，特性不再声明地址。 <br />
        /// 取法档是调用方与特性的并集（真 || 特性），特性缺的档不反被调用方否掉。 <br />
        /// 类型未登记当场抬错：窗口类必须标 <c>[Window]</c> 才进注册表，不再静默兜默认层级与地址。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="windowName">窗口名称（空串按描述符全名兜底）。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 <c>Resources</c> 下的相对路径）。</param>
        /// <param name="fromResources">调用方给的内置资源档。</param>
        /// <returns>已按描述符初始化好的新窗口。</returns>
        /// <exception cref="GameException">窗口类未登记（没标 <c>[Window]</c>），或开窗没带 <paramref name="windowId"/>。</exception>
        private UIWindow CreateInstance(Type type, string windowName, string windowId, bool fromResources)
        {
            if (!UIWindowRegistry.TryGet(type, out var entry))
            {
                throw new GameException(StringUtility.Format(
                    "UI 窗口 '{0}' 未注册：窗口类必须标 [Window] 才能经注册表开出（由 UIWindowCodegen 在编译期登记）",
                    type.FullName));
            }

            if (string.IsNullOrEmpty(windowId))
            {
                throw new GameException(StringUtility.Format(
                    "UI 窗口 '{0}' 开窗没带 windowId：面板地址只由窗口标识换算而来（配置表 configId，或 Resources 下的相对路径）",
                    type.FullName));
            }

            var window = entry.Factory();
            var descriptor = entry.Descriptor;

            if (string.IsNullOrEmpty(windowName))
            {
                windowName = descriptor.FullName;
            }

            var useResources = fromResources || descriptor.FromResources;

            window.Init(windowName, descriptor.WindowLayer, descriptor.FullScreen,
                UIService.ResolveWindowLocation(windowId, useResources),
                useResources, descriptor.HideTimeToClose,
                (EUIModal)descriptor.Modal, descriptor.CacheTimeToDestroy);

            return window;
        }

        #endregion

        #region 异步获取窗口 [GET WINDOW ASYNC]

        /// <summary>
        /// 异步获取窗口：栈上没有这一名、或那一只是别的类型时交回 null，否则把面板就绪等出来。
        /// </summary>
        /// <remarks>
        /// 问的是那条共享栈，两支后端的窗都在射程里，各轨处理器只是转发口。<br />
        /// 找不到时只发一条 Warning；装载失败或装载中被关闭的窗不再等超时交回，直接交回 null。
        /// </remarks>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>窗口实例。</returns>
        internal async UniTask<T> GetUIAsyncAwait<T>() where T : UIWindow
        {
            var ret = GetWindow(typeof(T).FullName) as T;
            if (ret == null)
            {
                LogUtility.Warning("GetUIAsyncAwait 栈上没有 '{0}' 类型的窗口：交回 null", typeof(T).FullName);
                return null;
            }

            if (ret.IsLoadDone)
            {
                return ret;
            }

            // 等面板就绪：先过一帧再入轮询（次帧首查语义），实例方法等待零闭包；超时经池租的取消源兜底
            var waitLease = MemoryPool.Acquire<UICtsLease>();
            waitLease.Source.CancelAfter(System.TimeSpan.FromSeconds(LOAD_WAIT_TIMEOUT_SECONDS));
            try
            {
                await UniTask.Yield();
                await ret.WaitPanelReadyAsync(waitLease.Source.Token, CancellationToken.None);
            }
            catch (System.OperationCanceledException)
            {
                LogUtility.Warning("GetUIAsyncAwait timed out waiting for window load: {0}", typeof(T).FullName);
            }
            finally
            {
                waitLease.Source.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);
                MemoryPool.Release(waitLease);
            }

            if (ret.IsLoadFailed || (ret.IsDestroyed && !ret.IsLoadDone))
            {
                return null;
            }

            return ret;
        }

        /// <summary>
        /// 异步获取窗口：与等待腿同一份栈、同一条判据，另把结果交回回调。
        /// </summary>
        /// <remarks>
        /// 找不到或类型不符时只发一条 Warning，回调不被调用。<br />
        /// 装载失败的窗不再把未就绪的那只交回回调。
        /// </remarks>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="callback">回调。</param>
        internal void GetUIAsync<T>(Action<T> callback) where T : UIWindow
        {
            var ret = GetWindow(typeof(T).FullName) as T;
            if (ret == null)
            {
                LogUtility.Warning("GetUIAsync 栈上没有 '{0}' 类型的窗口：回调不会被调用", typeof(T).FullName);
                return;
            }

            GetUIAsyncImp(callback).Forget();

            async UniTaskVoid GetUIAsyncImp(Action<T> ctx)
            {
                var waitLease = MemoryPool.Acquire<UICtsLease>();
                waitLease.Source.CancelAfter(System.TimeSpan.FromSeconds(LOAD_WAIT_TIMEOUT_SECONDS));
                try
                {
                    // 先过一帧再入轮询（次帧首查语义），实例方法等待零闭包
                    await UniTask.Yield();
                    await ret.WaitPanelReadyAsync(waitLease.Source.Token, CancellationToken.None);
                }
                catch (System.OperationCanceledException)
                {
                    LogUtility.Warning("GetUIAsync timed out waiting for window load: {0}", typeof(T).FullName);
                }
                finally
                {
                    waitLease.Source.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);
                    MemoryPool.Release(waitLease);
                }

                if (ret.IsLoadFailed || (ret.IsDestroyed && !ret.IsLoadDone))
                {
                    return;
                }

                ctx?.Invoke(ret);
            }
        }

        /// <summary>
        /// 结果腿的取窗等待：栈上没有这一名或那一只是别的类型时交 <see cref="EUIOpenStatus.Missing"/>，否则等面板到终态。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>取窗结果。</returns>
        internal async UniTask<UIOpenResult> GetUIAwaitResultImp<T>() where T : UIWindow
        {
            var ret = GetWindow(typeof(T).FullName) as T;
            if (ret == null)
            {
                return new UIOpenResult(EUIOpenStatus.Missing, null);
            }

            return await WaitWindowResultAsync(ret, typeof(T).FullName, LOAD_WAIT_TIMEOUT_SECONDS, CancellationToken.None);
        }

        /// <summary>
        /// 把窗口的装载终态等成 <see cref="UIOpenResult"/>：就绪/失败按实际终态落档，超时交回仍在装载的那一只。
        /// </summary>
        /// <remarks>
        /// 失败与超时的区分读终态位：失败位或「销毁而未就绪」即 Failed，其余未就绪档为 Timeout。
        /// </remarks>
        /// <param name="window">等终态的那一只。</param>
        /// <param name="windowName">窗口名称，只进超时文案。</param>
        /// <param name="timeoutSeconds">等待上限（秒）。</param>
        /// <param name="callerCt">调用方取消令牌；被它撤销即落 <see cref="EUIOpenStatus.Cancelled"/> 档。</param>
        /// <returns>开窗结果。</returns>
        internal static async UniTask<UIOpenResult> WaitWindowResultAsync(UIWindow window, string windowName, float timeoutSeconds,
            CancellationToken callerCt = default)
        {
            try
            {
                if (!await WaitForPanelReady(window, timeoutSeconds, callerCt) && !window.IsLoadDone)
                {
                    if (window.IsLoadFailed || window.IsDestroyed)
                    {
                        return new UIOpenResult(EUIOpenStatus.Failed, window);
                    }

                    LogUtility.Warning("UI 窗口 '{0}' 等待面板就绪超时（{1} 秒）", windowName, timeoutSeconds);
                    return new UIOpenResult(EUIOpenStatus.Timeout, window);
                }

                return new UIOpenResult(EUIOpenStatus.Opened, window);
            }
            catch (System.OperationCanceledException)
            {
                // 调用方取消：等待腿登记过等待者，由离场掐断装载；取窗腿未登记等待者，Leave 的下溢地板守卫令离场空转（不误掐装载）。
                window.Internal_LeaveOpenWaiter();
                return new UIOpenResult(EUIOpenStatus.Cancelled, window);
            }
        }

        /// <summary>
        /// 等窗口装载终态：就绪/失败/销毁按实际终态回，超时（<paramref name="timeoutSeconds"/> 秒）回假。
        /// </summary>
        /// <remarks>
        /// 取消源经 <see cref="UICtsLease"/> 租约从 <see cref="MemoryPool"/> 取还：超时计时还池前先解除，取消过的源由租约废弃。 <br />
        /// 超时与就绪竞速时以就绪为准：取消异常落定后回读一次就绪位；调用方取消原样上抛，交上层落 Cancelled 档。
        /// </remarks>
        /// <param name="window">等终态的那一只。</param>
        /// <param name="timeoutSeconds">等待上限（秒）。</param>
        /// <param name="callerCt">调用方取消令牌；被它撤销时原样上抛 <see cref="System.OperationCanceledException"/>。</param>
        /// <returns>面板就绪时为真。</returns>
        internal static async UniTask<bool> WaitForPanelReady(UIWindow window, float timeoutSeconds, CancellationToken callerCt = default)
        {
            var lease = MemoryPool.Acquire<UICtsLease>();
            lease.Source.CancelAfter(System.TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                return await window.WaitPanelReadyAsync(lease.Source.Token, callerCt);
            }
            catch (System.OperationCanceledException)
            {
                if (callerCt.IsCancellationRequested)
                {
                    throw; // 调用方取消：上抛交 WaitWindowResultAsync 落 Cancelled 档，与超时分档
                }

                return window.IsLoadDone;
            }
            finally
            {
                // 解除超时计时后再还池：活着的计时器会把池里别的租户打取消
                lease.Source.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);
                MemoryPool.Release(lease);
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

            if (window == null)
            {
                LogUtility.Debug("要关闭的窗口 '{0}' 不在栈上：本次关闭是空操作", windowName);
                return;
            }

            if (window.ParksOnClose)
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
                LogUtility.Debug("要隐藏的窗口 '{0}' 不在栈上：本次隐藏是空操作", windowName);
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
            window.HideTimerId = TimerService.Delay(window.HideTimeToClose, window.CloseDelegate);

            if (window.FullScreen)
            {
                OnSetWindowVisible();
            }
        }

        /// <summary>停放 TTL 到期：仍停放着才移出并终态销毁；已被取用/顶替的迟到到期静默落空。</summary>
        internal void ExpireParkedWindow(UIWindow window)
        {
            if (window == null)
            {
                return;
            }

            if (!_cache.TryGetValue(window.WindowName, out var parked) || !ReferenceEquals(parked, window))
            {
                return;
            }

            _cache.Remove(window.WindowName);
            window.InternalDestroy(isShutDown: true); // 终态：不再返停放表
        }

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        internal void CloseAll(bool isShutDown = false)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (!isShutDown && window.ParksOnClose)
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
            _history.Clear();
        }

        /// <summary>
        /// 关闭栈上被这一轨认得的那些窗口，其余连位置都不动：一支 handler 关停时只交自己那一轨的窗进来。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="CloseAll"/> 同一次序（自下而上）、同一分档（缓存窗交进停放表、非缓存窗直接销毁）。<br />
        /// 判据没认得的窗口留在原来的栈位上，由它自己那一轨去收。
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

                RemoveFromStack(window);
                i--;

                if (!isShutDown && window.ParksOnClose)
                {
                    _cache[window.WindowName] = window;
                    window.InternalClose();
                }
                else
                {
                    window.InternalDestroy(isShutDown);
                }
            }

            if (isShutDown && _cache.Count > 0)
            {
                _sweepScratch ??= new List<string>(8);
                try
                {
                    foreach (var pair in _cache)
                    {
                        if (onTrack(pair.Value))
                        {
                            _sweepScratch.Add(pair.Key);
                        }
                    }

                    for (int i = 0; i < _sweepScratch.Count; i++)
                    {
                        var parked = _cache[_sweepScratch[i]];
                        _cache.Remove(_sweepScratch[i]);
                        parked.InternalDestroy(isShutDown: true);
                    }
                }
                // 抛出型 DestroyPanel 不得把陈旧键留在 scratch 上：否则下一轨扫尾回读旧键即 KeyNotFoundException
                finally
                {
                    _sweepScratch.Clear();
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
        internal void CloseAllWithOut(EUILayer withOut)
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

                if (window.ParksOnClose)
                {
                    _cache[window.WindowName] = window;
                    window.InternalClose();
                }
                else
                {
                    window.InternalDestroy();
                }
                RemoveFromStack(window);
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
        /// <exception cref="GameException">同名窗口已在栈上。</exception>
        internal void Push(UIWindow window)
        {
            if (IsContains(window.WindowName))
            {
                throw new GameException($"Window {window.WindowName} is exist.");
            }

            // 插入点取所属层级末位之后；该层无窗时退到较低层级末位之后，仍无则落栈底
            int insertIndex = -1;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (window.WindowLayer == _uiStack[i].WindowLayer)
                {
                    insertIndex = i + 1;
                }
            }

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

            if (insertIndex == -1)
            {
                insertIndex = 0;
            }

            if (insertIndex > 0 && IsModal(window)) _uiStack[insertIndex - 1].Interactable = false;

            _uiStack.Insert(insertIndex, window);
            _history.Add(window); // 开启序追加：复用/停放重取经 Pop→Push 也会挪到最新；排在回执之前，令回执里重开的窗接在本窗之后
            UIService.Internal_RaiseWindowShown(window);
        }

        /// <summary>
        /// 把窗口移出堆栈并发一次关闭回执。
        /// </summary>
        internal void Pop(UIWindow window)
        {
            RemoveFromStack(window);
            UIService.Internal_RaiseWindowClosed(window);
        }

        /// <summary>
        /// 把窗口摘出堆栈：摘掉的是模态窗时重算紧邻下层的可交互位，它的新上方邻居仍是模态窗就继续压着。
        /// </summary>
        /// <remarks>摘掉非模态窗时一格都不写：它没压过下层，写回去会抢掉下层自己那轮过渡持有的锁。</remarks>
        /// <param name="window">要摘出的窗口；不在栈上时不动栈。</param>
        private void RemoveFromStack(UIWindow window)
        {
            int index = _uiStack.IndexOf(window);
            if (index < 0)
            {
                return;
            }

            UIWindow below = index > 0 ? _uiStack[index - 1] : null;
            UIWindow above = index + 1 < _uiStack.Count ? _uiStack[index + 1] : null;

            _uiStack.RemoveAt(index);

            if (below != null && IsModal(window))
            {
                below.Interactable = above == null || !IsModal(above);
            }

            _history.Remove(window);
        }

        #endregion

        #region 导航 [NAVIGATION]

        /// <summary>导航深度：开启序历史的长度（栈按层级排序答不出「最近开的是谁」，历史按开启序答）。</summary>
        internal int NavigationDepth => _history.Count;

        /// <summary>取最近开的那只走既有关闭政策：无历史/过渡中/政策拒关回假，历史不出栈。</summary>
        internal bool TryCloseTopWindow()
        {
            if (_history.Count == 0)
            {
                return false;
            }

            var top = _history[_history.Count - 1];
            if (!top.Internal_EvaluateCanClose())
            {
                return false;
            }

            top.TryClose().Forget();
            return true;
        }

        #endregion

        #region 测试接缝 [TEST SEAMS]

        /// <summary>
        /// 栈上窗口的只读视图：栈本体仍为 private，这一道门只给读、不给写。
        /// </summary>
        internal IReadOnlyList<UIWindow> PeekStack() => _uiStack;

        /// <summary>
        /// 停放表里是否有这个名字的窗：缓存实例关闭后落在这里，栈上已无。
        /// </summary>
        internal bool IsParked(string windowName) => _cache.ContainsKey(windowName);

        #endregion
    }
}
