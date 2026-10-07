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
    /// 中性外壳（生命周期、层级常量、属性、安全区、<see cref="System.Type"/> 形入口与共享栈上的关·隐·取）住在 <c>UIService.cs</c>，UI Toolkit 腿的三支住在 <c>UIService.UITK.cs</c>。 <br />
    /// 本文件还管着 uGUI 这一轨的槽位、认领门、取用属性、<see cref="IsUGUIValid"/> 与轨道自述——槽位与处理器位随各自的腿走，自述经静态初始化登记进门面目录。 <br />
    /// 本文件的三支各自叫下面那一枚 uGUI 轨自己的开窗实现，实现经 <see cref="UIService.UGUIHandler"/> 把窗口落进共享栈——没有「交给默认实现」那一条路。 <br />
    /// 形参表带着 <c>assetLocation</c> 与 <c>fromResources</c> 这两档 prefab 取法，UI Toolkit 腿自 R10 起同样带着它们；两支同名的差异只剩本轨不收的那一枚 <c>panelSettings</c>。两支同名，分辨同形实参的依据是各自的泛型约束。 <br />
    /// 类级属性与基类清单只在主声明上，本文件是同一枚类的另一份声明；本文件的形参表与约束就是这一腿的契约——改它要先红签名快照用例。
    /// </remarks>
    partial class UIService
    {
        /// <summary>uGUI 轨那一枚驱动者未就位时为假；只看本轨这一支。</summary>
        public static bool IsUGUIValid => s_UGUIHandler != null;

        private static volatile UGUIHandler s_UGUIHandler;

        /// <summary>uGUI 轨在门面目录里的自述：认窗判据、有效性探针、Type 形入口的开窗实现与宿主档关停位。</summary>
        /// <remarks>
        /// 静态初始化器自登记，主文件只枚举目录：本轨的窗口基类、槽位与开窗实现都在本文件自述，加第三轨＝加一枚同款 partial，主文件零改动。 <br />
        /// 关停取宿主档（<see cref="UITrack.SHUTDOWN_ORDER_HOST"/>）：这一轨那枚根是别轨那些文档壳的父级，门面按目录升序收口时它最后收。
        /// </remarks>
        private static readonly UITrack s_UGUITrack = Internal_RegisterTrack(new UITrack(
            "UGUI", typeof(UGUIWindow), UITrack.SHUTDOWN_ORDER_HOST,
            IsOnUGUITrack, IsUGUIValidProbe, OpenUGUIWindow));

        /// <summary>uGUI 轨的有效性探针：只读本轨这一枚槽位，目录不另记一份在位状态。</summary>
        private static bool IsUGUIValidProbe() => s_UGUIHandler != null;

        /// <summary>uGUI 轨的认窗判据：<see cref="UGUIWindow"/> 及其派生类落在 uGUI 轨上。</summary>
        /// <param name="windowType">窗口类型。</param>
        /// <returns>落在 uGUI 轨上时为真。</returns>
        private static bool IsOnUGUITrack(Type windowType) => typeof(UGUIWindow).IsAssignableFrom(windowType);

        /// <summary>
        /// uGUI 轨那枚<b>已启用</b>的驱动者：这一轨没启用时当场抬错，不静默落空、也不替本轨造一枚。
        /// </summary>
        /// <remarks>
        /// 槽位的来路只有 <see cref="OnInit"/> 按 <see cref="UIServiceSettings"/> 启用清单那一条：门面上既没有换入接缝， <br />
        /// 也没有「用到才 new」的隐式启用——取用不替配置造驱动者，未启用就是未启用。 <br />
        /// 文案点名是哪一轨、去哪一处启用；uGUI 腿的三支与 <see cref="System.Type"/> 入口的 uGUI 档都经这一枚取用。 <br />
        /// 轨专有查询走的是另一条分支：<see cref="UIRoot"/> 与 <see cref="UICamera"/> 读的是槽位本身，未启用那一轨时答 <c>null</c> 而不抬错。
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

        /// <summary>读 uGUI 轨那枚处理器槽：不叫认领门，也不产生任何副作用。</summary>
        internal static UGUIHandler Internal_PeekUGUIHandler() => s_UGUIHandler;

        /// <summary>
        /// 认领 uGUI 这一轨：把实现类自述交来的那一枚填进本轨的槽、挂上本轨那几条广播，再初始化它。
        /// </summary>
        /// <remarks>
        /// 形参就是 <see cref="UI.UGUIHandler"/>：哪一枚实例属于哪一轨由实现类的 <see cref="UIServiceHandler.Internal_Register"/> 自述，
        /// 这里不再有「按类型判轨」那层运行期分派，也没有替配置另造一枚的退路。 <br />
        /// 目录认领与挂钩都排在 <c>Internal_Init</c> 之前：init 抛了会留下「槽里有、关停回调与广播没挂」的静默档，而 <see cref="IsUGUIValid"/> 仍回真。
        /// </remarks>
        /// <param name="handler">自述归属到本轨的那一枚驱动者。</param>
        /// <exception cref="GameException">本轨已经有驱动者在位（同一轨的第二个注入项）。</exception>
        internal static void Internal_ClaimUGUITrack(UGUIHandler handler)
        {
            // 同一枚实例再注册一次是空操作：OnInit 可重入（那一档有格钉着），要否掉的是「换第二枚进来」；
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

        /// <summary>摘掉本轨那枚处理器槽：只清位，不跑关停，也不碰广播——广播由门面整批摘。</summary>
        private static void Internal_DetachUGUITrack()
        {
            Interlocked.Exchange(ref s_UGUIHandler, null);
        }

        /// <summary>
        /// 这一轨在门面上的全部挂钩：三条按支广播与一段摘槽。
        /// </summary>
        /// <remarks>
        /// 关停不挂广播：本轨在 <see cref="s_UGUITrack"/> 里自报宿主档，门面按目录升序收口——这一轨那枚根是别轨那些文档壳的父级，它必须最后收， <br />
        /// 次序由自报档位保证，不由各轨按配置就位的先后保证。
        /// </remarks>
        /// <param name="handler">按启用清单刚造出来的那一枚驱动者。</param>
        private static void Internal_SubscribeUGUITrack(UGUIHandler handler)
        {
            onTrackTick += handler.Tick;
            onScreenSafeArea += handler.ApplyScreenSafeRect;
            onNotchSimulate += handler.SimulateIPhoneXNotchScreen;
            onDetachTrackSlots += Internal_DetachUGUITrack;
        }

        /// <summary>UI根节点：uGUI 轨的资源只由这一轨答，UI Toolkit 那一枚答不出这两个。</summary>
        public static Transform UIRoot => s_UGUIHandler?.UIRoot;

        /// <summary>UI专用摄像机——同 <see cref="UIRoot"/>：UI Toolkit 轨没有本轨专用的摄像机。</summary>
        public static Camera UICamera => s_UGUIHandler?.UICamera;
        
        #region 显示窗口（uGUI 腿） [SHOW WINDOW UGUI]

        /// <summary>
        /// 异步打开窗口（uGUI 腿）。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public static void ShowUIAsync<T>(string windowName = null, string assetLocation = null, bool fromResources = false, params object[] userData)
            where T : UGUIWindow, new()
        {
            OpenUGUIWindow(typeof(T), true, windowName, assetLocation, fromResources, userData);
        }

        /// <summary>
        /// 同步打开窗口（uGUI 腿）。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        public static void ShowUI<T>(string windowName = null, string assetLocation = null, bool fromResources = false, params object[] userData)
            where T : UGUIWindow, new()
        {
            OpenUGUIWindow(typeof(T), SYNC_LOAD_USES_ASYNC, windowName, assetLocation, fromResources, userData);
        }

        /// <summary>
        /// 异步打开窗口并等待加载完成（uGUI 腿）。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public static async UniTask<UIWindow> ShowUIAsyncAwait<T>(string windowName = null, string assetLocation = null, bool fromResources = false, params object[] userData)
            where T : UGUIWindow, new()
        {
            return await OpenUGUIWindowAwait(typeof(T), true, windowName, assetLocation, fromResources, userData);
        }

        /// <summary>
        /// uGUI 轨的开窗实现：本文件的三支与 <see cref="System.Type"/> 入口的 uGUI 档都落在这里，窗口经协调者那一份共享栈压栈。
        /// </summary>
        /// <remarks>
        /// 交接钩子交 <c>null</c>：uGUI 轨没有「装载之前先交给实例一枚后端配置」这一档，<c>Canvas</c> 与排序画布都随面板本体一起装出来。
        /// </remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        private static void OpenUGUIWindow(Type type, bool isAsync, string windowName, string assetLocation, bool fromResources, object[] userData)
        {
            UGUIHandler.ShowUIImp(type, isAsync, windowName, assetLocation, fromResources, null, userData);
        }

        /// <summary>
        /// uGUI 轨的等待腿实现：与同步那一枚同一份共享栈，另把面板就绪等出来。
        /// </summary>
        /// <remarks>同上：本轨没有要在装载前交给实例的那一枚配置。</remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        private static UniTask<UIWindow> OpenUGUIWindowAwait(Type type, bool isAsync, string windowName, string assetLocation, bool fromResources, object[] userData)
        {
            return UGUIHandler.ShowUIAwaitImp(type, isAsync, windowName, assetLocation, fromResources, null, userData);
        }

        #endregion
    }
}
