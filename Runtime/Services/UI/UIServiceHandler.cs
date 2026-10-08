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

        /// <summary>模态动画期间交互压制的归属仲裁。两支后端共用这一份，与窗口堆栈同生命周期。</summary>
        internal UIInteractionLease InteractionLease => UIService.SharedLedger.InteractionLease;

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
            UIService.SharedLedger.ShowUIImp(type, isAsync, windowName, assetLocation, fromResources, onInstanceCreated, userData);
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
            return await UIService.SharedLedger.ShowUIAwaitImp(type, isAsync, windowName, assetLocation, fromResources, onInstanceCreated, userData);
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
            return UIService.SharedLedger.ShowUIAwaitResultImp(type, isAsync, windowName, assetLocation, fromResources, onInstanceCreated, userData);
        }

        #endregion

        #region 关闭窗口 [CLOSE WINDOW]

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
            UIService.SharedLedger.CloseAllWhere(isShutDown, IsWindowOnOwnTrack);
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
