using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.ConfigTable;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Input;
using Moirai.Atropos.Asset;
using Moirai.Atropos.Timer;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 服务外观（Facade）：全框架统一的静态 UI 访问入口。
    /// </summary>
    /// <remarks>
    /// 开窗入口按窗口基类平铺成同名两腿（<c>UGUIWindow</c> / <c>UITKWindow</c>），各腿直呼本轨 partial 的实现。 <br />
    /// 每轨一个驱动者，按 <see cref="UIServiceSettings"/> 的启用清单认领进各自槽位，共用同一份 <see cref="SharedLedger"/>。 <br />
    /// 各轨以一条 <see cref="UITrack"/> 自登记进目录，本文件只枚举目录：加一轨 = 加一个 partial，主文件零改动。 <br />
    /// <see cref="RegisterEnabledHandlers"/> 按目录关停档降序把 handler 注册进容器，容器反序拆除即档位升序收口。 <br />
    /// 线程契约：仅主线程；本类不进容器（多后端并行，无单一注册实体），<see cref="IsValid"/> 与归零门手写在本文件。
    /// </remarks>
    public static partial class UIService
    {
        #region 处理器持有 [HANDLER HOSTING]

        private static volatile UIWindowLedger s_Ledger;

        /// <summary>安全区矩形落到各轨自己面板的那一条广播。</summary>
        private static event Action<Rect> onScreenSafeArea;

        /// <summary>刘海屏模拟的逐轨广播。</summary>
        private static event Action onNotchSimulate;

        /// <summary>窗口入栈后广播，形参是刚入栈的窗口；装载在途也照发（回执说的是栈序，不是面板就绪）。</summary>
        /// <remarks>订阅者自配对退订；归零门会整批摘掉这两条广播，不让上一轮订阅者跨会话残留。</remarks>
        public static event Action<UIWindow> onWindowShown;

        /// <summary>窗口出栈后广播，形参是刚出栈的窗口：停放与销毁都发，一次出栈恰一次。</summary>
        public static event Action<UIWindow> onWindowClosed;

        /// <summary>轨道目录：各轨 partial 的静态初始化器把自述登记进这一份。</summary>
        /// <remarks>懒建＋compare-exchange：各 partial 静态字段的初始化次序没有契约保证，目录不能靠本文件的初始化器先就位。</remarks>
        private static volatile List<UITrack> s_Tracks;

        /// <summary>目录里任何一轨的驱动者就位即为真；一轨都没就位为假。</summary>
        /// <remarks>读各轨描述符自述的有效性探针，门面不另记一份在位状态。</remarks>
        public static bool IsValid
        {
            get
            {
                var tracks = s_Tracks;
                if (tracks == null)
                {
                    return false;
                }

                for (var i = 0; i < tracks.Count; i++)
                {
                    if (tracks[i].IsDriverValid)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 把一条轨道登记进门面目录：各轨 partial 的静态初始化器在类型就绪时各叫一次。
        /// </summary>
        /// <remarks>
        /// 按 <see cref="UITrack.ShutdownOrder"/> 升序插入、同档按登记序，关停即按这一序逐轨走一遍。 <br />
        /// 登记只发生在类型初始化里，插入本身不需要并发防护；不查重复登记，合成轨由 <see cref="Internal_UnregisterTrack"/> 摘回。
        /// </remarks>
        /// <param name="track">待登记的轨道自述。</param>
        /// <returns>原样交回登记进去的轨道自述，供 partial 存成自己的字段。</returns>
        internal static UITrack Internal_RegisterTrack(UITrack track)
        {
            var tracks = s_Tracks;
            if (tracks == null)
            {
                Interlocked.CompareExchange(ref s_Tracks, new List<UITrack>(4), null);
                tracks = s_Tracks;
            }

            var index = tracks.Count;
            while (index > 0 && tracks[index - 1].ShutdownOrder > track.ShutdownOrder)
            {
                index--;
            }

            tracks.Insert(index, track);
            return track;
        }

        /// <summary>
        /// 摘掉一条轨道登记：合成轨用它收回干净域，内建轨不摘。
        /// </summary>
        /// <param name="track">要摘除的轨道。</param>
        /// <returns>目录里真有它并摘掉了为真。</returns>
        internal static bool Internal_UnregisterTrack(UITrack track)
        {
            var tracks = s_Tracks;
            return tracks != null && tracks.Remove(track);
        }

        /// <summary>
        /// 目录只读视图：内建轨的登记完整性与合成轨的处置都由用例读它判定。
        /// </summary>
        internal static IReadOnlyList<UITrack> Internal_PeekTracks() => s_Tracks;

        /// <summary>逐轨广播的在位挂钩数：安全区与刘海两条广播的订阅者总和。</summary>
        /// <remarks>认领门每支后端各挂这两条一次，同实例重入按早退不再挂——夹具用它数重复认领有没有漏。</remarks>
        internal static int Internal_PeekTrackSubscriptionCount() =>
            (onScreenSafeArea?.GetInvocationList().Length ?? 0) + (onNotchSimulate?.GetInvocationList().Length ?? 0);

        /// <summary>各轨后端共用的窗口栈与停放表持有者：懒建；<see cref="Internal_ResetHandlerSlots"/> 归位时抹掉，下次读取再建。</summary>
        /// <remarks>
        /// 每读现取、不是构造期快照：共享持有者会被归位门换掉，驱动者则由资产反序列化器在任意时刻造出来。 <br />
        /// 线程契约：仅主线程。
        /// </remarks>
        internal static UIWindowLedger SharedLedger
        {
            get
            {
                var ledger = s_Ledger;
                if (ledger != null)
                {
                    return ledger;
                }

                var created = new UIWindowLedger();
                Interlocked.CompareExchange(ref s_Ledger, created, null);
                return s_Ledger;
            }
        }

        /// <summary>
        /// 共享持有者的归零事务（门面侧唯一的一处归零门）：栈与停放表清空、交互租约交回、未被归还的全局压制位归零。
        /// </summary>
        /// <remarks>各轨 handler 的初始化与关停都只收本轨的窗，这份共用存储由这一道门收。</remarks>
        internal static void Internal_ResetSharedLedger()
        {
            var ledger = SharedLedger;
            ledger.ResetStorage();
            if (ledger.InteractionLease.Reset())
            {
                InputService.PreventInteractionUI = false;
            }
        }

        /// <summary>
        /// 免域重载复位：处理器槽与共享持有者都是静态位，跨 Play 会话残留会把上一轮的栈漏给下一轮。
        /// </summary>
        /// <remarks>
        /// 处理器槽与订阅在 <see cref="Internal_ResetHandlerSlots"/> 内一并摘除，无需单独归位。 <br />
        /// s_Tracks 有意不清：目录只在类型初始化期登记（每域一次），清空会让免域重载的第二会话目录永久为空。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHandlerSlotsOnDomainReload()
        {
            Internal_ResetHandlerSlots();
        }

        /// <summary>
        /// 归位门：各轨摘槽、摘全部订阅与认领、共享持有者清回干净状态，落锤待销毁根。
        /// </summary>
        /// <remarks>
        /// 只清不置、不跑关停：槽里只剩 <c>OnInit</c> 按启用清单认领那一条来路，摘下的实例归 <c>OnShutdown</c> 收口。 <br />
        /// 夹具进门归位也用它。
        /// </remarks>
        internal static void Internal_ResetHandlerSlots()
        {
            Internal_DetachUGUITrack();
            Internal_DetachUITKTrack();
            Internal_ClearTrackSubscriptions();
            Interlocked.Exchange(ref s_Ledger, null);
            DestroyPendingUGUIRoot();
        }

        /// <summary>uGUI 轨移交给归零门的待销毁根：销毁推迟到全轨摘净（他轨面板还挂在其下）。</summary>
        private static GameObject s_PendingUGUIRoot;

        /// <summary>
        /// uGUI 根销毁挂归零门：反序拆除里宿主轨先拆，立即销毁会拆掉还挂着的 UITK 面板。
        /// </summary>
        internal static void HandUGUIRootToDestroyGate(GameObject rootParent) => s_PendingUGUIRoot = rootParent;

        private static void DestroyPendingUGUIRoot()
        {
            var root = s_PendingUGUIRoot;
            s_PendingUGUIRoot = null;
            if (root == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }

        /// <summary>
        /// 摘掉各轨挂在门面上的全部订阅与认领：四道广播置空、目录里每一轨的关停回调摘掉。
        /// </summary>
        /// <remarks>订阅与认领住在静态位上：槽清了而它们没清，下一轮仍会叫到上一轮已退役的实例。</remarks>
        private static void Internal_ClearTrackSubscriptions()
        {
            onScreenSafeArea = null;
            onNotchSimulate = null;
            onWindowShown = null;
            onWindowClosed = null;

            var tracks = s_Tracks;
            if (tracks != null)
            {
                for (var i = 0; i < tracks.Count; i++)
                {
                    tracks[i].ClearClaim();
                }
            }
        }

        #region 窗口事件触发 [WINDOW EVENT RAISERS]

        /// <summary>
        /// 发一次入栈回执：`event` 只能在声明类内触发，共享持有者经这一道口转手。
        /// </summary>
        /// <param name="window">刚入栈的窗口。</param>
        internal static void Internal_RaiseWindowShown(UIWindow window) => onWindowShown?.Invoke(window);

        /// <summary>
        /// 发一次出栈回执：停放与销毁两条路都由 <see cref="UIWindowLedger.Pop"/> 叫到这一道。
        /// </summary>
        /// <param name="window">刚出栈的窗口。</param>
        internal static void Internal_RaiseWindowClosed(UIWindow window) => onWindowClosed?.Invoke(window);

        #endregion

        #endregion

        #region 生命周期钩子 [LIFECYCLE HOOKS]

        /// <summary>调试面板的注册位（第一支认领的驱动者注册，最后一支收口注销）。</summary>
        private static bool s_DebuggerWindowRegistered;
        private static IDebuggerService s_Debugger;

        /// <summary>
        /// 组合根装配入口：把 <see cref="UIServiceSettings.EnabledHandlers"/> 启用清单按目录关停档降序注册进容器。
        /// </summary>
        /// <remarks>
        /// 目录登记时已按关停档升序排好：按目录降序注册 = 容器反序拆除按档位升序收口，宿主轨最后拆根。 <br />
        /// 契约键 = 各自具体类型（多后端并行无单一契约实例）；认领与编排随 handler 的 OnInit 走 → <see cref="OnHandlerInitialized"/>。
        /// </remarks>
        /// <exception cref="GameException">没有任何后端被启用、清单里有 <c>null</c> 项，或条目不落任何已登记轨。</exception>
        internal static void RegisterEnabledHandlers()
        {
            // 入口清账：重装配时上一轮的共享栈归零须赶在认领之前（认领门对同实例早退，钩子里的 anyUp 档看不到）
            Internal_ResetSharedLedger();

            var enabled = UIServiceSettings.EnabledHandlers;
            if (enabled == null || enabled.Length == 0)
            {
                throw new GameException(StringUtility.Format(
                    "No UI backend is enabled: {0} must list at least one handler to register at init.",
                    nameof(UIServiceSettings)));
            }

            for (var i = 0; i < enabled.Length; i++)
            {
                if (enabled[i] == null)
                {
                    throw new GameException(StringUtility.Format(
                        "UI backend entry #{0} is null: {1} must list handlers, not empty slots.",
                        i, nameof(UIServiceSettings)));
                }
            }

            // 注册序 = 关停档降序（宿主轨最先注册）：容器反序拆除 = 档位升序收口，宿主轨最后拆根
            var registered = new List<UIServiceHandler>(enabled.Length);
            var tracks = s_Tracks;
            if (tracks != null)
            {
                for (var i = tracks.Count - 1; i >= 0; i--)
                {
                    for (var j = 0; j < enabled.Length; j++)
                    {
                        var entry = enabled[j];
                        if (!registered.Contains(entry) && tracks[i].OwnsDriver(entry.GetType()))
                        {
                            GameServices.RegisterService(EServiceScopeKind.App, entry.GetType(), entry);
                            registered.Add(entry);
                        }
                    }
                }
            }

            for (var j = 0; j < enabled.Length; j++)
            {
                if (!registered.Contains(enabled[j]))
                {
                    throw new GameException(StringUtility.Format(
                        "UI backend entry #{0} ({1}) pairs with no registered UI track.",
                        j, enabled[j].GetType().FullName));
                }
            }
        }

        /// <summary>
        /// 容器编排（后端 OnInit 经 Manager 钩子调用）：首支认领时清账，注册调试面板。
        /// </summary>
        /// <remarks>重装配的归零归装配入口（本钩子跑到时本支已进槽，anyUp 恒真）。</remarks>
        internal static void OnHandlerInitialized(IDebuggerService debugger)
        {
            var tracks = s_Tracks;
            var anyUp = false;
            if (tracks != null)
            {
                for (var i = 0; i < tracks.Count; i++)
                {
                    anyUp |= tracks[i].IsDriverValid;
                }
            }

            if (!anyUp)
            {
                Internal_ResetSharedLedger();
            }

            if (!s_DebuggerWindowRegistered)
            {
                s_Debugger = debugger;
                debugger?.RegisterDebuggerWindow("Profiler/UI", new UIServiceDebuggerWindow());
                s_DebuggerWindowRegistered = true;
            }
        }

        /// <summary>
        /// 容器收尾（后端 OnShutdown 经 Manager 钩子调用）：全部轨摘净后收调试面板。
        /// </summary>
        /// <remarks>共享栈与订阅不在这里清——归零只剩 <see cref="Internal_ResetHandlerSlots"/> 与免域重载复位门两处。</remarks>
        internal static void Internal_OnTrackDown()
        {
            var tracks = s_Tracks;
            var anyUp = false;
            if (tracks != null)
            {
                for (var i = 0; i < tracks.Count; i++)
                {
                    anyUp |= tracks[i].IsDriverValid;
                }
            }

            if (anyUp) return;

            if (s_DebuggerWindowRegistered)
            {
                s_Debugger?.UnregisterDebuggerWindow("Profiler/UI");
                s_Debugger = null;
                s_DebuggerWindowRegistered = false;
            }
        }

        /// <summary>
        /// 按目录关停档升序逐轨收口：叫认领时绑定的那条关停回调（合成轨/测试语义的生产扫）。
        /// </summary>
        /// <remarks>叫过即清认领（<see cref="UITrack.ClearClaim"/>）：回调实例绑定旧驱动者，残留会在下一轮被误叫。</remarks>
        internal static void Internal_ShutDownTracksInOrder()
        {
            var tracks = s_Tracks;
            if (tracks == null)
            {
                return;
            }

            for (var i = 0; i < tracks.Count; i++)
            {
                tracks[i].ClaimedShutDown?.Invoke();
                tracks[i].ClearClaim();
            }
        }

        /// <summary>
        /// 测试接缝：按启用清单直驱认领（逐支 ServiceBase.Initialize → OnInit → Internal_Register），不走容器。
        /// </summary>
        /// <remarks>容器外直驱的装配路径：编排夹具以认领作入口，Context 缺席时每支的编排钩子自然跳过。</remarks>
        internal static void EnableHandlersForTests()
        {
            // 入口清账：与 RegisterEnabledHandlers 同语义
            Internal_ResetSharedLedger();

            var enabled = UIServiceSettings.EnabledHandlers;
            if (enabled == null || enabled.Length == 0)
            {
                throw new GameException(StringUtility.Format(
                    "No UI backend is enabled: {0} must list at least one handler to register at init.",
                    nameof(UIServiceSettings)));
            }

            for (var i = 0; i < enabled.Length; i++)
            {
                if (enabled[i] == null)
                {
                    throw new GameException(StringUtility.Format(
                        "UI backend entry #{0} is null: {1} must list handlers, not empty slots.",
                        i, nameof(UIServiceSettings)));
                }

                ((Moirai.Atropos.IServiceLifecycle)enabled[i]).Initialize();
            }
        }

        /// <summary>
        /// 共享栈结算（后端 IServiceTickable.Tick 经 Manager 钩子进入；帧守卫幂等，同帧多轨只结算一次）。
        /// </summary>
        internal static void TickShared(float realElapseSeconds)
        {
            SharedLedger.Tick(realElapseSeconds);
        }

        #endregion

        #region 常量 [CONSTANTS]

        // 层级常量
        public const int LAYER_DEEP = 2000;
        public const int WINDOW_DEEP = 100;
        public const int WINDOW_HIDE_LAYER = 2; // Ignore Raycast
        public const int WINDOW_SHOW_LAYER = 5; // UI

        /// <summary>同步开窗档在 WebGL 上交给异步装载（该平台没有同步装载）：两轨的同步腿与 Type 形入口共用这一位。</summary>
        private const bool SYNC_LOAD_USES_ASYNC = 
#if UNITY_WEBGL
            true;
#else
            false;
#endif

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>当前模态遮挡窗口：读的是那条各轨共用的栈，与哪一轨在位无关。</summary>
        public static UIWindow CurrentModal => SharedLedger.CurrentModal;
        
        #endregion

        #region 安全区域 [SAFE AREA]

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）：广播给各轨，各自把这块矩形落到本轨的面板上。
        /// </summary>
        /// <remarks>
        /// 换算输入在各轨形状不同（uGUI 吃 <c>CanvasScaler</c> 的参考分辨率），门面不替谁代答、也不任挑一支。 <br />
        /// 刘海屏模拟那一档的矩形换算各轨共用 <see cref="UIServiceHandler.ComputeIPhoneXNotchSafeRect"/>。
        /// </remarks>
        /// <param name="safeRect">安全区域。</param>
        public static void ApplyScreenSafeRect(Rect safeRect)
        {
            onScreenSafeArea?.Invoke(safeRect);
        }

        /// <summary>
        /// 模拟IPhoneX异形屏：安全区矩形按支算一次、广播给各自的面板落位。
        /// </summary>
        public static void SimulateIPhoneXNotchScreen()
        {
            onNotchSimulate?.Invoke();
        }

        #endregion

        #region 寻址换算 [ADDRESS RESOLVER]

        /// <summary>
        /// 把开窗传入的 <paramref name="windowId"/> 换算成面板资产地址：内置资源档按 <see cref="UIServiceSettings"/> 的 Resources 父目录拼，否则查配置表。
        /// </summary>
        /// <remarks>
        /// 只在账本造新实例那一格叫：复用栈上窗与停放重取不吃标识，配置表未就绪时停放窗照样能重开。 <br />
        /// 父目录为空时标识<b>原样</b>当 Resources 相对路径用（不拼分隔符）。 <br />
        /// 配置表档的降级口径归 <c>ConfigTableService</c>：未就绪回 <c>null</c>、查无此 id 回空串并记 Warning， <br />
        /// 两者都不回落成字面地址，直接落进装载失败回滚。 <br />
        /// 线程契约：仅主线程。
        /// </remarks>
        /// <param name="windowId">窗口标识：配置表 configId，或 <c>Resources</c> 下的相对路径。</param>
        /// <param name="fromResources">取哪一档：真走 <c>Resources</c>，假走配置表。</param>
        /// <returns>交给面板装载的地址。</returns>
        internal static string ResolveWindowLocation(string windowId, bool fromResources)
        {
            if (!fromResources)
            {
                return ConfigTableService.GetUIWindowLocation(windowId);
            }

            var folder = UIServiceSettings.ResourcesFolder;
            return string.IsNullOrEmpty(folder) ? windowId : StringUtility.Concat(folder, "/", windowId);
        }

        #endregion

        #region 窗口查询 [WINDOW QUERIES]

        // 读数都来自各轨共用的栈：门面直叫共享持有者，与哪一轨在位、哪一轨先就位无关。

        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UIWindow GetTopWindow() =>
            SharedLedger.GetTopWindow();

        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UIWindow GetTopWindow(int layer) =>
            SharedLedger.GetTopWindow(layer);

        /// <summary>
        /// 获取指定层级下顶部窗口的标识。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetTopWindowId(int layer) =>
            SharedLedger.GetTopWindowId(layer);

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAnyLoading() =>
            SharedLedger.IsAnyLoading();

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <typeparam name="T">界面类型。</typeparam>
        /// <returns>是否存在。</returns>
        public static bool HasWindow<T>(string windowId = null) where T : UIWindow =>
            SharedLedger.HasWindow<T>(windowId);

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <returns>是否存在。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasWindow(Type type, string windowId = null) =>
            SharedLedger.HasWindow(type, windowId);

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>窗口实例。</returns>
        public static T GetWindow<T>(string windowId = null) where T : UIWindow =>
            SharedLedger.GetWindow<T>(windowId);

        /// <summary>
        /// 判断指定 UI 对象是否被模态窗口遮挡。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBlockedByModal(GameObject obj) =>
            SharedLedger.IsBlockedByModal(obj);

        /// <summary>
        /// 判断窗口是否为模态窗口。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsModal(UIWindow window) =>
            SharedLedger.IsModal(window);

        /// <summary>
        /// 申请模态动画期间的 UI 交互压制。
        /// </summary>
        /// <returns>调用方应当置位压制时返回 true；非模态窗口恒为 false。</returns>
        /// <remarks>
        /// 压制位是无归属的全局布尔，仲裁见 <see cref="UIInteractionLease"/>。<br />
        /// 租约住在共享持有者那一份上，各轨据此争同一个压制位：任一轨在位都算门面有效，只剩一支时压制照样要争。
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool AcquireModalInteraction(UIWindow window) =>
            IsValid && SharedLedger.InteractionLease.Acquire(window, IsModal(window));

        /// <summary>
        /// 交还模态动画期间的 UI 交互压制。
        /// </summary>
        /// <returns>调用方是当前持有者、可以清除压制位时返回 true；压制归别人持有时返回 false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ReleaseModalInteraction(UIWindow window) =>
            IsValid && SharedLedger.InteractionLease.Release(window);

        #endregion

        #region 显示窗口（按类型形参） [SHOW WINDOW BY TYPE]

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <remarks>
        /// 寻址两档对各轨都有效；落在哪一轨由 <see cref="RequireOwningTrack"/> 按目录先认，再直呼那一轨自述的开窗实现。<br />
        /// 窗口级 <c>PanelSettings</c> 不在这一张形参表上：那是 UI Toolkit 泛型腿比 uGUI 腿多收的一个。
        /// </remarks>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUIAsync(Type type, string windowId,
            UIPayload payload = default, CancellationToken ct = default)
        {
            RequireOwningTrack(type).OpenWindow(type, true, windowId, payload, ct);
        }

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <remarks>
        /// 寻址两档与上一道同一判据：落在哪一轨由 <see cref="RequireOwningTrack"/> 按目录先认。<br />
        /// 同步档在 <c>UNITY_WEBGL</c> 上交给异步装载；内置资源那一路两支都不落 await。
        /// </remarks>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUI(Type type, string windowId,
            UIPayload payload = default, CancellationToken ct = default)
        {
            RequireOwningTrack(type).OpenWindow(type, SYNC_LOAD_USES_ASYNC, windowId, payload, ct);
        }

        /// <summary>
        /// 异步打开窗口并等待面板就绪（Type 形入口）。
        /// </summary>
        /// <remarks>
        /// 与两条 void 腿不同：等待腿不经各轨的 Type 形开窗实现（它只压栈、不等就绪），而是直叫共享账本的等待腿，故在此现读认轨结果与驱动者在位否。<br />
        /// 认轨当场抬错；认出的轨没有驱动者就位也当场抬错——不把窗口推进栈再等装载静默失败。
        /// </remarks>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="ct">调用方取消令牌；被它撤销时等待原样上抛 <see cref="System.OperationCanceledException"/>。</param>
        /// <returns>栈上那个窗口（面板就绪后交回；装载失败交回 null）。</returns>
        public static async UniTask<UIWindow> ShowUIAsyncAwait(Type type, string windowId,
            UIPayload payload = default, CancellationToken ct = default)
        {
            var track = RequireOwningTrack(type);
            if (!track.IsDriverValid)
            {
                throw new GameException(StringUtility.Format(
                    "UI backend track '{0}' has no driver in place: list it in {1} and let OnInit register it.",
                    track.TrackName, nameof(UIServiceSettings)));
            }

            return await SharedLedger.ShowUIAwaitImp(type, true, windowId, null, payload, ct);
        }

        /// <summary>
        /// 认轨守卫：<see cref="Type"/> 形入口按目录找到拥有这一窗口类的轨并交回它，认不出轨当场抬错。
        /// </summary>
        /// <remarks>
        /// 判据是各轨自述的窗口基类，本文件不登记具体后端；泛型腿编译期已挡死错配，只有 <see cref="Type"/> 形入口会运行期判错。 <br />
        /// 判在叫任何一轨的开窗实现之前：否则窗口进了栈、装载静默失败，留下一个开不出来的窗。 <br />
        /// 「认不出轨」与「认出的轨没启用」是两档错，后者由那一轨自己的开窗实现抬。
        /// </remarks>
        /// <param name="windowType">窗口类型；<c>null</c> 与认不出轨的类型同样当场抬错。</param>
        /// <returns>拥有这一窗口类的那一轨。</returns>
        /// <exception cref="GameException">窗口类为空，或不落任何已登记轨的窗口基类。</exception>
        private static UITrack RequireOwningTrack(Type windowType)
        {
            if (windowType == null)
            {
                throw new GameException("UI window type is null: an open request must name a window class.");
            }

            var tracks = s_Tracks;
            if (tracks != null)
            {
                for (var i = 0; i < tracks.Count; i++)
                {
                    var track = tracks[i];
                    if (track.OwnsWindowType(windowType))
                    {
                        return track;
                    }
                }
            }

            throw new GameException(StringUtility.Format(
                "UI window '{0}' pairs with no registered UI track: a window class must derive from one of the registered track window bases ({1}).",
                windowType.FullName, RenderTrackWindowBaseNames(tracks)));
        }

        /// <summary>
        /// 把目录里各轨的窗口基类名拼成一列：认不出轨的文案用它枚举可挑的基类。
        /// </summary>
        /// <param name="tracks">当前目录；没建出来时枚举不出任何基类。</param>
        /// <returns>逗号分隔的基类名清单。</returns>
        private static string RenderTrackWindowBaseNames(List<UITrack> tracks)
        {
            if (tracks == null || tracks.Count == 0)
            {
                return "none";
            }

            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < tracks.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(tracks[i].WindowBaseType.Name);
            }

            return builder.ToString();
        }

        #endregion

        #region 关闭窗口 [CLOSE WINDOW]

        // 关与隐落的也是那一条共享栈：各轨的窗混排在同一条序里，入口不分轨。

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        public static void CloseUI<T>(string windowId = null) where T : UIWindow =>
            SharedLedger.CloseUI<T>(windowId);

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        /// <param name="type">窗口类型。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CloseUI(Type type, string windowId = null) =>
            SharedLedger.CloseUI(type, windowId);

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        public static void HideUI<T>(string windowId = null) where T : UIWindow =>
            SharedLedger.HideUI<T>(windowId);

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        /// <param name="type">窗口类型。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void HideUI(Type type, string windowId = null) =>
            SharedLedger.HideUI(type, windowId);

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CloseAll(bool isShutDown = false) =>
            SharedLedger.CloseAll(isShutDown);

        /// <summary>
        /// 关闭所有窗口除了指定窗口。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CloseAllWithOut(UIWindow withOut) =>
            SharedLedger.CloseAllWithOut(withOut);

        /// <summary>
        /// 关闭所有窗口除了指定类型的窗口。
        /// </summary>
        public static void CloseAllWithOut<T>() where T : UIWindow =>
            SharedLedger.CloseAllWithOut<T>();

        /// <summary>
        /// 关闭所有窗口除了指定层级的窗口。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CloseAllWithOut(EUILayer withOut) =>
            SharedLedger.CloseAllWithOut(withOut);

        #endregion

        #region 异步获取窗口 [GET WINDOW ASYNC]

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>窗口实例。</returns>
        public static UniTask<T> GetUIAsyncAwait<T>(string windowId = null) where T : UIWindow =>
            SharedLedger.GetUIAsyncAwait<T>(windowId);

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="windowId">窗口标识：栈上没有这一枚时按类型扫栈取栈顶那一只。</param>
        /// <param name="callback">回调。</param>
        public static void GetUIAsync<T>(string windowId, Action<T> callback) where T : UIWindow =>
            SharedLedger.GetUIAsync(callback, windowId);

        /// <summary>
        /// 异步获取窗口并等装载终态：就绪/失败/缺失/超时按 <see cref="UIOpenResult"/> 交回。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>取窗结果。</returns>
        public static UniTask<UIOpenResult> GetUIAwaitResult<T>(string windowId = null) where T : UIWindow =>
            SharedLedger.GetUIAwaitResultImp<T>(windowId);

        #endregion

        #region 导航 [NAVIGATION]

        /// <summary>导航深度：开启序历史的长度（栈按层级排序答不出「最近开的是谁」，历史按开启序答）。</summary>
        public static int NavigationDepth => SharedLedger.NavigationDepth;

        /// <summary>
        /// 关闭最近打开的窗口：走既有 CanClose 政策，无历史/拒关/过渡中回假。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryCloseTopWindow() => SharedLedger.TryCloseTopWindow();

        #endregion

    }
}
