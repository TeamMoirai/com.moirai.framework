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
        public virtual bool FullScreen { get; private set; } = false;

        /// <summary>是内部资源无需AB加载。</summary>
        public bool FromResources { get; private set; }
        
        /// <summary>隐藏窗口关闭时间。</summary>
        public int HideTimeToClose { get; set; }
        
        public ulong HideTimerId { get; set; }
        
        /// <summary>缓存实例，关闭时不销毁。</summary>
        public bool CacheInstance { get; set; }

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
                    _OnSortDepth();
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
                    _OnSortDepth();
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
        internal bool IsLoadDone = false;
        
        /// <summary>是否被销毁。</summary>
        internal bool IsDestroyed = false;
                
        /// <summary>UI是否隐藏标志位。</summary>
        public bool IsHide { internal set; get; } = false;

        #endregion

        public void Init(string name, int layer, bool fullScreen, string assetLocation, bool fromResources, int hideTimeToClose, bool cacheInstance)
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

        internal async UniTaskVoid InternalLoad(string location, Action<UIWindow> prepareCallback, bool isAsync, System.Object[] @params)
        {
            _prepareCallback = prepareCallback;
            _params = @params;

            // 装载面板是后端的活：本类只认「装上没有」，装上之后统一把三份意图落到新面板上
            if (isAsync)
            {
                if (!await LoadPanelAsync(location, FromResources, CancellationToken.None))
                {
                    return;
                }
            }
            else
            {
                if (!LoadPanel(location, FromResources))
                {
                    return;
                }
            }

            PanelLoaded();
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

        internal bool InternalUpdate()
        {
            if (!IsPrepare || !Visible)
            {
                return false;
            }

            List<UIWidget> listNextUpdateChild = null;
            if (ChildList != null && ChildList.Count > 0)
            {
                listNextUpdateChild = _updateChildList;
                var updateListValid = _updateListValid;
                List<UIWidget> childList = null;
                if (!updateListValid)
                {
                    if (listNextUpdateChild == null)
                    {
                        listNextUpdateChild = new List<UIWidget>();
                        _updateChildList = listNextUpdateChild;
                    }
                    else
                    {
                        listNextUpdateChild.Clear();
                    }

                    childList = ChildList;
                }
                else
                {
                    childList = listNextUpdateChild;
                }

                for (int i = 0; i < childList.Count; i++)
                {
                    var uiWidget = childList[i];

                    if (uiWidget == null)
                    {
                        continue;
                    }

                    GameProfiler.BeginSample(uiWidget.WidgetName);
                    var needValid = uiWidget.InternalUpdate();
                    GameProfiler.EndSample();

                    if (!updateListValid && needValid)
                    {
                        listNextUpdateChild.Add(uiWidget);
                    }
                }

                if (!updateListValid)
                {
                    _updateListValid = true;
                }
            }

            GameProfiler.BeginSample("OnUpdate");

            bool needUpdate = false;
            if (listNextUpdateChild == null || listNextUpdateChild.Count <= 0)
            {
                _hasOverrideUpdate = true;
                OnUpdate();
                needUpdate = _hasOverrideUpdate;
            }
            else
            {
                OnUpdate();
                needUpdate = true;
            }

            GameProfiler.EndSample();

            return needUpdate;
        }
        
        protected internal virtual void InternalClose()
        {
            OnClose();
            InternalCloseAsync(++_interactionLifetime).Forget();
        }

        private async UniTaskVoid InternalCloseAsync(uint lifetime)
        {
            CancelCts();
            _cts = new CancellationTokenSource();

            LockInteraction();

            try { await CloseAnimation(); }
            catch (OperationCanceledException) { return; }

            if (IsDestroyed) return;

            // 交还锁与隐藏都是本轮转移的特权：代次被重开/销毁/新一轮动画接管后，
            // 子类动画若没观察 token 走到这里，继续执行会拆掉别人持有的锁、把刚重开的窗口重新隐藏。
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
                _cts.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// 打开动画等待。子类可 override 以播放打开动画（淡入、缩放等）。
        /// </summary>
        protected virtual async UniTask OpenAnimation()
        {
            await UniTask.WaitForSeconds(0.5f, true, cancellationToken: _cts.Token);
        }

        /// <summary>
        /// 关闭动画等待：子类可 override 以播放关闭动画（淡出、缩放等）。
        /// </summary>
        /// <remarks>
        /// 窗口在动画期间保持可见，动画结束后自动隐藏。
        /// </remarks>
        protected virtual async UniTask CloseAnimation()
        {
            await UniTask.WaitForSeconds(0.25f, true, cancellationToken: _cts.Token);
        }

        /// <summary>
        /// 上层窗口关闭后的交互延迟。子类可 override 以自定义延迟行为。
        /// </summary>
        protected virtual async UniTask TopRefreshWaiter()
        {
            await UniTask.WaitForSeconds(0.25f, true, cancellationToken: _cts.Token);
        }

        private async UniTaskVoid SetInteractWaiter(bool open)
        {
            if (UIService.GetTopWindow() != this) return;

            // 与关闭续体共用同一套代次协议；非栈顶的早退排在递增之前，不会作废他人在跑的动画
            var lifetime = ++_interactionLifetime;
            CancelCts();
            _cts = new CancellationTokenSource();

            LockInteraction();

            try
            {
                if (open) await OpenAnimation();
                else await TopRefreshWaiter();
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
