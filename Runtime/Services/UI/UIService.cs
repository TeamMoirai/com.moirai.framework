using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Input;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Timer;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 服务外观（Facade）：全框架统一的静态 UI 访问入口。
    /// </summary>
    /// <remarks>
    /// 开窗入口按窗口基类平铺成同名两腿（<c>UGUIWindow</c> / <c>UITKWindow</c>），各腿直呼本轨 partial 的实现，没有替全部后端开窗的默认通道。<br />
    /// UI Toolkit 腿比 uGUI 腿多收一枚 <c>panelSettings</c>：同名重载靠形参表分辨，它也是窗口级面板配置的落点。<br />
    /// 每轨一枚驱动者，由 <see cref="UIServiceSettings"/> 的启用清单认领进各自槽位，拿的都是同一份 <see cref="SharedLedger"/>：栈只有一条。<br />
    /// 各轨以一枚 <see cref="UITrack"/> 自述登记进目录；本文件只枚举目录，加一轨就是加一枚自登记的 partial。<br />
    /// 跨轨的全局操作（查询、关隐、租约、每帧结算）直叫共享持有者；轨专有的走门面私有广播，<see cref="UIRoot"/>、<see cref="UICamera"/> 只有 uGUI 轨答得出。<br />
    /// 关停按自报档位升序逐轨收口，uGUI 最后收。线程契约：仅主线程；不声明 <c>[HandlerHost]</c>，<see cref="IsValid"/> 与归零门手写在本文件。
    /// </remarks>
    [AutoRegisterService]
    [ServiceDependency(typeof(DebuggerService), typeof(ResourceService), typeof(TimerService), typeof(InputService))]
    public sealed partial class UIService : ServiceBase, IServiceTickable
    {
        #region 处理器持有 [HANDLER HOSTING]

        private static volatile UIWindowLedger s_Ledger;

        /// <summary>各轨驱动者的本轨帧职责：各自在认领进槽时订阅一次，门面每帧广播一遍。</summary>
        private static event Action<float, float> onTrackTick;

        /// <summary>安全区矩形落到各轨自己面板的那一条广播。</summary>
        private static event Action<Rect> onScreenSafeArea;

        /// <summary>刘海屏模拟那一档的按支广播。</summary>
        private static event Action onNotchSimulate;

        /// <summary>各轨各自摘掉自己那枚处理器槽的广播：关停与归零门都先叫它一次，销毁链里迟到的回叫因此当场落空。</summary>
        private static event Action onDetachTrackSlots;

        /// <summary>窗口入栈后广播，形参是刚入栈的那一只；装载在途也照发（回执说的是栈序，不是面板就绪）。</summary>
        /// <remarks>订阅者自持生命周期：门面的归零门会整批摘掉这两枚广播（禁用域重载时上一轮订阅者不得跨会话残留），但一次 <c>+=</c> 配一次 <c>-=</c> 仍是对话方的责任。</remarks>
        public static event Action<UIWindow> onWindowShown;

        /// <summary>窗口出栈后广播，形参是刚出栈的那一只：停放与销毁都发，一次出栈恰一次。</summary>
        public static event Action<UIWindow> onWindowClosed;

        /// <summary>轨道目录：各轨 partial 的静态初始化器把自述登记进这一份，门面只枚举它。</summary>
        /// <remarks>懒建＋compare-exchange：partial 各文件静态字段的初始化次序没有契约保证，目录不能靠本文件自己的初始化器先就位。</remarks>
        private static volatile List<UITrack> s_Tracks;

        /// <summary>目录里任何一轨的驱动者就位即为真；一轨都没就位为假。</summary>
        /// <remarks>读的是各轨描述符自述的有效性探针（探针读本轨槽位），门面不另记一份在位状态。</remarks>
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
        /// 把一枚轨道登记进门面目录：各轨 partial 的静态初始化器在类型就绪时各叫一次。
        /// </summary>
        /// <remarks>
        /// 按 <see cref="UITrack.ShutdownOrder"/> 升序插入、同档按登记序，关停就按这一序逐轨走一遍。<br />
        /// 登记只发生在类型初始化里（外部取用都排在静态初始化器之后），插入本身不需要并发防护。<br />
        /// 不查重复登记：内建轨各登记一次，测试的合成轨由 <see cref="Internal_UnregisterTrack"/> 摘回去。
        /// </remarks>
        /// <param name="track">待登记的轨道自述。</param>
        /// <returns>原样交回登记进去的那一枚，供 partial 存成自己的字段。</returns>
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

        /// <summary>摘掉一枚轨道登记：合成轨用它收回干净域，内建轨不摘。</summary>
        /// <param name="track">要摘的那一枚。</param>
        /// <returns>目录里真有它并摘掉了为真。</returns>
        internal static bool Internal_UnregisterTrack(UITrack track)
        {
            var tracks = s_Tracks;
            return tracks != null && tracks.Remove(track);
        }

        /// <summary>目录只读视图：内建轨的登记完整性与合成轨的处置都由用例读它判定。</summary>
        internal static IReadOnlyList<UITrack> Internal_PeekTracks() => s_Tracks;

        /// <summary>
        /// 各轨后端共用那一份窗口栈与停放表的持有者：懒建一枚，<see cref="Internal_ResetHandlerSlots"/> 归位时把它抹掉、下一读再建一枚。
        /// </summary>
        /// <remarks>
        /// 每读现取、不是构造期快照：共享持有者会被归位门换掉，而清单里的驱动者由资产反序列化器在任意时刻造出来。<br />
        /// 线程契约：仅主线程（开窗、关窗与每帧驱动都在主线程）。
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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHandlerSlotsOnDomainReload()
        {
            Internal_ResetHandlerSlots();
        }

        /// <summary>
        /// 归位门：叫各轨各自摘槽、摘掉挂在门面上的全部订阅与认领，再把共享持有者清回干净状态。
        /// </summary>
        /// <remarks>
        /// 只清不置：不收实参、也没有把对象放进槽的路径，槽里只剩 <see cref="OnInit"/> 按启用清单认领那一条来路。<br />
        /// 不跑关停：被摘下的实例归 <see cref="OnShutdown"/> 按目录档位收口。夹具按进门归位用它。
        /// </remarks>
        internal static void Internal_ResetHandlerSlots()
        {
            onDetachTrackSlots?.Invoke();
            Internal_ClearTrackSubscriptions();
            Interlocked.Exchange(ref s_Ledger, null);
        }

        /// <summary>
        /// 摘掉各轨挂在门面上的全部订阅与认领：四道广播置空、目录里每一轨的关停回调摘掉。
        /// </summary>
        /// <remarks>订阅与认领住在静态位上：槽清了而它们没清，下一轮仍会叫到上一轮已退役的实例。</remarks>
        private static void Internal_ClearTrackSubscriptions()
        {
            onDetachTrackSlots = null;
            onTrackTick = null;
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

        /// <summary>帧广播当前的订阅条数：一支驱动者认领成功才加一条，第二轮认领同一枚实例不再加。</summary>
        internal static int Internal_PeekTrackTickSubscriberCount() => onTrackTick?.GetInvocationList().Length ?? 0;

        #region 窗口事件触发 [WINDOW EVENT RAISERS]

        /// <summary>发一次入栈回执：`event` 只能在声明类内触发，共享持有者经这一道口转手。</summary>
        /// <param name="window">刚入栈的那一只。</param>
        internal static void Internal_RaiseWindowShown(UIWindow window) => onWindowShown?.Invoke(window);

        /// <summary>发一次出栈回执：停放与销毁两条路都由 <see cref="UIWindowLedger.Pop"/> 叫到这一道。</summary>
        /// <param name="window">刚出栈的那一只。</param>
        internal static void Internal_RaiseWindowClosed(UIWindow window) => onWindowClosed?.Invoke(window);

        #endregion

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>
        /// 初始化 UI 服务。由容器在构建期调用。
        /// </summary>
        /// <remarks>
        /// 先归零共享持有者，再按 <see cref="UIServiceSettings"/> 的启用清单逐支造出驱动者——启用哪几支由配置答，不由「谁先碰到哪一支的取用」答。<br />
        /// 各轨自此并存于同一条栈上，各自驱动本轨那半边的编排；窗口回叫问的是那条共享栈，门面不再交单一协调者出去。<br />
        /// 可重入：某一轨的驱动者已就位时不再造第二枚、不重复挂广播，但仍归零那一份共享存储。
        /// </remarks>
        public override void OnInit()
        {
            Internal_ResetSharedLedger();
            Internal_EnableHandlersFromSettings();
        }

        /// <summary>
        /// 按配置启用后端：逐条把 <see cref="UIServiceSettings.EnabledHandlers"/> 里的条目交回它自己那一轨去认领——填槽、挂广播、初始化。
        /// </summary>
        /// <remarks>
        /// 逐支叫 <see cref="UIServiceHandler.Internal_Register"/>，本文件既不判类型也不写槽。<br />
        /// 本层抬错档：清单为 <c>null</c> 或空、清单里有 <c>null</c> 项；同一轨被注入两次由那一轨自己的认领门抬错。<br />
        /// 抬错前可能已注册好前几项的驱动者——门面不补半程关停，已就位的由 <see cref="OnShutdown"/> 或归位门收。
        /// </remarks>
        /// <exception cref="GameException">没有任何后端被启用、清单里有 <c>null</c> 项，或同一轨被注入两次。</exception>
        private static void Internal_EnableHandlersFromSettings()
        {
            var enabled = UIServiceSettings.EnabledHandlers;
            if (enabled == null || enabled.Length == 0)
            {
                throw new GameException(StringUtility.Format(
                    "No UI backend is enabled: {0} must list at least one handler to register at init.",
                    nameof(UIServiceSettings)));
            }

            for (var i = 0; i < enabled.Length; i++)
            {
                var entry = enabled[i];
                if (entry == null)
                {
                    throw new GameException(StringUtility.Format(
                        "UI backend entry #{0} is null: {1} must list handlers, not empty slots.",
                        i, nameof(UIServiceSettings)));
                }

                entry.Internal_Register();
            }
        }

        /// <summary>
        /// 关闭 UI 服务。由容器在关闭期调用。
        /// </summary>
        /// <remarks>
        /// 顺序是契约：先广播摘槽，再按各轨自报的关停档位升序逐轨收口，最后整批摘掉订阅与认领。<br />
        /// 摘槽排在收口之前：<see cref="UIWindow"/> 的回叫钩子认 <see cref="IsValid"/>，槽清了它们就静默落空，既不拿到半关的驱动者、也不在账本正被收的时候重进去。<br />
        /// 收口叫认领时绑定的那一枚关停回调，不重读槽位（此刻重读会静默跳过关停）。<br />
        /// 摘订阅与归零放在 <c>finally</c>：销毁链抛了也不能把订阅留在已空的槽上，否则下一轮认领会给同一实例挂出第二遍广播。
        /// </remarks>
        public override void OnShutdown()
        {
            try
            {
                onDetachTrackSlots?.Invoke();
                Internal_ShutDownTracksInOrder();
            }
            finally
            {
                Internal_ClearTrackSubscriptions();
                Internal_ResetSharedLedger();
            }
        }

        /// <summary>
        /// 按档位升序逐轨收口：目录在登记时就排好升序，宿主轨（持有别轨面板挂靠的根）落在最后一段。
        /// </summary>
        /// <remarks>
        /// 叫的是认领进槽时绑定的那一枚关停回调：某一轨没被启用（没认领）时它没有回调，目录序跳过它。同档各轨按登记序，彼此没有宿主依赖。
        /// </remarks>
        private static void Internal_ShutDownTracksInOrder()
        {
            var tracks = s_Tracks;
            if (tracks == null)
            {
                return;
            }

            for (var i = 0; i < tracks.Count; i++)
            {
                tracks[i].ClaimedShutDown?.Invoke();
            }
        }

        /// <summary>
        /// 容器 Tick 驱动：每帧把那条共享栈结算一次，再广播各轨 handler 本轨专有那段。
        /// </summary>
        /// <remarks>
        /// 整条栈的驱动归这一处、每帧只结算一次；各轨广播里那份只剩本轨自己的帧职责，认领进槽时各挂一次。<br />
        /// 不得改成各轨各叫一次（整条栈每帧跑几遍），也不得任挑一支当代驱动（只剩另一轨在位时整条栈一帧都不结算，而 <see cref="IsValid"/> 回真）。
        /// </remarks>
        public void Tick(float elapseSeconds, float realElapseSeconds)
        {
            SharedLedger.Tick();
            onTrackTick?.Invoke(elapseSeconds, realElapseSeconds);
        }

        #endregion

        #region 常量 [CONSTANTS]

        // 层级常量
        public const int LAYER_DEEP = 2000;
        public const int WINDOW_DEEP = 100;
        public const int WINDOW_HIDE_LAYER = 2; // Ignore Raycast
        public const int WINDOW_SHOW_LAYER = 5; // UI

        /// <summary>同步开窗档在 WebGL 上交给异步装载（那一档平台没有同步装载）：两轨的同步腿与 Type 形入口共用这一位。</summary>
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
        /// 换算的输入在各轨里形状不同（uGUI 吃 <c>CanvasScaler</c> 的参考分辨率，UI Toolkit 那一路目前不吃），门面因此不替谁代答、也不任挑一支。 <br />
        /// 刘海屏那一档的矩形换算各轨共用 <see cref="UIServiceHandler.ComputeIPhoneXNotchSafeRect"/> 那一份。
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

        #region 窗口查询 [WINDOW QUERIES]

        // 这一族的读数都来自那条各轨共用的栈：门面直叫共享持有者，与哪一轨在位、哪一轨先就位无关。
        // 形参表与返回形状是包外调用点的编译依据，一枚都不动。

        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        public static UIWindow GetTopWindow() =>
            SharedLedger.GetTopWindow();

        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        public static UIWindow GetTopWindow(int layer) =>
            SharedLedger.GetTopWindow(layer);

        /// <summary>
        /// 获取指定层级下顶部的窗口名称。
        /// </summary>
        public static string GetTopWindowName(int layer) =>
            SharedLedger.GetTopWindowName(layer);

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        public static bool IsAnyLoading() =>
            SharedLedger.IsAnyLoading();

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <typeparam name="T">界面类型。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <returns>是否存在。</returns>
        public static bool HasWindow<T>(string windowName = null) where T : UIWindow =>
            SharedLedger.HasWindow<T>(windowName);

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <returns>是否存在。</returns>
        public static bool HasWindow(Type type, string windowName = null) =>
            SharedLedger.HasWindow(type, windowName);

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <returns>窗口实例。</returns>
        public static T GetWindow<T>(string windowName) where T : UIWindow =>
            SharedLedger.GetWindow<T>(windowName);

        /// <summary>
        /// 判断指定 UI 对象是否被模态窗口遮挡。
        /// </summary>
        public static bool IsBlockedByModal(GameObject obj) =>
            SharedLedger.IsBlockedByModal(obj);

        /// <summary>
        /// 判断窗口是否为模态窗口。
        /// </summary>
        public static bool IsModal(UIWindow window) =>
            SharedLedger.IsModal(window);

        /// <summary>
        /// 申请模态动画期间的 UI 交互压制。
        /// </summary>
        /// <returns>调用方应当置位压制时返回 true；非模态窗口恒为 false。</returns>
        /// <remarks>
        /// 压制位是无归属的全局布尔，仲裁见 <see cref="UIInteractionLease"/>。<br />
        /// 租约住在共享持有者那一份上，各轨据此争同一枚压制位：任一轨在位都算门面有效，只剩一支时压制照样要争。
        /// </remarks>
        internal static bool AcquireModalInteraction(UIWindow window) =>
            IsValid && SharedLedger.InteractionLease.Acquire(window, IsModal(window));

        /// <summary>
        /// 交还模态动画期间的 UI 交互压制。
        /// </summary>
        /// <returns>调用方是当前持有者、可以清除压制位时返回 true；压制归别人持有时返回 false。</returns>
        internal static bool ReleaseModalInteraction(UIWindow window) =>
            IsValid && SharedLedger.InteractionLease.Release(window);

        #endregion

        #region 显示窗口（按类型形参） [SHOW WINDOW BY TYPE]

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <remarks>
        /// 寻址两档对各轨都有效；落在哪一轨由 <see cref="RequireOwningTrack"/> 按目录先认，再直呼那一轨自述的开窗实现。<br />
        /// 窗口级 <c>PanelSettings</c> 不在这一张形参表上：那是 UI Toolkit 泛型腿比 uGUI 腿多出的那一枚。
        /// </remarks>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUIAsync(Type type, string windowName = null, string assetLocation = null, bool fromResources = false,
            UIPayload payload = default, CancellationToken ct = default)
        {
            RequireOwningTrack(type).OpenWindow(type, true, windowName, assetLocation, fromResources, payload, ct);
        }

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <remarks>
        /// 寻址两档与上一道同一判据：落在哪一轨由 <see cref="RequireOwningTrack"/> 按目录先认。<br />
        /// 同步档在 <c>UNITY_WEBGL</c> 上交给异步装载；内置资源（<paramref name="fromResources"/>）那一路两支都不落 await。
        /// </remarks>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="ct">调用方取消令牌；装载在途时它撤销即掐断装载并回滚。</param>
        public static void ShowUI(Type type, string windowName = null, string assetLocation = null, bool fromResources = false,
            UIPayload payload = default, CancellationToken ct = default)
        {
            RequireOwningTrack(type).OpenWindow(type, SYNC_LOAD_USES_ASYNC, windowName, assetLocation, fromResources, payload, ct);
        }

        /// <summary>
        /// 异步打开窗口并等待面板就绪（Type 形入口）。
        /// </summary>
        /// <remarks>
        /// 与两条 void 腿不同：等待腿不经各轨的 Type 形开窗实现（那一份只压栈、不等就绪），而是直叫共享账本的等待腿，故在此现读认轨结果与驱动者在位否。<br />
        /// 认轨当场抬错，认出来却没人认领驱动那一档也当场抬错——不把窗口推进栈再等装载静默失败。
        /// </remarks>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="ct">调用方取消令牌；被它撤销时等待原样上抛 <see cref="System.OperationCanceledException"/>。</param>
        /// <returns>栈上那一只窗口（面板就绪后交回；装载失败交回 null）。</returns>
        public static async UniTask<UIWindow> ShowUIAsyncAwait(Type type, string windowName = null, string assetLocation = null, bool fromResources = false,
            UIPayload payload = default, CancellationToken ct = default)
        {
            var track = RequireOwningTrack(type);
            if (!track.IsDriverValid)
            {
                throw new GameException(StringUtility.Format(
                    "UI backend track '{0}' has no driver in place: list it in {1} and let OnInit register it.",
                    track.TrackName, nameof(UIServiceSettings)));
            }

            return await SharedLedger.ShowUIAwaitImp(type, true, windowName, assetLocation, fromResources, null, payload, ct);
        }

        /// <summary>
        /// 认轨守卫：<see cref="Type"/> 形入口按目录找到拥有这一窗口类的轨并交回它，认不出轨当场抬错。
        /// </summary>
        /// <remarks>
        /// 判据是各轨描述符自述的窗口基类（<see cref="UGUIWindow"/>、<see cref="UITKWindow"/>），本文件不登记任何具体后端。<br />
        /// 泛型腿的约束在编译期就挡死跨轨实参与没挂窗口基类的窗口类，运行期会判出错配的只有 <see cref="Type"/> 形入口。<br />
        /// 判在叫任何一轨的开窗实现之前：否则窗口被推进栈、面板装载静默失败，留下一只开不出来的窗。<br />
        /// 「认不出轨」与「轨认出来了但那一轨没被启用」是两档不同的错，后者由那一轨自己的开窗实现抬。
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

        /// <summary>把目录里各轨的窗口基类名拼成一列：认不出轨的文案用它枚举可挑的基类。</summary>
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

        // 关与隐落的也是那一条共享栈：各轨的窗混排在同一条序里，门面上的关窗入口因此不分轨、也不看哪一轨先就位。

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        public static void CloseUI<T>(string windowName = null) where T : UIWindow =>
            SharedLedger.CloseUI<T>(windowName);

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称。</param>
        public static void CloseUI(Type type, string windowName = null) =>
            SharedLedger.CloseUI(type, windowName);

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        public static void HideUI<T>(string windowName = null) where T : UIWindow =>
            SharedLedger.HideUI<T>(windowName);

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称。</param>
        public static void HideUI(Type type, string windowName = null) =>
            SharedLedger.HideUI(type, windowName);

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        public static void CloseAll(bool isShutDown = false) =>
            SharedLedger.CloseAll(isShutDown);

        /// <summary>
        /// 关闭所有窗口除了指定窗口。
        /// </summary>
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
        public static void CloseAllWithOut(EUILayer withOut) =>
            SharedLedger.CloseAllWithOut(withOut);

        #endregion

        #region 异步获取窗口 [GET WINDOW ASYNC]

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>窗口实例。</returns>
        public static UniTask<T> GetUIAsyncAwait<T>() where T : UIWindow =>
            SharedLedger.GetUIAsyncAwait<T>();

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="callback">回调。</param>
        public static void GetUIAsync<T>(Action<T> callback) where T : UIWindow =>
            SharedLedger.GetUIAsync(callback);

        /// <summary>
        /// 异步获取窗口并等装载终态：就绪/失败/缺失/超时按 <see cref="UIOpenResult"/> 交回。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>取窗结果。</returns>
        public static UniTask<UIOpenResult> GetUIAwaitResult<T>() where T : UIWindow =>
            SharedLedger.GetUIAwaitResultImp<T>();

        #endregion

        #region 导航 [NAVIGATION]

        /// <summary>导航深度：开启序历史的长度（栈按层级排序答不出「最近开的是谁」，历史按开启序答）。</summary>
        public static int NavigationDepth => SharedLedger.NavigationDepth;

        /// <summary>关上最近开的那只：走既有 CanClose 政策，无历史/拒关/过渡中回假。</summary>
        public static bool TryCloseTopWindow() => SharedLedger.TryCloseTopWindow();

        #endregion
    }
}
