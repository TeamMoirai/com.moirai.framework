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
    /// 中性外壳（生命周期、层级常量、属性、安全区、<see cref="System.Type"/> 形入口与共享栈上的关·隐·取）住在 <c>UIService.cs</c>，uGUI 腿的三支住在 <c>UIService.UGUI.cs</c>。 <br />
    /// 本文件还管着 UI Toolkit 这一轨的槽位、认领门、取用属性、<see cref="IsUITKValid"/> 与轨道自述——槽位与处理器位随各自的腿走，自述经静态初始化登记进门面目录。 <br />
    /// 三支的形参表与 uGUI 腿逐枚同形，只在末尾多一枚 <c>panelSettings</c>：那既是两支同名重载过 <c>CS0111</c> 的那一处差异，也是本轨的真能力（窗口级 <c>PanelSettings</c>）。 <br />
    /// 面板地址与取法在本轨两档都收：<see cref="UITKWindow"/> 取模板那一条路 AB 与内置资源各一，与 uGUI 轨同形；都不给时地址仍按窗口类走那条既有链路。 <br />
    /// 本文件的三支各自叫下面那一枚 UI Toolkit 轨自己的开窗实现，实现经 <see cref="UITKHandler"/> 把窗口落进共享栈——与 uGUI 那一轨共用那一份栈，但不共用那条腿、也不借那一轨的驱动者。 <br />
    /// <c>panelSettings</c> 不进中性编排：它由本文件包成一枚只认 <see cref="UIWindow"/> 的交接钩子交下去，在装载之前落到 <see cref="UITKWindow"/> 的实例属性上。 <br />
    /// 类级属性与基类清单只在主声明上，本文件是同一枚类的另一份声明；本文件的形参表与约束就是这一腿的契约——改它要先红签名快照用例。
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
        /// 静态初始化器自登记，主文件只枚举目录：本轨的窗口基类、槽位与开窗实现都在本文件自述，加第三轨＝加一枚同款 partial，主文件零改动。 <br />
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
        /// 槽位的来路只有 <see cref="OnInit"/> 按 <see cref="UIServiceSettings"/> 启用清单那一条：与 uGUI 那一轨同形， <br />
        /// 取用不替配置造驱动者。文案点名是哪一轨、去哪一处启用；UI Toolkit 腿的三支与 <see cref="System.Type"/> 入口的 UI Toolkit 档都经这一枚取用。
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

        /// <summary>读 UI Toolkit 轨那枚处理器槽：同上，不叫认领门，也不产生任何副作用。</summary>
        internal static UITKHandler Internal_PeekUITKHandler() => s_UITKHandler;

        /// <summary>
        /// 认领 UI Toolkit 这一轨：把实现类自述交来的那一枚填进本轨的槽、挂上本轨那几条广播，再初始化它。
        /// </summary>
        /// <remarks>
        /// 形参就是 <see cref="UI.UITKHandler"/>：哪一枚实例属于哪一轨由实现类的 <see cref="UIServiceHandler.Internal_Register"/> 自述，
        /// 这里不再有「按类型判轨」那层运行期分派，也没有替配置另造一枚的退路。 <br />
        /// 目录认领与挂钩都排在 <c>Internal_Init</c> 之前：init 抛了会留下「槽里有、关停回调与广播没挂」的静默档，而 <see cref="IsUITKValid"/> 仍回真。
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
        /// 关停不挂广播：本轨在 <see cref="s_UITKTrack"/> 里自报默认档，门面按目录升序收口——这一轨的文档壳挂在 uGUI 那一轨那枚根下， <br />
        /// 必须排在宿主轨销毁那枚根之前收干净，次序由自报档位保证，不由各轨按配置就位的先后保证。
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
        /// 寻址两档与 uGUI 腿同形同序：<see cref="UITKWindow"/> 取模板那一条路本就分 AB 与内置资源两支，<paramref name="assetLocation"/> 为空时 <br />
        /// 仍由协调者按窗口类走那条既有链路（<c>[Window(location)]</c> 优先、缺省回类型名），<paramref name="fromResources"/> 与特性上的那一档并进取法。 <br />
        /// <paramref name="panelSettings"/> 是这一腿比 uGUI 腿多出的那一枚：既是同名两支过 <c>CS0111</c> 的形参表差异，也是窗口级主题/缩放的落点—— <br />
        /// 它不进中性的开栈编排，由本轨包成交接钩子在面板装载之前交给那一只新窗的实例属性；为 <c>null</c> 时该窗回退到共享那一份 <see cref="UITKWindow.SharedPanelSettings"/>。 <br />
        /// 与 uGUI 腿同名而形参表差这一枚 ⇒ 同一形状的实参按窗口类只落进一支（分辨依据是各自的约束，不是形参个数）。
        /// </remarks>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="userData">用户自定义数据。</param>
        public static void ShowUIAsync<T>(string windowName = null, string assetLocation = null, bool fromResources = false,
            PanelSettings panelSettings = null, params object[] userData)
            where T : UITKWindow, new()
        {
            OpenUITKWindow(typeof(T), true, windowName, assetLocation, fromResources, panelSettings, userData);
        }

        /// <summary>
        /// 同步打开窗口（UI Toolkit 腿）。
        /// </summary>
        /// <remarks>
        /// 与异步那一支同形（寻址两档 + 窗口级 <c>PanelSettings</c>），只是面板按同步档装载；<see cref="SYNC_LOAD_USES_ASYNC"/> 那一条平台分档与 uGUI 腿共用。 <br />
        /// 交给协调者的地址为空时，地址判据仍由窗口类走那条既有链路；覆盖为 <c>null</c> 时本窗回共享那一份 <see cref="PanelSettings"/>。
        /// </remarks>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="userData">用户自定义数据。</param>
        public static void ShowUI<T>(string windowName = null, string assetLocation = null, bool fromResources = false,
            PanelSettings panelSettings = null, params object[] userData)
            where T : UITKWindow, new()
        {
            OpenUITKWindow(typeof(T), SYNC_LOAD_USES_ASYNC, windowName, assetLocation, fromResources, panelSettings, userData);
        }

        /// <summary>
        /// 异步打开窗口并等待加载完成（UI Toolkit 腿）。
        /// </summary>
        /// <remarks>
        /// 与另外两支同形：<c>await</c> 只等「面板就绪」那一段，寻址与窗口级 <c>PanelSettings</c> 的交法都排在装载之前，与等待腿的续体跨不跨帧无关。 <br />
        /// 覆盖为 <c>null</c> 时本窗回共享那一份 <see cref="PanelSettings"/>；两者都缺位时装载当场报一条 Error 并拒开（判据在 <see cref="UITKWindow"/>）。
        /// </remarks>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时回共享那一份。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public static async UniTask<UIWindow> ShowUIAsyncAwait<T>(string windowName = null, string assetLocation = null, bool fromResources = false,
            PanelSettings panelSettings = null, params object[] userData)
            where T : UITKWindow, new()
        {
            return await OpenUITKWindowAwait(typeof(T), true, windowName, assetLocation, fromResources, panelSettings, userData);
        }

        /// <summary>
        /// Type 形入口落到本轨的开窗实现：没有窗口级 <c>PanelSettings</c> 那一档——那枚形参是泛型腿同名两支的分辨处，Type 入口各轨共用一张形参表。
        /// </summary>
        /// <remarks>
        /// 目录分派只交中性形参表（类型、同步档、寻址两档与 userData），本轨自己那枚窗口级配置由泛型腿给、这一条路不给——不给时装载回共享那一份。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        // ReSharper disable once InconsistentNaming
        private static void OpenUITKWindowForTypeEntry(Type type, bool isAsync, string windowName, string assetLocation, bool fromResources,
            object[] userData)
        {
            OpenUITKWindow(type, isAsync, windowName, assetLocation, fromResources, null, userData);
        }

        /// <summary>
        /// UI Toolkit 轨的开窗实现：本文件的三支与 <see cref="System.Type"/> 入口的 UI Toolkit 档都落在这里，窗口经协调者那一份共享栈压栈。
        /// </summary>
        /// <remarks>
        /// 寻址两档原样交给共享编排：为空时那条链路照旧按窗口类答地址，这一轨不再写死空与 AB 口径。 <br />
        /// <paramref name="panelSettings"/> 经 <see cref="HandoffPanelSettings"/> 包成本轨自己的交接钩子交下去，中性编排只认 <see cref="UIWindow"/>。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时不交接。</param>
        /// <param name="userData">用户自定义数据。</param>
        // ReSharper disable once InconsistentNaming
        private static void OpenUITKWindow(Type type, bool isAsync, string windowName, string assetLocation, bool fromResources,
            PanelSettings panelSettings, object[] userData)
        {
            UITKHandler.ShowUIImp(type, isAsync, windowName, assetLocation, fromResources, HandoffPanelSettings(panelSettings), userData);
        }

        /// <summary>
        /// UI Toolkit 轨的等待腿实现：与同步那一枚同一份共享栈，另把面板就绪等出来。
        /// </summary>
        /// <remarks>同上：交接钩子排在装载之前，等出来的那只窗带着本窗那一枚覆盖（或空，回共享那一份）。</remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="panelSettings">本窗口自己的 <see cref="PanelSettings"/>；为空时不交接。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        // ReSharper disable once InconsistentNaming
        private static UniTask<UIWindow> OpenUITKWindowAwait(Type type, bool isAsync, string windowName, string assetLocation, bool fromResources,
            PanelSettings panelSettings, object[] userData)
        {
            return UITKHandler.ShowUIAwaitImp(type, isAsync, windowName, assetLocation, fromResources, HandoffPanelSettings(panelSettings), userData);
        }

        /// <summary>
        /// 把窗口级 <see cref="PanelSettings"/> 包成一枚只认 <see cref="UIWindow"/> 的交接钩子：为空的场合一枚委托都不建。
        /// </summary>
        /// <remarks>
        /// 钩子里才认 <see cref="UITKWindow"/>：后端类型因此只住在这一轨的文件里，中性的 <see cref="UIWindowLedger"/> 与 <see cref="UIServiceHandler"/> 只收下这个形参。 <br />
        /// 强转不是漏判：认轨守卫（<c>RequireOwningTrack</c>）已在入口把不落本轨的类型否掉，泛型腿的约束更把这一条写在编译期；落在别人身上的赋值会当场抛，不静默落空。
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
