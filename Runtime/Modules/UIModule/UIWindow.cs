using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.UI
{
    public abstract partial class UIWindow : UIBase
    {
        #region Propreties

        private GameObject _panel;

        private Canvas _canvas;
        protected Canvas Canvas => _canvas;

        private GraphicRaycaster _raycaster;
        protected GraphicRaycaster GraphicRaycaster => _raycaster;

        private bool _isCreate = false;
        private Canvas[] _childCanvas;
        private GraphicRaycaster[] _childRaycaster;
        private Action<UIWindow> _prepareCallback;
        private SetUISafeFitHelper _setUISafeFitHelper;

        public override UIType Type => UIType.Window;

        /// <summary>
        /// 窗口位置组件。
        /// </summary>
        /// <remarks>保证与 Mono 的命名一致，沿袭使用习惯</remarks>
        public override Transform transform => _panel.transform;
        
        /// <summary>
        /// 窗口矩阵位置组件。
        /// </summary>
        /// <remarks>保证与 Mono 的命名一致，沿袭使用习惯</remarks>
        public override RectTransform rectTransform => _panel.transform as RectTransform;

        /// <summary>
        /// 窗口的实例资源对象。
        /// </summary>
        /// <remarks>保证与 Mono 的命名一致，沿袭使用习惯</remarks>
        public override GameObject gameObject => _panel;

        /// <summary>
        /// 窗口名称。
        /// </summary>
        public string WindowName { get; private set; }

        /// <summary>
        /// 窗口层级。
        /// </summary>
        public int WindowLayer { get; private set; }

        /// <summary>
        /// 资源定位地址。
        /// </summary>
        public string AssetName { get; private set; }

        /// <summary>
        /// 是否为全屏窗口。
        /// </summary>
        /// <remarks>将全屏下层的UI设为隐藏</remarks>
        public virtual bool FullScreen { get; private set; } = false;

        /// <summary>
        /// 是内部资源无需AB加载。
        /// </summary>
        public bool FromResources { get; private set; }
        
        /// <summary>
        /// 隐藏窗口关闭时间。
        /// </summary>
        public int HideTimeToClose { get; set; }
        
        public int HideTimerId { get; set; }
        
        /// <summary>
        /// 缓存实例，关闭时不销毁。
        /// </summary>
        public bool CacheInstance { get; set; }
        
        /// <summary>
        /// 窗口深度值。
        /// </summary>
        public int Depth
        {
            get
            {
                if (_canvas != null)
                {
                    return _canvas.sortingOrder;
                }
                else
                {
                    return 0;
                }
            }

            set
            {
                if (_canvas != null)
                {
                    if (_canvas.sortingOrder == value)
                    {
                        return;
                    }

                    var oldOrder = _canvas.sortingOrder;
                    // 设置父类
                    _canvas.sortingOrder = value;

                    // 设置子类
                    // int depth = value;
                    for (int i = 0; i < _childCanvas.Length; i++)
                    {
                        var canvas = _childCanvas[i];
                        if (canvas != _canvas)
                        {
                            // depth += 5; // 注意递增值
                            // canvas.sortingOrder = depth;
                            canvas.sortingOrder = value + (canvas.sortingOrder - oldOrder);
                        }
                    }

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
        }

        /// <summary>
        /// 窗口可见性
        /// </summary>
        public bool Visible
        {
            get
            {
                if (_canvas != null)
                {
                    return _canvas.gameObject.layer == UIModule.WINDOW_SHOW_LAYER;
                }
                else
                {
                    return false;
                }
            }

            set
            {
                if (_canvas != null)
                {
                    int setLayer = value ? UIModule.WINDOW_SHOW_LAYER : UIModule.WINDOW_HIDE_LAYER;

                    if (_canvas.gameObject.layer == setLayer) return;

                    // 显示设置
                    _canvas.gameObject.layer = setLayer;
                    for (int i = 0; i < _childCanvas.Length; i++)
                    {
                        _childCanvas[i].gameObject.layer = setLayer;
                    }

                    if (value && _isCreate)
                    {
                        _isSortingOrderDirty = false;
                        _OnSortDepth();
                    }

                    // Log.Info("[UI] Set '{0}' Visible {1}", WindowName, value);

                    // 虚函数
                    if (_isCreate)
                    {
                        OnSetVisible(value);
                    }
                }
            }
        }

        /// <summary>
        /// 窗口交互性
        /// </summary>
        private bool Interactable
        {
            get
            {
                if (_raycaster != null)
                {
                    return _raycaster.enabled;
                }
                else
                {
                    return false;
                }
            }

            set
            {
                if (_raycaster != null)
                {
                    _raycaster.enabled = value;
                    for (int i = 0; i < _childRaycaster.Length; i++)
                    {
                        _childRaycaster[i].enabled = value;
                    }
                }
            }
        }

        /// <summary>
        /// 是否加载完毕。
        /// </summary>
        internal bool IsLoadDone = false;
        
        /// <summary>
        /// 是否被销毁。
        /// </summary>
        internal bool IsDestroyed = false;
                
        /// <summary>
        /// UI是否隐藏标志位。
        /// </summary>
        public bool IsHide { internal set; get; } = false;

        #endregion

        public void Init(string name, int layer, bool fullScreen, string assetName, bool fromResources, int hideTimeToClose, bool cacheInstance)
        {
            WindowName = name;
            WindowLayer = layer;
            FullScreen = fullScreen;
            AssetName = assetName;
            FromResources = fromResources;
            HideTimeToClose = hideTimeToClose;
            CacheInstance = cacheInstance;
        }

        #region 刘海屏适配

        /// <summary>
        /// 移动设备屏幕适配
        /// </summary>
        /// <param name="fitRect">适配的RectTransform对象</param>
        /// <param name="liuHaiFit">是否开启刘海屏顶部适配</param>
        /// <param name="topSpacing">刘海屏顶部适配偏移高度</param>
        /// <param name="bottomFit">是否开启刘海屏底部适配</param>
        /// <param name="bottomSpacing">刘海屏底部适配偏移高度</param>
        public void SetUIFit(RectTransform fitRect, bool liuHaiFit = true, float topSpacing = 0, bool bottomFit = true, float bottomSpacing = 0)
        {
            if (_setUISafeFitHelper == null)
            {
                _setUISafeFitHelper = new SetUISafeFitHelper(fitRect, liuHaiFit, topSpacing, bottomFit, bottomSpacing);
            }
            _setUISafeFitHelper?.SetUIFit();
        }

        /// <summary>
        /// 设置 <see cref="rect"/> 不受当前适配影响
        /// </summary>
        /// <param name="rect"></param>
        public void SetUINotFit(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            _setUISafeFitHelper?.SetUINotFit(rect);
        }

        /// <summary>
        /// 设置某一个节点不受指定 <see cref="refRect"/> 的影响
        /// </summary>
        /// <param name="rect">设置的RectTransform</param>
        /// <param name="refRect">依赖的RectTransform</param>
        public void SetUINotFit(RectTransform rect, RectTransform refRect)
        {
            if (rect == null || refRect == null)
            {
                return;
            }
            if (_setUISafeFitHelper == null)
            {
                _setUISafeFitHelper = new SetUISafeFitHelper();
            }
            _setUISafeFitHelper?.SetUINotFit(rect, refRect);
        }

        #endregion

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
            if (!FromResources)
            {
                if (isAsync)
                {
                    var uiInstance = await GameModule.Resource.LoadGameObjectAsync(location, parent: UIModule.UIRoot);
                    Handle_Completed(uiInstance);
                }
                else
                {
                    var uiInstance = GameModule.Resource.LoadGameObject(location, parent: UIModule.UIRoot);
                    Handle_Completed(uiInstance);
                }
            }
            else
            {
                GameObject panel = Object.Instantiate(Resources.Load<GameObject>(location), UIModule.UIRoot);
                Handle_Completed(panel);
            }
        }

        /// <summary>
        /// 打开窗口后触发
        /// </summary>
        internal void InternalCreate()
        {
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
            // Log.Info("[UI] Open {0}", WindowName);
        }

        internal void InternalRefresh(bool open)
        {
            SetInteractWaiter(open).Forget();

            // Log.Info("[UI] Refresh {0}", WindowName);
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
            PreventInteraction(false).Forget();
            gameObject.SetActive(false);
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
            PreventInteraction(false).Forget();

            // 销毁面板对象
            if (!isShutDown && CacheInstance)
            {
                _panel.gameObject.SetActive(false);
            }
            else
            {
                if (_panel != null)
                {
                    Object.Destroy(_panel);
                    _panel = null;
                }
            }

            IsDestroyed = true;

            if (!isShutDown)
            {
                CancelHideToCloseTimer();
            }
        }

        /// <summary>
        /// 处理资源加载完成回调。
        /// </summary>
        /// <param name="panel">面板资源实例。</param>
        private void Handle_Completed(GameObject panel)
        {
            if (panel == null) return;

            IsLoadDone = true;
            
            if (IsDestroyed)
            {
                UnityEngine.Object.Destroy(panel);
                return;
            }
            
            panel.name = GetType().Name;
            _panel = panel;
            _panel.transform.localPosition = Vector3.zero;

            // 获取组件
            _canvas = _panel.GetComponent<Canvas>();
            if (_canvas == null)
            {
                throw new Exception($"Not found {nameof(Canvas)} in panel {WindowName}");
            }

            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 0;
            _canvas.sortingLayerName = "Default"; // 使用默认层级程序化 sortingOrder 排序，避免繁复的设置

            // 获取组件
            _raycaster = _panel.GetComponent<GraphicRaycaster>();
            _childCanvas = _panel.GetComponentsInChildren<Canvas>(true);
            _childRaycaster = _panel.GetComponentsInChildren<GraphicRaycaster>(true);

            // 通知UI管理器
            IsPrepare = true;
            _prepareCallback?.Invoke(this);
        }

        #region 交互相关

        /// <summary>
        /// 是否准备好了，即可进行交互
        /// </summary>
        public virtual bool IsReady { get; private set; }
        /// <summary>
        /// 打开弹窗后的可交互延迟
        /// </summary>
        /// <remarks>用于动画演出</remarks>
        protected virtual float InteractDelayOnCreate => 0.3f;
        /// <summary>
        /// 关闭上层弹窗后的可交互的延迟
        /// </summary>
        /// <remarks>用于避免立即触发</remarks>
        protected virtual float InteractDelayOnRefresh => 0.25f;

        protected CancellationTokenSource _cts;
        protected async UniTaskVoid SetInteractWaiter(bool open)
        {
            if (!GameModule.UI.IsModal(this)) return;

            _cts = new CancellationTokenSource();

            IsReady = false;
            await UniTask.WaitForSeconds(open ? InteractDelayOnCreate : InteractDelayOnRefresh, true, cancellationToken:_cts.Token);
            IsReady = true;

            PreventInteraction(true).Forget();
        }

        /// <summary>
        /// 禁止输入交互相关
        /// </summary>
        protected async UniTaskVoid PreventInteraction(bool state)
        {
            if (GameModule.UI != null && !GameModule.UI.IsModal(this)) return;

            // Log.Warning($"PreventInteraction {state}");
            if (GameModule.Input != null) GameModule.Input.LockPlayerController = state;

            // 打开弹窗时避免立即交互
            if (state)
            {
                if (GameModule.Input != null) GameModule.Input.PreventInteractionUI = true;
                Interactable = false;
                // Log.Info("[UI]{0} {1} Prevent Interaction", WindowName, Time.time);
                await UniTask.WaitUntil(() => IsReady, cancellationToken:_cts.Token, cancelImmediately:true);
                if (GameModule.Input != null) GameModule.Input.PreventInteractionUI = false;
                Interactable = true;
                // Log.Info("[UI]{0} {1} Allow Interaction", WindowName, Time.time);
            }
            else
            {
                if (GameModule.Input != null) GameModule.Input.PreventInteractionUI = false;
                Interactable = true;
                _cts?.Cancel();
                _cts?.Dispose();
            }
        }

        #endregion

        protected virtual void Hide()
        {
            GameModule.UI.HideUI(GetType(), WindowName);
        }

        protected virtual void Close()
        {
            GameModule.UI.CloseUI(GetType(), WindowName);
        }

        internal void CancelHideToCloseTimer()
        {
            IsHide = false;
            if (HideTimerId > 0)
            {
                GameModule.Timer.RemoveTimer(HideTimerId);
                HideTimerId = 0;
            }
        }

        /// <summary>
        /// 手动强制刷新所有子对象的布局
        /// </summary>
        /// <remarks>用于解决动态更新布局后不会自动刷新的问题</remarks>
        protected virtual void ForceRebuildLayoutImmediate()
        {
            foreach (var layout in transform.GetComponentsInChildren<LayoutGroup>())
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(layout.GetComponent<RectTransform>());
            }
        }
    }
}