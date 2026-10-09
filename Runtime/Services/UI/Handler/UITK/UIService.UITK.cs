using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 服务外观的 UI Toolkit 腿：开窗的同步、异步、等待三支都收在 <see cref="UITKWindow"/> 上。
    /// </summary>
    /// <remarks>
    /// 中性外壳（生命周期、层级常量、属性、安全区、<see cref="System.Type"/> 形入口与共享栈上的关·隐·取）住在 <c>UIService.cs</c>。<br />
    /// 本文件管着 UI Toolkit 轨的槽位、认领门、取用属性、<see cref="IsUITKValid"/> 与轨道自述，自述登记进门面目录；uGUI 腿住在 <c>UIService.UGUI.cs</c>。
    /// 三支各自直调 <see cref="SharedLedger"/> 把窗口落进共享栈，下面那一枚 <see cref="UITKHandler"/> 只作认领门探针——与 uGUI 轨共用那一份栈，但不共用那条腿。
    /// 形参表与 uGUI 腿同形，末尾多收一枚 <c>panelSettings</c>：同一形状的实参落进哪一支，依据是各自的泛型约束而不是形参个数。
    /// <c>panelSettings</c> 不进中性编排：由本文件包成一枚只认 <see cref="UIWindow"/> 的交接钩子，在装载之前落到 <see cref="UITKWindow"/> 的实例属性上。
    /// </remarks>
    partial class UIService
    {
        /// <summary>UI Toolkit 轨那一枚驱动者未就位时为假；只看本轨这一支。</summary>
        // ReSharper disable once InconsistentNaming
        public static bool IsUITKValid => s_UITKHandler != null;

        // ReSharper disable once InconsistentNaming
        private static volatile UITKHandler s_UITKHandler;

        /// <summary>UI Toolkit 轨在门面目录里的自述：认窗判据、有效性探针、Type 形入口的开窗实现与默认档关停位。</summary>
        /// <remarks>
        /// 关停取默认档（<see cref="UITrack.SHUTDOWN_ORDER_DEFAULT"/>）：这一轨的文档壳挂在 uGUI 那一轨那枚根下，门面按目录升序收口时排在宿主轨之前。
        /// </remarks>
        // ReSharper disable once InconsistentNaming
        private static readonly UITrack s_UITKTrack = Internal_RegisterTrack(new UITrack(
            "UITK", typeof(UITKWindow), UITrack.SHUTDOWN_ORDER_DEFAULT,
            IsOnUITKTrack, IsUITKValidProbe, OpenUITKWindowForTypeEntry));

        /// <summary>UI Toolkit 轨的有效性探针：只读本轨这一枚槽位，目录不另记一份在位状态。</summary>
        // ReSharper disable once InconsistentNaming
        private static bool IsUITKValidProbe() => s_UITKHandler != null;

        /// <summary>UI Toolkit 轨的认窗判据：<see cref="UITKWindow"/> 及其派生类落在 UI Toolkit 轨上。</summary>
        /// <param name="windowType">窗口类型。</param>
        /// <returns>落在 UI Toolkit 轨上时为真。</returns>
        // ReSharper disable once InconsistentNaming
        private static bool IsOnUITKTrack(Type windowType) => typeof(UITKWindow).IsAssignableFrom(windowType);

        /// <summary>
        /// UI Toolkit 轨那枚<b>已启用</b>的驱动者：这一轨没启用时当场抬错，不静默落空、也不替本轨造一枚。
        /// </summary>
        /// <remarks>
        /// 槽位只有 <see cref="OnInit"/> 按 <see cref="UIServiceSettings"/> 启用清单认领那一条来路：没有换入接缝，也不替配置造驱动者。 <br />
        /// UI Toolkit 腿的三支与 <see cref="System.Type"/> 入口的 UI Toolkit 档都只取用这一枚作认领门探针。
        /// </remarks>
        /// <exception cref="GameException">UI Toolkit 轨没有被启用（槽位空着）。</exception>
        // ReSharper disable once InconsistentNaming
        internal static UITKHandler UITKHandler
        {
            get
            {
                var handler = s_UITKHandler;
                if (handler == null)
                {
                    throw new GameException(StringUtility.Format(
                        "UI backend track '{0}' has no driver in place: list it in {1} and let OnInit register it.",
                        "UITK", nameof(UIServiceSettings)));
                }

                return handler;
            }
        }

        /// <summary>读 UI Toolkit 轨那枚处理器槽：不叫认领门，也不产生任何副作用。</summary>
        internal static UITKHandler Internal_PeekUITKHandler() => s_UITKHandler;

        /// <summary>
        /// 认领 UI Toolkit 这一轨：把实现类自述交来的那一枚填进本轨的槽、挂上本轨那几条广播，再初始化它。
        /// </summary>
        /// <remarks>
        /// 形参就是 <see cref="UI.UITKHandler"/>：归属哪一轨由实现类的 <see cref="UIServiceHandler.Internal_Register"/> 自述， <br />
        /// 这一道不按类型判轨，也不替配置另造一枚驱动者。 <br />
        /// 次序是填槽 → 挂关停与广播 → <c>Internal_Init</c>：同一枚实例重入是空操作，换第二枚进槽当场抬错。
        /// </remarks>
        /// <param name="handler">自述归属到本轨的那一枚驱动者。</param>
        /// <exception cref="GameException">本轨已经有驱动者在位（同一轨的第二个注入项）。</exception>
        internal static void Internal_ClaimUITKTrack(UITKHandler handler)
        {
            // 同一枚实例再注册一次是空操作：OnInit 可重入（那一档有格钉着），要否掉的是「换第二枚进来」；
            // 重入不重跑初始化还另有基类那一道幂等门
            if (ReferenceEquals(s_UITKHandler, handler))
            {
                return;
            }

            if (Interlocked.CompareExchange(ref s_UITKHandler, handler, null) != null)
            {
                throw new GameException(
                    "UI backend track 'UITK' already has a driver in place: one track takes exactly one driver.");
            }

            s_UITKTrack.AttachDriver(handler.Internal_Shutdown);
            Internal_SubscribeUITKTrack(handler);
            handler.Internal_Init();
        }

        /// <summary>摘掉本轨那枚处理器槽：只清位，不跑关停，也不碰广播——广播由门面整批摘。</summary>
        private static void Internal_DetachUITKTrack()
        {
            Interlocked.Exchange(ref s_UITKHandler, null);
        }

        /// <summary>
        /// 这一轨在门面上的全部挂钩：三条按支广播与一段摘槽。
        /// </summary>
        /// <remarks>
        /// 关停不挂广播：本轨在 <see cref="s_UITKTrack"/> 里自报默认档，收口次序由档位保证，不由各轨按配置就位的先后保证。
        /// </remarks>
        /// <param name="handler">按启用清单刚造出来的那一枚驱动者。</param>
        private static void Internal_SubscribeUITKTrack(UITKHandler handler)
        {
            onTrackTick += handler.Tick;
            onScreenSafeArea += handler.ApplyScreenSafeRect;
            onNotchSimulate += handler.SimulateIPhoneXNotchScreen;
            onDetachTrackSlots += Internal_DetachUITKTrack;
        }

        #region 显示窗口（UI Toolkit 腿） [SHOW WINDOW UITK]

        /// <summary>
        /// 异步打开窗口（UI Toolkit 腿）。
        /// </summary>
        /// <remarks>
        /// <paramref name="windowId"/> 为空时地址仍按窗口类走那条既有链路（<c>[Window(location)]</c> 优先、缺省回类型名）。 <br />
        /// <paramref name="panelSettings"/> 是本腿比 uGUI 腿多出的那一枚，也是窗口级主题与缩放的落点。 <br />
        /// 为 <c>null</c> 时该窗回共享那一份 <see cref="UITKWindow.SharedPanelSettings"/>。
        /// </remarks>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUIAsync<T>(string windowName = null, string windowId = null, bool fromResources = false,
            PanelSettings panelSettings = null, CancellationToken ct = default)
            where T : UITKWindow, new()
        {
            _ = UITKHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            SharedLedger.ShowUIImp(typeof(T), true, windowName, windowId, fromResources, HandoffPanelSettings(panelSettings), UIPayload.Empty, ct);
        }

        /// <summary>
        /// 同步打开窗口（UI Toolkit 腿）。
        /// </summary>
        /// <remarks>
        /// 与异步那一支同形，只是面板按同步档装载；<see cref="SYNC_LOAD_USES_ASYNC"/> 的平台分档与 uGUI 腿共用。
        /// </remarks>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUI<T>(string windowName = null, string windowId = null, bool fromResources = false,
            PanelSettings panelSettings = null, CancellationToken ct = default)
            where T : UITKWindow, new()
        {
            _ = UITKHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            SharedLedger.ShowUIImp(typeof(T), SYNC_LOAD_USES_ASYNC, windowName, windowId, fromResources, HandoffPanelSettings(panelSettings), UIPayload.Empty, ct);
        }

        /// <summary>
        /// 异步打开窗口并等待加载完成（UI Toolkit 腿）。
        /// </summary>
        /// <remarks>
        /// 与另外两支同形：<c>await</c> 只等「面板就绪」那一段，寻址与窗口级 <c>PanelSettings</c> 的交接都排在装载之前。 <br />
        /// <paramref name="panelSettings"/> 与共享那一份都缺位时，装载当场报一条 Error 并拒开（判据在 <see cref="UITKWindow"/>）。
        /// </remarks>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="ct">调用方取消令牌；被它撤销时等待原样上抛 <see cref="System.OperationCanceledException"/>。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public static async UniTask<UIWindow> ShowUIAsyncAwait<T>(string windowName = null, string windowId = null, bool fromResources = false,
            PanelSettings panelSettings = null, CancellationToken ct = default)
            where T : UITKWindow, new()
        {
            _ = UITKHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            return await SharedLedger.ShowUIAwaitImp(typeof(T), true, windowName, windowId, fromResources, HandoffPanelSettings(panelSettings), UIPayload.Empty, ct);
        }

        /// <summary>
        /// 异步打开窗口并等待装载终态（UI Toolkit 腿）：就绪/失败/超时按 <see cref="UIOpenResult"/> 交回。
        /// </summary>
        /// <remarks>
        /// 与等待腿同形：<paramref name="panelSettings"/> 经交接钩子排在装载之前；装载当场失败的窗口同帧落定为
        /// <see cref="EUIOpenStatus.Failed"/>。
        /// </remarks>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="ct">调用方取消令牌；被它撤销即落 <see cref="EUIOpenStatus.Cancelled"/> 档。</param>
        /// <returns>开窗结果。</returns>
        public static UniTask<UIOpenResult> ShowUIAwaitResult<T>(string windowName = null, string windowId = null, bool fromResources = false,
            PanelSettings panelSettings = null, CancellationToken ct = default)
            where T : UITKWindow, new()
        {
            _ = UITKHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            return SharedLedger.ShowUIAwaitResultImp(typeof(T), true, windowName, windowId, fromResources, HandoffPanelSettings(panelSettings), UIPayload.Empty, ct);
        }

        /// <summary>
        /// 异步打开带载荷窗口（UI Toolkit 腿）：泛型直塞，struct 不装箱。
        /// </summary>
        /// <typeparam name="TWindow">带载荷窗口类。</typeparam>
        /// <typeparam name="TArg">载荷类型。</typeparam>
        /// <param name="payload">本次开窗的载荷。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUIAsync<TWindow, TArg>(in TArg payload, string windowName = null, string windowId = null,
            bool fromResources = false, PanelSettings panelSettings = null, CancellationToken ct = default) where TWindow : UITKWindow<TArg>, new()
        {
            _ = UITKHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            SharedLedger.ShowUIImp<TArg>(typeof(TWindow), true, windowName, windowId, fromResources, HandoffPanelSettings(panelSettings), in payload, ct);
        }

        /// <summary>
        /// 同步打开带载荷窗口（UI Toolkit 腿）：泛型直塞，struct 不装箱。
        /// </summary>
        /// <typeparam name="TWindow">带载荷窗口类。</typeparam>
        /// <typeparam name="TArg">载荷类型。</typeparam>
        /// <param name="payload">本次开窗的载荷。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUI<TWindow, TArg>(in TArg payload, string windowName = null, string windowId = null,
            bool fromResources = false, PanelSettings panelSettings = null, CancellationToken ct = default) where TWindow : UITKWindow<TArg>, new()
        {
            _ = UITKHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            SharedLedger.ShowUIImp<TArg>(typeof(TWindow), SYNC_LOAD_USES_ASYNC, windowName, windowId, fromResources, HandoffPanelSettings(panelSettings), in payload, ct);
        }

        /// <summary>
        /// 异步打开带载荷窗口并等待加载完成（UI Toolkit 腿）。
        /// </summary>
        /// <remarks><paramref name="payload"/> 用普通形参而非 <c>in</c>：<c>async</c> 方法禁 <c>in</c> 形参（CS1988）。</remarks>
        /// <typeparam name="TWindow">带载荷窗口类。</typeparam>
        /// <typeparam name="TArg">载荷类型。</typeparam>
        /// <param name="payload">本次开窗的载荷。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="ct">调用方取消令牌；被它撤销时等待原样上抛 <see cref="System.OperationCanceledException"/>。</param>
        /// <returns>打开窗口操作句柄。</returns>
        /// <exception cref="GameException">按名取回的实例不是 <typeparamref name="TWindow"/>（缓存命中类型不符，消息带期望/实际双类型名）。</exception>
        public static async UniTask<TWindow> ShowUIAsyncAwait<TWindow, TArg>(TArg payload, string windowName = null, string windowId = null,
            bool fromResources = false, PanelSettings panelSettings = null, CancellationToken ct = default) where TWindow : UITKWindow<TArg>, new()
        {
            _ = UITKHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            var window = await SharedLedger.ShowUIAwaitImp<TArg>(typeof(TWindow), true, windowName, windowId, fromResources, HandoffPanelSettings(panelSettings), payload, ct);
            return window switch
            {
                null => null,
                TWindow typed => typed,
                _ => throw new GameException(StringUtility.Format(
                    "UI 窗口 '{0}' 按名命中的实例不是 {1}：实际是 {2}。",
                    window.WindowName, typeof(TWindow).Name, window.GetType().Name))
            };
        }

        /// <summary>
        /// 异步打开带载荷窗口并等待装载终态（UI Toolkit 腿）：就绪/失败/超时按 <see cref="UIOpenResult"/> 交回。
        /// </summary>
        /// <typeparam name="TWindow">带载荷窗口类。</typeparam>
        /// <typeparam name="TArg">载荷类型。</typeparam>
        /// <param name="payload">本次开窗的载荷。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="ct">调用方取消令牌；被它撤销即落 <see cref="EUIOpenStatus.Cancelled"/> 档。</param>
        /// <returns>开窗结果。</returns>
        public static UniTask<UIOpenResult> ShowUIAwaitResult<TWindow, TArg>(in TArg payload, string windowName = null, string windowId = null,
            bool fromResources = false, PanelSettings panelSettings = null, CancellationToken ct = default) where TWindow : UITKWindow<TArg>, new()
        {
            _ = UITKHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            return SharedLedger.ShowUIAwaitResultImp<TArg>(typeof(TWindow), true, windowName, windowId, fromResources, HandoffPanelSettings(panelSettings), payload, ct);
        }

        /// <summary>
        /// Type 形入口落到本轨的开窗实现：走各轨共用的那张中性形参表，因此不带窗口级 <c>PanelSettings</c>。
        /// </summary>
        /// <remarks>
        /// 这一条路不给窗口级配置（<c>panelSettings</c> 恒为 <c>null</c>），装载回共享那一份 <see cref="UITKWindow.SharedPanelSettings"/>。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="ct">调用方取消令牌。</param>
        // ReSharper disable once InconsistentNaming
        private static void OpenUITKWindowForTypeEntry(Type type, bool isAsync, string windowName, string windowId, bool fromResources,
            UIPayload payload, CancellationToken ct)
        {
            _ = UITKHandler; // 认领门：本轨未启用当场抬错（槽位取用即判据）
            SharedLedger.ShowUIImp(type, isAsync, windowName, windowId, fromResources, null, payload, ct);
        }

        /// <summary>
        /// 把窗口级 <see cref="PanelSettings"/> 包成一枚只认 <see cref="UIWindow"/> 的交接钩子：为空的场合一枚委托都不建。
        /// </summary>
        /// <remarks>
        /// 钩子里才认 <see cref="UITKWindow"/>：后端类型因此只住在这一轨的文件里。 <br />
        /// 中性的 <see cref="UIWindowLedger"/> 一枚收下这个形参：门面腿直调账本，驱动者不再过手。 <br />
        /// 落进钩子的窗由认轨守卫与泛型约束保证在本轨之上，不在时那一次赋值当场抛而不静默落空。
        /// </remarks>
        /// <param name="panelSettings">这一次要交给新窗的那一枚配置。</param>
        /// <returns>共享编排在新实例装载之前叫的那一枚钩子；无需交接时为 <c>null</c>。</returns>
        private static Action<UIWindow> HandoffPanelSettings(PanelSettings panelSettings)
        {
            if (panelSettings == null)
            {
                return null;
            }

            return window =>
            {
                ((UITKWindow) window).PanelSettingsOverride = panelSettings;
            };
        }

        #endregion
    }
}
