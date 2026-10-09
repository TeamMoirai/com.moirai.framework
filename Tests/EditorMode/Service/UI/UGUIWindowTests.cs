using System.Collections.Generic;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Service.UI
{
    /// <summary>
    /// uGUI 轨窗口（<see cref="UGUIWindow"/>）的面板访问面测试。
    /// </summary>
    /// <remarks>
    /// 「未绑定」四格是<b>下沉前护栏</b>：它们在产码未动时即绿，钉住对象模型的空引用同形口径—— <br />
    /// 面板未绑定时 <c>gameObject</c> 回 null，<c>transform</c> / <c>rectTransform</c> 仍抛 <see cref="System.NullReferenceException"/>（旧实现直读 <c>_panel</c>，无 <c>?.</c>）， <br />
    /// 对未绑定窗口写显隐/深度/交互不得崩。面板实现从 <see cref="UIWindow"/> 下沉到本类后，这四格必须逐字仍绿（改的是归属，不是行为）。 <br />
    /// 其余格钉下沉后的面板装配与三份意图的落地：判据与写入次序对着下沉前的 <c>UIWindow</c> 逐字搬来， <br />
    /// 序空间那一组格钉「深度意图经 <c>ApplyDepth</c> 落进面板根画布、回读取的是画布事实」，含负序、超出画布可表示范围与面板不在场三种口径， <br />
    /// 判据一律打在窗基类自己的钩子与 <see cref="UGUIWindow.PanelCanvas"/> 取口上。 <br />
    /// 三处与旧实现的口径差（<c>Visible</c> 读意图而非 layer、异层面板隐藏成空操作、SHOW 层面板首次转可见多发一次回执） <br />
    /// 以 <c>CurrentBehaviour_…</c> 命名<b>登记现状，不是背书</b>。 <br />
    /// 夹具不依赖场景、预制体与 <c>ResourceService</c>：面板由用例自备的 <see cref="GameObject"/> 经 <c>LoadPanel</c> 钩子注入。
    /// </remarks>
    [TestFixture]
    public sealed class UGUIWindowTests
    {
        private GameObject _host;
        private readonly List<GameObject> _panels = new List<GameObject>();

        /// <summary>
        /// 面板一律挂在一枚场景画布下：生产里窗口面板是 <c>Instantiate(prefab, UIService.UIRoot)</c> 的<b>嵌套</b>画布， <br />
        /// 而 Unity 只在非根画布上认 <c>overrideSorting</c> 与 <c>sortingOrder</c>——拿场景根物体当面板会让这两项被静默吞掉。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _host = new GameObject(nameof(UGUIWindowTests) + "Host");
            _host.AddComponent<Canvas>();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _panels.Count; i++)
            {
                if (_panels[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_panels[i]);
                }
            }

            _panels.Clear();

            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        #region 未绑定面板的对象模型口径 [UNBOUND PANEL]

        /// <summary>面板未绑定：<c>gameObject</c> 回 null（旧实现 <c>=> _panel</c> 的同形结果）。</summary>
        [Test]
        public void GameObject_Unbound_ReturnsNull()
        {
            var window = new ProbeWindow();

            Assert.IsNull(window.gameObject, "面板未绑定时 gameObject 必须是 null，不得伪造空物体");
        }

        /// <summary>面板未绑定：<c>transform</c> 抛 NRE（旧代码没有 <c>?.</c>，下沉后也不得补）。</summary>
        [Test]
        public void Transform_Unbound_ThrowsNullReference()
        {
            var window = new ProbeWindow();

            Assert.Throws<System.NullReferenceException>(() =>
            {
                _ = window.transform;
            }, "未绑定面板读 transform 必须是 NRE，不是静默 null");
        }

        /// <summary>面板未绑定：<c>rectTransform</c> 抛 NRE（同 <c>transform</c>）。</summary>
        [Test]
        public void RectTransform_Unbound_ThrowsNullReference()
        {
            var window = new ProbeWindow();

            Assert.Throws<System.NullReferenceException>(() =>
            {
                _ = window.rectTransform;
            }, "未绑定面板读 rectTransform 必须是 NRE，不是静默 null");
        }

        /// <summary>面板未绑定时写显隐/深度/交互不得崩（旧实现整段被 <c>if (_canvas != null)</c> 兜住）。</summary>
        [Test]
        public void PanelStateWrites_WhileUnbound_DoNotThrow()
        {
            var window = new ProbeWindow();
            window.Init(nameof(UGUIWindowTests), 1, false, nameof(ProbeWindow), false, 10, false);

            Assert.DoesNotThrow(() =>
            {
                window.Visible = true;
                window.Visible = false;
                window.Depth = 1200;
                window.Interactable = false;
                window.Interactable = true;
            }, "未绑定面板时三语义写入必须安全落地，不得触碰不存在的面板组件");
        }

        #endregion

        #region 装配面板 [BIND]

        /// <summary>拿不到面板物体：判装载失败，窗口停在未绑定态，引用不得被半个面板占住。</summary>
        [Test]
        public void BindPanel_NullPanel_ReportsFailure()
        {
            var window = new ProbeWindow();

            Assert.IsFalse(window.BindPanel(null), "null 面板必须判失败");
            Assert.IsNull(window.gameObject, "失败后仍应保持未绑定");
        }

        /// <summary>面板上没有 Canvas：抛 <see cref="Moirai.Atropos.GameException"/>（不静默退化成无排序面板）。</summary>
        [Test]
        public void BindPanel_MissingCanvas_ThrowsWithWindowName()
        {
            var panel = NewGameObject("NoCanvasPanel");
            var window = Unbound();
            window.Init("BattleWindow", 1, false, "NoCanvasPanel", false, 10, false);

            var error = Assert.Throws<Moirai.Atropos.GameException>(() => window.BindPanel(panel));

            StringAssert.Contains("BattleWindow", error.Message,
                "缺 Canvas 的文案要带上窗口名：拿到半个可用面板不如当场指认");
        }

        /// <summary>装配即初始化程序化排序：overrideSorting、sortingOrder 归零、sortingLayerName 固定 Default。</summary>
        [Test]
        public void BindPanel_PrepsCanvasForProgrammaticSorting()
        {
            var panel = NewPanel();
            var canvas = panel.GetComponent<Canvas>();
            var window = Unbound();

            Assert.IsTrue(window.BindPanel(panel), "带 Canvas 的面板应装配成功");

            Assert.AreEqual(nameof(ProbeWindow), panel.name, "面板要改写成窗口类型名（旧 Handle_Completed 的命名口径）");
            Assert.AreEqual(Vector3.zero, panel.transform.localPosition, "面板本地坐标要归零");
            Assert.IsTrue(canvas.overrideSorting, "窗口要自己管排序，不吃场景 Canvas 的序");
            Assert.AreEqual(0, canvas.sortingOrder, "装配后初始深度为 0");
            Assert.AreEqual("Default", canvas.sortingLayerName, "用默认层级程序化 sortingOrder 排序，避免繁复的设置");
        }

        /// <summary>装载路径把面板交给 <c>BindPanel</c> 后，三份意图当场落到新面板上，再进准备态。</summary>
        [Test]
        public void Load_BindsPanelAndFlushesIntent()
        {
            var panel = NewPanel(layer: UIService.WINDOW_SHOW_LAYER);
            var window = new ProbeWindow { Fixture = panel };
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);
            window.Visible = true;
            window.Depth = 500;

            window.InternalLoad("Panel", null, false);

            Assert.IsTrue(window.IsLoadDone, "面板装配成功要置 IsLoadDone");
            Assert.IsTrue(window.IsPrepare, "面板装配成功要置 IsPrepare");
            Assert.AreSame(panel, window.gameObject, "gameObject 应交回装配好的面板");
            Assert.AreEqual(UIService.WINDOW_SHOW_LAYER, panel.layer, "绑定前写的可见意图要在装载当场落地");
            Assert.AreEqual(500, panel.GetComponent<Canvas>().sortingOrder, "绑定前写的深度意图要在装载当场落地");
        }

        #endregion

        #region 三份意图的落地 [APPLY]

        /// <summary>显隐落地：面板根与子 Canvas 整棵子树一起切 SHOW/HIDE layer。</summary>
        [Test]
        public void ApplyVisible_SwitchesRootAndChildLayersTogether()
        {
            var panel = NewPanel(layer: UIService.WINDOW_SHOW_LAYER);
            var child = panel.transform.GetChild(0).gameObject;
            var window = Loaded(panel);

            // 装载当场已按隐藏意图把铺在 SHOW 上的面板写成 HIDE（那一半由 Load_BindsPanelAndFlushesIntent 钉），
            // 这里先走一次 setter 显出来，本格的两个半段才都测在 ApplyVisible 上
            window.Visible = true;

            Assert.AreEqual(UIService.WINDOW_SHOW_LAYER, panel.layer, "显示要切面板根");
            Assert.AreEqual(UIService.WINDOW_SHOW_LAYER, child.layer, "显示要连子 Canvas 一起切");

            window.Visible = false;

            Assert.AreEqual(UIService.WINDOW_HIDE_LAYER, panel.layer, "隐藏要切面板根");
            Assert.AreEqual(UIService.WINDOW_HIDE_LAYER, child.layer, "隐藏要连子 Canvas 一起切");
        }

        /// <summary>深度落地：父 Canvas 写绝对值，子 Canvas 保持自己那份偏移一起平移（差分细节不下漏到对象模型）。</summary>
        [Test]
        public void ApplyDepth_ShiftsChildCanvasByItsOwnOffset()
        {
            var panel = NewPanel();
            var childCanvas = panel.transform.GetChild(0).GetComponent<Canvas>();
            var window = Loaded(panel);

            window.Depth = 1000;

            Assert.AreEqual(1000, panel.GetComponent<Canvas>().sortingOrder, "父 Canvas 取绝对值");
            Assert.AreEqual(1020, childCanvas.sortingOrder, "装配时子 Canvas 偏移 20，平移后要保住这 20");

            window.Depth = 1100;

            Assert.AreEqual(1100, panel.GetComponent<Canvas>().sortingOrder, "再次改深度");
            Assert.AreEqual(1120, childCanvas.sortingOrder, "差分按上一次的实际序算，不得把偏移吃成累加");
        }

        /// <summary>交互落地：面板自身与全部子 Raycaster 的 enabled 一起推。</summary>
        [Test]
        public void ApplyInteractable_TogglesSelfAndChildRaycasters()
        {
            var panel = NewPanel();
            var childRaycaster = panel.transform.GetChild(0).GetComponent<GraphicRaycaster>();
            var window = Loaded(panel);

            window.Interactable = true;

            Assert.IsTrue(panel.GetComponent<GraphicRaycaster>().enabled, "自身 Raycaster 要跟着意图走");
            Assert.IsTrue(childRaycaster.enabled, "子 Raycaster 也要一起放开");

            window.Interactable = false;

            Assert.IsFalse(panel.GetComponent<GraphicRaycaster>().enabled, "屏蔽交互要连子 Raycaster 一起掐");
            Assert.IsFalse(childRaycaster.enabled, "子 Raycaster 也要一起掐");
        }

        /// <summary>停放面板：物体留着但不激活，绑定关系不动（缓存实例还要复用）。</summary>
        [Test]
        public void ParkPanel_DeactivatesButKeepsPanelBound()
        {
            var panel = NewPanel();
            var window = Loaded(panel);

            window.ParkPanel();

            Assert.IsFalse(panel.activeSelf, "停放要掐掉激活");
            Assert.AreSame(panel, window.gameObject, "停放不是销毁，gameObject 仍指向面板");
        }

        #endregion

        #region 序空间：意图位与画布事实 [ORDER SPACE]

        /// <summary>
        /// 绑定好的窗口：深度意图经 <c>ApplyDepth</c> 落进面板根画布的序空间，而 <see cref="UGUIWindow.PanelCanvas"/> 交回的就是被写那一枚。
        /// </summary>
        /// <remarks>
        /// 判据打在窗基类自己的钩子与它的画布取口上：意图位与画布实际序必须同源，否则「面板上到底是几」无从问起。 <br />
        /// 子 Canvas 的偏移差分由 <c>ApplyDepth_ShiftsChildCanvasByItsOwnOffset</c> 那一格钉，本格不重复那半句。
        /// </remarks>
        [Test]
        public void DepthIntent_BoundWindow_LandsOnPanelCanvasAndReadsBackSameOrder()
        {
            var panel = NewPanel();
            var window = Loaded(panel);

            window.Depth = 1200;

            Assert.AreEqual(1200, window.Depth, "意图位跟着这次写入");
            Assert.AreEqual(1200, window.PanelCanvas.sortingOrder, "深度意图落进面板根画布的序空间");
            Assert.AreSame(panel.GetComponent<Canvas>(), window.PanelCanvas,
                "画布取口交回的须是被写入的那一枚：换个口径读就不是面板事实");
        }

        /// <summary>
        /// 回读口径：面板被从背后挪过时，窗口问得出画布上的实际序，而 <c>Depth</c> 仍是自己那份没跟上的意图。
        /// </summary>
        /// <remarks>
        /// 夹具只写画布、一个字节都没碰窗口侧 ⇒ 第一句钉的是「意图位不是面板事实的缓存」，第二句钉的是「面板事实只从画布取」。 <br />
        /// 共存会话仲裁谁在上面时读的就是这一笔，拿意图位代答会读出一份从没落到面板上的排序。
        /// </remarks>
        [Test]
        public void PanelCanvas_PanelMovedBehindWindowBack_ReportsCanvasOrderWhileIntentStaysStale()
        {
            var panel = NewPanel();
            var window = Loaded(panel);

            window.PanelCanvas.sortingOrder = 4242;

            Assert.AreEqual(0, window.Depth, "量具前提坏了：夹具没动意图位，窗口侧仍是装载时结算的初值");
            Assert.AreEqual(4242, window.PanelCanvas.sortingOrder, "回读给画布上的实际序，与意图位分叉");
        }

        /// <summary>负序是 Canvas 序空间里的合法取值：经 <c>ApplyDepth</c> 写进去、读回来仍带符号，不夹到 0。</summary>
        [Test]
        public void DepthIntent_NegativeOrder_WritesThroughWithoutClamping()
        {
            var panel = NewPanel();
            var window = Loaded(panel);

            window.Depth = -1200;

            Assert.AreEqual(-1200, window.PanelCanvas.sortingOrder, "画布不夹下界：负序原样写进去");
        }

        /// <summary>
        /// 现行行为：超出画布可表示范围的大序在 Canvas 上被折回低位的另一笔（实测 <c>2^24+1</c> 读回 <c>1</c>），窗口的回读同样与意图位分叉。
        /// </summary>
        /// <remarks>
        /// 第一句是量具前提：不经窗口、直接挪画布也读到同一笔折回 ⇒ 这一档是 Canvas 侧的事实，不是本轨实现的缺陷，被测代码只负责把它如实回读出来。 <br />
        /// UI Toolkit 那一侧同一笔是就近归整到 <c>2^24</c>（见 <c>UITKWindowTests</c> 的对格）⇒ 两支的折法不同但都分叉，比序一律按回读值。 <br />
        /// 负序与小序不受影响（见 <see cref="DepthIntent_NegativeOrder_WritesThroughWithoutClamping"/>）。
        /// </remarks>
        [Test]
        public void CurrentBehaviour_DepthAboveCanvasRepresentableRange_ReadsBackWrappedCanvasOrder()
        {
            var panel = NewPanel();
            var window = Loaded(panel);

            window.PanelCanvas.sortingOrder = 16777217;
            Assert.AreEqual(1, window.PanelCanvas.sortingOrder, "量具前提坏了：这一笔在画布上就被折回 1，与窗口代码无关");

            window.PanelCanvas.sortingOrder = 0;
            window.Depth = 16777217;

            Assert.AreEqual(16777217, window.Depth, "意图位是 int，原样收着这一笔");
            Assert.AreEqual(1, window.PanelCanvas.sortingOrder, "回读给画布上的事实：与意图位分叉，且这条分叉量得出来");
        }

        /// <summary>面板从未绑过：写深度只持意图、不抛，画布取口回 null（装载当场由窗口自己结算到面板）。</summary>
        [Test]
        public void DepthWrite_WhilePanelUnbound_HoldsIntentAndKeepsCanvasAbsent()
        {
            var window = Unbound();

            Assert.DoesNotThrow(() => window.Depth = 900, "面板未绑定时写深度不得触碰不存在的画布");
            Assert.AreEqual(900, window.Depth, "写入攒在意图位上，面板不在场时它是唯一可信的深度");
            Assert.IsNull(window.PanelCanvas, "未绑定即没有画布事实可读：取口不得伪造一枚");
        }

        #endregion

        #region 与旧实现的口径差（登记现状，不是背书）[RECORDED DIVERGENCE]

        /// <summary>
        /// 现行行为：面板被外部挪到既非 SHOW 也非 HIDE 的 layer 后，再写 <c>Visible = false</c> 什么都不发生。
        /// </summary>
        /// <remarks>
        /// 本格只<b>登记</b>意图位口径带来的这条差，不作取舍：下沉前的 <c>UIWindow.Visible</c> 拿实际 layer 当判据， <br />
        /// 彼时 layer 是 0（既非 5 也非 2）⇒ <c>Visible = false</c> 会把整棵子树写成 2 并发 <c>OnSetVisible(false)</c>； <br />
        /// 现在判据是意图位，意图已是 false ⇒ 既不写 layer 也不发回执。要改判「外部改层后重新隐藏也生效」应动契约，不是动本用例。
        /// </remarks>
        [Test]
        public void CurrentBehaviour_HideOnForeignLayerPanel_WritesNothing_RecordsDivergence()
        {
            var panel = NewPanel();
            var child = panel.transform.GetChild(0).gameObject;
            var window = Created(panel);

            // 装载时的隐藏意图已把子树写成 HIDE(2)，这里模拟「外部把面板挪到别的 layer」这一现实
            panel.layer = 0;
            child.layer = 0;
            window.SetVisibleCount = 0;

            window.Visible = false;

            Assert.AreEqual(0, panel.layer, "现状：意图未变则不写 layer（旧实现此处会写成 2）");
            Assert.AreEqual(0, child.layer, "现状：子 Canvas 也不被带动");
            Assert.AreEqual(0, window.SetVisibleCount, "现状：OnSetVisible 也不发（旧实现此处会发一次 false）");
        }

        /// <summary>
        /// 现行行为：面板被外部挪离 SHOW layer 后，<c>Visible</c> 的 getter 仍回意图值 true。
        /// </summary>
        /// <remarks>登记现状：旧 getter 读 <c>canvas.gameObject.layer == WINDOW_SHOW_LAYER</c>，外部改层会读回 false； <br />
        /// 改判 R1 ① 把这条副产品明确排除在契约之外，代价是「谁挪了面板的层」窗口自己不再知道。</remarks>
        [Test]
        public void CurrentBehaviour_PanelMovedOffShowLayer_VisibleStillReportsIntent()
        {
            var panel = NewPanel();
            var window = Created(panel);
            window.Visible = true;

            panel.layer = UIService.WINDOW_HIDE_LAYER;

            Assert.IsTrue(window.Visible, "现状：getter 回意图，不回读面板 layer（旧实现此处回 false）");
        }

        /// <summary>
        /// 现行行为：面板已处在 SHOW(5) 时，首次 <c>Visible = true</c> 仍会刷一次排序、发一次 <c>OnSetVisible(true)</c>。
        /// </summary>
        /// <remarks>
        /// 本格只<b>登记</b>「同 layer 早退」换位带来的这条差，不作取舍：下沉前 <c>UIWindow.Visible</c> 的早退判据住在 layer 相等这一步， <br />
        /// 排在基类记账<b>之前</b>，而预制件本身就带 SHOW 层（生产常态）时首次转可见正好命中它 ⇒ 既不发 <c>_OnSortDepth()</c> 也不发 <c>OnSetVisible(true)</c>； <br />
        /// 现在早退住在 <c>UGUIWindow.ApplyVisible</c> 里，基类只看意图是否变化，照常结算脏位、刷排序、发回执。 <br />
        /// 要改判「面板已到位就不回执」应动契约，不是动本用例。
        /// </remarks>
        [Test]
        public void CurrentBehaviour_ShowLayerPanel_FirstVisibleNowFires_RecordsDivergence()
        {
            var panel = NewPanel();
            var child = panel.transform.GetChild(0).gameObject;
            var window = Created(panel);

            // 装载时的隐藏意图已把子树写成 HIDE(2)；自制预制件本来带着 SHOW(5) 进来、旧实现装载时不碰 layer，这里把它摆回那一状态
            panel.layer = UIService.WINDOW_SHOW_LAYER;
            child.layer = UIService.WINDOW_SHOW_LAYER;
            window.SetVisibleCount = 0;
            window.SortDepthCount = 0;

            window.Visible = true;

            Assert.AreEqual(UIService.WINDOW_SHOW_LAYER, panel.layer, "现状：layer 已到位，ApplyVisible 自己早退，不重复写");
            Assert.AreEqual(1, window.SortDepthCount, "现状：转可见照刷一次排序（旧实现此处不刷）");
            Assert.AreEqual(1, window.SetVisibleCount, "现状：OnSetVisible 照发一次（旧实现此处不发）");
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>带 Canvas + GraphicRaycaster 的面板根，其下挂一枚偏移 20 的子 Canvas 与子 Raycaster。</summary>
        private GameObject NewPanel(int layer = 0)
        {
            var panel = NewGameObject("Panel", layer);
            panel.AddComponent<Canvas>();
            panel.AddComponent<GraphicRaycaster>();

            var child = NewGameObject("Child", layer);
            child.transform.SetParent(panel.transform, false);
            var childCanvas = child.AddComponent<Canvas>();
            // 子级画布要自己管序（生产预制件里的子 Canvas 同样带这个开关），否则 sortingOrder 被父级同化，差分项没得测
            childCanvas.overrideSorting = true;
            childCanvas.sortingOrder = 20;
            child.AddComponent<GraphicRaycaster>();

            return panel;
        }

        private GameObject NewGameObject(string name, int layer = 0)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(_host.transform, false);
            _panels.Add(go);
            return go;
        }

        /// <summary>走一遍装载钩子把面板绑上（停在未创建态）。</summary>
        private static ProbeWindow Loaded(GameObject fixture)
        {
            var window = new ProbeWindow { Fixture = fixture };
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);
            window.InternalLoad("Panel", null, false);
            return window;
        }

        /// <summary>同上，准备回调里补一次 <c>InternalCreate</c>：<c>OnSetVisible</c> 与「转可见结算脏位」都以 <c>_isCreate</c> 为门槛。</summary>
        private static ProbeWindow Created(GameObject fixture)
        {
            var window = new ProbeWindow { Fixture = fixture };
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);
            window.InternalLoad("Panel", w => w.InternalCreate(), false);
            return window;
        }

        /// <summary>没有面板的窗口（未绑定口径用）。</summary>
        private static ProbeWindow Unbound()
        {
            var window = new ProbeWindow();
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);
            return window;
        }

        /// <summary>把用例自备的面板物体当成加载结果交回的后端探针。</summary>
        private sealed class ProbeWindow : UGUIWindow
        {
            internal GameObject Fixture;
            internal int SetVisibleCount;
            internal int SortDepthCount;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => BindPanel(Fixture);

            protected override void OnSetVisible(bool visible) => SetVisibleCount++;

            protected override void OnSortDepth() => SortDepthCount++;
        }

        #endregion
    }
}
