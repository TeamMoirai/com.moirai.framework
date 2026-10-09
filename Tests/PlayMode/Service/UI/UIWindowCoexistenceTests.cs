using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Service.UI
{
    /// <summary>
    /// 两支共存生命周期用例：同一会话里混开 uGUI 窗与 UI Toolkit 窗，判显隐、序空间、关一只的影响与混合栈上的查询与全关。
    /// </summary>
    /// <remarks>
    /// 运行前提：播放态测试域里框架已自动 Boot（<c>GameAppHost.SubsystemRegistration</c> 与 <c>GameAppSettings.Initiation</c> 两枚
    /// <c>RuntimeInitializeOnLoadMethod</c>），本文件的开窗一律走门面 <see cref="UIService"/> 落到那一份生产协调者
    /// （<see cref="UGUIHandler"/> 兼任），夹具不自建协调者、不经换入换出接缝——共存要在真帧推进下判，替身答不了帧。 <br />
    /// 面板来源两支各自不同：uGUI 那一轨的探针窗用代码建出的 <see cref="Canvas"/> 面板，UI Toolkit 那一轨的探针窗用代码建出的
    /// 文档壳（<see cref="UIDocument"/> 配好 <see cref="UITKWindow.SharedPanelSettings"/> 后交 <c>BindPanel</c>，模板传 null ⇒
    /// 内容根由代码建树，不新增任何 <c>.uxml</c> 资产）；壳停在未激活态，本文件的判据只读窗口自己结算的意图落点
    /// （<see cref="UIWindow.Visible"/> / <see cref="UIWindow.Depth"/> 经 <c>ApplyVisible</c> / <c>ApplyDepth</c> 落到画布层与文档排序）。 <br />
    /// 唯一走真装载的那一格吃包内实存的两份资产（<c>DebuggerPanelSettings</c> 与 <c>EventsDebugger</c> 模板），开门先各量一行前提：
    /// 取不到就红在前提上，不让下面的判据去猜。UI Toolkit 后端的共享配置是 static 写口，进门存原值、出门按原值交回。 <br />
    /// 序空间判据的取口是两支各自的后端事实（<see cref="UGUIWindow.PanelCanvas"/> 与 <see cref="UITKWindow.Document"/>），不经反射；
    /// 层级一律取非模态档以外的组合时也只判显隐与序，遮挡与拾取不在本文件射程。帧推进口径：需要等条件走带超时上界的
    /// <see cref="PumpUntil"/>，<c>yield return null</c> 不保证跑过 <c>FixedUpdate</c>，本文件的判据也不需要。 <br />
    /// 序空间那一组判据取的是面板事实；面板本体的在场与离场一律用 Unity 的 <c>==</c> 运算符判：帧末落账的
    /// <c>Object.Destroy</c> 把物体折成假 null，NUnit 的引用判据读回来仍是"非 null 而打印成 &lt;null&gt;"， <br />
    /// 只有 Unity 自己的运算符认得出已销毁。窗口对象与协调者本体不是 Unity 物体，那两处的 <c>IsNull</c> 判的是真 null。 <br />
    /// 线程契约：仅主线程。
    /// </remarks>
    [TestFixture]
    public sealed class UIWindowCoexistenceTests
    {
        /// <summary>UI Toolkit 模板的内置资源地址：包内实存的调试器事件面板模板，只读消费，本文件不新增测试资产。</summary>
        private const string TEMPLATE_RESOURCE_NAME = "EventsDebugger";

        /// <summary><see cref="EUILayer.UI"/> 那一层的序空间基址。</summary>
        private const int UI_LAYER_BASE = (int)EUILayer.UI * UIService.LAYER_DEEP;

        /// <summary><see cref="EUILayer.Tips"/> 那一层的序空间基址。</summary>
        private const int TIPS_LAYER_BASE = (int)EUILayer.Tips * UIService.LAYER_DEEP;

        /// <summary>异步装载探针窗的面板延迟（秒，不受时间缩放）：等待腿要等的就是这一段时间。</summary>
        private const float PANEL_DELAY_SECONDS = 0.2f;

        /// <summary>帧推进的等待上界（秒）：超过即把现场交回用例判红，不写无限等。</summary>
        private const float PUMP_TIMEOUT_SECONDS = 5f;

        private UGUIHandler _coordinator;
        private PanelSettings _enteredSharedPanelSettings;
        private PanelSettings _sharedPanelSettings;
        private readonly List<GameObject> _trackedShells = new List<GameObject>();

        /// <summary>等待腿的交回物：两只窗各自的续体落点——装载跨过帧的窗同帧还是 null，装载同帧落定的窗当场就有值。</summary>
        private UIWindow _awaitedUgui;
        private UIWindow _awaitedKit;

        /// <summary>进门存回共享 <c>PanelSettings</c> 的原值，并把包内那枚带主题的夹具配置交 UI Toolkit 后端使用。</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _enteredSharedPanelSettings = UITKWindow.SharedPanelSettings;
            _sharedPanelSettings = Resources.Load<PanelSettings>("DebuggerPanelSettings");
            Assert.IsNotNull(_sharedPanelSettings,
                "量具前提坏了：取不到 DebuggerPanelSettings 夹具资产，UI Toolkit 那一轨的壳无从装配");
            UITKWindow.SharedPanelSettings = _sharedPanelSettings;
        }

        /// <summary>出门把共享 <c>PanelSettings</c> 按进门那一份交回，不给同域后跑的用例留夹具配置。</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // 后端共享配置是程序集级静态：按进门那一份交回，不给后跑的用例留夹具配置
            UITKWindow.SharedPanelSettings = _enteredSharedPanelSettings;
        }

        /// <summary>
        /// 进门判据：协调者必须是框架自动 Boot 交出去的那一份生产处理器，且共享栈干净。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _coordinator = UIService.Internal_PeekUGUIHandler();
            Assert.IsNotNull(_coordinator, "量具前提坏了：播放态框架没把那份协调者交出来，开窗腿拿不到共享栈");
            Assert.IsNull(UIService.GetTopWindow(), "量具前提坏了：进门时共享栈上不干净");
        }

        /// <summary>出门收口：栈上的窗一律关净（两支的面板各自收走），清理表里的面板本体当场销毁，等待腿的交回物归零。</summary>
        [TearDown]
        public void TearDown()
        {
            UIService.CloseAll(true);

            for (var i = 0; i < _trackedShells.Count; i++)
            {
                if (_trackedShells[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_trackedShells[i]);
                }
            }

            _trackedShells.Clear();

            _awaitedUgui = null;
            _awaitedKit = null;
            _coordinator = null;
        }

        #region 共存生命周期 [COEXISTENCE]

        /// <summary>
        /// 同一会话混开两支窗：两只都可见，深度各自落进自己后端的序空间——画布层的 layer 与文档组件的排序值是两回事。
        /// </summary>
        /// <remarks>
        /// 判据两侧都取面板事实而不是意图位：uGUI 那一侧是面板物体的 layer 与 <see cref="Canvas.sortingOrder"/>，
        /// UI Toolkit 那一侧是内容根的 <c>display</c>（<c>keyword</c> 离开 <see cref="StyleKeyword.Null"/> 才是真落过笔的凭据）
        /// 与 <see cref="UIDocument.sortingOrder"/>。两只窗分处 <see cref="EUILayer.UI"/> 与 <see cref="EUILayer.Tips"/>，
        /// 同一次层级排序喂给两支的却是各自的序空间。
        /// </remarks>
        [UnityTest]
        public IEnumerator Coexist_TwoTracksOpenInOneSession_EachStaysVisibleInItsOwnOrderSpace()
        {
            UIService.ShowUI<ProbeUGUIWindowOnUiLayer>("MixUGUI");
            UIService.ShowUI<ProbeUITKWindowOnTipsLayer>("MixUITK");

            var ugui = UIService.GetWindow<ProbeUGUIWindowOnUiLayer>("MixUGUI");
            var kit = UIService.GetWindow<ProbeUITKWindowOnTipsLayer>("MixUITK");
            Track(ugui);
            Track(kit);

            Assert.IsNotNull(ugui, "uGUI 腿开出的窗落在协调者那一份栈上");
            Assert.IsNotNull(kit, "UI Toolkit 腿开出的窗落进同一份共享栈");
            Assert.IsTrue(ugui.IsLoadDone && kit.IsLoadDone, "两支的面板都装载完了");
            Assert.IsTrue(ugui.IsPrepare && kit.IsPrepare, "两支都进了准备态");

            var stack = _coordinator.Internal_PeekStack();
            Assert.AreEqual(2, stack.Count, "混合栈上两只窗，一支一枚");
            Assert.AreSame(ugui, stack[0], "先开的在栈底");
            Assert.AreSame(kit, stack[1], "后开的压在栈顶");
            Assert.AreSame(kit, UIService.GetTopWindow(), "栈顶答的是后开的那一支");

            Assert.IsTrue(ugui.Visible, "uGUI 那支可见");
            Assert.IsTrue(kit.Visible, "UI Toolkit 那支同样可见——一支的开窗不压掉另一支");

            Assert.AreEqual(UI_LAYER_BASE, ugui.Depth, "uGUI 那支的深度落进本层基址");
            Assert.AreEqual(TIPS_LAYER_BASE, kit.Depth, "UI Toolkit 那支的深度落进自己那层基址");
            Assert.AreEqual(UI_LAYER_BASE, ugui.PanelCanvas.sortingOrder, "uGUI 的序落在画布排序上");
            Assert.AreEqual(UIService.WINDOW_SHOW_LAYER, ugui.PanelCanvas.gameObject.layer, "uGUI 的显隐落在面板 layer 上");
            Assert.AreEqual(TIPS_LAYER_BASE, kit.Document.sortingOrder, "UI Toolkit 的序落在文档组件的排序上");
            Assert.AreEqual(DisplayStyle.Flex, kit.RootVisual.style.display.value, "UI Toolkit 的显隐落在内容根的 display 上");
            Assert.AreNotEqual(StyleKeyword.Null, kit.RootVisual.style.display.keyword,
                "显示意图必须真写进槽：没写过的槽读回来同样是 Flex，只有 keyword 离开 Null 才证明落过笔");

            Assert.IsNull(ugui.PanelCanvas.GetComponent<UIDocument>(), "uGUI 的面板不背文档组件");
            Assert.IsNull(kit.gameObject.GetComponent<Canvas>(), "UI Toolkit 的壳不背画布");
            Assert.IsInstanceOf<UGUIWindow>(ugui);
            Assert.IsNotInstanceOf<UITKWindow>(ugui, "两支的窗口基类互不派生");
            Assert.IsInstanceOf<UITKWindow>(kit);
            Assert.IsNotInstanceOf<UGUIWindow>(kit, "两支的窗口基类互不派生");

            Assert.AreEqual(_sharedPanelSettings, kit.Document.panelSettings, "文档壳配的是后端共享的那份 PanelSettings");
            Assert.IsNotNull(kit.RootVisual, "内容根由窗口自己建出来");
            Assert.AreEqual(nameof(ProbeUITKWindowOnTipsLayer), kit.RootVisual.name, "内容根按窗口类型名命名（代码建树的落点）");

            yield return null;
        }

        /// <summary>
        /// 两支并在同一层：层级排序给同一层内的两只窗依次发序位，两支各拿自己那枚面板事实；关掉栈顶那一支不动栈底那一支。
        /// </summary>
        /// <remarks>
        /// <see cref="UIWindowLedger.OnSortWindowDepth"/> 的深度重排按栈序从本层基址起逐窗口加一档 <see cref="UIService.WINDOW_DEEP"/>，
        /// 认的是窗口而不是轨——所以 uGUI 面板的 <see cref="Canvas.sortingOrder"/> 与 UI Toolkit 文档的
        /// <see cref="UIDocument.sortingOrder"/> 拿到的是同一份序位表里的两格。
        /// </remarks>
        [UnityTest]
        public IEnumerator Coexist_TwoTracksOnOneLayer_SecondWindowTakesTheNextSlotOfThatLayer()
        {
            UIService.ShowUI<ProbeUGUIWindowOnTipsLayer>("SameUGUI");
            UIService.ShowUI<ProbeUITKWindowOnTipsLayer>("SameKit");

            var ugui = UIService.GetWindow<ProbeUGUIWindowOnTipsLayer>("SameUGUI");
            var kit = UIService.GetWindow<ProbeUITKWindowOnTipsLayer>("SameKit");
            Track(ugui);
            Track(kit);

            Assert.AreEqual(TIPS_LAYER_BASE, ugui.Depth, "同层第一只拿本层基址");
            Assert.AreEqual(TIPS_LAYER_BASE + UIService.WINDOW_DEEP, kit.Depth, "同层第二只拿下一档序位");
            Assert.AreEqual(TIPS_LAYER_BASE, ugui.PanelCanvas.sortingOrder, "第一只的序位落到画布");
            Assert.AreEqual(TIPS_LAYER_BASE + UIService.WINDOW_DEEP, kit.Document.sortingOrder, "第二只的序位落到文档");

            var kitPanel = kit.gameObject;
            UIService.CloseUI<ProbeUITKWindowOnTipsLayer>("SameKit");

            Assert.IsFalse(UIService.HasWindow<ProbeUITKWindowOnTipsLayer>("SameKit"), "关掉的那支已不在栈上");
            Assert.IsTrue(UIService.HasWindow<ProbeUGUIWindowOnTipsLayer>("SameUGUI"), "另一支照旧在栈上");
            Assert.AreSame(ugui, UIService.GetTopWindow(), "栈顶交回剩下的那一支");
            Assert.IsTrue(ugui.Visible, "关掉同层的栈顶不压掉剩下的那支");
            Assert.AreEqual(TIPS_LAYER_BASE, ugui.Depth, "剩下的那支仍是本层基址：层级重排没挪它");
            Assert.AreEqual(TIPS_LAYER_BASE, ugui.PanelCanvas.sortingOrder, "画布上的序位没被重算成别的值");

            yield return null;

            // 已销毁的 Unity 物体只有 Unity 的 == 运算符认得出（NUnit 的引用判据读回来是"非 null 但打印成 <null>"），
            // 本文件所有面板本体的在场/离场判据一律走这一枚运算符。
            Assert.IsTrue(kitPanel == null, "关掉那一支的文档壳在帧末真销毁");
            Assert.IsFalse(ugui.gameObject == null, "剩下的那支面板还在");
        }

        /// <summary>
        /// 关掉一支不影响另一支：另一支的可见性、序位与面板物体都不动，被关那一支的窗口与面板当场收走。
        /// </summary>
        /// <remarks>
        /// 被关的是 <see cref="EUILayer.UI"/>、留下的是 <see cref="EUILayer.Tips"/>：深度重排只重算被关那一支所属的层，
        /// 显隐回执从栈顶重发一次——两支因此各自答「没被动过」。
        /// </remarks>
        [UnityTest]
        public IEnumerator Coexist_CloseUGUIWindow_UITKWindowKeepsVisibilityAndOrder()
        {
            UIService.ShowUI<ProbeUGUIWindowOnUiLayer>("SoloUGUI");
            UIService.ShowUI<ProbeUITKWindowOnTipsLayer>("SoloKit");

            var ugui = UIService.GetWindow<ProbeUGUIWindowOnUiLayer>("SoloUGUI");
            var kit = UIService.GetWindow<ProbeUITKWindowOnTipsLayer>("SoloKit");
            Track(ugui);
            Track(kit);

            var kitDepthBefore = kit.Depth;
            var kitOrderBefore = kit.Document.sortingOrder;
            var kitPanel = kit.gameObject;
            var uguiPanel = ugui.gameObject;
            Assert.IsFalse(kitPanel == null, "量具前提坏了：UI Toolkit 那支的面板此刻应已绑上");
            Assert.IsFalse(uguiPanel == null, "量具前提坏了：uGUI 那支的面板此刻应已绑上");

            UIService.CloseUI<ProbeUGUIWindowOnUiLayer>("SoloUGUI");

            Assert.IsFalse(UIService.HasWindow<ProbeUGUIWindowOnUiLayer>("SoloUGUI"), "关掉的那支不在栈上了");
            Assert.IsTrue(ugui.IsDestroyed, "被关那一支的窗口对象模型记着已销毁");
            Assert.IsTrue(kit.Visible, "另一支照旧可见");
            Assert.AreEqual(kitDepthBefore, kit.Depth, "另一支的深度意图没被这次关闭改动");
            Assert.AreEqual(kitOrderBefore, kit.Document.sortingOrder, "另一支面板上的序位也没被改动");
            Assert.AreEqual(1, _coordinator.Internal_PeekStack().Count, "栈上只剩另一支");
            Assert.AreSame(kit, UIService.GetTopWindow(), "栈顶是另一支");
            Assert.IsFalse(kit.gameObject == null, "另一支的面板物体没被这次关闭牵连");

            yield return null;

            Assert.IsTrue(uguiPanel == null, "被关那一支的面板在帧末真销毁");
            Assert.IsFalse(kitPanel == null, "另一支的面板照旧在场");
        }

        /// <summary>
        /// 混合栈上的查询与全关：<see cref="UIService.HasWindow{T}(string)"/>、两枚 <see cref="UIService.GetTopWindow()"/> 与
        /// <see cref="UIService.CloseAll(bool)"/> 答的都是同一条栈，两支不各自留一份。
        /// </summary>
        [UnityTest]
        public IEnumerator MixedStack_ThreeWindowsOnTwoTracks_QueriesAndCloseAllAnswerOneStack()
        {
            UIService.ShowUI<ProbeUITKWindowOnTipsLayer>("LedgerKitA");
            UIService.ShowUI<ProbeUGUIWindowOnUiLayer>("LedgerUGUI");
            UIService.ShowUI<ProbeUITKWindowOnTipsLayer>("LedgerKitB");

            var kitA = UIService.GetWindow<ProbeUITKWindowOnTipsLayer>("LedgerKitA");
            var ugui = UIService.GetWindow<ProbeUGUIWindowOnUiLayer>("LedgerUGUI");
            var kitB = UIService.GetWindow<ProbeUITKWindowOnTipsLayer>("LedgerKitB");
            Track(kitA);
            Track(ugui);
            Track(kitB);

            var stack = _coordinator.Internal_PeekStack();
            Assert.AreEqual(3, stack.Count, "三条开栈落进同一份栈");
            Assert.IsTrue(UIService.HasWindow<ProbeUITKWindowOnTipsLayer>("LedgerKitA"), "同型两只窗按名字各答各的");
            Assert.IsTrue(UIService.HasWindow<ProbeUITKWindowOnTipsLayer>("LedgerKitB"), "同型两只窗按名字各答各的");
            Assert.IsTrue(UIService.HasWindow<ProbeUGUIWindowOnUiLayer>("LedgerUGUI"), "uGUI 那支也在同一份栈上");
            Assert.IsFalse(UIService.HasWindow<ProbeUGUIWindowOnUiLayer>("NoSuchWindow"), "没开过的名字答假");
            Assert.AreSame(kitB, UIService.GetTopWindow(), "全栈栈顶是最后压上的那一只");
            Assert.AreSame(ugui, UIService.GetTopWindow((int)EUILayer.UI), "按层取顶答的是本层那一只 uGUI 窗");
            Assert.AreSame(kitB, UIService.GetTopWindow((int)EUILayer.Tips), "按层取顶在 UI Toolkit 那两只里取栈序末位");
            Assert.AreEqual("LedgerKitB", UIService.GetTopWindowName((int)EUILayer.Tips), "按层取顶名同判据");
            Assert.IsFalse(UIService.IsAnyLoading(), "三支的面板都装载完了");

            var kitAPanel = kitA.gameObject;
            var uguiPanel = ugui.gameObject;
            var kitBPanel = kitB.gameObject;

            UIService.CloseAll();

            Assert.AreEqual(0, stack.Count, "全关之后栈空");
            Assert.IsNull(UIService.GetTopWindow(), "全关之后没有栈顶");
            Assert.IsFalse(UIService.HasWindow<ProbeUITKWindowOnTipsLayer>("LedgerKitA"), "全关之后查询答假");
            Assert.IsFalse(UIService.HasWindow<ProbeUITKWindowOnTipsLayer>("LedgerKitB"), "全关之后查询答假");
            Assert.IsFalse(UIService.HasWindow<ProbeUGUIWindowOnUiLayer>("LedgerUGUI"), "全关之后查询答假");

            yield return null;

            Assert.IsTrue(kitAPanel == null, "两支的面板都随全关销毁：UI Toolkit 壳一枚不剩");
            Assert.IsTrue(kitBPanel == null, "两支的面板都随全关销毁：UI Toolkit 壳一枚不剩");
            Assert.IsTrue(uguiPanel == null, "uGUI 那支的面板也随全关销毁");
        }

        #endregion

        #region 真装载与内置资源分支 [REAL LOAD]

        /// <summary>
        /// <c>fromResources=true</c> 那一支的可达性与真装载：模板走 <c>Resources.Load&lt;VisualTreeAsset&gt;</c>，
        /// 壳由 <see cref="UITKWindow"/> 自己按「未激活 → 配置 → 激活」建出来，与同会话的 uGUI 窗并排在场。
        /// </summary>
        /// <remarks>
        /// 开门两行是前提实测，不是被测判据：内置资源那一路在播放态取不取得到、缓存实例的停放与重开都要靠它，
        /// 取不到就红在这里并带资源名，不让下面的装载判据去猜是配置坏了还是代码坏了。 <br />
        /// 这一格是本轮唯一真点亮文档壳的一格：<see cref="UIDocument"/> 的根元素与面板在启用流程里才建，
        /// 判据因此读得到内容根已挂进文档根元素——代码建树的那几格故意停在未激活态，读不到这一笔。
        /// </remarks>
        [UnityTest]
        public IEnumerator ShowUI_FromResourcesTemplate_BuildsActiveDocumentShellBesideTheUGUIWindow()
        {
            Assert.IsNotNull(Resources.Load<VisualTreeAsset>(TEMPLATE_RESOURCE_NAME),
                "量具前提坏了：播放态取不到内置资源模板 {0}，fromResources 那一支无从覆盖", TEMPLATE_RESOURCE_NAME);

            UIService.ShowUI<ProbeCachedResourcesKitWindow>("RealKit");
            var kit = UIService.GetWindow<ProbeCachedResourcesKitWindow>("RealKit");
            Track(kit);

            Assert.IsFalse(kit.gameObject == null, "真装载必须建出文档壳");
            Assert.IsTrue(kit.IsLoadDone, "模板取到即装载完成");
            Assert.Greater(kit.RootVisual.childCount, 0, "模板克隆进内容根：代码建树之外这一支吃的是资产");
            Assert.IsNotNull(kit.RootVisual.parent, "壳已激活 ⇒ 文档根元素在场，内容根应挂上去");
            Assert.AreEqual(_sharedPanelSettings, kit.Document.panelSettings, "面板配置由装载路径写入");
            Assert.IsTrue(kit.Visible, "真装载的 UI Toolkit 窗照样可见");

            UIService.ShowUI<ProbeUGUIWindowOnUiLayer>("RealUGUI");
            var ugui = UIService.GetWindow<ProbeUGUIWindowOnUiLayer>("RealUGUI");
            Track(ugui);

            Assert.AreEqual(2, _coordinator.Internal_PeekStack().Count, "真装载的窗与代码面板的窗并排落在同一条栈");
            Assert.AreEqual(TIPS_LAYER_BASE, kit.Depth, "本层只有它一只：序位仍是本层基址");
            Assert.AreEqual(UI_LAYER_BASE, ugui.PanelCanvas.sortingOrder, "后开的 uGUI 窗把序位落在自己那层的基址上");
            Assert.IsTrue(kit.Visible && ugui.Visible, "两支同时可见");
            Assert.AreEqual(UIService.WINDOW_SHOW_LAYER, ugui.PanelCanvas.gameObject.layer, "uGUI 那支的显隐落点");
            Assert.AreEqual(DisplayStyle.Flex, kit.RootVisual.style.display.value, "UI Toolkit 那支的显隐落点");

            yield return null;
        }

        #endregion

        #region 等待腿 [AWAIT LEG]

        /// <summary>
        /// 等待腿等的是「面板就绪」：面板在若干帧之后才绑上时，续体等到位才交回窗口，交回的就是栈上那一只。
        /// </summary>
        /// <remarks>
        /// 复用支路在第一个 await 之前就返回，量不到这一半；本格的装载钩子真的排了 <see cref="PANEL_DELAY_SECONDS"/> 的帧，
        /// 于是「同帧没落定 → 跨帧落定」是量出来的而不是推的。等待期间 <see cref="UIService.IsAnyLoading()"/> 答真，
        /// 两支各自把序位结算到自己那枚面板事实上。 <br />
        /// 超时那一档（账本 <see cref="UIWindowLedger"/> 的 60 秒上界 <c>LOAD_WAIT_TIMEOUT_SECONDS</c> 与那条 Warning）不在本文件射程：等它是 60 秒真实时间，
        /// 判据要红也要等满，交回别的量具口径。
        /// </remarks>
        [UnityTest]
        public IEnumerator ShowUIAsyncAwait_PanelReadyLater_ReturnsWindowAfterItsPanelIsBound()
        {
            KickDelayedUGUI().Forget();
            KickDelayedKit().Forget();

            var ugui = UIService.GetWindow<ProbeDelayedUGUIWindow>("WaitUGUI");
            var kit = UIService.GetWindow<ProbeDelayedUITKWindow>("WaitKit");
            Track(ugui);
            Track(kit);

            Assert.IsNotNull(ugui, "等待腿把窗口压栈之后才去等面板");
            Assert.IsNotNull(kit, "等待腿把窗口压栈之后才去等面板");
            Assert.AreEqual(2, _coordinator.Internal_PeekStack().Count, "两支都已在同一条栈上");
            Assert.IsFalse(ugui.IsLoadDone, "量具前提坏了：面板此刻还没到");
            Assert.IsFalse(kit.IsLoadDone, "量具前提坏了：面板此刻还没到");
            Assert.IsNull(_awaitedUgui, "面板没就绪时等待腿同帧不落定");
            Assert.IsNull(_awaitedKit, "面板没就绪时等待腿同帧不落定");
            Assert.IsTrue(UIService.IsAnyLoading(), "有窗在装载路上即答真");

            yield return PumpUntil(() => _awaitedUgui != null && _awaitedKit != null, PUMP_TIMEOUT_SECONDS);

            Assert.IsTrue(ugui.IsLoadDone, "面板在若干帧之后绑上");
            Assert.IsTrue(kit.IsLoadDone, "面板在若干帧之后绑上");
            Assert.AreSame(ugui, _awaitedUgui, "等待腿交回的就是栈上那一只 uGUI 窗");
            Assert.AreSame(kit, _awaitedKit, "等待腿交回的就是栈上那一只 UI Toolkit 窗");
            Assert.IsFalse(UIService.IsAnyLoading(), "两支都就绪之后不再答真");
            Assert.IsTrue(ugui.Visible && kit.Visible, "就绪回执把显隐意图结算到位");
            Assert.AreEqual(UI_LAYER_BASE, ugui.PanelCanvas.sortingOrder, "就绪之后 uGUI 的序位落到画布");
            Assert.AreEqual(TIPS_LAYER_BASE, kit.Document.sortingOrder, "就绪之后 UI Toolkit 的序位落到文档");

            yield return null;
        }

        /// <summary>
        /// 面板同帧就绪时等待腿同帧落定：账本的 <c>IsLoadDone</c> 短路在第一个 await 之前就把栈上那一只交回，跨帧那一段根本不曾开始。
        /// </summary>
        /// <remarks>
        /// 硬切后的两档以「装载有没有跨帧」为界，不再以「是不是新开的窗」为界（引 spec §4.1「复用/停放命中 → 同步完成」）：
        /// 账本 <c>UIWindowLedger.ShowUIAwaitImp</c> 的早退判的是 <c>ResolveOrStartLoad</c> 交回时 <see cref="UIWindow.IsLoadDone"/>
        /// 已为真，这一档如今吃三条来路——栈上已有同名窗、停放表命中，以及新开窗的装载当场落定。新开窗那一条按两支探针各自实测：
        /// 本格的 <c>ProbeUGUIWindowOnUiLayer</c> 与 <c>ProbeUITKWindowOnTipsLayer</c> 异步腿交出的是已完成的 <c>UniTask</c>
        /// （<c>UniTask.FromResult</c>），await 一枚已完成的 awaiter 由状态机就地续跑、不排下一帧 ⇒ 装载在同一次
        /// <c>InternalLoad(...).Forget()</c> 里走到 <c>PanelLoaded</c>，等待腿因此同帧交回；上一格的
        /// <c>ProbeDelayedUGUIWindow</c> 与 <c>ProbeDelayedUITKWindow</c> 异步腿 await 的是未完成的 <c>UniTask.WaitForSeconds</c> ⇒
        /// 就绪位在检查时还是假，这才走到 <c>UniTask.Yield</c> + <c>WaitPanelReadyAsync</c> 的跨帧腿。 <br />
        /// 本格旧文钉的是被硬切推翻的那一份（「就绪位已为真仍同帧不落定」），随 §4.1 拍板退役；交回物既已同帧在位，
        /// 判据不再写跨帧等待，末尾那一帧只补判「同帧落定之后不补压第二只、交回物也没被换掉」。
        /// </remarks>
        [UnityTest]
        public IEnumerator ShowUIAsyncAwait_PanelReadyInPlace_ResolvesInTheSameFrame()
        {
            KickInPlaceUGUI().Forget();
            KickInPlaceKit().Forget();

            var ugui = UIService.GetWindow<ProbeUGUIWindowOnUiLayer>("InPlaceUGUI");
            var kit = UIService.GetWindow<ProbeUITKWindowOnTipsLayer>("InPlaceKit");
            Track(ugui);
            Track(kit);

            Assert.IsTrue(ugui.IsLoadDone, "同步就绪的装载钩子当场把面板绑上");
            Assert.IsTrue(kit.IsLoadDone, "同步就绪的装载钩子当场把面板绑上");
            Assert.IsNotNull(_awaitedUgui,
                "spec §4.1「复用/停放命中 → 同步完成」：装载同帧落定的新开窗同走账本 ShowUIAwaitImp 的 IsLoadDone 短路，等待腿当帧就交回");
            Assert.IsNotNull(_awaitedKit,
                "spec §4.1「复用/停放命中 → 同步完成」：另一轨同判据——就绪位同帧为真即同帧交回，不等下一帧");

            Assert.AreSame(ugui, _awaitedUgui, "同帧交回的就是栈上那一只");
            Assert.AreSame(kit, _awaitedKit, "同帧交回的就是栈上那一只");
            Assert.AreEqual(2, _coordinator.Internal_PeekStack().Count, "等待腿不压第二只");

            // 交回物的落定不靠续体，这一帧因此没有时序要等；它判的是同帧短路之后不再有第二次结算
            yield return null;

            Assert.AreEqual(2, _coordinator.Internal_PeekStack().Count, "跨一帧栈上仍是两只：等待腿没有延后的第二次压入");
            Assert.AreSame(ugui, _awaitedUgui, "跨一帧交回物仍是同一只实例");
            Assert.AreSame(kit, _awaitedKit, "跨一帧交回物仍是同一只实例");
        }

        private async UniTaskVoid KickDelayedUGUI()
        {
            _awaitedUgui = await UIService.ShowUIAsyncAwait<ProbeDelayedUGUIWindow>("WaitUGUI");
        }

        private async UniTaskVoid KickDelayedKit()
        {
            _awaitedKit = await UIService.ShowUIAsyncAwait<ProbeDelayedUITKWindow>("WaitKit");
        }

        private async UniTaskVoid KickInPlaceUGUI()
        {
            _awaitedUgui = await UIService.ShowUIAsyncAwait<ProbeUGUIWindowOnUiLayer>("InPlaceUGUI");
        }

        private async UniTaskVoid KickInPlaceKit()
        {
            _awaitedKit = await UIService.ShowUIAsyncAwait<ProbeUITKWindowOnTipsLayer>("InPlaceKit");
        }

        #endregion

        #region 停放与未绑定 [PARK]

        /// <summary>
        /// 缓存实例的停放与重开（uGUI 轨）：关闭按瞬时档当场停放（面板留在但不再激活），重开时同一只实例回到栈上并重新激活。
        /// </summary>
        /// <remarks>
        /// 关闭默认无过渡（<see cref="UIWindow.Transition"/> 缺位即瞬时）：停放与出栈同帧发生，不推帧也量得到。 <br />
        /// 停放表与栈的归属判据走协调者那两枚 internal 门缝，面板激活位取的是物体事实。
        /// </remarks>
        [UnityTest]
        public IEnumerator ParkPanel_CachedUGUIWindowAfterInstantClose_StaysParkedUntilReopened()
        {
            UIService.ShowUI<ProbeCachedUGUIWindow>("ParkUGUI");
            var ugui = UIService.GetWindow<ProbeCachedUGUIWindow>("ParkUGUI");
            Track(ugui);

            var panel = ugui.gameObject;
            Assert.IsFalse(panel == null, "量具前提坏了：面板此刻应已绑上");
            Assert.IsTrue(panel.activeSelf, "开出来的面板带激活态");

            UIService.CloseUI<ProbeCachedUGUIWindow>("ParkUGUI");

            Assert.IsNull(UIService.GetWindow<ProbeCachedUGUIWindow>("ParkUGUI"), "关掉的窗已不在栈上");
            Assert.IsFalse(panel.activeSelf, "瞬时关闭当场停放：物体留着但不激活");
            Assert.IsTrue(_coordinator.Internal_IsParked("ParkUGUI"), "缓存实例进协调者那一份停放表");
            Assert.IsNull(UIService.GetTopWindow(), "栈上空");

            UIService.ShowUI<ProbeCachedUGUIWindow>("ParkUGUI");

            Assert.IsTrue(panel.activeSelf, "重开交回同一只实例并重新激活");
            Assert.AreSame(ugui, UIService.GetWindow<ProbeCachedUGUIWindow>("ParkUGUI"), "重开的是那一只缓存实例");
            Assert.IsFalse(_coordinator.Internal_IsParked("ParkUGUI"), "停放表里已无这一名");
            Assert.IsTrue(ugui.Visible, "重开之后可见性意图照旧结算");
            Assert.AreEqual(UI_LAYER_BASE, ugui.PanelCanvas.sortingOrder, "重开之后序位仍归本层基址");

            yield return null;
        }

        /// <summary>
        /// 登记现状：<c>ParkPanel</c> 不守卫绑定——面板从未绑上的窗口走到这一枚钩子直接抛 <see cref="NullReferenceException"/>，两支同形。
        /// </summary>
        /// <remarks>
        /// 这一格只钉「现状」，不是背书：同名的三份意图写入与 <see cref="UITKWindow.ApplySafeInsets"/> 都在未绑定时判为空操作，
        /// 唯独停放直读物体引用。窗口在装载被拒之后仍留在栈上，若走到关闭动画末尾的停放，抛点就落在续体里——
        /// 判据因此打在钩子本身上，不由续体的时序代答。
        /// </remarks>
        [Test]
        public void ParkPanel_WindowWithUnboundPanel_ThrowsNullReferenceOnBothTracks()
        {
            var kit = new ProbeUITKWindowOnTipsLayer();
            kit.Init("UnboundKit", (int)EUILayer.Tips, false, "Panel", false, 10, false);
            Assert.IsNull(kit.gameObject, "量具前提坏了：未装载的窗口此刻没有面板本体");
            Assert.Throws<NullReferenceException>(() => kit.ParkForTest(),
                "UI Toolkit 那一轨的停放不守卫绑定：未绑定时即抛");

            var ugui = new ProbeUGUIWindowOnUiLayer();
            ugui.Init("UnboundUGUI", (int)EUILayer.UI, false, "Panel", false, 10, false);
            Assert.IsNull(ugui.gameObject, "量具前提坏了：未装载的窗口此刻没有面板本体");
            Assert.Throws<NullReferenceException>(() => ugui.ParkForTest(),
                "uGUI 那一轨的停放同形：未绑定时即抛");
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>带超时上界的帧推进：条件成立即出门，超过上界把现场交回用例判红，不写无限等。</summary>
        /// <param name="condition">出门条件。</param>
        /// <param name="timeoutSeconds">等待上界（秒，真实时间）。</param>
        private static IEnumerator PumpUntil(Func<bool> condition, float timeoutSeconds)
        {
            float deadline = Time.unscaledTime + timeoutSeconds;
            while (!condition() && Time.unscaledTime < deadline)
            {
                yield return null;
            }
        }

        /// <summary>把窗口此刻的面板本体登记进出门清理表；面板不在场时留一条空位，清理侧自会跳过。</summary>
        private void Track(UIWindow window)
        {
            _trackedShells.Add(window?.gameObject);
        }

        /// <summary>
        /// 代码建出的 uGUI 面板：只带一枚 <see cref="Canvas"/>——它是 <see cref="UGUIWindow.BindPanel"/> 认的那一枚组件，
        /// 拾取器与资产地址都不在本文件的判据里。
        /// </summary>
        /// <param name="name">面板物体名（<c>BindPanel</c> 会按窗口类型名改写）。</param>
        private static GameObject NewCodeUGUIPanel(string name)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.SetActive(true);
            panel.transform.SetParent(UIService.UIRoot, false);
            panel.AddComponent<Canvas>();
            return panel;
        }

        /// <summary>
        /// 代码建出的 UI Toolkit 文档壳：建成即停在未激活态，<see cref="UIDocument"/> 配好共享配置后交 <c>BindPanel</c>。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="UITKWindow"/> 装载路径同一条配置判据（先把 <see cref="PanelSettings"/> 就位再谈激活），
        /// 本文件的判据不读文档根元素，因此故意不点亮这一枚壳。
        /// </remarks>
        private static GameObject NewCodeUITKShell()
        {
            var shell = new GameObject("UITKShell", typeof(RectTransform));
            shell.SetActive(false);
            shell.transform.SetParent(UIService.UIRoot, false);
            var document = shell.AddComponent<UIDocument>();
            document.panelSettings = UITKWindow.SharedPanelSettings;
            return shell;
        }

        /// <summary>uGUI 轨探针窗（<see cref="EUILayer.UI"/>）：同步与异步装载都当场交出代码面板。</summary>
        [Window(EUILayer.UI)]
        internal sealed class ProbeUGUIWindowOnUiLayer : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));

            internal void ParkForTest() => ParkPanel();
        }

        /// <summary>uGUI 轨探针窗（<see cref="EUILayer.Tips"/>）：与另一轨同层时判序位表用。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class ProbeUGUIWindowOnTipsLayer : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));
        }

        /// <summary>UI Toolkit 轨探针窗（<see cref="EUILayer.Tips"/>）：代码建树，内容根不带模板克隆。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class ProbeUITKWindowOnTipsLayer : UITKWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUITKShell(), null);

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));

            internal void ParkForTest() => ParkPanel();
        }

        /// <summary>uGUI 轨延迟装载探针窗（<see cref="EUILayer.UI"/>）：面板在若干帧之后才绑上，等待腿要等的就是这一段。</summary>
        [Window(EUILayer.UI)]
        internal sealed class ProbeDelayedUGUIWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override async UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct)
            {
                await UniTask.WaitForSeconds(PANEL_DELAY_SECONDS, true, cancellationToken: ct);
                return LoadPanel(assetLocation, fromResources);
            }
        }

        /// <summary>UI Toolkit 轨延迟装载探针窗（<see cref="EUILayer.Tips"/>）：同上一条腿，另一轨各量一次。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class ProbeDelayedUITKWindow : UITKWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUITKShell(), null);

            protected internal override async UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct)
            {
                await UniTask.WaitForSeconds(PANEL_DELAY_SECONDS, true, cancellationToken: ct);
                return LoadPanel(assetLocation, fromResources);
            }
        }

        /// <summary>uGUI 轨缓存实例探针窗（<see cref="EUILayer.UI"/>）：关闭后停放，重开交回同一只。</summary>
        [Window(EUILayer.UI, cacheInstance: true)]
        internal sealed class ProbeCachedUGUIWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));
        }

        /// <summary>UI Toolkit 轨真装载探针窗（<see cref="EUILayer.Tips"/>）：模板取内置资源、缓存实例，装载路径不覆写。</summary>
        [Window(EUILayer.Tips, TEMPLATE_RESOURCE_NAME, true, cacheInstance: true)]
        internal sealed class ProbeCachedResourcesKitWindow : UITKWindow
        {
        }

        #endregion
    }
}
