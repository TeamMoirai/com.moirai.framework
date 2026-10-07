using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// uGUI 轨窗口基类：面板实现（GameObject/Canvas/GraphicRaycaster）后端专有，对象模型不认这些类型。
    /// </summary>
    /// <remarks>
    /// 覆写 <see cref="UIWindow"/> 的七枚面板钩子，把 <see cref="UIWindow.Visible"/> / <see cref="UIWindow.Depth"/> / <see cref="UIWindow.Interactable"/> <br />
    /// 三份意图落到真实面板上：显隐切整棵子树的 layer（SHOW/HIDE）、深度给父 Canvas 写绝对值并按各自偏移差分同步子 Canvas、 <br />
    /// 交互推面板自身与全部子 <see cref="GraphicRaycaster"/> 的 <c>enabled</c>。判据与写入次序逐字承自下沉前的 <c>UIWindow</c>，未作修正。 <br />
    /// 空引用口径同样保持：未绑定时 <c>gameObject</c> 回 null，<c>transform</c> / <c>rectTransform</c> 抛 <see cref="NullReferenceException"/>（不补 <c>?.</c>）。 <br />
    /// 排序刷新与 <c>OnSetVisible</c> 的回执仍由 <see cref="UIWindow"/> 决策，本类不知道 <c>_isCreate</c>。线程契约：仅主线程。
    /// </remarks>
    public abstract class UGUIWindow : UIWindow
    {
        #region 面板 [PANEL]

        private GameObject _panel;
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private Canvas[] _childCanvas;
        private GraphicRaycaster[] _childRaycaster;

        /// <summary>窗口位置组件。</summary>
        /// <remarks>保证与 Mono 的命名一致，沿袭使用习惯</remarks>
        public override Transform transform => _panel.transform;

        /// <summary>窗口矩阵位置组件。</summary>
        /// <remarks>保证与 Mono 的命名一致，沿袭使用习惯</remarks>
        public override RectTransform rectTransform => _panel.transform as RectTransform;

        /// <summary>窗口的实例资源对象。</summary>
        /// <remarks>保证与 Mono 的命名一致，沿袭使用习惯</remarks>
        public override GameObject gameObject => _panel;

        /// <summary>面板根上那枚被 <see cref="BindPanel"/> 初始化并被 <see cref="ApplyDepth"/> 写序的 <see cref="Canvas"/>。</summary>
        /// <remarks>未绑定面板时为 <c>null</c>；与 <see cref="UITKWindow.Document"/> 同档——面板事实的唯一真值来源，取口只在这一处。</remarks>
        internal Canvas PanelCanvas => _canvas;

        #endregion

        #region 面板钩子 [PANEL HOOKS]

        /// <summary>装载面板：按 <paramref name="fromResources"/> 走 AB 或内置资源，交给自己装配。</summary>
        protected internal override bool LoadPanel(string assetLocation, bool fromResources)
        {
            if (!fromResources)
            {
                return BindPanel(ResourceService.LoadGameObject(assetLocation, parent: UIService.UIRoot));
            }

            return BindPanel(UObject.Instantiate(Resources.Load<GameObject>(assetLocation), UIService.UIRoot));
        }

        /// <summary>装载面板（异步）：AB 路径 await，内置资源路径与旧实现一样仍走同步 <c>Resources.Load</c>。</summary>
        /// <remarks><paramref name="ct"/> 暂不下给资源层：旧调用点没传令牌，异步开关只决定取资源的方式。</remarks>
        protected internal override async UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct)
        {
            if (fromResources)
            {
                return LoadPanel(assetLocation, true);
            }

            var uiInstance = await ResourceService.LoadGameObjectAsync(assetLocation, parent: UIService.UIRoot);
            return BindPanel(uiInstance);
        }

        /// <summary>显隐落地：整棵子树一起切显示/隐藏 layer，实际 layer 已到位则不重复写。</summary>
        protected internal override void ApplyVisible(bool value)
        {
            if (_canvas == null) return;

            int setLayer = value ? UIService.WINDOW_SHOW_LAYER : UIService.WINDOW_HIDE_LAYER;

            if (_canvas.gameObject.layer == setLayer) return;

            // 显示设置
            _canvas.gameObject.layer = setLayer;
            for (int i = 0; i < _childCanvas.Length; i++)
            {
                _childCanvas[i].gameObject.layer = setLayer;
            }
        }

        /// <summary>深度落地：父 Canvas 取绝对值，子 Canvas 按各自原有偏移一同平移；实际序已到位则不重复写。</summary>
        protected internal override void ApplyDepth(int value)
        {
            if (_canvas == null) return;

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
        }

        /// <summary>交互落地：推给面板自身与全部子 Raycaster；面板没挂 Raycaster 时写不进去。</summary>
        protected internal override void ApplyInteractable(bool value)
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

        /// <summary>停放面板：物体留着但不激活（缓存实例的关闭态、关闭动画结束后的隐藏）。</summary>
        protected internal override void ParkPanel()
        {
            _panel.SetActive(false);
        }

        /// <summary>收走面板：销毁物体并断开全部后端引用，令窗口回到「未绑定」口径。</summary>
        protected internal override void DestroyPanel()
        {
            if (_panel != null)
            {
                UObject.Destroy(_panel);
                _panel = null;
            }

            // 引用一并断开：装载完成前窗口已被销毁时，旧实现压根没装配过面板，后续写意图必须同样落空
            _canvas = null;
            _raycaster = null;
            _childCanvas = null;
            _childRaycaster = null;
        }

        /// <summary>
        /// 接管一块 uGUI 面板：命名、归零、校验并初始化 <see cref="Canvas"/>，抓取面板自身与全部子 Canvas / Raycaster。
        /// </summary>
        /// <param name="panel">面板物体；null 时直接判装载失败。</param>
        /// <returns>装配成功返回 true。</returns>
        /// <remarks>缺 <see cref="Canvas"/> 时抛异常并沿用旧文案——调用方不得拿到半个可用面板。</remarks>
        internal bool BindPanel(GameObject panel)
        {
            if (panel == null) return false;

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

            return true;
        }

        #endregion

        #region 刘海屏适配 [NOTCH ADAPTATION]

        private SetUISafeFitHelper _setUISafeFitHelper;

        /// <summary>
        /// 移动设备屏幕适配。
        /// </summary>
        /// <param name="fitRect">适配的RectTransform对象。</param>
        /// <param name="liuHaiFit">是否开启刘海屏顶部适配。</param>
        /// <param name="topSpacing">刘海屏顶部适配偏移高度。</param>
        /// <param name="bottomFit">是否开启刘海屏底部适配。</param>
        /// <param name="bottomSpacing">刘海屏底部适配偏移高度。</param>
        public void SetUIFit(RectTransform fitRect, bool liuHaiFit = true, float topSpacing = 0, bool bottomFit = true, float bottomSpacing = 0)
        {
            if (_setUISafeFitHelper == null)
            {
                _setUISafeFitHelper = new SetUISafeFitHelper(fitRect, liuHaiFit, topSpacing, bottomFit, bottomSpacing);
            }
            _setUISafeFitHelper?.SetUIFit();
        }

        /// <summary>
        /// 设置 <see cref="rectTransform"/> 不受当前适配影响。
        /// </summary>
        public void SetUINotFit(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            _setUISafeFitHelper?.SetUINotFit(rect);
        }

        /// <summary>
        /// 设置某一个节点不受指定 <see cref="refRect"/> 的影响。
        /// </summary>
        /// <param name="rect">设置的RectTransform。</param>
        /// <param name="refRect">依赖的RectTransform。</param>
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

        /// <summary>
        /// 手动强制刷新所有子对象的布局。
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
