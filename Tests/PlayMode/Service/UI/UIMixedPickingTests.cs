using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Service.UI
{
    /// <summary>
    /// 跨技术拾取与遮挡用例：真指针（合成 <see cref="Mouse"/>）打在屏幕上，判「谁真的吃到了这次点击」，
    /// 再拿它与协调者的模态遮挡判据对表。
    /// </summary>
    /// <remarks>
    /// 量具只走 <see cref="InputTestFixture"/> + <see cref="InputSystem.AddDevice{TDevice}"/> 这一条路：
    /// <c>VisualElement.SendEvent()</c> 是直投元素、<b>跳过 hit-test</b>，层序再错也恒绿，答不了本文件的题；
    /// 2022.3 一侧 <c>UnityEngine.UIElements.Panel</c> 是 internal、<c>UIDocument.panel</c> 也不存在 ⇒ 没有任何公开的
    /// hit-test 查询 API，只能派真事件再断「谁收到了」。 <br />
    /// 投递坐标：面板矩形以<b>左上</b>为原点，<see cref="Mouse.position"/> 以<b>左下</b>为原点，本文件按
    /// <see cref="PanelYOriginIsTopLeft"/> 换算一次；点击点一律取<b>版式之后量到的</b> <c>worldBound</c>，不假设屏幕尺寸。
    /// 两枚盒子的相对位置让中心点在 Y 翻转下不动、且「只盖住下面那一枚」的点在两种口径下都落在同一侧，
    /// 于是正对照的绿红不取决于原点约定，只取决于派发本身通不通。 <br />
    /// 版式前提：元素未 attach 就没有可命中的矩形，投点前一律 <see cref="PumpUntil"/> 等到 <c>worldBound</c> 有宽度为止；
    /// 等不到就把现场读数（屏幕尺寸、<c>root.panel</c> 是否为空、两枚 <c>worldBound</c>）交回失败消息——
    /// 那些读数是「本环境能否机器证拾取」的唯一凭据。 <br />
    /// 运行前提与共存生命周期夹具同源：播放态框架已自动 Boot，开窗一律走门面 <see cref="UIService"/> 落到那一份生产
    /// 协调者（<see cref="UGUIHandler"/> 兼任），夹具不自建协调者、不经换入换出接缝。UI Toolkit 的共享
    /// <see cref="PanelSettings"/> 是程序集级静态写口，进门存原值、出门按原值交回。 <br />
    /// 线程契约：仅主线程。
    /// </remarks>
    [TestFixture]
    public sealed class UIMixedPickingTests : InputTestFixture
    {
        /// <summary>投递坐标的 Y 原点口径：<b>真</b> 表示面板按左上原点换算成 <c>Mouse.position</c>（左下原点）。</summary>
        /// <remarks>第一次红只改这一枚口径（连同它下面的换算），不动任何断言；翻完仍红才归因到量具。</remarks>
        private const bool PanelYOriginIsTopLeft = true;

        /// <summary>等版式落定的上界（秒，真实时间）：超过即把现场读数交回用例判红，不写无限等。</summary>
        private const float LAYOUT_TIMEOUT_SECONDS = 5f;

        /// <summary><see cref="EUILayer.Tips"/> 那一层的序空间基址。</summary>
        private const int TIPS_LAYER_BASE = (int)EUILayer.Tips * UIService.LAYER_DEEP;

        private UGUIHandler _coordinator;
        private PanelSettings _enteredSharedPanelSettings;
        private PanelSettings _sharedPanelSettings;
        private Mouse _mouse;
        private readonly List<GameObject> _trackedPanels = new List<GameObject>();

        /// <summary>进门把包内那枚带主题的夹具配置交 UI Toolkit 后端使用；取不到就红在前提上。</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _enteredSharedPanelSettings = UITKWindow.SharedPanelSettings;
            _sharedPanelSettings = Resources.Load<PanelSettings>("DebuggerPanelSettings");
            Assert.IsNotNull(_sharedPanelSettings,
                "量具前提坏了：取不到 DebuggerPanelSettings 夹具资产，UI Toolkit 那一轨的面板无从建成");
            UITKWindow.SharedPanelSettings = _sharedPanelSettings;
        }

        /// <summary>出门把共享 <c>PanelSettings</c> 按进门那一份交回，不给同域后跑的用例留夹具配置。</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            UITKWindow.SharedPanelSettings = _enteredSharedPanelSettings;
        }

        /// <summary>
        /// 每格进门：先把 InputSystem 收进测试夹具的隔离态（合成设备要在这之后才加），再钉框架侧的三条运行前提。
        /// </summary>
        /// <remarks>
        /// <see cref="InputTestFixture.Setup"/> 会复位整个 InputSystem 并断言「复位后没有设备」，所以合成
        /// <see cref="Mouse"/> 只能在 <c>base.Setup()</c> 之后加；坐标写口认的也正是这一份夹具态里的设备。
        /// </remarks>
        public override void Setup()
        {
            base.Setup();

            _mouse = InputSystem.AddDevice<Mouse>();
            Assert.IsNotNull(_mouse, "量具前提坏了：合成 Mouse 设备没建出来");

            _coordinator = UIService.Internal_PeekUGUIHandler();
            Assert.IsNotNull(_coordinator, "量具前提坏了：播放态框架没把那份协调者交出来");
            Assert.IsNull(_coordinator.GetTopWindow(), "量具前提坏了：进门时共享栈上不干净");
        }

        /// <summary>出门收口：先关净栈上的窗、销毁面板本体，再把 InputSystem 交回夹具外的真实态。</summary>
        public override void TearDown()
        {
            UIService.CloseAll(true);

            for (var i = 0; i < _trackedPanels.Count; i++)
            {
                if (_trackedPanels[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_trackedPanels[i]);
                }
            }

            _trackedPanels.Clear();

            _mouse = null;
            _coordinator = null;

            base.TearDown();
        }

        #region 量具正对照 [POSITIVE CONTROL]

        /// <summary>
        /// 正对照：同一枚面板里叠两盒，后加入的那一枚盖在先加入的上面；真指针投在重叠区，断<b>后者吃到、前者没吃到</b>。
        /// </summary>
        /// <remarks>
        /// 先投「只盖住下面那一枚」的点：那一格若也吃不到，说明本环境根本没把合成指针派发到 UIElements，
        /// 失败消息带屏幕尺寸与两枚 <c>worldBound</c> 的现场读数——后面每一格的红绿都以这一格为凭。 <br />
        /// 再投重叠区：只有真的走了 hit-test 才会答出上面那一枚，这是「量具不是恒绿」的证据（<c>SendEvent</c> 给不出这一笔）。 <br />
        /// 两枚盒子的几何让重叠区中心落在屏幕上（近似中心），且「只盖住下面那一枚」那点按 Y 翻转前后都仍在同一侧，
        /// 因此这一格的红绿与原点约定无关。
        /// </remarks>
        /// <remarks>
        /// 【为什么挂在 <c>Explicit</c>：本环境不能机器证拾取，只能单跑】这一格单独投一轮是绿的；只要同一次播放域里
        /// 有前序格子建过 <c>EventSystem</c> + <c>InputSystemUIInputModule</c>，同一次真按下就会在 <c>UIElements</c>
        /// 一侧派发<b>两遍</b>（<c>Expected: 1 But was: 2</c>，两遍是同一个事件实例）或干脆<b>一遍不派</b>
        /// （<c>received=0</c>）。前序格离场时场景里的伴随物已被摘干净（<c>eventSystems=0</c>、<c>modules=0</c>、
        /// <c>canvases=0</c>，进下一格时的现场与全场第一格进门时逐字相同），串味的是<b>本域里派发拓扑本身</b>：
        /// 合成指针经 <c>com.unity.inputsystem</c> 的 <c>InputForUI</c> 桥进 <c>UIElements</c>，该桥的注册随
        /// <c>InputTestFixture</c> 的逐格复位与本夹具自己的 <c>EventSystem</c> 生灭而错乱（把模块改为全夹具常驻后，
        /// 六格一组直接炸出 <c>InputSystemProvider.OnPointerPerformed</c> 的 <c>NullReferenceException</c>）。
        /// ⇒ 遮挡语义与断言都不背这个锅；改投递坐标、改摆位、改建时机都翻不动它。
        /// 【复现配方】不依赖任何未入库的用例也能复现：先造一枚"在自己这格里建 <c>EventSystem</c> +
        /// <c>InputSystemUIInputModule</c>、又在下一格之前把它拆掉"的前序格，把它的<b>全名</b>与本格的<b>全名</b>
        /// 放进同一次 PlayMode 投单连跑 ⇒ 本格红（<c>Expected: 1 But was: 2</c>，两遍是同一个事件实例；换一轮漂移成
        /// <c>received=0</c>）；只投本格一枚 ⇒ 本格绿。再把前序那枚建 <c>EventSystem</c> 的那一步短路掉连跑 ⇒
        /// 本格在同一次连跑里转绿。红绿只由那一次"建了又拆"决定，与本格的投递坐标、摆位、断言都无关。
        /// </remarks>
        [Ignore("本环境不能机器证拾取（同域连跑时派发拓扑翻倍，见上方 remarks 的复现配方）；跨技术遮挡与拾取仅人工验收，工单见计划 N25")]
        [UnityTest]
        public IEnumerator PointerPicking_TwoElementsInOnePanel_LaterAddedOneTakesTheOverlap()
        {
            var window = OpenUITKWindow("PickPositiveKit");
            var root = window.RootVisual;

            // 内容根铺满屏幕：两枚子盒的绝对坐标才有可命的屏幕矩形（根自己零高时子树会被裁出命中区）
            root.style.width = Screen.width;
            root.style.height = Screen.height;

            var bottom = new VisualElement { name = "BottomBox" };
            var top = new VisualElement { name = "TopBox" };
            StyleScreenBox(bottom, new Vector2(Screen.width * 0.5f - 200f, Screen.height * 0.5f - 200f), 400f);
            StyleScreenBox(top, new Vector2(Screen.width * 0.5f - 100f, Screen.height * 0.5f - 100f), 300f);

            var bottomDown = 0;
            var topDown = 0;
            var bottomClick = 0;
            var topClick = 0;
            bottom.RegisterCallback<PointerDownEvent>(_ => bottomDown++);
            top.RegisterCallback<PointerDownEvent>(_ => topDown++);
            bottom.RegisterCallback<ClickEvent>(_ => bottomClick++);
            top.RegisterCallback<ClickEvent>(_ => topClick++);

            root.Add(bottom);
            root.Add(top);

            yield return PumpUntil(() => bottom.worldBound.width > 0f && top.worldBound.width > 0f,
                LAYOUT_TIMEOUT_SECONDS);

            Assert.IsNotNull(root.parent, "量具前提坏了：内容根没挂进文档根元素（壳没点亮 / 面板没建成）");
            Assert.IsNotNull(root.panel,
                "量具没派发：内容根没有面板可依附，屏幕上根本没有可命中的矩形。{0}", Facts(root, bottom, top));
            Assert.Greater(bottom.worldBound.width, 0f,
                "量具没派发：下面那一枚没有版式宽度，投点无从命中。{0}", Facts(root, bottom, top));
            Assert.Greater(top.worldBound.width, 0f,
                "量具没派发：上面那一枚没有版式宽度，投点无从命中。{0}", Facts(root, bottom, top));

            var overlap = Intersection(bottom.worldBound, top.worldBound);
            var bottomOnly = new Vector2(bottom.worldBound.xMin + 10f, bottom.worldBound.yMin + 10f);
            Assert.Greater(overlap.width, 0f, "量具前提坏了：两枚盒子在屏幕上没交叠。{0}", Facts(root, bottom, top));
            Assert.IsFalse(top.worldBound.Contains(bottomOnly),
                "量具前提坏了：这个投点在两枚盒子里都落得住，判不出先后。{0}", Facts(root, bottom, top));

            // 第一段：只盖住下面那一枚的点——派发链路本身通不通，就看这一笔
            yield return ClickPanelPoint(bottomOnly);
            Assert.Greater(bottomDown, 0,
                "量具没派发：合成指针打在只属于下面那一枚的点上，UIElements 一侧一次 PointerDown 都没收到。{0}",
                Facts(root, bottom, top));
            Assert.AreEqual(0, topDown, "量具前提坏了：投点落到了上面那一枚的矩形里。{0}", Facts(root, bottom, top));

            bottomDown = topDown = bottomClick = topClick = 0;

            // 第二段：重叠区——只有真走 hit-test 才会答出后加入的那一枚
            yield return ClickPanelPoint(overlap.center);

            Assert.AreEqual(1, topDown, "重叠区里后加入的那一枚没吃到点击。{0}", Facts(root, bottom, top));
            Assert.AreEqual(1, topClick, "重叠区里后加入的那一枚没结算出点击。{0}", Facts(root, bottom, top));
            Assert.AreEqual(0, bottomDown, "重叠区里先加入的那一枚吃到了点击：面板内的层序没被 hit-test 认。{0}",
                Facts(root, bottom, top));
            Assert.AreEqual(0, bottomClick, "重叠区里先加入的那一枚吃到了点击：面板内的层序没被 hit-test 认。{0}",
                Facts(root, bottom, top));
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>开一扇代码建树、壳已点亮的 UI Toolkit 窗，并把它的面板本体登记进出门清理表。</summary>
        private ProbeLiveUITKWindow OpenUITKWindow(string windowName)
        {
            UIService.ShowUI<ProbeLiveUITKWindow>(windowName);
            var window = UIService.GetWindow<ProbeLiveUITKWindow>(windowName);
            Assert.IsNotNull(window, "开栈失败：UI Toolkit 那一轨的窗没落到协调者那一份栈上");
            Assert.IsNotNull(window.RootVisual, "开栈失败：窗口的内容根没建出来");
            Assert.IsTrue(window.Visible, "开栈失败：窗口的可见意图没结算");
            Assert.AreEqual(TIPS_LAYER_BASE, window.Document.sortingOrder, "开栈失败：序位没落到文档组件上");
            _trackedPanels.Add(window.gameObject);
            return window;
        }

        /// <summary>把一盒子按屏幕坐标（左上原点）摆成绝对定位的正方形。</summary>
        /// <param name="element">要摆的盒子。</param>
        /// <param name="topLeft">盒子左上角在屏幕上的位置（像素，左上原点）。</param>
        /// <param name="size">盒子边长（像素）。</param>
        private static void StyleScreenBox(VisualElement element, Vector2 topLeft, float size)
        {
            var style = element.style;
            style.position = Position.Absolute;
            style.left = topLeft.x;
            style.top = topLeft.y;
            style.width = size;
            style.height = size;
        }

        /// <summary>两块矩形的交集；不相交时宽高为负，由调用方判红。</summary>
        private static Rect Intersection(Rect a, Rect b)
        {
            var xMin = Mathf.Max(a.xMin, b.xMin);
            var yMin = Mathf.Max(a.yMin, b.yMin);
            var xMax = Mathf.Min(a.xMax, b.xMax);
            var yMax = Mathf.Min(a.yMax, b.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>现场读数：屏幕尺寸、面板依附位与两枚矩形的绝对位置——「能否机器证拾取」的唯一凭据。</summary>
        private string Facts(VisualElement root, VisualElement bottom, VisualElement top)
        {
            return $"[screen={Screen.width}x{Screen.height}, mouse={(_mouse == null ? "<null>" : _mouse.position.ReadValue().ToString())}, " +
                   $"root.panel={(root.panel == null ? "null" : "set")}, root.worldBound={root.worldBound}, " +
                   $"root.resolved={root.resolvedStyle.width}x{root.resolvedStyle.height}, " +
                   $"bottom.worldBound={bottom.worldBound}, top.worldBound={top.worldBound}]";
        }

        /// <summary>
        /// 把面板坐标（左上原点）投成一次真点击：位置→按下→抬起各占一帧，最后一帧交回事件结算。
        /// </summary>
        /// <remarks>
        /// <see cref="InputTestFixture"/> 在 <c>[UnityTest]</c> 里只排队状态事件，落账由玩家循环推进 ⇒ 每一步都要跨帧。
        /// </remarks>
        private IEnumerator ClickPanelPoint(Vector2 panelPoint)
        {
            var mouseY = PanelYOriginIsTopLeft ? Screen.height - panelPoint.y : panelPoint.y;
            Set(_mouse.position, new Vector2(panelPoint.x, mouseY));
            yield return null;

            Press(_mouse.leftButton);
            yield return null;

            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        /// <summary>带超时上界的帧推进：条件成立即出门，超过上界把现场交回用例判红，不写无限等。</summary>
        /// <param name="condition">出门条件。</param>
        /// <param name="timeoutSeconds">等待上界（秒，真实时间）。</param>
        private static IEnumerator PumpUntil(Func<bool> condition, float timeoutSeconds)
        {
            var deadline = Time.unscaledTime + timeoutSeconds;
            while (!condition() && Time.unscaledTime < deadline)
            {
                yield return null;
            }
        }

        /// <summary>
        /// 点亮过的 UI Toolkit 文档壳：与 <see cref="UITKWindow"/> 装载路径同一条配置判据（先配 <see cref="PanelSettings"/>
        /// 再激活），差别只在本文件的判据要真命中屏幕矩形，因此壳必须停在<b>激活</b>态——面板与根元素都在启用流程里才建得出来。
        /// </summary>
        private static GameObject NewLiveDocumentShell()
        {
            var shell = new GameObject("PickingShell", typeof(RectTransform));
            shell.SetActive(false);
            shell.transform.SetParent(UIService.UIRoot, false);

            var document = shell.AddComponent<UIDocument>();
            document.panelSettings = UITKWindow.SharedPanelSettings;

            shell.SetActive(true);
            return shell;
        }

        /// <summary>UI Toolkit 轨探针窗（<see cref="EUILayer.Tips"/>）：代码建树、壳点亮，面板真有屏幕矩形可命中。</summary>
        [Window(EUILayer.Tips, false)]
        internal sealed class ProbeLiveUITKWindow : UITKWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewLiveDocumentShell(), null);

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));
        }

        #endregion
    }
}
