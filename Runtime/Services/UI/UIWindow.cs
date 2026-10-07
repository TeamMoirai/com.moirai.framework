using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Input;
using Moirai.Atropos.Timer;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 窗口对象模型：身份、生命周期与显隐/深度/交互的<b>意图位</b>，不含任何渲染后端类型。
    /// </summary>
    /// <remarks>
    /// 面板本体住在各后端的派生基类里（uGUI 轨见 <see cref="UGUIWindow"/>），本类经七枚 <c>virtual</c> 钩子交接：<br />
    /// 装载走 <c>LoadPanel(Async)</c>，落意图走三枚 <c>Apply*</c>，收面板走 <c>ParkPanel</c> / <c>DestroyPanel</c>。<br />
    /// 默认实现什么都不做：未挂后端基类的窗口不崩，但也开不出来。<br />
    /// 显隐/深度/交互以意图位为准，同值二次赋值不重复落钩子。<br />
    /// 自关（<see cref="Close"/>）一律等可交互之后过 <see cref="CanClose"/> 门再结算。<br />
    /// 线程契约：仅主线程。
    /// </remarks>
    public abstract partial class UIWindow : UIBase
    {
        #region 属性 [PROPERTIES]

        private bool _isCreate = false;
        private Action<UIWindow> _prepareCallback;
        // 交互/可见性交接代次：每次状态转移（打开/关闭/重开/销毁）递增，只有最新一轮的续体可以交还交互锁与隐藏窗口
        private uint _interactionLifetime;

        protected CancellationTokenSource _cts;

        public override UIType Type => UIType.Window;

        /// <summary>窗口名称。</summary>
        public string WindowName { get; private set; }

        /// <summary>窗口层级。</summary>
        public int WindowLayer { get; private set; }

        /// <summary>资源定位地址。</summary>
        public string AssetLocation { get; private set; }

        /// <summary>是否为全屏窗口。</summary>
        /// <remarks>将全屏下层的UI设为隐藏</remarks>
        public bool FullScreen { get; private set; }

        /// <summary>是内部资源无需AB加载。</summary>
        public bool FromResources { get; private set; }
        
        /// <summary>隐藏窗口关闭时间。</summary>
        public int HideTimeToClose { get; internal set; }

        /// <summary>隐藏转关闭的定时器标识。</summary>
        public ulong HideTimerId { get; internal set; }

        /// <summary>缓存实例，关闭时不销毁。</summary>
        public bool CacheInstance { get; internal set; }

        private int _depth;
        /// <summary>窗口深度值（意图）。</summary>
        /// <remarks>
        /// 同值二次赋值不落钩子、不刷排序；落地由 <see cref="ApplyDepth"/> 负责，子级偏移的差分属后端内部事实。<br />
        /// 回读给的是意图值，不是面板上的实际排序值——后端会按自己的序空间截断或归整。
        /// </remarks>
        public int Depth
        {
            get => _depth;

            set
            {
                if (_depth == value)
                {
                    return;
                }

                _depth = value;

                // 面板落地（后端自己的事：父级取绝对值、子级按各自偏移一同平移）
                ApplyDepth(value);

                // 虚函数
                if (Visible)
                {
                    Internal_OnSortDepth();
                }
                else
                {
                    _isSortingOrderDirty = true;
                }
            }
        }

        private bool _visible;
        /// <summary>窗口可见性（意图）。</summary>
        /// <remarks>
        /// getter 回调用方要的值，不回读面板所在的 Unity layer（外部改层不算契约）。<br />
        /// 切 layer 由 <see cref="ApplyVisible"/> 落地；<see cref="UIBase.OnSetVisible"/> 与排序脏位的结算仍归本类。
        /// </remarks>
        public bool Visible
        {
            get => _visible;

            set
            {
                if (_visible == value)
                {
                    return;
                }

                _visible = value;

                // 面板落地
                ApplyVisible(value);

                if (value && _isCreate)
                {
                    _isSortingOrderDirty = false;
                    Internal_OnSortDepth();
                }

                // LogUtility.Info("[UI] Set '{0}' Visible {1}", WindowName, value);

                // 虚函数
                if (_isCreate)
                {
                    OnSetVisible(value);
                }
            }
        }

        private bool _interactable;
        /// <summary>窗口交互性（意图）。</summary>
        /// <remarks>同值不重复落钩子；把 <c>enabled</c> 推给面板拾取器的动作在 <see cref="ApplyInteractable"/>。</remarks>
        public bool Interactable
        {
            get => _interactable;

            set
            {
                if (_interactable == value) return;

                // LogUtility.Info("{0}'s Interactable: {1}", WindowName, value);
                _interactable = value;
                ApplyInteractable(value);
            }
        }

        /// <summary>是否加载完毕。</summary>
        /// <remarks>装载路径私有写：装载完成置位，回滚不回落（失败窗走 <see cref="IsLoadFailed"/> + <see cref="IsDestroyed"/>）。</remarks>
        internal bool IsLoadDone { get; private set; }

        /// <summary>是否被销毁。</summary>
        /// <remarks>销毁路径私有写：显式关闭与装载失败作废两个来路。</remarks>
        internal bool IsDestroyed { get; private set; }

        /// <summary>UI是否隐藏标志位。</summary>
        public bool IsHide { get; internal set; }

        #endregion

        /// <summary>
        /// 按描述符各档初始化窗口身份与配置。
        /// </summary>
        /// <remarks>internal：开窗调用方（注册表链路）与测试接缝专用，游戏代码经门面开窗不直接初始化。</remarks>
        /// <param name="name">窗口名称。</param>
        /// <param name="layer">窗口层级。</param>
        /// <param name="fullScreen">是否为全屏窗口。</param>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">是内部资源无需AB加载。</param>
        /// <param name="hideTimeToClose">隐藏后转关闭的秒数。</param>
        /// <param name="cacheInstance">缓存实例，关闭时不销毁。</param>
        internal void Init(string name, int layer, bool fullScreen, string assetLocation, bool fromResources, int hideTimeToClose, bool cacheInstance)
        {
            WindowName = name;
            WindowLayer = layer;
            FullScreen = fullScreen;
            AssetLocation = assetLocation;
            FromResources = fromResources;
            HideTimeToClose = hideTimeToClose;
            CacheInstance = cacheInstance;
        }

        internal void TryInvoke(Action<UIWindow> prepareCallback, System.Object[] @params)
        {
            CancelHideToCloseTimer();
            _params = @params;
            if (IsPrepare)
            {
                prepareCallback?.Invoke(this);
            }
            else
            {
                _prepareCallback = prepareCallback;
            }
        }

        /// <summary>装载期的取消源：装载在途时窗口被关闭（<see cref="InternalDestroy"/>）即掐断资源装载。</summary>
        private CancellationTokenSource _loadCts;

        /// <summary>装载失败位：面板装载回 false 或抛出后置位，等待腿据此判 <see cref="EUIOpenStatus.Failed"/>。</summary>
        internal bool IsLoadFailed { get; private set; }

        internal async UniTaskVoid InternalLoad(string location, Action<UIWindow> prepareCallback, bool isAsync, System.Object[] @params)
        {
            _prepareCallback = prepareCallback;
            _params = @params;

            var loadCts = UICtsPool.Rent();
            _loadCts = loadCts;

            // 装载面板是后端的活：本类只认「装上没有」，装上之后统一把三份意图落到新面板上
            try
            {
                bool loaded;
                try
                {
                    loaded = isAsync
                        ? await LoadPanelAsync(location, FromResources, loadCts.Token)
                        : LoadPanel(location, FromResources);
                }
                catch (OperationCanceledException)
                {
                    // 装载被取消：装载期显式关闭或关停掐断了取消源，不算错误，静默收口
                    RollbackFailedLoad();
                    return;
                }
                catch (Exception ex)
                {
                    LogUtility.Error("UI 窗口 '{0}' 装载面板 {1} 抛出异常：{2}", WindowName, location, ex);
                    RollbackFailedLoad();
                    return;
                }

                if (!loaded)
                {
                    if (!loadCts.IsCancellationRequested)
                    {
                        LogUtility.Error("UI 窗口 '{0}' 装载面板 {1} 失败：已从栈上回滚", WindowName, location);
                    }
                    RollbackFailedLoad();
                    return;
                }

                PanelLoaded();
            }
            finally
            {
                if (ReferenceEquals(_loadCts, loadCts))
                {
                    _loadCts = null;
                }
                // 取消过的源由池内废弃，干净的回池复用
                UICtsPool.Return(loadCts);
            }
        }

        /// <summary>
        /// 装载失败的收口：门面还有驱动者在位时把窗口交回共享栈回滚（摘栈、补深度与显隐、发关闭回执），否则只作废本窗。
        /// </summary>
        /// <remarks>
        /// 回叫侧守卫与 <see cref="Hide"/>/<see cref="Close"/> 同一道：关停摘干净各轨之后不再动那条栈。<br />
        /// 失败收口不触发 <see cref="OnDestroy"/>：本窗从未到过 <see cref="OnCreate"/>，不得凭空补一次销毁回执。
        /// </remarks>
        private void RollbackFailedLoad()
        {
            if (UIService.IsValid)
            {
                UIService.SharedLedger.RollbackFailedLoad(this);
            }
            else
            {
                AbortFailedLoad();
            }
        }

        /// <summary>
        /// 作废装载失败的窗口：置失败位与销毁位、撤准备回调、防御性收走可能半绑定的面板。
        /// </summary>
        internal void AbortFailedLoad()
        {
            IsLoadFailed = true;
            IsDestroyed = true;
            _prepareCallback = null;
            DestroyPanel();
        }

        /// <summary>掐断装载期取消源：装载在途的窗口被关闭时，资源装载随之取消。</summary>
        private void CancelLoadCts()
        {
            _loadCts?.Cancel();
        }

        /// <summary>
        /// 等面板装载到终态：就绪回真；装载失败或窗口已被销毁回假。
        /// </summary>
        /// <remarks>
        /// 实例方法轮询、无闭包分配；终态先于首帧检查，已就绪/已失败的窗口同帧落定。<br />
        /// 超时落在 <see cref="OperationCanceledException"/>，由调用方归为超时档。
        /// </remarks>
        /// <param name="ct">等待方的超时令牌。</param>
        /// <returns>面板就绪时为真。</returns>
        internal async UniTask<bool> WaitPanelReadyAsync(CancellationToken ct)
        {
            while (!IsLoadDone)
            {
                if (IsLoadFailed || IsDestroyed)
                {
                    return false;
                }

                ct.ThrowIfCancellationRequested();
                await UniTask.Yield();
            }

            return true;
        }

        /// <summary>
        /// 面板装载完成：置加载位、撤掉已销毁窗口的面板，然后把三份意图落到刚出现的面板上并通知准备回调。
        /// </summary>
        /// <remarks>
        /// 三份意图必须在 <c>IsPrepare</c> 之前落地：开窗前窗口已被压进栈、并被别的窗口调过 <c>Depth</c>/<c>Visible</c>，这些调用攒在意图位上、装载当场结算。
        /// </remarks>
        private void PanelLoaded()
        {
            IsLoadDone = true;

            if (IsDestroyed)
            {
                // 装载完成前窗口已被关闭：面板留着也没人认，直接收走且不进准备态
                DestroyPanel();
                return;
            }

            ApplyVisible(_visible);
            ApplyDepth(_depth);
            ApplyInteractable(_interactable);

            // 通知UI管理器
            IsPrepare = true;
            _prepareCallback?.Invoke(this);
        }

        /// <summary>
        /// 打开窗口后触发。
        /// </summary>
        internal void InternalCreate()
        {
            // 缓存实例重开时 _isCreate 仍为 true，上一轮的动画续体可能还挂在路上：
            // 先作废其代次并掐掉动画，再交还交互锁定状态，交给本次打开流程重新决策。
            _interactionLifetime++;
            CancelCts();
            UnlockInteraction();

            if (_isCreate == false)
            {
                _isCreate = true;
                Inject();
                ScriptGenerator();
                BindMemberProperty();
                RegisterEvent();
                OnCreate();
            }

            InternalRefresh(true);
            // LogUtility.Info("[UI] Open {0}", WindowName);
        }

        internal void InternalRefresh(bool open)
        {
            SetInteractWaiter(open).Forget();

            // LogUtility.Info("[UI] Refresh {0}", WindowName);
            OnRefresh();
        }

        /// <summary>
        /// 每帧驱动：窗口一侧在就绪门之上再加可见性门，子级与 OnUpdate 的结算共用基类核心。
        /// </summary>
        /// <returns>还要被栈继续驱动时为真。</returns>
        internal override bool Internal_Update()
        {
            if (!IsPrepare || !Visible)
            {
                return false;
            }

            return UpdateCore();
        }

        protected internal virtual void InternalClose()
        {
            OnClose();
            InternalCloseAsync(++_interactionLifetime).Forget();
        }

        private async UniTaskVoid InternalCloseAsync(uint lifetime)
        {
            var transition = Transition;
            if (transition == null)
            {
                // 瞬时关闭：不过渡、不锁交互，面板当场停放
                ParkPanel();
                return;
            }

            CancelCts();
            _cts = UICtsPool.Rent();

            LockInteraction();

            try { await transition.Play(false, _cts.Token); }
            catch (OperationCanceledException) { return; }

            if (IsDestroyed) return;

            // 交还锁与停放都是本轮转移的特权：代次被重开/销毁/新一轮过渡接管后，
            // 子类过渡若没观察 token 走到这里，继续执行会拆掉别人持有的锁、把刚重开的窗口重新隐藏。
            // 被取消的那一轮其锁已由接管方（InternalCreate/InternalDestroy）交还。
            if (lifetime != _interactionLifetime) return;

            UnlockInteraction();

            CancelCts();
            ParkPanel();
        }

        protected internal void InternalDestroy(bool isShutDown = false)
        {
            _isCreate = false;

            UnregisterEvent();
            
            for (int i = 0; i < ChildList.Count; i++)
            {
                var uiChild = ChildList[i];
                uiChild.CallDestroy();
                uiChild.OnDestroyWidget();
            }

            // 注销回调函数
            _prepareCallback = null;

            OnDestroy();

            // 清理交互状态：代次先行作废，在途的打开/关闭续体不得再交还锁或隐藏
            _interactionLifetime++;
            CancelCts();
            CancelLoadCts();
            UnlockInteraction();

            // 销毁面板对象
            if (!isShutDown && CacheInstance)
            {
                ParkPanel();
            }
            else
            {
                DestroyPanel();
            }

            IsDestroyed = true;

            if (!isShutDown)
            {
                CancelHideToCloseTimer();
            }
        }

        #region 面板钩子 [PANEL HOOKS]

        /// <summary>
        /// 装载并装配面板（同步路径）。
        /// </summary>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">是否为内置资源（不走资源包加载）。</param>
        /// <returns>面板装上返回 true；回 false 时窗口停在未就绪态，不置 <see cref="IsLoadDone"/>、不发准备回调。</returns>
        /// <remarks>后端专有语义（uGUI 轨要带排序画布的面板物体，UI Toolkit 轨要挂文档组件）不住在本类，默认实现什么都加载不了。</remarks>
        protected internal virtual bool LoadPanel(string assetLocation, bool fromResources) => false;

        /// <summary>
        /// 装载并装配面板（异步路径）。
        /// </summary>
        /// <param name="assetLocation">资源定位地址。</param>
        /// <param name="fromResources">是否为内置资源（不走资源包加载）。</param>
        /// <param name="ct">装载取消令牌。</param>
        /// <returns>面板装上返回 true。</returns>
        /// <remarks>同步走完要把已完成的任务交回去：调用方在装载之后还有置位与落意图的活要办。</remarks>
        protected internal virtual UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
            UniTask.FromResult(false);

        /// <summary>把 <see cref="Visible"/> 意图落到面板上。</summary>
        protected internal virtual void ApplyVisible(bool value) { }

        /// <summary>把 <see cref="Depth"/> 意图落到面板上。</summary>
        protected internal virtual void ApplyDepth(int value) { }

        /// <summary>把 <see cref="Interactable"/> 意图落到面板上。</summary>
        protected internal virtual void ApplyInteractable(bool value) { }

        /// <summary>停放面板：物体留着但不激活（缓存实例的关闭态与关闭动画结束后的隐藏）。</summary>
        protected internal virtual void ParkPanel() { }

        /// <summary>收走面板：销毁面板物体并断开后端引用。</summary>
        protected internal virtual void DestroyPanel() { }

        #endregion

        #region 交互相关 [INTERACTION]

        private void LockInteraction()
        {
            Interactable = false;
            if (UIService.AcquireModalInteraction(this))
            {
                InputService.PreventInteractionUI = true;
            }
        }

        private void UnlockInteraction()
        {
            Interactable = true;
            // 压制位归别人持有时只交还本窗口的交互，不清全局
            if (UIService.ReleaseModalInteraction(this))
            {
                InputService.PreventInteractionUI = false;
            }
        }

        private void CancelCts()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                // 取消过的源由池内废弃，干净的由池回收
                UICtsPool.Return(_cts);
                _cts = null;
            }
        }

        /// <summary>
        /// 本窗的开/关过渡；为 null 表示瞬时——不过渡、不锁交互、不占全局压制位。
        /// </summary>
        /// <remarks>
        /// 默认瞬时：开窗与关闭都当场结算，不再有内置的延迟与输入锁。 <br />
        /// 覆写它交回 <see cref="IUITransition"/> 即启用过渡：过渡期间本窗锁交互（模态窗还占全局压制位）， <br />
        /// 被重开/销毁/新一轮过渡接管时按取消令牌掐断，代次守卫由本类接办。
        /// </remarks>
        protected internal virtual IUITransition Transition => null;

        private async UniTaskVoid SetInteractWaiter(bool open)
        {
            if (UIService.GetTopWindow() != this) return;

            var transition = Transition;
            if (transition == null)
            {
                // 瞬时档：无过渡即无锁——非栈顶的早退与瞬时档共用这一道，不作废他人在跑的过渡
                return;
            }

            // 与关闭续体共用同一套代次协议；非栈顶的早退排在递增之前，不会作废他人在跑的过渡
            var lifetime = ++_interactionLifetime;
            CancelCts();
            _cts = UICtsPool.Rent();

            LockInteraction();

            try
            {
                await transition.Play(open, _cts.Token);
            }
            catch (OperationCanceledException) { return; }

            if (IsDestroyed) return;

            // 被后续转移接管时，交互锁由那一方交还
            if (lifetime != _interactionLifetime) return;

            UnlockInteraction();
        }

        #endregion

        #region 关闭策略 [CLOSE POLICY]

        /// <summary>关闭的门：<see cref="TryClose"/> 等到可交互后过这一道，回真才真关。</summary>
        protected virtual bool CanClose => true;

        /// <summary>试图关闭但关不了时调用（<see cref="CanClose"/> 为假的那一轮）。</summary>
        protected virtual void OnCloseFail() { }

        /// <summary>
        /// 关闭本窗：已可交互就当场过门，否则等交互位让位、本窗被接管或销毁为止。
        /// </summary>
        /// <remarks>
        /// 等待是被动观察、不接管交互锁：被重开/销毁接管的那一轮静默终止，锁由接管方交还。<br />
        /// 已销毁、代次已换或令牌取消都终止本轮，不对已销毁的窗空转轮询。<br />
        /// 需要绕开门与等待的覆写者走 <see cref="ForceClose"/>。
        /// </remarks>
        public virtual async UniTaskVoid TryClose()
        {
            try
            {
                if (!Interactable)
                {
                    var lifetime = _interactionLifetime;
                    await UniTask.WaitUntil(
                        () => Interactable || IsDestroyed || lifetime != _interactionLifetime,
                        cancellationToken: _cts != null ? _cts.Token : CancellationToken.None);

                    if (IsDestroyed || lifetime != _interactionLifetime)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (CanClose)
            {
                ForceClose();
            }
            else
            {
                OnCloseFail();
            }
        }

        #endregion

        /// <summary>
        /// 隐藏当前弹窗：交进那条共享栈，门面还有驱动者在位才结算。
        /// </summary>
        /// <remarks>
        /// 回叫直呼共享持有者、不分轨，各轨的窗走的是同一道。<br />
        /// 关停把各轨引用摘掉之后 <see cref="UIService.IsValid"/> 回假，这一道与 <see cref="Close"/> 都静默落空。
        /// </remarks>
        protected internal virtual void Hide()
        {
            if (UIService.IsValid)
            {
                UIService.SharedLedger.HideUI(GetType(), WindowName);
            }
        }

        /// <summary>
        /// 关闭当前弹窗：走 <see cref="TryClose"/>，等可交互后过 <see cref="CanClose"/> 门。
        /// </summary>
        /// <remarks>与 <see cref="Hide"/> 同一道守卫：门面还有驱动者在位才结算。</remarks>
        protected internal virtual void Close()
        {
            TryClose().Forget();
        }

        /// <summary>
        /// 立即关闭当前弹窗：跳过 <see cref="TryClose"/> 的等待与 <see cref="CanClose"/> 门。
        /// </summary>
        /// <remarks>与 <see cref="Hide"/> 同一道守卫。</remarks>
        protected void ForceClose()
        {
            if (UIService.IsValid)
            {
                UIService.SharedLedger.CloseUI(GetType(), WindowName);
            }
        }

        private Action _closeDelegate;

        /// <summary>缓存的本窗关闭委托：隐藏转关闭的定时器复用同一枚，不在每次隐藏时分配。</summary>
        internal Action CloseDelegate => _closeDelegate ??= Close;

        internal void CancelHideToCloseTimer()
        {
            IsHide = false;
            if (HideTimerId != 0UL)
            {
                TimerService.Cancel(HideTimerId);
                HideTimerId = 0UL;
            }
        }
    }
}
