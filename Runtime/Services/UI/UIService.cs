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
    /// 开窗入口按实现类平铺成同名两腿（uGUI 腿 <c>where T : UGUIWindow</c>、UI Toolkit 腿 <c>where T : UITKWindow</c>）， <br />
    /// 两支的约束各自收在自己的窗口基类上：一支留中性 <c>UIWindow</c> 时 UI Toolkit 窗同时满足两支，一枚实参的同形调用当场判二义（CS0121）。 <br />
    /// UI Toolkit 腿的形参表与 uGUI 腿同序收下寻址两档，末尾再多一枚 <c>panelSettings</c>：那既是两支同名重载的分辨处，也是窗口级面板配置的落点。 <br />
    /// 两支共用同一条开栈编排：各腿叫自己那一轨 partial 里的实现，uGUI 腿经 <see cref="UGUIHandler"/>、UI Toolkit 腿经 <see cref="UITKHandler"/> 把窗口落进共享栈。 <br />
    /// 两支的实现按轨分住在各自的 partial 文件里：uGUI 腿三支在 <c>UIService.UGUI.cs</c>、UI Toolkit 腿三支在 <c>UIService.UITK.cs</c>；本文件只留中性外壳。 <br />
    /// 开窗腿没有「交给默认实现」那条通道：门面上不存在一个后端替全部后端开窗的路径，也没有拿 <c>?.</c> 静默落空的那一档。 <br />
    /// 每支后端各一枚驱动者，槽位住在各自那一轨的 partial 里（uGUI 轨那枚在 <c>Handler/UGUI/UIService.UGUI.cs</c>、UI Toolkit 轨那枚在 <c>Handler/UITK/UIService.UITK.cs</c>）， <br />
    /// 都由 <see cref="UIServiceSettings"/> 的启用清单在 <c>OnInit</c> 里认领进来，手里拿的都是同一份 <see cref="SharedLedger"/>——各轨各一份 handler，窗口栈只有一条。 <br />
    /// 各轨经一枚 <see cref="UITrack"/> 自述登记进门面的轨道目录（认窗判据、有效性探针、Type 形入口分派与关停档位）， <br />
    /// 主文件只枚举目录、不登记任何具体后端：加一轨＝加一枚 partial 自登记，本文件零改动。 <br />
    /// 门面的取用分两档：跨轨的全局操作（查询、关隐、租约、每帧结算）直叫那一份共享持有者，与哪一轨在位无关； <br />
    /// 本轨专有那一段（帧职责、安全区落点、刘海屏模拟）走门面私有的三条广播，各轨在认领进槽时各挂一次； <br />
    /// 关停不走广播：按各轨在目录里自报的档位升序逐轨收口，持有别轨面板挂靠的宿主根那一轨（uGUI）自报宿主档、最后收。 <br />
    /// 轨专有操作才认支——<see cref="UIRoot"/> 与 <see cref="UICamera"/> 只问 uGUI 那一枚（另一轨答不出这两个）， <br />
    /// <see cref="ApplyScreenSafeRect"/> 与 <see cref="SimulateIPhoneXNotchScreen"/> 各轨各叫一次，让每轨把安全区落到自己的面板上。 <br />
    /// <see cref="IsValid"/> 与归零门在本文件，认领门与取用属性随各轨的 partial 走： <br />
    /// UI 门面不声明 <c>[HandlerHost]</c>，源生成器因此不为它产成员。
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

        /// <summary>轨道目录：各轨 partial 的静态初始化器把自述登记进这一份，门面只枚举它、不登记任何具体后端。</summary>
        /// <remarks>懒建＋compare-exchange 占位与 <see cref="s_Ledger"/> 同款：partial 各文件的静态字段初始化次序没有契约保证，目录不能靠主文件自己的初始化器先就位。</remarks>
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
        /// 目录按 <see cref="UITrack.ShutdownOrder"/> 升序插入、同档按登记序——门面关停只按目录序走一遍，持有别轨面板挂靠的宿主根那一轨因此落在最后一段。 <br />
        /// 登记只发生在类型初始化里（外部任何取用都排在全部静态初始化器之后），插入本身不需要并发防护。 <br />
        /// 没有重复登记的检查：内建轨各登记一次，测试的合成轨由 <see cref="Internal_UnregisterTrack"/> 摘回去。
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
        /// 处理器侧取用的是这一位的<b>现读</b>（<c>UIServiceHandler.Ledger</c>），不是构造期那一份快照：持有者会被归位门换掉， <br />
        /// 而清单里的驱动者由资产反序列化器在任意时刻造出来——把持有者定格在构造期，「只有一条栈」就变成写进一份、查询走另一份。 <br />
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
        /// 共享持有者的归零事务（门面侧唯一的一处归零门）：栈与停放表清空、交互租约交回、上一轮没还回来的全局压制位归零。
        /// <para>各轨 handler 的初始化都不抹这份共用的存储；关停时各轨各关自己那一轨的窗（见 <see cref="UI.UGUIHandler"/> 与 <see cref="UI.UITKHandler"/>），这道门收的是剩下的停放表与租约。</para>
        /// </summary>
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
        /// 免域重载复位：各轨处理器槽与那份共享持有者都是静态位，跨 Play 会话残留会把上一轮的栈漏给下一轮（房内先例 <see cref="UIBase"/> 的注入点位复位）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHandlerSlotsOnDomainReload()
        {
            Internal_ResetHandlerSlots();
        }

        /// <summary>
        /// 归位门：叫各轨各自摘掉槽、摘干净挂在门面上的所有订阅与认领，再把那份共享持有者清回「干净域」那一份状态。
        /// <para>只清不置：这一道不收任何实参，也没有把对象放进槽的路径——槽里从此只有 <c>OnInit</c> 按启用清单认领那一条来路。夹具按进门归位用它。</para>
        /// <para>它不跑关停：被摘下的那一枚实例归谁关，是 <see cref="OnShutdown"/> 按目录档位收口的事。</para>
        /// </summary>
        internal static void Internal_ResetHandlerSlots()
        {
            onDetachTrackSlots?.Invoke();
            Internal_ClearTrackSubscriptions();
            Interlocked.Exchange(ref s_Ledger, null);
        }

        /// <summary>
        /// 摘掉各轨挂在门面上的全部订阅与认领：四道广播一起置空、目录里每一轨的关停回调一并摘掉，第二轮认领才各挂一次。
        /// <para>订阅与认领挂在静态位上，槽清了而它们没清，下一轮仍会叫到上一轮那枚已退役的实例。</para>
        /// </summary>
        private static void Internal_ClearTrackSubscriptions()
        {
            onDetachTrackSlots = null;
            onTrackTick = null;
            onScreenSafeArea = null;
            onNotchSimulate = null;

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

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>
        /// 初始化 UI 服务。由容器在构建期调用。
        /// </summary>
        /// <remarks>
        /// 先走这一次归零：共享持有者归各轨后端共用，复用它的第二轮从门面这一处起步，不再由协调者的 <c>OnInit</c> 抹掉别轨留在栈上的窗口。 <br />
        /// 再按 <see cref="UIServiceSettings"/> 的启用清单逐支造出驱动者——启用哪几支由配置答，不由「谁先碰到哪一支的取用」答。 <br />
        /// 各轨自此并存于同一条栈上，各自驱动本轨那半边的编排；门面不再交「那一份协调者」出去，R8 之后回叫没有单点落点（窗口问的是那条共享栈，见 <see cref="UIWindow"/> 的 <c>Hide</c>/<c>Close</c>）。 <br />
        /// 可重入：某一轨的驱动者已就位时这一道不再造第二枚，也不重复挂广播；第二轮的 <c>OnInit</c> 仍只归零那一份共享存储。
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
        /// 归属由实现类自述：本文件只逐支叫 <see cref="UIServiceHandler.Internal_Register"/>，既不判类型也不写槽，加一轨是加一枚实现类与一条认领门、本文件一行都不多。 <br />
        /// 本层的两档抬错是<b>清单为 <c>null</c> 或空</b>（资产缺这一枚键时读回来的就是 <c>null</c>，<c>SerializeReference</c> 不跑字段初始值）与<b>某一项是 <c>null</c></b>；<b>同一轨被注入两次</b>由那一轨自己的认领门抬错。 <br />
        /// 「注入一枚不属于内建两支的驱动者」这一档在形状上已不存在：认领门的形参就是那一轨的具体类型，编译期就否掉了它。 <br />
        /// 抬错之前可能已经注册好了前几项的驱动者——配置面坏了这本就是启动失败，已经就位的那几枚由 <see cref="OnShutdown"/> 或归位门收，门面不在抬错之后补一次半程关停。
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
        /// 本文件只广播与按目录收口，不认识任何一轨：先叫 <c>onDetachTrackSlots</c>，再按各轨自报的关停档位升序逐轨收口，最后把订阅与认领整批摘掉。 <br />
        /// 先摘干净各轨的槽再收口：<see cref="UIWindow"/> 的两枚回叫钩子认的是 <see cref="IsValid"/>，槽清了它们就静默落空，既不拿到半关的驱动者也不当场抬错、更不会在账本正被收的时候重进去。 <br />
        /// 档位由各轨在描述符里自报：持有别轨面板挂靠的宿主根那一轨（uGUI）取宿主档最后收，其余取默认档先收——次序由自报档位表述，不由认领先后决定。 <br />
        /// 收口叫的是认领时绑定的那一枚关停回调、不是重读槽位：摘槽广播已在前面跑过，此刻重读槽会静默跳过关停。 <br />
        /// 全部收口之后走这一次归零：那才是关停侧收停放表与租约的一道门，此刻栈已由各轨各自关空。 <br />
        /// 收口包在 <c>finally</c> 之外：销毁链抛了也不能把订阅与认领留在已空的槽上——下一轮认领走的是 compare-exchange 而不是同枚实例的早退，
        /// 那会给同一实例挂出第二遍广播。
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
        /// 叫的是认领进槽时绑定的那一枚关停回调：某一轨没被启用（没认领）时它没有回调，目录序跳过它。 <br />
        /// 同档的收口次序按登记序：同档各轨之间没有宿主依赖，先后不构成契约。
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
        /// 栈只有一条、结算因此每帧只有一次：整条栈的驱动归门面这一处，各轨的 <see cref="UIServiceHandler.Tick"/> 只剩本轨自己的帧职责（uGUI 那一轨交出去的是 UI 根的续等）。 <br />
        /// 既不是各轨各叫一次（那等于每帧把整条栈跑几遍），也不是任挑一支当代驱动（只剩 UI Toolkit 那一枚在位时整条栈一帧都不结算，而 <see cref="IsValid"/> 回真——静默致命档）。 <br />
        /// 这一族按支的帧职责是广播：<see cref="UI.UGUIHandler"/> 与 <see cref="UI.UITKHandler"/> 各自在认领进槽时挂一次，谁先就位就先驱谁，几轨都在位时同一条栈照旧只结算一次。
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

        // 这一族的读数都来自那一条各轨共用的栈：门面直叫共享持有者，与哪一轨在位、哪一轨先就位无关。
        // 形参表与返回形状是包外调用点的编译依据，一枚都不动（IsBlockedByModal 那枚 GameObject 形参照旧）。

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
        /// <remarks>压制位本身是无归属的全局布尔，仲裁见 <see cref="UIInteractionLease"/>；租约住在共享持有者那一份上，各轨后端据此争同一枚压制位——任一轨在位都算门面有效，只剩 UI Toolkit 那一枚时压制照样要争。</remarks>
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
        /// 寻址两档对各轨都有效：UI Toolkit 轨取面板模板那一条路同样分 AB 与内置资源，这一入口不再只喂 uGUI 那一轨。 <br />
        /// 分派按目录找到拥有这一窗口类的轨、直呼那一轨自述的开窗实现：主文件不登记任何具体后端。 <br />
        /// 窗口级 <c>PanelSettings</c> 不在这一张形参表上：那是 UI Toolkit 泛型腿比 uGUI 腿多出的那一枚，同名两支靠它过 <c>CS0111</c>。
        /// </remarks>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public static void ShowUIAsync(Type type, string windowName = null, string assetLocation = null, bool fromResources = false, params object[] userData)
        {
            RequireOwningTrack(type).OpenWindow(type, true, windowName, assetLocation, fromResources, userData);
        }

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <remarks>
        /// 寻址两档与上一道同一判据：落在哪一轨由 <see cref="RequireOwningTrack"/> 按目录先认，各轨都吃这两档。 <br />
        /// 同步档在 <c>UNITY_WEBGL</c> 上交给异步装载，那一档与 <paramref name="fromResources"/> 不相冲：内置资源那一路两条腿都不落 await。
        /// </remarks>
        /// <param name="type">窗口类型。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public static void ShowUI(Type type, string windowName = null, string assetLocation = null, bool fromResources = false, params object[] userData)
        {
            RequireOwningTrack(type).OpenWindow(type, SYNC_LOAD_USES_ASYNC, windowName, assetLocation, fromResources, userData);
        }

        /// <summary>
        /// 认轨守卫：<see cref="Type"/> 形入口按目录找到拥有这一窗口类的轨并交回它，认不出轨当场抬错。
        /// </summary>
        /// <remarks>
        /// 判据是各轨描述符自述的窗口基类（uGUI 轨认 <see cref="UGUIWindow"/>、UI Toolkit 轨认 <see cref="UITKWindow"/>），主文件不登记任何具体后端： <br />
        /// 加一轨＝加一枚 <c>UIService.&lt;轨&gt;.cs</c> partial 自登记，这里零改动、不新增接口、不登记任何对象。 <br />
        /// 认不出轨的窗口类在这里被拒开，经 <see cref="Type"/> 形入口进不来；文案按目录枚举各轨的窗口基类，登记进来的轨都答得上。 <br />
        /// 泛型腿的约束在编译期就把跨轨实参与没挂任何一枚窗口基类的窗口类一起挡死（各轨各自收在自己的窗口基类上）， <br />
        /// 因此运行期这一判断得出错配的只有 <see cref="Type"/> 形入口。 <br />
        /// 判在叫任何一轨的开窗实现之前：认不出轨的窗口会被推进栈、面板装载静默失败，留下一只开不出来的窗； <br />
        /// 轨认出来了但那一轨没被启用时，交回的轨自己那枚开窗实现会抬「没有驱动者在位」（各轨自己的取用属性），与「认不出轨」是两档不同的错。
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
        public static void CloseAllWithOut(UILayer withOut) =>
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

        #endregion
    }
}
