using System;
using System.Collections.Generic;
using Moirai.Atropos;
using Moirai.Atropos.Input;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Service.UI
{
    /// <summary>
    /// UI 协调者的开栈编排用例：压栈与移出、当前模态、深度重排、显隐回执、每帧驱动、栈的归零与回执的结算次序，
    /// 以及 uGUI 处理器经继承走的那一圈往返。
    /// </summary>
    /// <remarks>
    /// 载体是那一份两支后端共用的账本 <see cref="UIService.SharedLedger"/>：栈序、深度、显隐、回执次序与每帧驱动一律直接叫它—— <br />
    /// 两支 handler 手里拿的就是这一份，因此这里判到的都是生产路径上的同一份存储。 <br />
    /// 要吃驱动者语义的格子（初始化、关停、按支分流、安全区落点）拿生产那两枚处理器 <see cref="UGUIHandler"/> 与 <see cref="UITKHandler"/> 走， <br />
    /// 就位的来路只有门面按 <see cref="UIServiceSettings"/> 的启用清单认领——槽位没有换入接缝，替身也进不了具体类型的槽。 <br />
    /// 两支后端的窗口在同一次会话里并进同一份栈（uGUI 轨与 UI Toolkit 轨各一只），这就是共存的样本。 <br />
    /// 探针窗的面板钩子只记账，不建 Canvas 也不建壳，故本文件不需要资产、不需要场景、也不点 <c>UIDocument</c>； <br />
    /// 安全区那两格吃真实 UI 根（<c>UIRootBinding</c> + <c>Canvas</c> + <c>CanvasScaler</c>），判的是落到根那枚 <c>RectTransform</c> 上的偏移。 <br />
    /// 线程契约：仅主线程（EditMode 用例即主线程）。
    /// </remarks>
    [TestFixture]
    public sealed class UIServiceHandlerOrchestrationTests
    {
        /// <summary>安全区落点用的参考分辨率宽档：<c>CanvasScaler</c> 的换算按这一档走。</summary>
        private const float REFERENCE_WIDTH = 1920f;

        /// <summary>安全区落点用的参考分辨率高档。</summary>
        private const float REFERENCE_HEIGHT = 1080f;

        private bool _savedPreventInteraction;
        private UIServiceHandler[] _savedEnabledHandlers;
        private GameObject _uiRootGo;
        private GameObject _uiCanvasGo;
        private readonly List<GameObject> _objects = new List<GameObject>();

        /// <summary>进门归位：两支处理器槽与那份共享持有者清回「干净域」那一份状态，压制位与启用清单按进门值快照。</summary>
        /// <remarks>
        /// 归位门只清不置：两支槽此后的来路只有 <see cref="UIService.OnInit"/> 按启用清单造那一枚，栈与停放表也是新一份， <br />
        /// 因此要吃驱动者的夹具一律先叫 <c>new UIService().OnInit()</c>，不叫任何属性就拿到驱动者的形状已不存在。 <br />
        /// 启用清单住在设置资产那一份实例上，是跨夹具的静态位：按进门值快照，出门交回，改清单的用例不给后跑的用例留配置。 <br />
        /// 压制位是无归属的全局布尔，本文件的租约那一族会写到它，出门按进门值交回。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            _savedPreventInteraction = InputService.PreventInteractionUI;
            _savedEnabledHandlers = UIServiceSettings.EnabledHandlers;
            UIService.Internal_ResetHandlerSlots();
        }

        [TearDown]
        public void TearDown()
        {
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(_savedEnabledHandlers);
            for (var i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();
            if (_uiCanvasGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_uiCanvasGo);
            }

            if (_uiRootGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_uiRootGo);
            }

            _uiCanvasGo = null;
            _uiRootGo = null;
            // 压制位与共享栈都是跨夹具的静态位：本夹具借走的这一份按进门值交回，不给后跑的用例留位
            InputService.PreventInteractionUI = _savedPreventInteraction;
        }

        #region 压入与移出 [PUSH & POP]

        /// <summary>
        /// 压栈次序：同层窗口接在该层末位之后，没有同层时接在更低层级之后，两支后端的窗口并进同一份栈。
        /// </summary>
        /// <remarks>
        /// 三只窗按 Bottom(0)→UI(1)→Bottom(0) 压入，第三次必须落在第一只之后、第二只之前； <br />
        /// 第三只取 UI Toolkit 轨的窗口类，判据是栈序而不是窗口类型——同栈混排两支轨。
        /// </remarks>
        [Test]
        public void Push_PerLayerAndCrossTrackWindows_LandInOneStackByLayerOrder()
        {
            var bottom = Window<ProbeUGUIWindow>("Bottom1", (int)UILayer.Bottom);
            var middle = Window<ProbeUGUIWindow>("Mid1", (int)UILayer.UI);
            var lateBottom = Window<ProbeUITKWindow>("Bottom2", (int)UILayer.Bottom);

            UIService.SharedLedger.Push(bottom);
            UIService.SharedLedger.Push(middle);
            UIService.SharedLedger.Push(lateBottom);

            CollectionAssert.AreEqual(new UIWindow[] { bottom, lateBottom, middle }, Stack,
                "同层的后到者紧跟在本层末位之后，更高一层的窗口仍在栈顶");
        }

        /// <summary>栈上已有同名窗口时压栈当场抬错，且不留第二只同名窗。</summary>
        [Test]
        public void Push_DuplicateWindowName_FailsFastWithoutInserting()
        {
            var first = Window<ProbeUGUIWindow>("Duplicated", (int)UILayer.Tips);
            var second = Window<ProbeUGUIWindow>("Duplicated", (int)UILayer.Tips);
            UIService.SharedLedger.Push(first);

            Assert.Throws<GameException>(() => UIService.SharedLedger.Push(second), "同名窗口不得第二次压栈");
            Assert.AreEqual(1, Stack.Count, "抬错排在插入之前：栈上只多出一只");
            Assert.AreSame(first, Stack[0], "栈上仍是先压进来的那一只");
        }

        /// <summary>
        /// 模态窗口压掉它下面那一只的可交互位，非模态的顶级提示压上来时不动下层。
        /// </summary>
        /// <remarks>
        /// 下层窗口的 <c>Interactable</c> 先置真，制造一次真转移：<c>Push</c> 写假才看得见（同值早退会把 no-op 藏起来）。 <br />
        /// 第二档把 Tips(3)（非模态层）压在 Popup(2)（模态层）之上：栈顶换了，但下层那位必须保持为真。
        /// </remarks>
        [Test]
        public void Push_ModalWindowAbove_SuppressesInteractableOfWindowBelow()
        {
            var below = Window<ProbeUGUIWindow>("BelowModal", (int)UILayer.Bottom);
            below.Interactable = true;
            UIService.SharedLedger.Push(below);

            UIService.SharedLedger.Push(Window<ProbeUGUIWindow>("ModalAbove", (int)UILayer.Popup));

            Assert.IsFalse(below.Interactable, "模态窗口压在上方时要写掉下层的可交互位");

            var belowTips = Window<ProbeUGUIWindow>("BelowTips", (int)UILayer.Bottom);
            belowTips.Interactable = true;
            UIService.SharedLedger.Push(belowTips);

            UIService.SharedLedger.Push(Window<ProbeUITKWindow>("TipsOnTop", (int)UILayer.Tips));

            Assert.IsTrue(belowTips.Interactable, "非模态的顶级提示压上来时不写下层的可交互位");
        }

        /// <summary>移出只摘掉那一只，其余保持原序。</summary>
        [Test]
        public void Pop_NamedWindow_RemovesItAndKeepsTheRestInOrder()
        {
            var first = Window<ProbeUGUIWindow>("First", (int)UILayer.Bottom);
            var second = Window<ProbeUITKWindow>("Second", (int)UILayer.UI);
            var third = Window<ProbeUGUIWindow>("Third", (int)UILayer.Popup);
            UIService.SharedLedger.Push(first);
            UIService.SharedLedger.Push(second);
            UIService.SharedLedger.Push(third);

            UIService.SharedLedger.Pop(second);

            CollectionAssert.AreEqual(new UIWindow[] { first, third }, Stack,
                "移出那一只之后其余保持原序");
        }

        #endregion

        #region 当前模态 [CURRENT MODAL]

        /// <summary>
        /// 当前模态按栈序回答最后一枚模态窗口：同栈混排两支后端的窗口时判据不变，非模态层不参与。
        /// </summary>
        /// <remarks>
        /// Bottom(0) 与 Tips(3) 都不是模态层（模态判据是 UI/Popup/System），压在它们之上的模态窗才是答案； <br />
        /// 两枚模态并存时取栈序末位那一枚，末位的非模态提示不改变答案。
        /// </remarks>
        [Test]
        public void CurrentModal_MixedTrackStack_AnswersLastModalInStackOrder()
        {
            var hud = Window<ProbeUGUIWindow>("Hud", (int)UILayer.Bottom);
            UIService.SharedLedger.Push(hud);
            Assert.IsNull(UIService.SharedLedger.CurrentModal, "只有非模态层时没有遮挡者");

            var firstModal = Window<ProbeUITKWindow>("Modal1", (int)UILayer.UI);
            UIService.SharedLedger.Push(firstModal);
            Assert.AreSame(firstModal, UIService.SharedLedger.CurrentModal, "模态窗压上来后由它回答遮挡");

            var secondModal = Window<ProbeUGUIWindow>("Modal2", (int)UILayer.Popup);
            UIService.SharedLedger.Push(secondModal);
            Assert.AreSame(secondModal, UIService.SharedLedger.CurrentModal, "两枚模态并存时取栈序末位");

            UIService.SharedLedger.Push(Window<ProbeUGUIWindow>("Tip", (int)UILayer.Tips));
            Assert.AreSame(secondModal, UIService.SharedLedger.CurrentModal, "末位的非模态提示不改变遮挡答案");
        }

        #endregion

        #region 窗口查询 [WINDOW QUERIES]

        /// <summary>
        /// 栈顶查询分全栈与单层两档：全栈取末位，单层取该层栈序末位，名称档在空层回空串而不是 null。
        /// </summary>
        /// <remarks>
        /// 压栈次序 Bottom(0)→UI(1)→UI(1)→Popup(2)，其中 System(4) 层一只都没压，用来跑空层那一档。 <br />
        /// 四只窗里两只是 UI Toolkit 轨的，答的是同一份栈——查询不分轨。
        /// </remarks>
        [Test]
        public void GetTopWindow_PerLayerAndWholeStack_AnswerByStackOrderWithinLayer()
        {
            var bottom = Window<ProbeUGUIWindow>("QBottom", (int)UILayer.Bottom);
            var firstUi = Window<ProbeUITKWindow>("QFirstUi", (int)UILayer.UI);
            var secondUi = Window<ProbeUGUIWindow>("QSecondUi", (int)UILayer.UI);
            var popup = Window<ProbeUITKWindow>("QPopup", (int)UILayer.Popup);
            UIService.SharedLedger.Push(bottom);
            UIService.SharedLedger.Push(firstUi);
            UIService.SharedLedger.Push(secondUi);
            UIService.SharedLedger.Push(popup);

            Assert.AreSame(popup, UIService.SharedLedger.GetTopWindow(), "全栈档取栈末位");
            Assert.AreSame(secondUi, UIService.SharedLedger.GetTopWindow((int)UILayer.UI), "单层档取该层栈序末位");
            Assert.AreEqual("QSecondUi", UIService.SharedLedger.GetTopWindowName((int)UILayer.UI), "名称档跟着单层档走");
            Assert.IsNull(UIService.SharedLedger.GetTopWindow((int)UILayer.System), "没压过窗的层级回 null");
            Assert.AreEqual(string.Empty, UIService.SharedLedger.GetTopWindowName((int)UILayer.System),
                "空层的名称档回空串");
        }

        /// <summary>加载查询按窗口自己的 <c>IsLoadDone</c> 回答：面板没就绪的那一只把整栈报成加载中。</summary>
        [Test]
        public void IsAnyLoading_BeforePanelLoaded_ReportsLoadingAndClearsAfterLoad()
        {
            var loading = Window<ProbeUGUIWindow>("Loading", (int)UILayer.UI);
            UIService.SharedLedger.Push(loading);
            Assert.IsTrue(UIService.SharedLedger.IsAnyLoading(), "面板没就绪的窗口在栈上时要报加载中");

            loading.InternalLoad("Panel", null, false, null);

            Assert.IsFalse(UIService.SharedLedger.IsAnyLoading(), "面板就绪后加载查询落回假");
        }

        /// <summary>
        /// 存在性查询的两档：给了窗口名就按名字判，没给时回落到窗口类型的 <c>FullName</c>；泛型入口与 Type 入口同判据。
        /// </summary>
        /// <remarks>
        /// 栈上压一只名字就是类型 <c>FullName</c> 的窗口（<c>CreateInstance</c> 缺省命名走的正是这一形状）， <br />
        /// 于是 <c>HasWindow&lt;T&gt;(null)</c> 与 <c>HasWindow(type, null)</c> 命中、换个名字与换个类型都不命中。
        /// </remarks>
        [Test]
        public void HasWindow_TypeAndGenericEntry_FallBackToTypeFullNameWhenNameMissing()
        {
            var namedByType = Window<ProbeUGUIWindow>(typeof(ProbeUGUIWindow).FullName, (int)UILayer.UI);
            UIService.SharedLedger.Push(namedByType);

            Assert.IsTrue(UIService.SharedLedger.HasWindow<ProbeUGUIWindow>(null), "名字缺省时按类型全名命中");
            Assert.IsTrue(UIService.SharedLedger.HasWindow(typeof(ProbeUGUIWindow), null), "Type 入口同一判据");
            Assert.IsFalse(UIService.SharedLedger.HasWindow<ProbeUITKWindow>(null), "类型不同、栈上无该类型全名时不命中");
            Assert.IsFalse(UIService.SharedLedger.HasWindow(typeof(ProbeUGUIWindow), "OtherName"), "给了别的名字就按名字判");
            Assert.IsTrue(UIService.SharedLedger.HasWindow(typeof(ProbeUGUIWindow), namedByType.WindowName),
                "名字与栈上一致时按名字命中");
        }

        /// <summary>按类型与名字取窗：两者都要对上，跨轨取不到人。</summary>
        [Test]
        public void GetWindowGeneric_TypeAndNameBothMustMatch_AcrossTracks()
        {
            var uguiWindow = Window<ProbeUGUIWindow>("KitOrUGUI1", (int)UILayer.UI);
            var kitWindow = Window<ProbeUITKWindow>("KitOrUGUI2", (int)UILayer.Popup);
            UIService.SharedLedger.Push(uguiWindow);
            UIService.SharedLedger.Push(kitWindow);

            Assert.AreSame(uguiWindow, UIService.SharedLedger.GetWindow<ProbeUGUIWindow>("KitOrUGUI1"), "同轨同名取到那一只");
            Assert.AreSame(kitWindow, UIService.SharedLedger.GetWindow<ProbeUITKWindow>("KitOrUGUI2"), "另一轨同理");
            Assert.IsNull(UIService.SharedLedger.GetWindow<ProbeUITKWindow>("KitOrUGUI1"), "名字对上但轨不对时取不到");
            Assert.IsNull(UIService.SharedLedger.GetWindow<ProbeUGUIWindow>("NoSuchWindow"), "名字没人应时取不到");
        }

        /// <summary>
        /// 遮挡判据按当前模态窗口的面板物体回答：模态自己与它的子物体不算被挡，栈上没有模态时一律不挡。
        /// </summary>
        /// <remarks>
        /// 探针窗把 <c>gameObject</c> 交回一枚真实 GameObject，用例再挂一个子物体与一个路人： <br />
        /// 三档分别落 <c>curModal == obj</c>、<c>obj.IsChildOf(curModal)</c> 与两条都不成立的「被挡」， <br />
        /// 空栈档与「模态没有面板物体」档都回不挡（前者 CurrentModal 为 null，后者 curModal 为 null）。
        /// </remarks>
        [Test]
        public void IsBlockedByModal_ModalsPanelOwnsItAndItsChildren_NotBlocked()
        {
            var empty = Object("BystanderOnEmptyStack");
            Assert.IsFalse(UIService.SharedLedger.IsBlockedByModal(empty), "栈上没有模态时谁都不挡");

            var modal = new ProbeOwnedWindow();
            modal.Init("OwnedModal", (int)UILayer.UI, false, "Panel", false, 10, false);
            var panel = Object("ModalPanel");
            modal.Root = panel;
            var child = Object("ModalChild");
            child.transform.SetParent(panel.transform, false);
            var bystander = Object("OtherPanel");
            UIService.SharedLedger.Push(modal);

            Assert.IsFalse(UIService.SharedLedger.IsBlockedByModal(panel), "模态自己的面板不算被挡");
            Assert.IsFalse(UIService.SharedLedger.IsBlockedByModal(child), "模态面板的子物体不算被挡");
            Assert.IsTrue(UIService.SharedLedger.IsBlockedByModal(bystander), "别的面板被模态挡住");

            var panelless = Window<ProbeUGUIWindow>("PanellessModal", (int)UILayer.Popup);
            UIService.SharedLedger.Push(panelless);
            Assert.IsFalse(UIService.SharedLedger.IsBlockedByModal(bystander),
                "末位模态没有面板物体时按 curModal 取空早退，谁都不挡");
        }

        #endregion

        #region 排序与显隐回执 [SORT & VISIBILITY RECEIPTS]

        /// <summary>同层两枚窗口按栈序拿到递增深度：栈序第一枚拿该层基址，后一枚再上一档。</summary>
        [Test]
        public void OnSortWindowDepth_TwoWindowsOnSameLayer_AssendDepthByStackOrder()
        {
            var first = Prepared("Sort1", (int)UILayer.Tips);
            var second = Prepared("Sort2", (int)UILayer.Tips);

            UIService.SharedLedger.OnSortWindowDepth((int)UILayer.Tips);

            Assert.AreEqual((int)UILayer.Tips * UIService.LAYER_DEEP, first.Depth, "栈序第一枚拿到该层基址");
            Assert.AreEqual((int)UILayer.Tips * UIService.LAYER_DEEP + UIService.WINDOW_DEEP, second.Depth,
                "同层后一枚比前一枚高一档");
        }

        /// <summary>面板就绪回执把窗口补建起来、按层落一次深度并重发显隐，三者一次做完。</summary>
        [Test]
        public void OnWindowPrepare_ReadyWindow_CreatesSortsAndShowsIt()
        {
            var window = Window<ProbeUGUIWindow>("Prepare1", (int)UILayer.UI);
            UIService.SharedLedger.Push(window);
            window.InternalLoad("Panel", null, false, null);

            UIService.SharedLedger.OnWindowPrepare(window);

            Assert.AreEqual(1, window.CreateCalls, "就绪回执要补建一次");
            Assert.AreEqual((int)UILayer.UI * UIService.LAYER_DEEP, window.Depth, "就绪回执要按层落一次深度");
            Assert.IsTrue(window.Visible, "就绪回执要把栈顶窗口置为可见");
        }

        /// <summary>
        /// 显隐回执自栈顶向下发：一枚已准备的全屏窗口把压在它下面的窗口置为不可见。
        /// </summary>
        /// <remarks>
        /// 下层窗口先手动置回可见，制造一次真转移：回执的全屏判定把假写下去才看得见； <br />
        /// 全屏判据取 <c>IsPrepare</c> 与 <c>FullScreen</c> 两位同时为真，没建起来的窗口不得遮住下层。
        /// </remarks>
        [Test]
        public void OnSetWindowVisible_FullScreenPreparedOnTop_HidesWindowsBelow()
        {
            var below = Prepared("HiddenBelow", (int)UILayer.Bottom);
            var top = Prepared("FullScreenTop", (int)UILayer.UI, true);
            below.Visible = true;

            UIService.SharedLedger.OnSetWindowVisible();

            Assert.IsTrue(top.Visible, "栈顶的全屏窗口自己保持可见");
            Assert.IsFalse(below.Visible, "被全屏窗压住的下层窗口要置为不可见");
        }

        /// <summary>显隐回执跳过挂着隐藏标志的窗口：它既不接可见也不接不可见。</summary>
        [Test]
        public void OnSetWindowVisible_HiddenWindowBelow_IsSkippedByReceipt()
        {
            var hidden = Prepared("HiddenFlag", (int)UILayer.Bottom);
            hidden.IsHide = true;
            hidden.Visible = false;
            var top = Prepared("TopAfterHidden", (int)UILayer.UI);

            UIService.SharedLedger.OnSetWindowVisible();

            Assert.IsFalse(hidden.Visible, "隐藏标志位上的窗口不被回执翻回可见");
            Assert.IsTrue(top.Visible, "栈顶窗口照常可见");
        }

        #endregion

        #region 每帧驱动 [TICK]

        /// <summary>每帧驱动按栈序自下而上逐个走到窗口的内部更新。</summary>
        [Test]
        public void Tick_PreparedAndVisibleWindows_DrivesEveryWindowInStackOrder()
        {
            var order = new List<string>();
            var first = Prepared("Tick1", (int)UILayer.Bottom);
            var second = Prepared("Tick2", (int)UILayer.UI);
            first.UpdateSink = order;
            second.UpdateSink = order;

            UIService.SharedLedger.Tick();

            CollectionAssert.AreEqual(new[] { "Tick1", "Tick2" }, order, "驱动次序按栈序自下而上，两只都走到");
        }

        /// <summary>没进准备态的窗口不被驱动：<c>Tick</c> 交出去的内部更新自己按准备位与可见位早退。</summary>
        [Test]
        public void Tick_WindowNotPrepared_StaysUndriven()
        {
            var order = new List<string>();
            var window = Window<ProbeUGUIWindow>("NotPrepared", (int)UILayer.Bottom);
            window.UpdateSink = order;
            UIService.SharedLedger.Push(window);

            UIService.SharedLedger.Tick();

            Assert.AreEqual(0, order.Count, "面板还没就绪的窗口不该走内部更新");
        }

        /// <summary>
        /// 驱动途中栈被改动（一次更新里移走了一枚窗口）时本轮立刻收尾，不再按进圈时的计数继续走。
        /// </summary>
        /// <remarks>
        /// 这一档钉的是 <c>Tick</c> 里那句栈长自检：少了它，被移除的那只之后的窗口会被漏掉或被错index驱动。 <br />
        /// 探针把移除动作挂在第一枚窗口的更新回调里，模拟「一次更新改动栈」的真实时机。
        /// </remarks>
        [Test]
        public void Tick_StackChangedMidLoop_StopsThisRound()
        {
            var first = Prepared("Mutator", (int)UILayer.Bottom);
            var second = Prepared("Removed", (int)UILayer.UI);
            var third = Prepared("Skipped", (int)UILayer.Popup);
            first.Updates = 0;
            second.Updates = 0;
            third.Updates = 0;
            first.OnUpdateHook = () => UIService.SharedLedger.Pop(second);

            UIService.SharedLedger.Tick();

            Assert.AreEqual(1, first.Updates, "改动栈的那一枚自己被驱动过");
            Assert.AreEqual(0, third.Updates, "本轮不再往下驱动");
            Assert.AreSame(third, Stack[1], "栈里剩下的窗口次序不变");
        }

        /// <summary>
        /// 两支驱动者都在位时，一帧下去整条共享栈只结算一次：门面直叫共享账本一次，两支各叫一次会数到 2。
        /// </summary>
        /// <remarks>
        /// 载体是门面的 <see cref="UIService.Tick"/>——R8 那条形状判据「不是两支各叫一次，也不是任挑一支当代驱动」就钉在这一格的计数上。 <br />
        /// 两支驱动者都由门面的启用清单认领就位，那是槽位唯一的来路；本轨帧职责走的是生产那一份（uGUI 那一轨交出去的是 UI 根的续等）。
        /// </remarks>
        [Test]
        public void Tick_TwoTrackDriversInPlace_SharedStackSettlesExactlyOncePerFrame()
        {
            new UIService().OnInit();
            Assert.IsNotNull(UIService.Internal_PeekUGUIHandler(), "量具前提坏了：按配置启用之后 uGUI 那一轨的驱动者没就位");
            Assert.IsNotNull(UIService.Internal_PeekUITKHandler(), "量具前提坏了：按配置启用之后 UI Toolkit 那一轨的驱动者没就位");
            var window = Prepared("SettleOnce", (int)UILayer.Bottom);
            window.Updates = 0;

            new UIService().Tick(0f, 0f);

            Assert.AreEqual(1, window.Updates, "一帧只结算一次：两支各驱一次会数到 2");
        }

        /// <summary>
        /// 只剩 UI Toolkit 那一枚驱动者在位时整条栈照旧结算：门面若仍钉在 uGUI 那一支上，这一格数到 0，而 <see cref="UIService.IsValid"/> 回真。
        /// </summary>
        /// <remarks>
        /// 这一档是静默致命档的形状：uGUI 那一枚不在位不代表栈上没有窗——两只窗可能是 UI Toolkit 轨开出来的。 <br />
        /// 只启用 UI Toolkit 那一支，uGUI 那一枚槽因此空着——这就是「配置只启用一支」那一档。 <br />
        /// 门面的 <c>Tick</c> 直接叫共享账本，因此这一帧照样结算一次；把它改回「任挑一支当代驱动」就红在这里。
        /// </remarks>
        [Test]
        public void Tick_OnlyUITKDriverInPlace_SharedStackStillSettlesOnce()
        {
            EnableOnlyUITKTrackAndInit();
            Assert.IsNotNull(UIService.Internal_PeekUITKHandler(), "量具前提坏了：配置只启用 UI Toolkit 那一支时它没就位");
            Assert.IsNull(UIService.Internal_PeekUGUIHandler(), "量具前提坏了：这一格判的是只剩 UI Toolkit 那一枚的档");
            var window = Prepared("KitOnlyFrame", (int)UILayer.Bottom);
            window.Updates = 0;

            Assert.IsTrue(UIService.IsValid, "量具前提坏了：UI Toolkit 那一枚就位时门面就该算有效");

            new UIService().Tick(0f, 0f);

            Assert.AreEqual(1, window.Updates, "uGUI 那一枚不在位不是静默的理由：这一帧栈要结算一次");
        }

        #endregion

        #region 按配置启用后端 [ENABLEMENT FROM SETTINGS]

        /// <summary>一支都没启用是 <see cref="UIService.OnInit"/> 当场抬错：不等第一次开窗，也不静默兜底造一支。</summary>
        /// <remarks>
        /// 抬错之后两支槽都不该有驱动者、门面该算无效——「配置没选中」这一档在运行期不留下任何半就位的状态。
        /// </remarks>
        [Test]
        public void OnInit_NoBackendEnabled_ThrowsAtInitTime()
        {
            UIServiceSettings.Internal_SetEnabledHandlers(Array.Empty<UIServiceHandler>());

            Assert.Throws<GameException>(() => new UIService().OnInit(), "清单为空要在初始化当场抬错");
            Assert.IsNull(UIService.Internal_PeekUGUIHandler(), "抬错之后不留 uGUI 轨的驱动者");
            Assert.IsNull(UIService.Internal_PeekUITKHandler(), "抬错之后不留 UI Toolkit 轨的驱动者");
            Assert.IsFalse(UIService.IsValid, "一支都没启用时门面不算有效");
        }

        /// <summary>同一轨在清单里出现两项 ⇒ 抬错，不「取第一个」；文案点名是哪一轨。</summary>
        /// <remarks>
        /// 认领门用 compare-exchange 占位，第二次占不上就当场抬错：既不换掉在位的那一枚，也不静默收下第二枚，因此这一档不会留下两枚实例， <br />
        /// 但也不能悄悄启用一支就往下走。第一项已在位、另一轨没被这一份清单提到，槽因此仍空着。 <br />
        /// 这一档判的是两枚<b>不同实例</b>；同一枚实例被列两次走的是可重入那条空操作，由下一格钉成判据。
        /// </remarks>
        [Test]
        public void OnInit_SameTrackEnabledTwice_ThrowsInsteadOfTakingTheFirst()
        {
            UIServiceSettings.Internal_SetEnabledHandlers(new UIServiceHandler[]
            {
                new UGUIHandler(),
                new UGUIHandler(),
            });

            var error = Assert.Throws<GameException>(() => new UIService().OnInit(), "同一轨两项要抬错，不取第一个");
            StringAssert.Contains("UGUI", error.Message, "文案点名重复的是哪一轨");
            Assert.IsNull(UIService.Internal_PeekUITKHandler(), "这份清单没提 UI Toolkit 那一轨");
        }

        /// <summary>同一枚实例被清单列两次是按可重入空操作收下的：不抬错、也不重复挂广播。</summary>
        /// <remarks>
        /// 「资产里手抖把同一支写了两遍」与「同轨两枚不同实例」是两档：前者结果仍是一支一挂，后者才是要否掉的那一档（<c>OnInit_SameTrackEnabledTwice_…</c>）。 <br />
        /// 这一格数的是认领门里那条同实例早退真的生效——把早退删掉，帧广播就挂成两条。
        /// </remarks>
        [Test]
        public void OnInit_SameInstanceListedTwice_RegistersOnce()
        {
            var twice = new UGUIHandler();
            UIServiceSettings.Internal_SetEnabledHandlers(new UIServiceHandler[] { twice, twice });

            Assert.DoesNotThrow(() => new UIService().OnInit(), "同一枚实例重复出现按可重入的空操作收下");
            Assert.AreSame(twice, UIService.Internal_PeekUGUIHandler(), "槽里仍是清单里那一份实例");
            Assert.AreEqual(1, UIService.Internal_PeekTrackTickSubscriberCount(), "同实例两项只挂一条帧广播");
        }

        /// <summary>清单里某一项是 <c>null</c> ⇒ 当场抬错并点名第几项，没有「跳过空项」那一档。</summary>
        /// <remarks>
        /// 后端是固定集合，<c>null</c> 表示清单本身没配好而不是「这一项没启用」。 <br />
        /// 抬错排在认领循环中间：第一项已经注册成功、门面不补半程关停——配置面坏了本就是启动失败，这一格按现状钉住那一档（收它的是 <c>OnShutdown</c> 或夹具的归位门）。
        /// </remarks>
        [Test]
        public void OnInit_NullEntryInSettingsList_ThrowsAndNamesTheIndex()
        {
            UIServiceSettings.Internal_SetEnabledHandlers(new UIServiceHandler[]
            {
                new UGUIHandler(),
                null,
            });

            var error = Assert.Throws<GameException>(() => new UIService().OnInit(), "清单里的 null 项要抬错，不静默跳过");
            StringAssert.Contains("#1", error.Message, "文案点名的是第几项");
            Assert.IsNotNull(UIService.Internal_PeekUGUIHandler(), "抬错之前第一项已就位，门面不在抬错后回滚");
            Assert.IsNull(UIService.Internal_PeekUITKHandler(), "这份清单没提 UI Toolkit 那一轨");
        }

        /// <summary>第二轮 <see cref="UIService.OnInit"/> 不重复挂帧广播：两支各一条，计数不因再走一遍OnInit 而翻倍。</summary>
        /// <remarks>
        /// 订阅挂在认领门里、认领门对同一枚实例早退，这一格数的是那条早退真的生效——摘掉早退就是 4 条而不是 2 条。
        /// </remarks>
        [Test]
        public void OnInit_SecondPassWithTheSameEntries_SubscribesEachTrackTickOnce()
        {
            EnableBothTracksAndInit();
            Assert.AreEqual(2, UIService.Internal_PeekTrackTickSubscriberCount(), "量具前提坏了：两支各挂一条帧广播");

            new UIService().OnInit();

            Assert.AreEqual(2, UIService.Internal_PeekTrackTickSubscriberCount(), "同一枚实例再注册不重复挂广播");
        }

        /// <summary>
        /// 两支都启用并走一遍生产初始化：本夹具里要吃驱动者的用例都从这一道起步，不再叫任何属性去造驱动者。
        /// </summary>
        private void EnableBothTracksAndInit()
        {
            UIServiceSettings.Internal_SetEnabledHandlers(new UIServiceHandler[]
            {
                new UGUIHandler(),
                new UITKHandler(),
            });
            new UIService().OnInit();
        }

        /// <summary>
        /// 「只剩 UI Toolkit 那一枚在位」这一档唯一的造法：把启用清单换成只提 UI Toolkit 那一支，再走一遍生产初始化。
        /// </summary>
        /// <remarks>
        /// 这一档不靠归位门手工造私有状态，也不由测试往槽里放对象：清单进配置、槽位仍只由 <see cref="UIService.OnInit"/> 按清单填。 <br />
        /// 清单由夹具在进门时快照、出门交回（见 <see cref="SetUp"/> 与 <see cref="TearDown"/>）。
        /// </remarks>
        private void EnableOnlyUITKTrackAndInit()
        {
            UIServiceSettings.Internal_SetEnabledHandlers(new UIServiceHandler[]
            {
                new UITKHandler(),
            });
            new UIService().OnInit();
        }

        /// <summary>
        /// 未启用那一轨的开窗腿当场抬错，文案点名是哪一轨、去哪一处启用：没有「顺手替本轨造一枚驱动者」那一条隐式启用。
        /// </summary>
        /// <remarks>
        /// 三条腿各判一次，因为 uGUI 那一轨的开窗实现住的两处（同步/异步共用一枚、等待腿另一枚）都在守卫之后： <br />
        /// 同步与异步两支是同步抛（<c>void</c> 腿），等待那一支把异常收进交回的 <c>UniTask</c> 里，因此按 <c>ThrowsAsync</c> 读。 <br />
        /// 判据是异常本身与文案里的两枚名字，而不是「栈上没多出窗」：抬错排在压栈之前，栈因此原样——最后一行数的是这件事。 <br />
        /// 这一条分支与「轨专有查询答 null」那条分支（<see cref="UGUISpecificFacadeQueries_UnenabledUGUITrack_AnswerNullWhileTheOtherLegStillOpens"/>）不是一条路，两格各钉一头。
        /// </remarks>
        [Test]
        public void ShowUI_UnenabledUGUITrack_LegThrowsAndNamesTheTrackAndTheSettings()
        {
            EnableOnlyUITKTrackAndInit();
            Assert.IsNull(UIService.Internal_PeekUGUIHandler(), "量具前提坏了：这一格判的是 uGUI 那一轨没被启用的档");

            var error = Assert.Throws<GameException>(
                () => UIService.ShowUI<ProbeUGUIWindow>("DisabledTrack"), "未启用那一轨的同步腿不得静默落空");
            StringAssert.Contains("UGUI", error.Message, "文案点名是没启用的哪一轨");
            StringAssert.Contains(nameof(UIServiceSettings), error.Message, "文案点名该去哪一处启用");

            Assert.Throws<GameException>(
                () => UIService.ShowUIAsync<ProbeUGUIWindow>("DisabledTrackAsync"), "异步腿走的也是同一道守卫");
            Assert.Throws<GameException>(
                () => UIService.ShowUI(typeof(ProbeUGUIWindow), "DisabledTrackByType"), "Type 形入口的 uGUI 档同判据");
            Assert.Throws<GameException>(
                () => UIService.ShowUIAsyncAwait<ProbeUGUIWindow>("DisabledTrackAwait").GetAwaiter().GetResult(),
                "等待腿把异常收进交回的那一份 UniTask");

            Assert.AreEqual(0, Stack.Count, "抬错排在压栈之前：栈上一只窗都不多");
        }

        /// <summary>
        /// 未启用那一轨的轨专有查询答 <c>null</c> 而不抬错，同一时刻被启用那一轨照样开得起来：开窗腿与轨专有查询是两条分支。
        /// </summary>
        /// <remarks>
        /// <see cref="UIService.UIRoot"/> 与 <see cref="UIService.UICamera"/> 只问 uGUI 那一轨，那一轨没启用时它们答不出任何东西—— <br />
        /// 这一档若也抬错，调用方就没法用「先问根在不在」判配置；反过来把开窗腿也改成静默落空就是回到隐式启用那一档。 <br />
        /// 判据两头都读：uGUI 未启用时两枚查询为 null、门面仍有效，而被启用的 UI Toolkit 那一轨开窗真落进共享栈。
        /// </remarks>
        [Test]
        public void UGUISpecificFacadeQueries_UnenabledUGUITrack_AnswerNullWhileTheOtherLegStillOpens()
        {
            EnableOnlyUITKTrackAndInit();

            Assert.IsTrue(UIService.IsValid, "量具前提坏了：被启用的那一轨就位时门面就该算有效");
            Assert.IsNull(UIService.UIRoot, "未启用的 uGUI 轨答不出 UI 根");
            Assert.IsNull(UIService.UICamera, "同判据的另一枚轨专有查询");

            UIService.ShowUI<ProbeUITKWindow>("EnabledTrackOpens");

            Assert.IsNotNull(UIService.GetTopWindow(), "未启用另一轨不是本轨开窗落空的理由");
            Assert.AreEqual("EnabledTrackOpens", UIService.GetTopWindow().WindowName, "开出来的就是刚叫开的那一只");
        }

        #endregion

        #region 安全区域 [SAFE AREA]

        /// <summary>
        /// 刘海屏模拟用的是两支共用那一份换算：矩形按屏幕取向换算一次，再交回本轨的落点写到 UI 根的偏移上。
        /// </summary>
        /// <remarks>
        /// 落点读的是真实 <see cref="UGUIHandler"/> 写进根那枚 <c>RectTransform</c> 的两个偏移角点——按 <c>CanvasScaler</c> 的参考分辨率换算； <br />
        /// 期望值独立按 iPhone X 那一档基准重算一遍，因此改数值、改取向判据、把这份换算复制成第二份、 <br />
        /// 或让本轨的落点不再被叫到，都会红。<see cref="UGUIHandler"/> 没有 <c>SimulateIPhoneXNotchScreen</c> 的覆写——基类那一份就是它的实现。
        /// </remarks>
        [Test]
        public void SimulateIPhoneXNotchScreen_UGUITrackDriver_WritesTheNotchRectIntoTheRootOffsets()
        {
            var expected = Screen.height > Screen.width
                ? new Rect(0f, Screen.height * (102f / 2436f), Screen.width, Screen.height * (2202f / 2436f))
                : new Rect(Screen.width * (132f / 2436f), Screen.height * (63f / 1125f),
                    Screen.width * (2172f / 2436f), Screen.height * (1062f / 1125f));

            Assert.AreEqual(expected, UIServiceHandler.ComputeIPhoneXNotchSafeRect(Screen.width, Screen.height),
                "两支后端与协调者共用的就是这一份换算");

            var handler = new UGUIHandler();
            var rootRect = BindUGUIRoot(handler);
            // 偏移先拨成零位：落点没被叫到与落点写到同一位，靠这两格的读数是分不开的
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            handler.SimulateIPhoneXNotchScreen();

            var offsets = ExpectedOffsets(expected);
            Assert.AreEqual(offsets[0], rootRect.offsetMin, "刘海安全区按参考分辨率换算后写进根的左下偏移");
            Assert.AreEqual(offsets[1], rootRect.offsetMax, "同一枚换算写进根的右上偏移");
        }

        /// <summary>
        /// 刘海屏那一档从门面进来也落得到本轨：门面广播一次，槽里那枚驱动者把根偏移写成同一份换算。
        /// </summary>
        /// <remarks>
        /// 与上一格的分工：上一格直接叫驱动者实例，判的是那份换算与那个落点；这一格叫门面，判的是**订阅真的挂上了**—— <br />
        /// 认领那一步没把本轨的 <c>SimulateIPhoneXNotchScreen</c> 挂进广播，根的偏移就停在零位。收件人是槽里那一枚，不是夹具自造的一枚。
        /// </remarks>
        [Test]
        public void SimulateIPhoneXNotchScreen_FacadeBroadcast_WritesTheNotchRectIntoTheSlottedDriversRoot()
        {
            EnableBothTracksAndInit();
            var handler = UIService.UGUIHandler;
            try
            {
                var rootRect = BindUGUIRoot(handler);
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;

                UIService.SimulateIPhoneXNotchScreen();

                var offsets = ExpectedOffsets(UIServiceHandler.ComputeIPhoneXNotchSafeRect(Screen.width, Screen.height));
                Assert.AreEqual(offsets[0], rootRect.offsetMin, "门面的那条广播要叫到槽里这枚驱动者的落点：左下偏移");
                Assert.AreEqual(offsets[1], rootRect.offsetMax, "同一份换算经门面广播写进根的右上偏移");
            }
            finally
            {
                // 叫出来的是槽里那一枚生产驱动者：出门按夹具既有的形状把它关回去，不把已退役的实例留给后跑的用例
                handler.Internal_Shutdown();
            }
        }

        #endregion

        #region 共享存储的归零 [SHARED STORAGE RESET]

        /// <summary>
        /// 复用实例重入初始化时共享栈保持原样：归零已上收到门面一次，协调者的 <c>OnInit</c> 不再抹这份两支共用的存储。
        /// </summary>
        /// <remarks>
        /// <c>FrameworkHandler.Internal_Init</c> 有已初始化早退，因此这一格仍走「初始化→压栈→关停→再压一只→再初始化」 <br />
        /// 那条复用实例的真实次序，判据按共享语义反过来读：两轮压进去的窗都还在——抹它的那一刀归 <see cref="FacadeReset_EmptiesTheSharedStackOnceAndReleasesGlobalSuppressionFlag"/>。 <br />
        /// 载体是 UI Toolkit 轨那一枚生产驱动者、栈上两只是 uGUI 轨的窗：关停交进来的只有本轨认得的那些，这一格因此判得准「初始化不抹栈」。
        /// </remarks>
        [Test]
        public void Init_ReenteredWithLeftoverWindow_KeepsTheSharedStack()
        {
            var driver = new UITKHandler();
            driver.Internal_Init();
            var leftover = Window<ProbeUGUIWindow>("Round1", (int)UILayer.UI);
            UIService.SharedLedger.Push(leftover);
            driver.Internal_Shutdown();
            UIService.SharedLedger.Push(Window<ProbeUITKWindow>("Round2", (int)UILayer.UI));

            driver.Internal_Init();

            Assert.AreEqual(2, Stack.Count, "重入初始化不清共享栈：归零只剩门面那一处");
            Assert.AreSame(leftover, Stack[0], "上一轮遗留的那只仍在栈上，没跟着被抹掉");
        }

        /// <summary>另一枚驱动者初始化时不抹这一枚留下的窗口：「第二枚抹第一枚」这一档红线由这一格把住。</summary>
        /// <remarks>
        /// 两枚指的是两枚 handler 实例，不是两轨：它们手里是同一份持有者，所以第二枚的初始化只能补自己的后端位，不能动共享栈。 <br />
        /// 读的是另一枚自己的转发口——同一份存储换一枚实例来读，判据不因为「反正都是那一枚」而变松。
        /// </remarks>
        [Test]
        public void Init_SecondDriver_LeavesTheFirstOnesWindowOnTheStack()
        {
            var first = new UGUIHandler();
            first.Internal_Init();
            var mine = Window<ProbeUGUIWindow>("FirstDriverWindow", (int)UILayer.UI);
            UIService.SharedLedger.Push(mine);

            var other = new UITKHandler();
            other.Internal_Init();

            Assert.AreEqual(1, Stack.Count, "另一枚驱动者初始化后，共享栈上仍只有那一只");
            Assert.AreSame(mine, other.GetTopWindow(), "另一枚读到的就是这一枚压进去的那只窗");
        }

        /// <summary>关停一枚驱动者不再清空共享栈，也不再丢弃压制权的归属：那两件事都不再属于驱动者。</summary>
        /// <remarks>
        /// 共享之后关停一清栈就是抹掉另一支的窗口，因此这一格判两处**都没发生**——栈还在、归属也还在，收口的工作交给门面。 <br />
        /// 载体是 UI Toolkit 轨那一枚驱动者、栈上是 uGUI 轨的窗：一次关停只交本轨认得的窗进共享栈的关闭流程，栈因此原样留着。 <br />
        /// 归属才是这里的可观测面：<c>InputService.PreventInteractionUI</c> 在没有输入后端时写入静默忽略，判它不判归属会把量具建成空断言。
        /// </remarks>
        [Test]
        public void Shutdown_OfOneDriver_LeavesStackAndLeaseAlone()
        {
            var driver = new UITKHandler();
            driver.Internal_Init();
            var modal = Prepared("HeldModal", (int)UILayer.UI);
            Assert.IsTrue(HoldLease(modal), "量具前提坏了：模态窗口没占到压制权");

            driver.Internal_Shutdown();

            Assert.AreEqual(1, Stack.Count, "驱动者的关停不再清共享栈");
            Assert.IsTrue(LeaseHeldBy(modal), "压制权的归属还在这只窗手里：关停一支不交回它");
        }

        /// <summary>门面的那一次归零把两件事一起做完：共享栈清空，上一轮没还回来的全局压制位交回。</summary>
        /// <remarks>
        /// 压制位先由一枚模态窗口按生产口径占住（<c>InteractionLease.Acquire</c> 回真才写位）， <br />
        /// 占位与清位是同一笔事务的两端，这一格因此制造一次真占再由 <see cref="UIService.Internal_ResetSharedLedger"/> 真清—— <br />
        /// 门面 <see cref="UIService.OnInit"/> 与 <see cref="UIService.OnShutdown"/> 叫的就是这一枚事务，两支后端各自就位与否都不影响它只被做一次的形状。
        /// </remarks>
        [Test]
        public void FacadeReset_EmptiesTheSharedStackOnceAndReleasesGlobalSuppressionFlag()
        {
            var modal = Prepared("FacadeHeldModal", (int)UILayer.UI);
            Assert.IsTrue(HoldLease(modal), "量具前提坏了：模态窗口没占到压制权");
            InputService.PreventInteractionUI = true;

            UIService.Internal_ResetSharedLedger();

            Assert.AreEqual(0, Stack.Count, "归零收在门面：这一次把共享栈清干净");
            Assert.IsFalse(LeaseHeldBy(modal), "上一轮未交还的归属在这同一笔事务里被丢弃");
            Assert.IsFalse(InputService.PreventInteractionUI, "压制位跟着归属一起清掉（没接输入后端时按静默档读，判据落在上一行的归属上）");
        }

        #endregion

        #region 只有一条栈 [ONE STACK]

        /// <summary>
        /// 共栈哨兵（R8「窗口栈只有一条」的量具）：两支生产驱动者 <c>Internal_PeekStack()</c> 交出的是同一枚栈实例，不是两份相等的拷贝。
        /// </summary>
        /// <remarks>
        /// 持有者交的是活引用（<c>UIWindowLedger.PeekStack()</c> 直出栈本体，经 <c>UIServiceHandler.Internal_PeekStack()</c> 转发），同一性当场可判。 <br />
        /// 两枚驱动者取的都是门面那一份 <see cref="UIService.SharedLedger"/>，因此这一格判的是生产路径那两枚具体处理器，而不是替身的巧合。
        /// </remarks>
        [Test]
        public void SharedStack_TwoProductionHandlers_PeekTheSameStackInstance()
        {
            var ugui = new UGUIHandler();
            var uikit = new UITKHandler();

            Assert.AreSame(ugui.Internal_PeekStack(), uikit.Internal_PeekStack(),
                "两支驱动者的栈必须是同一枚实例：R8 只有一条栈");
            Assert.AreSame(Stack, ugui.Internal_PeekStack(),
                "门面的持有者交出的也是同一枚——不存在第二份栈给谁去转发");
        }

        /// <summary>
        /// 共栈哨兵的时序档：驱动者造在「那份共享存储被换掉」之前，取用的仍是门面<b>当前</b>那一份，而不是它出生那一刻定格的那一份。
        /// </summary>
        /// <remarks>
        /// 配置里的驱动者由 <c>SerializeReference</c> 的资产反序列化器在任意时刻造出来，那一刻门面的持有者未必已归位； <br />
        /// 把存储绑在构造期，写栈走旧那一份、查询走新那一份，栈看着永远不干净。守卫取引用同一性：构造后先把存储抹掉一次再认领。
        /// </remarks>
        /// <remarks>
        /// 配置里的驱动者由 <c>SerializeReference</c> 的资产反序列化器在任意时刻造出来，那一刻门面的持有者未必已归位； <br />
        /// 把存储绑在构造期，写栈走旧那一份、查询走新那一份。这一格先往<b>旧那一份里压一只真窗</b>再换掉存储， <br />
        /// 让「钉在旧那一份」的一侧可观测地非空：快照版本会答出那只窗，现读版本答 null。守卫取持有者本身，两份都印得出来。
        /// </remarks>
        [Test]
        public void SharedStack_HandlerBornBeforeStorageRenewal_ReadsTheCurrentStack()
        {
            var handler = new UGUIHandler();
            var stale = Window<ProbeUITKWindow>("RenewalStale", (int)UILayer.UI);
            UIService.SharedLedger.Push(stale);
            UIService.Internal_ResetHandlerSlots();

            UIService.Internal_ClaimUGUITrack(handler);

            Assert.IsNull(handler.GetTopWindow(), "归位门抹掉的那一份里的窗不该跟着处理器进新的存储");
            Assert.AreSame(UIService.SharedLedger, handler.Internal_PeekLedger(),
                "处理器读的必须是门面当前那一份持有者，不是它出生时那一份");
        }

        /// <summary>同一枚栈的第二判据是行为而不是引用：一支压进去的窗，另一支的栈顶查询必须答得出它。</summary>
        /// <remarks>
        /// 与上一格是一对：引用相同而另一支读的是别的存储，照样红在这一格。 <br />
        /// 压栈直接叫那份共享持有者，查询走另一枚驱动者自己的转发口，两轨的探针窗因此并进同一条栈序。
        /// </remarks>
        [Test]
        public void SharedStack_PushOnSharedLedger_ShowsUpInTheOtherDriversTopWindow()
        {
            var ugui = new UGUIHandler();
            var pushed = Window<ProbeUITKWindow>("OtherDriverTop", (int)UILayer.UI);
            UIService.SharedLedger.Push(pushed);

            Assert.IsNotNull(ugui.GetTopWindow(), "另一支的栈顶查询要答出这一支压进去的那只窗");
            Assert.AreSame(pushed, ugui.GetTopWindow(), "答的就是同一只窗口实例，不是它的副本");
        }

        /// <summary>
        /// 共栈哨兵的生产档：门面 <see cref="UIService.OnInit"/> 交出来的两支驱动者，手里是同一枚栈实例，
        /// 且 uGUI 腿真开出的那只窗由 UI Toolkit 轨那一枚的栈顶查询答出。
        /// </summary>
        /// <remarks>
        /// 上面两格判的是「两枚驱动者取同一份存储」，这一格判的是<b>生产路径真的交出了两支</b>： <br />
        /// <see cref="UGUIHandler"/> 与 <see cref="UITKHandler"/> 各由门面的启用清单认领一枚、各跑一次自己的初始化，拿的都是 <see cref="UIService.SharedLedger"/>。 <br />
        /// 把取用改回「每一枚 handler 自己 new 一份持有者」，下面两行一起红——这就是 R8 那条裁定唯一的量具。 <br />
        /// 两支槽归位排在 <c>finally</c>，造出来的生产处理器也在那一处关停收口（与 <see cref="UIService.OnShutdown"/> 同款次序），红也不漏给后序用例。
        /// </remarks>
        [Test]
        public void SharedStack_TwoProductionTrackHandlers_ShareOneStackAndEachOthersTopWindow()
        {
            try
            {
                new UIService().OnInit();

                var ugui = UIService.Internal_PeekUGUIHandler();
                var uikit = UIService.Internal_PeekUITKHandler();
                Assert.IsNotNull(ugui, "量具前提坏了：门面没把 uGUI 轨的驱动者交出来");
                Assert.IsNotNull(uikit, "量具前提坏了：门面没把 UI Toolkit 轨的驱动者交出来");
                Assert.AreSame(ugui.Internal_PeekStack(), uikit.Internal_PeekStack(),
                    "两支生产 handler 手里必须是同一枚栈实例：R8 只有一条栈");

                UIService.ShowUI<HandlerProbeWindow>("ProductionCrossProbe", "ProductionCrossProbe", false);
                var opened = ugui.GetWindow<HandlerProbeWindow>("ProductionCrossProbe");
                Assert.IsNotNull(opened, "量具前提坏了：uGUI 腿没把窗口开进那条栈");
                _objects.Add(opened.gameObject);

                Assert.AreSame(opened, uikit.GetTopWindow(), "另一轨的驱动者答出的栈顶就是这一轨开出的那只窗");
            }
            finally
            {
                UIService.Internal_PeekUGUIHandler()?.Internal_Shutdown();
                UIService.Internal_PeekUITKHandler()?.Internal_Shutdown();
                UIService.Internal_ResetHandlerSlots();
            }
        }

        /// <summary>
        /// 共栈判据按派生类普查：运行时装配里每一枚 <see cref="UIServiceHandler"/> 的具体派生类都要有一条构造路径。
        /// </summary>
        /// <remarks>
        /// 这一格把「新立一轨不许自带一份存储」升成派生类清单：新枚处理器没进构造清单就红在这里，判据只遍历 API 形状取派生类、经既有的 internal 门缝取引用， <br />
        /// 不反射读写字段（房内口径见 <c>CLAUDE.md</c>「测试可见性」）。 <br />
        /// 清单就是运行时装配里那两枚具体派生类：内建两支后端各一枚，槽位收的也是这两枚的类型。 <br />
        /// 「不许自带第二份持有者」那一半原本由这里比引用，<c>Ledger</c> 改成现读之后那一比恒真（两边都是门面那一位）， <br />
        /// 有牙的版本挪到 <c>SharedStack_HandlerBornBeforeStorageRenewal_ReadsTheCurrentStack</c>：那一格先换掉存储再认领，快照与现读在那一档分得出。
        /// </remarks>
        [Test]
        public void SharedStack_EveryHandlerDerivedType_IsCoveredByTheConstructionCensus()
        {
            var constructed = new List<UIServiceHandler>
            {
                new UGUIHandler(),
                new UITKHandler(),
            };

            foreach (var derived in typeof(UIServiceHandler).Assembly.GetTypes())
            {
                if (derived.IsAbstract || !typeof(UIServiceHandler).IsAssignableFrom(derived)) continue;

                Assert.IsTrue(constructed.Exists(handler => handler.GetType() == derived),
                    "派生类没进共栈 census 的构造清单：新增一轨要把它加进来才判得到它，类型为 " + derived.Name);
            }
        }

        /// <summary>
        /// 只剩 UI Toolkit 那一枚驱动者在位时，门面上的跨轨查询与关窗照旧答得出、也照旧结算：这一档钉的是那条栈而不是 uGUI 那一支。
        /// </summary>
        /// <remarks>
        /// 这一族入口直叫那份共享持有者，与哪一轨在位无关：写成只认 uGUI 那一支的取用（<c>s_UGUIHandler?.</c>）就红在这一档——门面仍回真，栈上却有答不出的窗。 <br />
        /// 只剩 UI Toolkit 那一枚在位这一档由「归位门 + 只启用 UI Toolkit 那一支」造出来，uGUI 那一枚槽因此空着。 <br />
        /// 三行读数（栈顶、存在性、模态）加一次关窗后的栈长，把「查询·关隐不分轨」钉成一条判据。
        /// </remarks>
        [Test]
        public void FacadeQueriesAndClose_OnlyUITKDriverInPlace_AnswerAndSettleOnTheSharedStack()
        {
            EnableOnlyUITKTrackAndInit();
            Assert.IsNotNull(UIService.Internal_PeekUITKHandler(), "量具前提坏了：配置只启用 UI Toolkit 那一支时它没就位");
            Assert.IsNull(UIService.Internal_PeekUGUIHandler(), "量具前提坏了：这一格判的是只剩 UI Toolkit 那一枚的档");
            var window = Prepared("KitOnlyQuery", (int)UILayer.UI);

            Assert.IsTrue(UIService.IsValid, "量具前提坏了：只剩 UI Toolkit 那一枚时门面就该算有效");
            Assert.AreSame(window, UIService.GetTopWindow(), "门面的栈顶查询答的是那条共享栈上的那只窗");
            Assert.IsTrue(UIService.HasWindow<ProbeUGUIWindow>("KitOnlyQuery"), "存在性查询同样不分轨");
            Assert.IsNotNull(UIService.CurrentModal, "模态查询读的也是那一条栈");

            UIService.CloseUI<ProbeUGUIWindow>("KitOnlyQuery");

            Assert.AreEqual(0, Stack.Count, "uGUI 那一枚不在位不是关窗落空的理由");
        }

        /// <summary>
        /// uGUI 专有的后端资源不由另一轨代答：只剩 UI Toolkit 那一枚在位时门面的 UI 根与摄像机仍答 null，而门面本身有效。
        /// </summary>
        [Test]
        public void UGUISpecificFacadeProperties_OnlyUITKDriverInPlace_AnswerNull()
        {
            EnableOnlyUITKTrackAndInit();
            Assert.IsNotNull(UIService.Internal_PeekUITKHandler(), "量具前提坏了：配置只启用 UI Toolkit 那一支时它没就位");
            Assert.IsNull(UIService.Internal_PeekUGUIHandler(), "量具前提坏了：这一格判的是只剩 UI Toolkit 那一枚的档");

            Assert.IsTrue(UIService.IsValid, "量具前提坏了：只剩 UI Toolkit 那一枚时门面就该算有效");
            Assert.IsNull(UIService.UIRoot, "UI Toolkit 轨没有 Canvas 根，不得代答 uGUI 轨的资源");
            Assert.IsNull(UIService.UICamera, "UI Toolkit 轨没有本轨专用的摄像机，同判据");
        }

        /// <summary>
        /// 安全区按支驱动：两支都在位时各叫一次各自的落点，门面既不替谁换算、也不任挑一支去落。
        /// </summary>
        /// <remarks>
        /// 两支驱动者都由启用清单认领就位，落点读的是真实 <see cref="UGUIHandler"/> 写进 UI 根那枚 <c>RectTransform</c> 的两个偏移角点； <br />
        /// UI Toolkit 那一枚的落点目前不做事，叫到与否读不出差别，这一格因此判 uGUI 那一半真的落了地—— <br />
        /// 把门面改回「只叫 UI Toolkit 那一枚」或「任挑一支」都红在下面两行：根的偏移停在被拨零的那一位上。
        /// </remarks>
        [Test]
        public void ApplyScreenSafeRect_TwoTrackDriversInPlace_UGUILandingWritesTheCallersRect()
        {
            EnableBothTracksAndInit();
            var handler = UIService.UGUIHandler;
            Assert.IsNotNull(UIService.Internal_PeekUITKHandler(), "量具前提坏了：UI Toolkit 那一轨的驱动者没就位");
            var rootRect = BindUGUIRoot(handler);
            var given = new Rect(1f, 2f, 3f, 4f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            UIService.ApplyScreenSafeRect(given);

            var offsets = ExpectedOffsets(given);
            Assert.AreEqual(offsets[0], rootRect.offsetMin, "调用方给的那枚矩形由 uGUI 这一轨的落点写进根的左下偏移");
            Assert.AreEqual(offsets[1], rootRect.offsetMax, "同一枚矩形写进根的右上偏移");
        }

        /// <summary>
        /// 窗口的 <c>Close</c> 回叫交进那条共享栈：两支轨的窗各结算一次，栈上读得出真效果。
        /// </summary>
        /// <remarks>
        /// R8 重定路由之后回叫不认「哪一枚是主协调者」，它叫的是共享持有者；守卫只剩 <see cref="UIService.IsValid"/> 一道。 <br />
        /// 两支各走一遍：判据是「这一只从栈上移走了、另一只还在」，不看哪一轨的驱动者在位——把路由改回单点就红在另一轨那半段。
        /// </remarks>
        [Test]
        public void Close_WindowCallbackOnBothTracks_SettlesOnTheSharedStack()
        {
            EnableBothTracksAndInit();
            Assert.IsNotNull(UIService.Internal_PeekUGUIHandler(), "量具前提坏了：回叫的守卫认的是门面有驱动者在位");
            var ugui = Prepared("CallbackUGUI", (int)UILayer.Bottom);
            var kit = PreparedKit("CallbackKit", (int)UILayer.UI);

            ugui.Close();

            Assert.IsNull(UIService.SharedLedger.GetWindow<ProbeUGUIWindow>("CallbackUGUI"), "uGUI 轨那一只的关闭回叫结算进了栈");
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeUITKWindow>("CallbackKit"), "同一时刻另一轨那一只不受影响");

            kit.Close();

            Assert.IsNull(UIService.SharedLedger.GetWindow<ProbeUITKWindow>("CallbackKit"), "UI Toolkit 轨那一只走的也是那条栈");
        }

        /// <summary>
        /// 只剩 UI Toolkit 那一枚驱动者在位时，uGUI 轨那一只窗的关闭回叫照样结算：回叫认的是那条栈，不是 uGUI 那一支。
        /// </summary>
        /// <remarks>
        /// 这一档与 <c>Tick_OnlyUITKDriverInPlace</c> 是同一病因的两侧：回叫叫的是那份共享持有者，它不认「哪一枚是主协调者」。 <br />
        /// 守卫是门面的 <see cref="UIService.IsValid"/>——把它收成只认 uGUI 那一支的位，这一格当场红：栈上仍留着这一只窗。
        /// </remarks>
        [Test]
        public void Close_WindowCallback_OnlyUITKDriverInPlace_StillSettlesOnTheSharedStack()
        {
            EnableOnlyUITKTrackAndInit();
            Assert.IsNotNull(UIService.Internal_PeekUITKHandler(), "量具前提坏了：配置只启用 UI Toolkit 那一支时它没就位");
            Assert.IsNull(UIService.Internal_PeekUGUIHandler(), "量具前提坏了：这一格判的是只剩 UI Toolkit 那一枚的档");
            var window = Prepared("KitOnlyClose", (int)UILayer.Bottom);

            Assert.IsTrue(UIService.IsValid, "量具前提坏了：只剩 UI Toolkit 那一枚时门面就该算有效");

            window.Close();

            Assert.IsNull(UIService.SharedLedger.GetWindow<ProbeUGUIWindow>("KitOnlyClose"), "另一轨的驱动者不在位不是回叫落空的理由");
        }

        /// <summary>
        /// 只剩 UI Toolkit 那一枚驱动者在位时，隐藏回叫照样结算：与关闭那一格同一道守卫，另一条路径。
        /// </summary>
        /// <remarks>
        /// 隐藏与关闭是 <c>UIWindow</c> 上两枚各自独立的钩子，守卫各写一遍：把 <c>Hide</c> 那一道收成只认 uGUI 那一支的位， <br />
        /// 上一格仍绿而这一格红——两枚都要在位才算门面有效，这一档判的就是 <see cref="UIService.IsValid"/>。
        /// </remarks>
        [Test]
        public void Hide_WindowCallback_OnlyUITKDriverInPlace_StillSettlesOnTheSharedStack()
        {
            EnableOnlyUITKTrackAndInit();
            Assert.IsNotNull(UIService.Internal_PeekUITKHandler(), "量具前提坏了：配置只启用 UI Toolkit 那一支时它没就位");
            Assert.IsNull(UIService.Internal_PeekUGUIHandler(), "量具前提坏了：这一格判的是只剩 UI Toolkit 那一枚的档");
            var covering = Prepared("KitOnlyHideCover", (int)UILayer.UI, true);
            var below = Prepared("KitOnlyHideBelow", (int)UILayer.Bottom);
            Assert.IsFalse(below.Visible, "量具前提坏了：栈顶那枚全屏窗的就绪回执应已把下层压住");

            covering.Hide();

            Assert.IsTrue(below.Visible, "隐藏回叫没结算进那条共享栈时，下层不会重新可见");
            Assert.IsFalse(covering.Visible, "被隐的那一只自己停在不可见位");
        }

        /// <summary>
        /// 只剩 UI Toolkit 那一枚驱动者在位时，模态动画的交互压制照样要争：租约住在那份共享持有者上，不分轨。
        /// </summary>
        /// <remarks>
        /// 门面那两枚包装各带一道 <see cref="UIService.IsValid"/> 守卫，守卫收成只认 uGUI 那一支时这里当场红（拿不到压制）； <br />
        /// 压制位本身是无归属的全局布尔，交回时要由持有者来交，非持有者交不回。
        /// </remarks>
        [Test]
        public void AcquireModalInteraction_OnlyUITKDriverInPlace_LeaseIsStillArbitrated()
        {
            EnableOnlyUITKTrackAndInit();
            Assert.IsNotNull(UIService.Internal_PeekUITKHandler(), "量具前提坏了：配置只启用 UI Toolkit 那一支时它没就位");
            Assert.IsNull(UIService.Internal_PeekUGUIHandler(), "量具前提坏了：这一格判的是只剩 UI Toolkit 那一枚的档");
            var modal = Prepared("KitOnlyLease", (int)UILayer.UI);

            Assert.IsTrue(UIService.IsValid, "量具前提坏了：只剩 UI Toolkit 那一枚时门面就该算有效");
            var other = Prepared("KitOtherWindow", (int)UILayer.UI);

            Assert.IsTrue(UIService.AcquireModalInteraction(modal), "模态窗在另一轨上时压制照样要争得到");
            Assert.IsFalse(UIService.ReleaseModalInteraction(other), "不是持有者就交不回这枚压制");
            Assert.IsTrue(UIService.ReleaseModalInteraction(modal), "持有者交回这一枚压制");
        }

        #endregion

        #region 关闭与隐藏 [CLOSE & HIDE]

        /// <summary>
        /// 关闭分两档：非缓存实例当场销毁，缓存实例交进停放表；两档都要出栈，存在性查询因此不再答它。
        /// </summary>
        /// <remarks>
        /// 缓存档的判据是「物体没被销毁、但栈上找不到、停放表里有它」三处一起看；非缓存档反过来要求 <c>IsDestroyed</c> 落真。 <br />
        /// 名字没人应的那一档静默返回，栈上一只都不少。
        /// </remarks>
        [Test]
        public void CloseUI_CachedAndPlainWindows_RouteSeparatelyAndLeaveStack()
        {
            var plain = Prepared("PlainClose", (int)UILayer.Bottom);
            var cached = Window<ProbeUGUIWindow>("CachedClose", (int)UILayer.UI, false, true);
            UIService.SharedLedger.Push(cached);
            cached.InternalLoad("Panel", null, false, null);
            UIService.SharedLedger.OnWindowPrepare(cached);

            UIService.SharedLedger.CloseUI(typeof(ProbeUGUIWindow), "NoSuchWindow");
            Assert.AreEqual(2, Stack.Count, "名字没人应时不得动栈");

            UIService.SharedLedger.CloseUI(typeof(ProbeUGUIWindow), plain.WindowName);
            UIService.SharedLedger.CloseUI(typeof(ProbeUGUIWindow), cached.WindowName);

            Assert.IsTrue(plain.IsDestroyed, "非缓存窗关闭要走销毁");
            Assert.IsFalse(cached.IsDestroyed, "缓存窗关闭不得销毁物体");
            Assert.IsTrue(UIService.SharedLedger.IsParked(cached.WindowName), "缓存窗要进停放表");
            Assert.AreEqual(0, Stack.Count, "两支都得出栈");
            Assert.IsFalse(UIService.SharedLedger.HasWindow<ProbeUGUIWindow>(cached.WindowName),
                "存在性查询按栈答，不按停放表");
        }

        /// <summary>关闭缺名档按类型全名找窗：与开窗那侧的缺省命名同一条规则。</summary>
        [Test]
        public void CloseUI_WithoutName_ResolvesByTypeFullName()
        {
            var window = Window<ProbeUGUIWindow>(typeof(ProbeUGUIWindow).FullName, (int)UILayer.Bottom);
            UIService.SharedLedger.Push(window);

            UIService.SharedLedger.CloseUI(typeof(ProbeUGUIWindow), null);

            Assert.AreEqual(0, Stack.Count, "按类型全名也要把这只窗关掉");
        }

        /// <summary>隐藏时长为非正数时直接走关闭：不落隐藏标志、不挂计时器、物体销毁。</summary>
        [Test]
        public void HideUI_ZeroHideTime_ClosesWindowInsteadOfHiding()
        {
            var window = Window<ProbeUGUIWindow>("InstantClose", (int)UILayer.Bottom);
            window.Init("InstantClose", (int)UILayer.Bottom, false, "Panel", false, 0, false);
            UIService.SharedLedger.Push(window);
            window.InternalLoad("Panel", null, false, null);
            UIService.SharedLedger.OnWindowPrepare(window);

            UIService.SharedLedger.HideUI(typeof(ProbeUGUIWindow), "InstantClose");

            Assert.IsTrue(window.IsDestroyed, "隐藏时长为 0 时按关闭处理");
            Assert.IsFalse(window.IsHide, "走关闭就不该落上隐藏标志");
            Assert.AreEqual(0, Stack.Count, "关掉就要出栈");
        }

        /// <summary>
        /// 隐藏窗口落上隐藏标志、把可见位按下并留在栈上；全屏窗隐藏时重发一次显隐回执，
        /// 被它压住的下层窗口因此重新可见。
        /// </summary>
        /// <remarks>
        /// 隐藏转关闭的那枚计时器不在本格的断言里：EditMode 下 <c>TimerService</c> 没进注册表， <br />
        /// <c>Delay</c> 交回 0 号，断它等于是断环境的注册状态。下层窗口在隐藏前必须是被压住的（全屏档的量具前提）， <br />
        /// 否则「重新可见」这半句没有着力点。
        /// </remarks>
        [Test]
        public void HideUI_FullScreenWindow_HidesItAndRepublishesVisibilityReceipt()
        {
            var below = Prepared("BelowFullScreen", (int)UILayer.Bottom);
            var fullScreen = Prepared("FullScreenHide", (int)UILayer.UI, true);
            Assert.IsFalse(below.Visible, "量具前提坏了：全屏窗压在栈顶时下层应被压住");

            UIService.SharedLedger.HideUI(typeof(ProbeUGUIWindow), "FullScreenHide");

            Assert.IsTrue(fullScreen.IsHide, "隐藏要落上隐藏标志");
            Assert.IsFalse(fullScreen.Visible, "隐藏要把可见位落下去");
            Assert.IsTrue(below.Visible, "全屏窗隐藏后回执重发，下层重新可见");
            Assert.AreEqual(2, Stack.Count, "隐藏不出栈：两只窗口都还在栈上，只是那一只不可见");
        }

        /// <summary>全关按 <c>isShutDown</c> 分档：非关停轮把缓存窗交进停放表、其余销毁；关停轮一律销毁且不进停放表。</summary>
        [Test]
        public void CloseAll_PerShutdownFlag_CachesOnNormalRoundAndDestroysOnShutdownRound()
        {
            var cached = Window<ProbeUGUIWindow>("AllCached", (int)UILayer.Bottom, false, true);
            var plain = Window<ProbeUGUIWindow>("AllPlain", (int)UILayer.UI);
            UIService.SharedLedger.Push(cached);
            UIService.SharedLedger.Push(plain);

            UIService.SharedLedger.CloseAll(false);

            Assert.AreEqual(0, Stack.Count, "全关要把栈清空");
            Assert.IsTrue(UIService.SharedLedger.IsParked("AllCached"), "非关停轮缓存窗进停放表");
            Assert.IsTrue(plain.IsDestroyed, "非缓存窗一律销毁");

            var shutdownCached = Window<ProbeUGUIWindow>("AllCached2", (int)UILayer.Bottom, false, true);
            UIService.SharedLedger.Push(shutdownCached);

            UIService.SharedLedger.CloseAll(true);

            Assert.IsTrue(shutdownCached.IsDestroyed, "关停轮连缓存窗也一并销毁");
            Assert.IsFalse(UIService.SharedLedger.IsParked("AllCached2"), "关停轮不得把窗交进停放表");
        }

        /// <summary>
        /// 全关留人的三枚入口各按自己的判据跳过：按实例、按类型、按层级，其余一律关掉并留在栈上。
        /// </summary>
        /// <remarks>
        /// 三档之间各归零一次共享栈，免得前一档留下的栈序与停放表串进下一档的判据里； <br />
        /// 按类型那一档留的是 UI Toolkit 轨那一只——同栈混排时「按类型留人」跨的是轨而不是层。
        /// </remarks>
        [Test]
        public void CloseAllWithOut_InstanceTypeAndLayer_PreserveExactlyTheirOwnWindows()
        {
            var keepInstance = Window<ProbeUGUIWindow>("KeepInstance", (int)UILayer.Bottom);
            UIService.SharedLedger.Push(keepInstance);
            UIService.SharedLedger.Push(Window<ProbeUITKWindow>("GoUITK", (int)UILayer.UI));

            UIService.SharedLedger.CloseAllWithOut(keepInstance);

            CollectionAssert.AreEqual(new UIWindow[] { keepInstance }, Stack,
                "按实例留人只留那一只");

            // 三档共用的就是那一份存储：换档之间要把共享栈与压制位归零，否则下一档读到的仍是上一档留下的栈序
            UIService.Internal_ResetSharedLedger();
            var keepKit = Window<ProbeUITKWindow>("KeepKit", (int)UILayer.Bottom);
            UIService.SharedLedger.Push(keepKit);
            UIService.SharedLedger.Push(Window<ProbeUGUIWindow>("GoUGUI", (int)UILayer.UI));

            UIService.SharedLedger.CloseAllWithOut<ProbeUITKWindow>();

            CollectionAssert.AreEqual(new UIWindow[] { keepKit }, Stack,
                "按类型留人留下的是 UI Toolkit 轨那一类");

            UIService.Internal_ResetSharedLedger();
            var keepTips = Window<ProbeUGUIWindow>("KeepTips", (int)UILayer.Tips);
            UIService.SharedLedger.Push(keepTips);
            UIService.SharedLedger.Push(Window<ProbeUGUIWindow>("GoPopup", (int)UILayer.Popup));

            UIService.SharedLedger.CloseAllWithOut(UILayer.Tips);

            CollectionAssert.AreEqual(new UIWindow[] { keepTips }, Stack,
                "按层级留人只留 Tips 层");
        }

        /// <summary>
        /// 关停按支分流：uGUI 那一枚的关停只关掉 uGUI 轨的窗，UI Toolkit 轨那一只留在共享栈上等它自己那一枚来收。
        /// </summary>
        /// <remarks>
        /// 两支生产处理器都只初始化一次（<c>Internal_Init</c> 之后 <c>Internal_Shutdown</c> 才真的走到本轨的关停），不带面板本体也不销毁 UI 根。 <br />
        /// 判据落在栈上那两只窗的去处：本轨那只因关停被销毁、另一轨那只既没被销毁也没离开栈。 <br />
        /// 把这一轨的关停改回不分流的那一枚入口（一次关掉整条栈）就红在「另一轨的窗还在栈上」这一行。
        /// </remarks>
        [Test]
        public void Shutdown_OneTrackDriver_ClosesOnlyItsOwnTrackWindowsOnTheSharedStack()
        {
            var uguiDriver = new UGUIHandler();
            uguiDriver.Internal_Init();
            var uguiWindow = Prepared("ShutUGUI", (int)UILayer.Bottom);
            var kitWindow = Window<ProbeUITKWindow>("ShutKit", (int)UILayer.Bottom);
            UIService.SharedLedger.Push(kitWindow);
            kitWindow.InternalLoad("Panel", null, false, null);
            UIService.SharedLedger.OnWindowPrepare(kitWindow);

            uguiDriver.Internal_Shutdown();

            Assert.IsTrue(uguiWindow.IsDestroyed, "本轨那一只随本轨的关停一起销毁");
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeUITKWindow>("ShutKit"), "另一轨的窗不被这一轨带走");
            Assert.IsFalse(kitWindow.IsDestroyed, "另一轨那一只此刻还完好，等它自己那一枚来收");
        }

        /// <summary>
        /// 门面的关停次序：先叫 UI Toolkit 那一枚收掉挂在下面的壳，再叫 uGUI 那一枚销毁那枚 UI 根。
        /// </summary>
        /// <remarks>
        /// UI Toolkit 轨的壳物体挂在 uGUI 轨那枚 UI 根下（<see cref="UITKWindow"/> 建壳时取的是门面那枚根）， <br />
        /// 次序倒了就是先销毁父物体、再让还挂在父物体里的壳走自己的销毁链——面板被拆在用的那一个。 <br />
        /// 读数取的是「本轨那只窗的面板被收走的那一刻，uGUI 轨那枚登记的根还在不在」：两支都是生产驱动者，次序由门面的 <see cref="UIService.OnShutdown"/> 给。 <br />
        /// 这一格判次序，上一格判分流；两支驱动者都由门面的启用清单认领就位，UI 根由夹具登记。
        /// </remarks>
        [Test]
        public void OnShutdown_TwoTrackDrivers_ClosesTheUITKTrackBeforeTheUGUIRootGoes()
        {
            EnableBothTracksAndInit();
            var uguiDriver = UIService.UGUIHandler;
            Assert.IsNotNull(UIService.Internal_PeekUITKHandler(), "量具前提坏了：UI Toolkit 那一枚没被认领进槽");
            BindUGUIRoot(uguiDriver);
            Assert.IsTrue(_uiRootGo != null && uguiDriver.UIRoot != null, "量具前提坏了：UI 根没绑上，销毁那一步判不到");

            var kitWindow = new OrderProbeUITKWindow();
            kitWindow.Init("OrderKit", (int)UILayer.Bottom, false, "Panel", false, 10, false);
            kitWindow.RootToObserve = _uiRootGo;
            var ledger = UIService.SharedLedger;
            ledger.Push(kitWindow);
            kitWindow.InternalLoad("Panel", null, false, null);
            ledger.OnWindowPrepare(kitWindow);

            new UIService().OnShutdown();

            Assert.IsTrue(kitWindow.RootStillAliveAtPanelDestroy,
                "UI Toolkit 那一枚该先关：轮到它收本轨那只窗时，uGUI 轨那枚根还该在位");
            Assert.IsTrue(kitWindow.IsDestroyed, "另一轨那一只由它自己那一枚关掉");
            Assert.IsTrue(_uiRootGo == null, "uGUI 那一枚随后销毁登记那一枚父物体，挂在其下的壳因此不会被拆在用的那一个");
        }

        #endregion

        #region 栈顶一致性与回执结算次序 [TOP WINDOW & RECEIPT SETTLEMENT]

        /// <summary>
        /// 栈顶查询跟着出入栈走：压上来的是谁栈顶就是谁，移出栈顶后回到下一只，空栈回 null。
        /// </summary>
        /// <remarks>
        /// 全栈档读栈末位、单层档读该层栈序末位，两档在「该层只剩一只」时必须答同一只窗口； <br />
        /// 名称档随单层档，空层回空串而不是 null——这是它自己那条早退，另两档没有。
        /// </remarks>
        [Test]
        public void PushPop_KeepGetTopWindowOnTheStackTail()
        {
            var bottom = Window<ProbeUGUIWindow>("SyncBottom", (int)UILayer.Bottom);
            var popup = Window<ProbeUITKWindow>("SyncPopup", (int)UILayer.Popup);

            UIService.SharedLedger.Push(bottom);
            Assert.AreSame(bottom, UIService.SharedLedger.GetTopWindow(), "刚压上来的那一只就是栈顶");

            UIService.SharedLedger.Push(popup);
            Assert.AreSame(popup, UIService.SharedLedger.GetTopWindow(), "更高层级压上来后栈顶换人");
            Assert.AreSame(popup, UIService.SharedLedger.GetTopWindow((int)UILayer.Popup), "单层档答的是同一只");
            Assert.AreEqual("SyncPopup", UIService.SharedLedger.GetTopWindowName((int)UILayer.Popup), "名称档随单层档");

            UIService.SharedLedger.Pop(popup);
            Assert.AreSame(bottom, UIService.SharedLedger.GetTopWindow(), "移出栈顶那一只后回到下一只");
            Assert.IsNull(UIService.SharedLedger.GetTopWindow((int)UILayer.Popup), "该层已经没人");
            Assert.AreEqual(string.Empty, UIService.SharedLedger.GetTopWindowName((int)UILayer.Popup), "空层的名称档回空串");

            UIService.SharedLedger.Pop(bottom);
            Assert.IsNull(UIService.SharedLedger.GetTopWindow(), "空栈的栈顶回 null");
        }

        /// <summary>
        /// 全关留人只出栈并重刷栈顶，深度与显隐都不重发；单关闭则按被关窗口的层级重排深度、重发显隐再重刷栈顶。
        /// </summary>
        /// <remarks>
        /// 两档都从「下层窗被上层全屏窗压住、同层后到者拿到第二档深度」这一现场起步， <br />
        /// 于是「没重发」看得见（停在上一次落到的值）、「重发了」也看得见（翻回可见、落回该层基址）。 <br />
        /// 留下的那只同层只剩它一个时仍带第二档深度，是 <c>CloseAllWithOut</c> 的既有分歧，本格按现状钉住不修。
        /// </remarks>
        [Test]
        public void CloseAllWithOut_LeavesReceiptsUnsettled_WhileCloseUIRepublishesThem()
        {
            var earlier = Prepared("SameLayerEarlier", (int)UILayer.UI);
            var kept = Prepared("SameLayerKept", (int)UILayer.UI);
            var fullScreenTop = Prepared("FullScreenTop", (int)UILayer.Popup, true);
            Assert.IsFalse(kept.Visible, "量具前提坏了：上层全屏窗的就绪回执应已把下层压住");
            Assert.AreEqual((int)UILayer.UI * UIService.LAYER_DEEP + UIService.WINDOW_DEEP, kept.Depth,
                "量具前提坏了：同层后到者按栈序拿到第二档深度");

            UIService.SharedLedger.CloseAllWithOut(kept);

            CollectionAssert.AreEqual(new UIWindow[] { kept }, Stack, "全关留人只留下那一只");
            Assert.IsTrue(earlier.IsDestroyed && fullScreenTop.IsDestroyed, "其余两只走销毁");
            Assert.IsFalse(kept.Visible, "既有分歧：全关留人不重发显隐回执，下层窗停在被压住的假位上");
            Assert.AreEqual((int)UILayer.UI * UIService.LAYER_DEEP + UIService.WINDOW_DEEP, kept.Depth,
                "既有分歧：同层只剩它一只时深度也不重排，沿用上一轮的第二档");

            // 两档各归零一次那一份共享栈：上一档留下的栈序与停放表不得串进这一档的判据里
            UIService.Internal_ResetSharedLedger();
            var earlierShown = Prepared("CloseEarlier", (int)UILayer.UI);
            var survivor = Prepared("CloseSurvivor", (int)UILayer.UI);
            var top = Prepared("CloseTop", (int)UILayer.Popup, true);
            Assert.IsFalse(survivor.Visible, "量具前提坏了：单关闭这一侧同样先被全屏窗压住");

            UIService.SharedLedger.CloseUI(typeof(ProbeUGUIWindow), top.WindowName);

            Assert.IsTrue(survivor.Visible, "关掉栈顶的全屏窗后单关闭重发显隐回执，下层重新可见");

            UIService.SharedLedger.CloseUI(typeof(ProbeUGUIWindow), earlierShown.WindowName);

            Assert.AreEqual((int)UILayer.UI * UIService.LAYER_DEEP, survivor.Depth,
                "单关闭按被关窗口的层级重排深度：同层只剩一只时落回该层基址");
            Assert.IsTrue(earlierShown.IsDestroyed, "被单关闭的那只走销毁");
        }

        /// <summary>
        /// 缓存实例关闭走停放：进停放表、出栈、物体留着，但可见意图与深度都不动，此后的回执也再也答不到它。
        /// </summary>
        /// <remarks>
        /// 停放那一支只调 <c>InternalClose</c>（关闭动画结束后才 <c>ParkPanel</c>），不写 <c>Visible</c>， <br />
        /// 与「关掉就该看不见」的直觉相反，是既有分歧；同批的下层窗接了重发的显隐回执，两相对照才看得出这条支路被跳过。 <br />
        /// 出栈之后再发一轮显隐与深度回执，那只窗的两个意图位都不改——回执只走栈。
        /// </remarks>
        [Test]
        public void CloseUI_CachedWindowParksItWithoutWritingTheVisibleIntent()
        {
            var below = Prepared("ParkBelow", (int)UILayer.Bottom);
            var cached = Window<ProbeUGUIWindow>("ParkFullScreen", (int)UILayer.UI, true, true);
            UIService.SharedLedger.Push(cached);
            cached.InternalLoad("Panel", null, false, null);
            UIService.SharedLedger.OnWindowPrepare(cached);
            Assert.IsTrue(cached.Visible, "量具前提坏了：就绪回执要把栈顶那只置为可见");
            Assert.IsFalse(below.Visible, "量具前提坏了：全屏缓存窗压在栈顶时下层应被压住");

            UIService.SharedLedger.CloseUI(typeof(ProbeUGUIWindow), cached.WindowName);

            Assert.IsTrue(UIService.SharedLedger.IsParked(cached.WindowName), "缓存窗关闭要进停放表");
            Assert.IsFalse(cached.IsDestroyed, "停放不得销毁物体");
            Assert.IsTrue(cached.Visible, "既有分歧：停放这一支不写可见意图，关掉的缓存窗仍答可见");
            Assert.IsTrue(below.Visible, "同一次关闭里下层窗接到了重发的显隐回执");

            UIService.SharedLedger.OnSetWindowVisible();
            UIService.SharedLedger.OnSortWindowDepth((int)UILayer.UI);

            Assert.IsTrue(cached.Visible, "出栈之后显隐回执答不到它：可见意图停在停放时那一位");
            Assert.AreEqual((int)UILayer.UI * UIService.LAYER_DEEP, cached.Depth, "深度回执同样答不到它");
        }

        #endregion

        #region 处理器经继承走的那一圈 [INHERITED ORCHESTRATION ROUND TRIP]

        /// <summary>
        /// 经 <see cref="UGUIHandler"/> 的开窗入口写栈：查询、遮挡与全关答的都是协调者那一份栈，
        /// 全关后重开取回停放表里同一只窗并把它的面板重新点亮。
        /// </summary>
        /// <remarks>
        /// 这一格是「生产路径真跑到」的判据：开窗族住在门面上、落进那一条共享栈，<c>UGUIHandler</c> 经继承拿到的是同一份栈、同一份停放表与同一份遮挡判据。 <br />
        /// 栈与停放表在持有者上是 <c>private</c>：派生轨既读不到也写不进第二份，重开取回的仍是停放表里那一只。 <br />
        /// 面板物体由探针窗装载时自建，用例把它收进夹具的物体表按出门销毁。 <br />
        /// 驱动者由门面的启用清单认领进来——那是槽位唯一的来路；夹具出门把两支槽与那份共享持有者一起归位， <br />
        /// 任何一档断言红都不许把生产处理器漏给后序全部用例（姊妹夹具的同一口径）。线程契约：仅主线程。
        /// </remarks>
        [Test]
        public void UGUIHandler_OpenAndCloseAllRoundTrip_LandsOnTheCoordinatorsSingleStackAndCache()
        {
            EnableBothTracksAndInit();
            var handler = UIService.UGUIHandler;
            // 开窗腿在门面上（协调者已无那条默认腿）：这一格判的是「经门面写入 → 处理器手里那一份栈答到」
            UIService.ShowUI<HandlerProbeWindow>("HandlerProbe", "HandlerProbe", false);

            var opened = handler.GetWindow<HandlerProbeWindow>("HandlerProbe");
            Assert.IsNotNull(opened, "处理器写入的窗口要能在协调者那一份栈上查到");
            // 登记排在一切断言之前：后面任何一档红都不给本轮漏下一只没人销毁的面板
            _objects.Add(opened.gameObject);
            Assert.AreSame(opened, handler.GetTopWindow(), "栈顶查询答的是同一只窗口");
            Assert.AreSame(opened, handler.CurrentModal, "UI 层是模态层，当前模态答的也是同一只");
            Assert.IsTrue(handler.IsBlockedByModal(Object("HandlerProbeBystander")), "路人面板被处理器开的模态窗挡住");

            handler.CloseAll(false);

            Assert.IsFalse(handler.HasWindow<HandlerProbeWindow>("HandlerProbe"), "全关清空的就是那一份栈");

            opened.gameObject.SetActive(false);
            UIService.ShowUI<HandlerProbeWindow>("HandlerProbe", "HandlerProbe", false);

            Assert.AreSame(opened, handler.GetTopWindow(), "重开取回停放表里那一只，而不是第二份实例");
            Assert.IsTrue(opened.gameObject.activeSelf, "缓存复用支路真的走到把面板重新点亮");
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>造一枚本体物体并在用例出门时销毁：遮挡判据要拿真的 <c>GameObject</c> 比包含关系。</summary>
        /// <param name="name">物体名。</param>
        /// <returns>新建的 <see cref="GameObject"/>。</returns>
        private GameObject Object(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        /// <summary>按窗口名与层级现造一只探针窗（面板地址与取法在本文件的判据里不参与）。</summary>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="layer">窗口层级。</param>
        /// <param name="fullScreen">是否全屏。</param>
        /// <param name="cacheInstance">关闭时是否缓存实例。</param>
        /// <returns>未入栈的探针窗。</returns>
        private static T Window<T>(string windowName, int layer, bool fullScreen = false, bool cacheInstance = false)
            where T : UIWindow, new()
        {
            var window = new T();
            window.Init(windowName, layer, fullScreen, "Panel", false, 10, cacheInstance);
            return window;
        }

        /// <summary>
        /// 造一只走完「压栈→面板就绪→就绪回执」的窗口：栈上就位、<c>IsPrepare</c> 为真、深度与显隐都被回执写过一次。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="layer">窗口层级。</param>
        /// <param name="fullScreen">是否全屏。</param>
        /// <returns>已就绪的探针窗。</returns>
        private static ProbeUGUIWindow Prepared(string windowName, int layer, bool fullScreen = false)
        {
            var ledger = UIService.SharedLedger;
            var window = Window<ProbeUGUIWindow>(windowName, layer, fullScreen);
            ledger.Push(window);
            window.InternalLoad("Panel", null, false, null);
            ledger.OnWindowPrepare(window);
            return window;
        }

        /// <summary>
        /// 造一只走完同一圈的 UI Toolkit 轨窗口：与共存样本同形，压栈、面板就绪、就绪回执三步都在那一条栈上。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="layer">窗口层级。</param>
        /// <returns>已就绪的探针窗。</returns>
        private static ProbeUITKWindow PreparedKit(string windowName, int layer)
        {
            var ledger = UIService.SharedLedger;
            var window = Window<ProbeUITKWindow>(windowName, layer);
            ledger.Push(window);
            window.InternalLoad("Panel", null, false, null);
            ledger.OnWindowPrepare(window);
            return window;
        }

        /// <summary>
        /// 那条共享栈的当前次序：账本交的是栈本体那一份活引用，用例读它判插入位置、移除结果与栈长。
        /// </summary>
        private static IReadOnlyList<UIWindow> Stack => UIService.SharedLedger.PeekStack();

        /// <summary>占住全局压制权的一份归属：回真是「调用方应当置位」的口径，与门面那两枚入口同判据。</summary>
        /// <param name="window">要占位的窗口。</param>
        /// <returns>占到位、调用方应写压制位时为真。</returns>
        private static bool HoldLease(UIWindow window)
        {
            var ledger = UIService.SharedLedger;
            return ledger.InteractionLease.Acquire(window, ledger.IsModal(window));
        }

        /// <summary>
        /// 归属探针：这只窗此刻是否还压着全局交互位。<c>Release</c> 只在持有者本人身上成立—— <br />
        /// 回真说明归属仍在它手里（探针同时把它交还），回假说明归属已经被别处丢弃。
        /// </summary>
        /// <param name="window">要问归属的窗口。</param>
        /// <returns>它仍是当前持有者时为真。</returns>
        private static bool LeaseHeldBy(UIWindow window) => UIService.SharedLedger.InteractionLease.Release(window);

        /// <summary>
        /// 给 uGUI 轨那枚驱动者配一枚真实 UI 根：<c>UIRootBinding</c> 登记 + 带 <c>CanvasScaler</c> 的 Canvas 子物体。
        /// </summary>
        /// <remarks>
        /// 安全区那一档的落点住在根上：<see cref="UGUIHandler.ApplyScreenSafeRect"/> 按 <c>CanvasScaler</c> 的参考分辨率换算， <br />
        /// 把结果写成根那枚 <c>RectTransform</c> 的 <c>offsetMin</c> 与 <c>offsetMax</c>——读这两格就是读落点。 <br />
        /// Canvas 挂在子物体上，因此驱动者关停时销毁的是登记那一枚父物体（与生产同一条路径）。 <br />
        /// 两枚物体都由夹具出门销毁，UI 根登记随物体一起交回。
        /// </remarks>
        /// <param name="handler">待就位的 uGUI 轨驱动者。</param>
        /// <returns>落在 UI 根上的 <c>RectTransform</c>。</returns>
        private RectTransform BindUGUIRoot(UGUIHandler handler)
        {
            _uiRootGo = new GameObject("OrchestrationUIRoot");
            _uiCanvasGo = new GameObject("OrchestrationCanvas", typeof(RectTransform));
            _uiCanvasGo.transform.SetParent(_uiRootGo.transform, false);
            _uiCanvasGo.AddComponent<Canvas>();
            var scaler = _uiCanvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);

            _uiRootGo.AddComponent<UIRootBinding>().Internal_Bind();
            handler.TryBindRoot();

            return _uiCanvasGo.GetComponent<RectTransform>();
        }

        /// <summary>
        /// 按 <c>CanvasScaler</c> 的参考分辨率把一枚安全区矩形换算成根那枚 <c>RectTransform</c> 的两个偏移角点：
        /// 期望值独立按 iPhone X 那一档基准重算，改换算、改取向判据或把门面不再按支驱动都会红。
        /// </summary>
        /// <param name="safeRect">交给落点的安全区域。</param>
        /// <returns>偏移角点：<c>offsetMin</c> 与 <c>offsetMax</c>。</returns>
        private static Vector2[] ExpectedOffsets(Rect safeRect)
        {
            var rateX = REFERENCE_WIDTH / Screen.width;
            var rateY = REFERENCE_HEIGHT / Screen.height;
            var posX = (int)(safeRect.position.x * rateX);
            var posY = (int)(safeRect.position.y * rateY);
            var width = (int)(safeRect.size.x * rateX);
            var height = (int)(safeRect.size.y * rateY);
            return new[]
            {
                new Vector2(posX, posY),
                new Vector2(-(REFERENCE_WIDTH - width - posX), -(REFERENCE_HEIGHT - height - posY)),
            };
        }

        /// <summary>uGUI 轨的编排探针窗：面板钩子只记账，不建 Canvas、不取资产。</summary>
        private sealed class ProbeUGUIWindow : UGUIWindow
        {
            /// <summary>本窗被 <c>Tick</c> 驱动的次数。</summary>
            internal int Updates;

            /// <summary>补建次数：<c>OnWindowPrepare</c> 走没走到 <c>InternalCreate</c> 由它回答。</summary>
            internal int CreateCalls;

            /// <summary>驱动落点：把本窗的名字记进调用方给的次序表里。</summary>
            internal List<string> UpdateSink;

            /// <summary>更新回调里要顺手做的事（模拟一次改动栈的时机）。</summary>
            internal System.Action OnUpdateHook;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }

            protected internal override void DestroyPanel() { }

            protected override void OnCreate() => CreateCalls++;

            protected override void OnUpdate()
            {
                Updates++;
                UpdateSink?.Add(WindowName);
                OnUpdateHook?.Invoke();
            }
        }

        /// <summary>
        /// 由 uGUI 轨的开窗入口实例化的探针窗：<c>cacheInstance</c> 为真才会被停放，面板是一枚真实物体，
        /// 供复用支路的 <c>SetActive</c> 被看见。
        /// </summary>
        /// <remarks>
        /// 面板钩子一律只记账：没有 Canvas 也没有 UI Document，只有装载时自建的那枚物体。 <br />
        /// 默认构造必须显式写成 public——开窗族那侧用 <c>Activator.CreateInstance</c> 实例化窗口类型。
        /// </remarks>
        [Window(UILayer.UI, cacheInstance: true)]
        private sealed class HandlerProbeWindow : UGUIWindow
        {
            private GameObject _panel;

            /// <summary>窗口本体：装载时自建的真实物体。</summary>
            public override GameObject gameObject => _panel;

            /// <summary>开窗族要求的 public 无参构造。</summary>
            public HandlerProbeWindow()
            {
            }

            protected internal override bool LoadPanel(string assetLocation, bool fromResources)
            {
                _panel = new GameObject(nameof(HandlerProbeWindow));
                return true;
            }

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }

            protected internal override void DestroyPanel() { }
        }

        /// <summary>
        /// 把面板物体交回真实 <see cref="GameObject"/> 的探针窗：遮挡判据要吃 <c>gameObject</c>，这一只专门供它。
        /// </summary>
        /// <remarks>面板钩子一律只记账：没有 Canvas 也没有 UI Document，只有 <c>Root</c> 这一枚物体参与包含判断。</remarks>
        private sealed class ProbeOwnedWindow : UGUIWindow
        {
            /// <summary>用例挂给本窗的「面板」物体。</summary>
            internal GameObject Root;

            public override GameObject gameObject => Root;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }

            protected internal override void DestroyPanel() { }
        }

        /// <summary>UI Toolkit 轨的编排探针窗：与 uGUI 探针同形，只证「同一份栈两支窗」这一档。</summary>
        private sealed class ProbeUITKWindow : UITKWindow
        {
            /// <summary>本窗被 <c>Tick</c> 驱动的次数。</summary>
            internal int Updates;

            /// <summary>驱动落点，同 <see cref="ProbeUGUIWindow.UpdateSink"/>。</summary>
            internal List<string> UpdateSink;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }

            protected internal override void DestroyPanel() { }

            protected override void OnCreate() { }

            protected override void OnUpdate()
            {
                Updates++;
                UpdateSink?.Add(WindowName);
            }
        }

        /// <summary>
        /// 只记「本窗的面板被收走的那一刻，uGUI 轨那枚登记的根还在不在」的 UI Toolkit 探针窗：关停次序那一格唯一的读数。
        /// </summary>
        /// <remarks>
        /// 读数排在面板销毁那一步里，因此判的是门面的关停次序而不是用例的先后：次序对了，这一步走到时根还在位。 <br />
        /// 面板钩子一律只记账：不建壳、不点 <c>UIDocument</c>，也不接管任何后端资源。
        /// </remarks>
        private sealed class OrderProbeUITKWindow : UITKWindow
        {
            /// <summary>销毁本窗面板那一刻要问的那枚物体（生产侧是 UI 根登记那一枚）。</summary>
            internal GameObject RootToObserve;

            /// <summary>本窗的面板被收走时根是否还活着：还在为真。</summary>
            internal bool RootStillAliveAtPanelDestroy;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }

            protected internal override void DestroyPanel()
            {
                RootStillAliveAtPanelDestroy = RootToObserve != null;
            }
        }

        #endregion
    }
}
