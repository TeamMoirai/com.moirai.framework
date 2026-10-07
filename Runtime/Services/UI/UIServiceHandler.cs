using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 协调者：两支后端驱动者的公共基类，把窗口栈、停放表与交互租约落在同一份共享存储上。
    /// </summary>
    /// <remarks>
    /// 栈本体、编排与查询住在 <see cref="UIWindowLedger"/>，各轨处理器只是转发口，两支后端因此并存于同一份栈。<br />
    /// 面板本体（根节点、摄像机、装载与拾取）住在各后端的派生处理器里，本类经 <see cref="UIRoot"/>、<see cref="UICamera"/> 取用。<br />
    /// 具体类型记在 <see cref="UIServiceSettings"/> 的启用清单里，由 <c>OnInit</c> 逐条叫 <see cref="Internal_Register"/> 认领。<br />
    /// 新增一支后端：一枚派生实现类、一条认领门、一枚 <c>UIService.&lt;轨&gt;.cs</c> partial 文件。
    /// </remarks>
    [Serializable]
    public abstract class UIServiceHandler : FrameworkHandler
    {
        /// <summary>两支共用的那一份窗口栈与停放表：每次取用现读门面那一位，不得缓存引用。</summary>
        private UIWindowLedger Ledger => UIService.SharedLedger;

        /// <summary>UI根节点。</summary>
        public abstract Transform UIRoot { get; }

        /// <summary>UI专用摄像机。</summary>
        public abstract Camera UICamera { get; }

        /// <summary>当前模态遮挡窗口。</summary>
        public virtual UIWindow CurrentModal => Ledger.CurrentModal;

        /// <summary>
        /// 判断窗口是否为模态窗口。
        /// </summary>
        public virtual bool IsModal(UIWindow window) => Ledger.IsModal(window);

        /// <summary>模态动画期间交互压制的归属仲裁。两支后端共用这一份，与窗口堆栈同生命周期。</summary>
        internal UIInteractionLease InteractionLease => Ledger.InteractionLease;

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 本轨专有的每帧职责。
        /// </summary>
        /// <remarks>
        /// 整条共享栈的结算由门面每帧叫一次（<see cref="UIService.Tick"/>）；覆写里再叫一次持有者的 <c>Tick</c> 就是每帧跑两遍。<br />
        /// uGUI 那一轨交出的是 UI 根的续等，UI Toolkit 那一轨当前没有帧职责。<br />
        /// 形参供需要按帧时长行事的轨使用，本轨没有帧时长可吃时不必用它。
        /// </remarks>
        /// <param name="elapseSeconds">逻辑经过的秒数。</param>
        /// <param name="realElapseSeconds">真实经过的秒数。</param>
        public virtual void Tick(float elapseSeconds, float realElapseSeconds)
        {
        }

        #endregion

        #region 设置安全区域 [SET SAFE AREA]

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）：把安全区落到本轨的面板上。
        /// </summary>
        /// <remarks>
        /// 把安全区落到面板上是各轨自己的事，两支的换算形状不同。<br />
        /// 安全区矩形本身的换算由 <see cref="ComputeIPhoneXNotchSafeRect"/> 这一份共享实现给出，各轨不必复制第二份。
        /// </remarks>
        /// <param name="safeRect">安全区域。</param>
        public abstract void ApplyScreenSafeRect(Rect safeRect);

        /// <summary>
        /// 模拟 IPhoneX 异形屏：取共享的刘海安全区，交回本轨的 <see cref="ApplyScreenSafeRect"/> 落到面板上。
        /// </summary>
        public virtual void SimulateIPhoneXNotchScreen()
        {
            ApplyScreenSafeRect(ComputeIPhoneXNotchSafeRect(Screen.width, Screen.height));
        }

        /// <summary>
        /// 计算模拟异形屏的安全区矩形（绝对像素，原点为左下角），两支后端与协调者共用这一份换算。
        /// </summary>
        /// <param name="screenWidth">屏幕宽度（像素）。</param>
        /// <param name="screenHeight">屏幕高度（像素）。</param>
        /// <returns>交给 <see cref="ApplyScreenSafeRect"/> 的安全区域。</returns>
        internal static Rect ComputeIPhoneXNotchSafeRect(int screenWidth, int screenHeight)
        {
            Rect rect;
            if (screenHeight > screenWidth)
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

            return new Rect(screenWidth * rect.x, screenHeight * rect.y, screenWidth * rect.width, screenHeight * rect.height);
        }

        #endregion

        #region 窗口查询 [WINDOW QUERIES]

        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        public virtual UIWindow GetTopWindow() => Ledger.GetTopWindow();

        /// <summary>
        /// 获取指定层级下顶部的窗口名称。
        /// </summary>
        public virtual string GetTopWindowName(int layer) => Ledger.GetTopWindowName(layer);

        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        public virtual UIWindow GetTopWindow(int layer) => Ledger.GetTopWindow(layer);

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        public virtual bool IsAnyLoading() => Ledger.IsAnyLoading();

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <typeparam name="T">界面类型。</typeparam>
        /// <param name="windowName">窗口名称。</param>
        /// <returns>是否存在。</returns>
        public virtual bool HasWindow<T>(string windowName = null) where T : UIWindow => Ledger.HasWindow<T>(windowName);

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <returns>是否存在。</returns>
        public virtual bool HasWindow(Type type, string windowName = null) => Ledger.HasWindow(type, windowName);

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        public virtual T GetWindow<T>(string windowName) where T : UIWindow => Ledger.GetWindow<T>(windowName);

        /// <summary>
        /// 判断是否被模态窗口遮挡。
        /// </summary>
        public virtual bool IsBlockedByModal(GameObject obj) => Ledger.IsBlockedByModal(obj);

        /// <summary>
        /// 按窗口名取栈上的窗口：栈上没有同名窗口时回 null。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <returns>栈上那一只同名窗口。</returns>
        protected UIWindow GetWindow(string windowName) => Ledger.GetWindow(windowName);

        /// <summary>
        /// 查询窗口名称是否已在栈上（<c>windowName</c> 由调用方给全，未命名窗口取类型全名的规则在调用方一侧）。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <returns>栈上有同名窗口时为真。</returns>
        protected bool IsContains(string windowName) => Ledger.IsContains(windowName);

        #endregion

        #region 显示窗口 [SHOW WINDOW]
        /// <summary>
        /// 开栈编排的同步腿：认名→复用栈上那一只 / 取回停放的那一只 / 造一只新的，然后压栈并发起装载。
        /// </summary>
        /// <remarks>
        /// 编排本体住在 <see cref="UIWindowLedger"/>，本类只接转发：形参含义与交接时机以它为准。<br />
        /// 本轨专有的配置由开窗腿包成钩子交进来，后端类型不落进共享编排的签名。
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
            Ledger.ShowUIImp(type, isAsync, windowName, assetLocation, fromResources, onInstanceCreated, userData);
        }

        /// <summary>
        /// 开栈编排的等待腿：与同步腿同一份栈、同一次压入，另把「面板就绪」等出来再交回窗口。
        /// </summary>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="onInstanceCreated">新实例装载前的交接钩子；不需要交接时为 null。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>栈上那一只窗口。</returns>
        internal async UniTask<UIWindow> ShowUIAwaitImp(Type type, bool isAsync, string windowName, string assetLocation, bool fromResources,
            Action<UIWindow> onInstanceCreated, params object[] userData)
        {
            return await Ledger.ShowUIAwaitImp(type, isAsync, windowName, assetLocation, fromResources, onInstanceCreated, userData);
        }

        /// <summary>
        /// 开栈编排的结果腿：与等待腿同一份栈、同一次压入，把「就绪/失败/超时」等成结果交回。
        /// </summary>
        /// <remarks>编排本体住在 <see cref="UIWindowLedger"/>，本类只接转发：形参含义与状态语义以它为准。</remarks>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">从 Resources 加载资源。</param>
        /// <param name="onInstanceCreated">新实例装载前的交接钩子；不需要交接时为 null。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>开窗结果。</returns>
        internal UniTask<UIOpenResult> ShowUIAwaitResultImp(Type type, bool isAsync, string windowName, string assetLocation, bool fromResources,
            Action<UIWindow> onInstanceCreated, params object[] userData)
        {
            return Ledger.ShowUIAwaitResultImp(type, isAsync, windowName, assetLocation, fromResources, onInstanceCreated, userData);
        }

        #endregion

        #region 关闭窗口 [CLOSE WINDOW]

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        public virtual void CloseUI<T>(string windowName = null) where T : UIWindow
        {
            Ledger.CloseUI<T>(windowName);
        }

        public virtual void CloseUI(Type type, string windowName = null)
        {
            Ledger.CloseUI(type, windowName);
        }

        public virtual void HideUI<T>(string windowName = null) where T : UIWindow
        {
            Ledger.HideUI<T>(windowName);
        }

        public virtual void HideUI(Type type, string windowName = null)
        {
            Ledger.HideUI(type, windowName);
        }

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        public virtual void CloseAll(bool isShutDown = false)
        {
            Ledger.CloseAll(isShutDown);
        }

        /// <summary>
        /// 本轨认窗判据：一次关停里只有被本轨认得的窗才交进共享栈的关闭流程，另一轨的窗留在栈上由它自己那一轨去收。
        /// </summary>
        /// <remarks>
        /// 抽象且无默认实现：栈是两支共用的，缺判据的派生者会把另一轨的窗一并清空。<br />
        /// 两支内建处理器各自认自己的窗口基类。
        /// </remarks>
        /// <param name="window">栈上待判的那一只。</param>
        /// <returns>属于本轨时为真。</returns>
        protected abstract bool IsWindowOnOwnTrack(UIWindow window);

        /// <summary>
        /// 关掉本轨那一半的窗：走的仍是那一条共享栈，只挑本轨认得的那些。
        /// </summary>
        /// <param name="isShutDown">关停轮：连缓存窗也一并销毁，不进停放表。</param>
        protected void CloseOwnTrackWindows(bool isShutDown)
        {
            Ledger.CloseAllWhere(isShutDown, IsWindowOnOwnTrack);
        }

        /// <summary>
        /// 把这一枚驱动者注册进它自己那一轨的门面槽：归属由实现类自述。
        /// </summary>
        /// <remarks>
        /// 由 <see cref="UIService.OnInit"/> 按 <see cref="UIServiceSettings.EnabledHandlers"/> 逐支调用。<br />
        /// 同一轨再来第二枚不同实例时抬错，不静默换掉在位的那一枚。<br />
        /// 同一枚实例重复注册是空操作（<c>OnInit</c> 可重入）。
        /// </remarks>
        /// <exception cref="GameException">本轨已经有驱动者在位。</exception>
        internal abstract void Internal_Register();

        /// <summary>
        /// 关闭所有窗口除了指定窗口。
        /// </summary>
        public virtual void CloseAllWithOut(UIWindow withOut)
        {
            Ledger.CloseAllWithOut(withOut);
        }

        /// <summary>
        /// 关闭所有窗口除了指定类型的窗口。
        /// </summary>
        public virtual void CloseAllWithOut<T>() where T : UIWindow
        {
            Ledger.CloseAllWithOut<T>();
        }

        /// <summary>
        /// 关闭所有窗口除了指定层级的窗口。
        /// </summary>
        public virtual void CloseAllWithOut(UILayer withOut)
        {
            Ledger.CloseAllWithOut(withOut);
        }

        #endregion

        #region 异步获取窗口 [GET WINDOW ASYNC]

        /// <summary>
        /// 异步获取窗口：本枚处理器只是转发口，等的是那条共享栈上的那一只。
        /// </summary>
        /// <returns>打开窗口操作句柄。</returns>
        public virtual async UniTask<T> GetUIAsyncAwait<T>() where T : UIWindow
        {
            return await Ledger.GetUIAsyncAwait<T>();
        }

        /// <summary>
        /// 异步获取窗口：同上，本枚处理器只是转发口。
        /// </summary>
        /// <param name="callback">回调。</param>
        public virtual void GetUIAsync<T>(Action<T> callback) where T : UIWindow
        {
            Ledger.GetUIAsync(callback);
        }

        #endregion

        #region 窗口堆栈 [WINDOW STACK]

        /// <summary>
        /// 窗口面板就绪：补建窗口、按层级重排深度、重发显隐回执。
        /// </summary>
        /// <param name="window">面板装载完成并进入准备态的窗口。</param>
        protected void OnWindowPrepare(UIWindow window)
        {
            Ledger.OnWindowPrepare(window);
        }

        /// <summary>
        /// 把窗口压入堆栈：按所属层级定位插入点，模态窗口压掉下层窗口的可交互位，末尾发一次打开回执。
        /// </summary>
        /// <param name="window">待压入的窗口。</param>
        protected void Push(UIWindow window)
        {
            Ledger.Push(window);
        }

        /// <summary>
        /// 把窗口移出堆栈并发一次关闭回执。
        /// </summary>
        /// <param name="window">待移出的窗口。</param>
        protected void Pop(UIWindow window)
        {
            Ledger.Pop(window);
        }

        #endregion

        #region 内部门缝 [INTERNAL SEAMS]

        /// <summary>
        /// 现读本枚处理器此刻用的那份共享持有者。
        /// </summary>
        /// <returns>门面当前那一份 <see cref="UIWindowLedger"/>。</returns>
        internal UIWindowLedger Internal_PeekLedger() => Ledger;

        /// <summary>
        /// 栈上窗口的只读视图：栈本体住在持有者里，这一道门只给读、不给写。
        /// </summary>
        /// <returns>当前栈序的那一份真值（不是拷贝）。</returns>
        internal IReadOnlyList<UIWindow> Internal_PeekStack() => Ledger.PeekStack();

        /// <summary>
        /// 停放表里是否有这个名字的窗：缓存实例关闭后落在这里，栈上已无。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <returns>停放表命中时为真。</returns>
        internal bool Internal_IsParked(string windowName) => Ledger.IsParked(windowName);

        #endregion
    }
}
