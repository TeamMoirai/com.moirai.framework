using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 服务外观的 uGUI 腿：开窗的同步、异步、等待三支都收在 <see cref="UGUIWindow"/> 上。
    /// </summary>
    /// <remarks>
    /// 中性外壳（生命周期、层级常量、属性、安全区、<see cref="System.Type"/> 形入口与共享栈上的关·隐·取）住在 <c>UIService.cs</c>。<br />
    /// 本文件管着 uGUI 轨的槽位、认领门、取用属性、<see cref="IsUGUIValid"/> 与轨道自述，自述登记进门面目录；UI Toolkit 腿住在 <c>UIService.UITK.cs</c>。
    /// 三支各自直调 <see cref="SharedLedger"/> 把窗口落进共享栈，下面的 <see cref="UIService.UGUIHandler"/> 只作认领门探针——没有「交给默认实现」那一条路。
    /// 形参表与 UI Toolkit 腿只差本轨不收的 <c>panelSettings</c>：同一形状的实参落进哪一支，依据是各自的泛型约束而不是形参个数。
    /// 类级属性与基类清单只在主声明上，本文件是同一个类的另一份声明；这里的形参表与约束就是这一腿的契约。
    /// </remarks>
    partial class UIService
    {
        /// <summary>uGUI 轨的驱动者未就位时为假；只看本轨这一支。</summary>
        public static bool IsUGUIValid => s_UGUIHandler != null;

        private static volatile UGUIHandler s_UGUIHandler;

        /// <summary>uGUI 轨在门面目录里的自述：认窗判据、有效性探针、Type 形入口的开窗实现与宿主档关停位。</summary>
        /// <remarks>
        /// 关停取宿主档（<see cref="UITrack.SHUTDOWN_ORDER_HOST"/>）：这一轨的根是别轨那些文档壳的父级，门面按目录升序收口时它最后收。
        /// </remarks>
        private static readonly UITrack s_UGUITrack = Internal_RegisterTrack(new UITrack(
            "UGUI", typeof(UGUIWindow), UITrack.SHUTDOWN_ORDER_HOST,
            IsOnUGUITrack, IsUGUIValidProbe, OpenUGUIWindowForTypeEntry));

        /// <summary>uGUI 轨的有效性探针：只读本轨槽位，目录不另记一份在位状态。</summary>
        private static bool IsUGUIValidProbe() => s_UGUIHandler != null;

        /// <summary>uGUI 轨的认窗判据：<see cref="UGUIWindow"/> 及其派生类落在 uGUI 轨上。</summary>
        /// <param name="windowType">窗口类型。</param>
        /// <returns>落在 uGUI 轨上时为真。</returns>
        private static bool IsOnUGUITrack(Type windowType) => typeof(UGUIWindow).IsAssignableFrom(windowType);

        /// <summary>
        /// uGUI 轨<b>已启用</b>的驱动者：这一轨没启用时当场抬错，不静默落空、也不替本轨造一个。
        /// </summary>
        /// <remarks>
        /// 槽位只有 <see cref="OnInit"/> 按 <see cref="UIServiceSettings"/> 启用清单认领那一条来路：没有换入接缝，也没有用到才 new 的隐式启用。 <br />
        /// uGUI 腿的三支与 <see cref="System.Type"/> 入口的 uGUI 档都只取用它作认领门探针。 <br />
        /// 轨专有查询走的是另一条分支：<see cref="UIRoot"/> 与 <see cref="UICamera"/> 直读槽位，本轨未启用时答 <c>null</c> 而不抬错。
        /// </remarks>
        /// <exception cref="GameException">uGUI 轨没有被启用（槽位空着）。</exception>
        internal static UGUIHandler UGUIHandler
        {
            get
            {
                var handler = s_UGUIHandler;
                if (handler == null)
                {
                    throw new GameException(StringUtility.Format(
                        "UI backend track '{0}' has no driver in place: list it in {1} and let OnInit register it.",
                        "UGUI", nameof(UIServiceSettings)));
                }

                return handler;
            }
        }

        /// <summary>读 uGUI 轨的处理器槽：不叫认领门，也不产生任何副作用。</summary>
        internal static UGUIHandler Internal_PeekUGUIHandler() => s_UGUIHandler;

        /// <summary>
        /// 认领 uGUI 轨：把实现类自述交来的驱动者填进本轨槽位、挂上本轨那几条广播，再初始化它。
        /// </summary>
        /// <remarks>
        /// 形参就是 <see cref="UI.UGUIHandler"/>：归属哪一轨由实现类的 <see cref="UIServiceHandler.Internal_Register"/> 自述， <br />
        /// 这一道不按类型判轨，也不替配置另造驱动者。 <br />
        /// 次序是填槽 → 挂关停与广播 → <c>Internal_Init</c>：同一实例重入是空操作，换第二个进槽当场抬错。
        /// </remarks>
        /// <param name="handler">自述归属到本轨的驱动者。</param>
        /// <exception cref="GameException">本轨已经有驱动者在位（同一轨的第二个注入项）。</exception>
        internal static void Internal_ClaimUGUITrack(UGUIHandler handler)
        {
            // 同一实例再注册一次是空操作：OnInit 可重入（那一档有格钉着），要否掉的是「换第二个进来」；
            // 重入不重跑初始化还另有基类那一道幂等门
            if (ReferenceEquals(s_UGUIHandler, handler))
            {
                return;
            }

            if (Interlocked.CompareExchange(ref s_UGUIHandler, handler, null) != null)
            {
                throw new GameException(
                    "UI backend track 'UGUI' already has a driver in place: one track takes exactly one driver.");
            }

            s_UGUITrack.AttachDriver(handler.Internal_Shutdown);
            Internal_SubscribeUGUITrack(handler);
            handler.Internal_Init();
        }

        /// <summary>摘掉本轨的处理器槽：只清位，不跑关停，也不碰广播——广播由门面整批摘。</summary>
        private static void Internal_DetachUGUITrack()
        {
            Interlocked.Exchange(ref s_UGUIHandler, null);
        }

        /// <summary>
        /// 这一轨在门面上的全部挂钩：三条按支广播与一段摘槽。
        /// </summary>
        /// <remarks>
        /// 关停不挂广播：本轨在 <see cref="s_UGUITrack"/> 里自报宿主档，收口次序由档位保证，不由各轨按配置就位的先后保证。
        /// </remarks>
        /// <param name="handler">按启用清单刚造出来的驱动者。</param>
        private static void Internal_SubscribeUGUITrack(UGUIHandler handler)
        {
            onTrackTick += handler.Tick;
            onScreenSafeArea += handler.ApplyScreenSafeRect;
            onNotchSimulate += handler.SimulateIPhoneXNotchScreen;
            onDetachTrackSlots += Internal_DetachUGUITrack;
        }

        /// <summary>UI根节点：uGUI 轨的资源只由这一轨答，UI Toolkit 轨答不出这两个。</summary>
        public static Transform UIRoot => s_UGUIHandler?.UIRoot;

        /// <summary>UI专用摄像机——同 <see cref="UIRoot"/>：UI Toolkit 轨没有本轨专用的摄像机。</summary>
        public static Camera UICamera => s_UGUIHandler?.UICamera;
        
        #region 显示窗口（uGUI 腿） [SHOW WINDOW UGUI]

        /// <summary>
        /// 异步打开窗口（uGUI 腿，无载荷）。
        /// </summary>
        /// <remarks>取法（AB / 内置资源）不在这一张形参表上：它由窗口类的 <c>[Window(fromResources:)]</c> 独任。</remarks>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUIAsync<T>(string windowId, CancellationToken ct = default) where T : UGUIWindow, new()
        {
            _ = UGUIHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            SharedLedger.ShowUIImp(typeof(T), true, windowId, null, UIPayload.Empty, ct);
        }

        /// <summary>
        /// 同步打开窗口（uGUI 腿，无载荷）。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUI<T>(string windowId, CancellationToken ct = default) where T : UGUIWindow, new()
        {
            _ = UGUIHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            SharedLedger.ShowUIImp(typeof(T), SYNC_LOAD_USES_ASYNC, windowId, null, UIPayload.Empty, ct);
        }

        /// <summary>
        /// 异步打开窗口并等待加载完成（uGUI 腿，无载荷）。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="ct">调用方取消令牌；被它撤销时等待原样上抛 <see cref="System.OperationCanceledException"/>。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public static async UniTask<UIWindow> ShowUIAsyncAwait<T>(string windowId, CancellationToken ct = default)
            where T : UGUIWindow, new()
        {
            _ = UGUIHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            return await SharedLedger.ShowUIAwaitImp(typeof(T), true, windowId, null, UIPayload.Empty, ct);
        }

        /// <summary>
        /// 异步打开窗口并等待装载终态（uGUI 腿，无载荷）：就绪/失败/超时按 <see cref="UIOpenResult"/> 交回。
        /// </summary>
        /// <remarks>
        /// 装载当场失败或装载中被关闭的窗口同帧落定为 <see cref="EUIOpenStatus.Failed"/>；跨帧装载按实际就绪帧落定。
        /// </remarks>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="ct">调用方取消令牌；被它撤销即落 <see cref="EUIOpenStatus.Cancelled"/> 档。</param>
        /// <returns>开窗结果。</returns>
        public static UniTask<UIOpenResult> ShowUIAwaitResult<T>(string windowId, CancellationToken ct = default)
            where T : UGUIWindow, new()
        {
            _ = UGUIHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            return SharedLedger.ShowUIAwaitResultImp(typeof(T), true, windowId, null, UIPayload.Empty, ct);
        }

        /// <summary>
        /// 异步打开带载荷窗口（uGUI 腿）：泛型直塞，struct 不装箱；标识在前、载荷随后。
        /// </summary>
        /// <typeparam name="TWindow">带载荷窗口类。</typeparam>
        /// <typeparam name="TArg">载荷类型。</typeparam>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="payload">本次开窗的载荷。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUIAsync<TWindow, TArg>(string windowId, in TArg payload, CancellationToken ct = default)
            where TWindow : UGUIWindow<TArg>, new()
        {
            _ = UGUIHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            SharedLedger.ShowUIImp<TArg>(typeof(TWindow), true, windowId, null, in payload, ct);
        }

        /// <summary>
        /// 同步打开带载荷窗口（uGUI 腿）：泛型直塞，struct 不装箱；标识在前、载荷随后。
        /// </summary>
        /// <typeparam name="TWindow">带载荷窗口类。</typeparam>
        /// <typeparam name="TArg">载荷类型。</typeparam>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="payload">本次开窗的载荷。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUI<TWindow, TArg>(string windowId, in TArg payload, CancellationToken ct = default)
            where TWindow : UGUIWindow<TArg>, new()
        {
            _ = UGUIHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            SharedLedger.ShowUIImp<TArg>(typeof(TWindow), SYNC_LOAD_USES_ASYNC, windowId, null, in payload, ct);
        }

        /// <summary>
        /// 异步打开带载荷窗口并等待加载完成（uGUI 腿）：标识在前、载荷随后。
        /// </summary>
        /// <remarks><paramref name="payload"/> 用普通形参而非 <c>in</c>：<c>async</c> 方法禁 <c>in</c> 形参（CS1988）。</remarks>
        /// <typeparam name="TWindow">带载荷窗口类。</typeparam>
        /// <typeparam name="TArg">载荷类型。</typeparam>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="payload">本次开窗的载荷。</param>
        /// <param name="ct">调用方取消令牌；被它撤销时等待原样上抛 <see cref="System.OperationCanceledException"/>。</param>
        /// <returns>打开窗口操作句柄。</returns>
        /// <exception cref="GameException">按标识取回的实例不是 <typeparamref name="TWindow"/>（缓存命中类型不符，消息带期望/实际双类型名）。</exception>
        public static async UniTask<TWindow> ShowUIAsyncAwait<TWindow, TArg>(string windowId, TArg payload,
            CancellationToken ct = default) where TWindow : UGUIWindow<TArg>, new()
        {
            _ = UGUIHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            var window = await SharedLedger.ShowUIAwaitImp<TArg>(typeof(TWindow), true, windowId, null, payload, ct);
            return window switch
            {
                null => null,
                TWindow typed => typed,
                _ => throw new GameException(StringUtility.Format(
                    "UI 窗口 '{0}' 按标识命中的实例不是 {1}：实际是 {2}。",
                    window.WindowId, typeof(TWindow).Name, window.GetType().Name))
            };
        }

        /// <summary>
        /// 异步打开带载荷窗口并等待装载终态（uGUI 腿）：就绪/失败/超时按 <see cref="UIOpenResult"/> 交回；标识在前、载荷随后。
        /// </summary>
        /// <typeparam name="TWindow">带载荷窗口类。</typeparam>
        /// <typeparam name="TArg">载荷类型。</typeparam>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="payload">本次开窗的载荷。</param>
        /// <param name="ct">调用方取消令牌；被它撤销即落 <see cref="EUIOpenStatus.Cancelled"/> 档。</param>
        /// <returns>开窗结果。</returns>
        public static UniTask<UIOpenResult> ShowUIAwaitResult<TWindow, TArg>(string windowId, in TArg payload,
            CancellationToken ct = default) where TWindow : UGUIWindow<TArg>, new()
        {
            _ = UGUIHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            return SharedLedger.ShowUIAwaitResultImp<TArg>(typeof(TWindow), true, windowId, null, payload, ct);
        }

        /// <summary>
        /// uGUI 轨的 Type 形入口开窗实现：认领门后把窗口经协调者那一份共享栈压栈。
        /// </summary>
        /// <remarks>
        /// 交接钩子交 <c>null</c>：uGUI 轨没有「装载之前先交给实例后端配置」这一档，<c>Canvas</c> 与排序画布都随面板本体一起装出来。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="ct">调用方取消令牌。</param>
        private static void OpenUGUIWindowForTypeEntry(Type type, bool isAsync, string windowId,
            UIPayload payload, CancellationToken ct)
        {
            _ = UGUIHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            SharedLedger.ShowUIImp(type, isAsync, windowId, null, payload, ct);
        }

        #endregion
    }
}
