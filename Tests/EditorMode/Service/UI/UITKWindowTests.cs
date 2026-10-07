using System.Collections.Generic;
using Moirai.Atropos.Tests.EditorMode;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Service.UI
{
    /// <summary>
    /// UI Toolkit 轨窗口（<see cref="UITKWindow"/>）的面板访问面测试。
    /// </summary>
    /// <remarks>
    /// 断言只落在窗口自己 new 出来的内容根与用例自备的 <see cref="UIDocument"/> 上（纯托管值：<c>style.display</c> 的 <c>value</c> 与 <c>keyword</c> / <c>pickingMode</c> / <c>padding</c>）， <br />
    /// <b>不回读</b> <c>document.rootVisualElement</c>：EditMode 下 <c>AddComponent</c> 不保证跑 <c>Awake</c>、面板未必 attach，读它是拿量具当被测。 <br />
    /// <see cref="UITKWindow.DestroyPanel"/> 不在本文件测：它按 uGUI 轨同语义走 <c>Object.Destroy</c>（生产正确），而 <c>Destroy</c> 在 EditMode 会打错误日志、把用例打成假红。 <br />
    /// 夹具不依赖场景与预制体：装配判据用的面板由用例自备的空 <see cref="GameObject"/> 经 <c>BindPanel</c> 接缝注入， <br />
    /// <c>PanelSettings</c> 取包内 <c>Runtime/Services/Debugger/Resources/DebuggerPanelSettings</c> 作正对照——取不到是量具前提坏了，不是被测代码坏。 <br />
    /// 装载路径的三格成功用例（同步 / 异步 / 内置资源）不吃接缝、走真实现，因此只读引用包内<b>实存</b>资产（一枚 <c>.uxml</c>、一枚 <c>.uss</c>）： <br />
    /// 本文件不新增测试资产，测试桩一律代码注入。异步那格今天在 EditMode 就判得了：编辑器域里资源服务未初始化， <br />
    /// <c>ResourceService.TryLoadAssetAsync</c> 走 <c>AssetDatabase</c> 那一支不经过任何 await 站点，<c>InternalLoad</c> 的续体当场跑完（不是「推测要 PlayMode」）。 <br />
    /// 真装载建出的壳交 <c>TearDown</c> 清理，且登记排在断言之前：<c>InternalLoad</c> 是 <c>async UniTaskVoid</c>，中途抛错时壳已经挂到场景根，漏登记就是串味。 <br />
    /// 序空间那一组格钉「深度意图经 <c>ApplyDepth</c> 落进文档组件的 <c>float</c> 序、回读取的是文档事实」，含负序、超出 <c>float</c> 逐整数可表示范围、 <br />
    /// 面板从未装载与被拒开四种口径；判据一律打在窗基类自己的钩子与 <see cref="UITKWindow.Document"/> 取口上。 <br />
    /// 壳的父级按 <c>parent == UIService.UIRoot</c> 断：不把「EditMode 没有 UIRoot」这一环境事实钉成 <c>null</c>，日后同域真 boot 了也不会假红。
    /// </remarks>
    [TestFixture]
    public sealed class UITKWindowTests
    {
        /// <summary>真模板资产的定位地址：包内实存的调试器事件面板模板（只读消费，成功路径的装载判据）。</summary>
        private const string TemplateAssetPath = "Packages/com.moirai.framework/Editor/Foundation/Events/Resources/EventsDebugger.uxml";

        /// <summary>真主题资产的定位地址：包内实存的调试器样式表（<c>ApplyTheme</c> 成功分支的挂载判据）。</summary>
        private const string ThemeAssetPath = "Packages/com.moirai.framework/Runtime/Services/Debugger/Resources/Debugger UI.uss";

        /// <summary>同一枚样式表的 Resources 地址：拿它与 <paramref name="ThemeAssetPath"/> 取到的对象比同一性，钉住「挂的就是取到的那一份」。</summary>
        private const string ThemeResourceName = "Debugger UI";

        /// <summary>那枚真模板的内置资源地址（它在 <c>Editor/Foundation/Events/Resources/</c> 下）：先证 <c>Resources.Load</c> 取不取得到，再决定 <c>fromResources</c> 那一支覆不覆。</summary>
        private const string TemplateResourceName = "EventsDebugger";

        private GameObject _host;
        private PanelSettings _panelSettings;
        private PanelSettings _originalSharedPanelSettings;
        private readonly List<GameObject> _shells = new List<GameObject>();

        /// <summary>
        /// 壳物体统一挂在一只场景空父下便于清理；<c>SharedPanelSettings</c> 是程序集级静态，先存原值再交给用例改写。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _host = new GameObject(nameof(UITKWindowTests) + "Host");
            _shells.Clear();

            _originalSharedPanelSettings = UITKWindow.SharedPanelSettings;
            _panelSettings = Resources.Load<PanelSettings>("DebuggerPanelSettings");
            Assert.IsNotNull(_panelSettings, "量具前提坏了：取不到 DebuggerPanelSettings 夹具资产，本文件的装配判据无从判起");
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _shells.Count; i++)
            {
                if (_shells[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_shells[i]);
                }
            }

            _shells.Clear();

            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }

            UITKWindow.SharedPanelSettings = _originalSharedPanelSettings;
        }

        #region 未绑定面板的对象模型口径 [UNBOUND PANEL]

        /// <summary>面板未绑定：<c>gameObject</c> 回 null（实现 <c>=&gt; _shell</c> 的同形结果，不伪造空物体）。</summary>
        [Test]
        public void GameObject_Unbound_ReturnsNull()
        {
            var window = new ProbeWindow();

            Assert.IsNull(window.gameObject, "面板未绑定时 gameObject 必须是 null，不得伪造空物体");
        }

        /// <summary>面板未绑定：<c>transform</c> 抛 NRE（实现直读 <c>_shell</c>，不得补 <c>?.</c>）。</summary>
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

        #endregion

        #region 装载判据 [LOAD]

        /// <summary>
        /// 两枚配置都缺位（窗口级覆盖没给、共享那一份也被清空）：当场拒开——只报一条 Error、回 false，且不造半个面板。
        /// </summary>
        /// <remarks>
        /// 拒开必须发生在壳物体与 <c>UIDocument</c> 之前（窗口仍停在未绑定态），三份意图与 <c>ApplySafeInsets</c> 的写入照旧落空而不抛—— <br />
        /// 与 <c>UGUIWindow</c> 的 <c>if (_canvas != null)</c> 同一口径。不拿 <c>CreateInstance&lt;PanelSettings&gt;()</c> 兜底： <br />
        /// 缺配置的窗口拿到一份没有主题、没有缩放模式的裸面板，比拒开更难查。 <br />
        /// 「只报一条」由 <c>UtfLogExpect.Error()</c> 钉：多报一条当场红在「意外日志」上，配置缺位与模板缺位两条病因因此不会混成一段。
        /// </remarks>
        [Test]
        public void LoadPanel_MissingSharedPanelSettings_RefusesOpenAndKeepsWindowUnbound()
        {
            UITKWindow.SharedPanelSettings = null;
            var window = new ProbeWindow();
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);

            UtfLogExpect.Error();
            window.InternalLoad("Panel", null, false, null);

            Assert.IsFalse(window.IsLoadDone, "缺 PanelSettings 不得进准备态");
            Assert.IsNull(window.gameObject, "拒开时不得留下壳物体");
            Assert.IsNull(window.Document, "拒开时不得留下文档组件引用");
            Assert.IsNull(window.RootVisual, "拒开时不得留下内容根");
            Assert.DoesNotThrow(() =>
            {
                window.Visible = true;
                window.Depth = 1200;
                window.Interactable = false;
                window.ApplySafeInsets(1f, 2f, 3f, 4f);
            }, "未绑定窗口写三份意图与安全区必须安全落空");
        }

        /// <summary>
        /// 配置齐了但取不到面板模板：同样当场拒开——只报一条 Error、不进准备态、一个壳都不建。
        /// </summary>
        /// <remarks>
        /// 这一格走真 <c>LoadPanel</c>（探针窗没有自备壳时不覆写任何东西）：<c>EnsurePanelSettings</c> 已过、栽在取模板那一步， <br />
        /// 与「配置缺位」是两种病因，判据要各自钉住；拒开发生在 <c>NewDocumentShell()</c> 之前，所以现场不得留下壳物体与文档组件。
        /// </remarks>
        [Test]
        public void LoadPanel_MissingTemplateAsset_RefusesOpenAndKeepsWindowUnbound()
        {
            UITKWindow.SharedPanelSettings = _panelSettings;
            const string missingPath = "Assets/Nope/Missing Panel.uxml";
            var window = new ProbeWindow();
            window.Init(nameof(ProbeWindow), 1, false, missingPath, false, 10, false);

            UtfLogExpect.Error();
            window.InternalLoad(missingPath, null, false, null);

            Assert.IsFalse(window.IsLoadDone, "取不到模板不得进准备态");
            Assert.IsNull(window.gameObject, "取不到模板时不得建出壳物体");
            Assert.IsNull(window.Document, "取不到模板时不得留下文档组件引用");
            Assert.IsNull(window.RootVisual, "取不到模板时不得留下内容根");
        }

        /// <summary>
        /// 真模板资产的成功路径：装载建壳、把 <c>SharedPanelSettings</c> 写进文档组件、克隆模板，然后交给自己装配并进准备态。
        /// </summary>
        /// <remarks>
        /// 这一格是 <c>LoadPanel</c> 成功侧（含 <c>NewDocumentShell</c> 里的 <c>panelSettings</c> 写入点）的覆盖： <br />
        /// 只读消费包内实存的 <c>EventsDebugger.uxml</c>，不新增测试资产；地址交给 <c>TryLoadAsset</c>，编辑器域里由 <c>AssetDatabase</c> 按路径当场给出。 <br />
        /// 壳的登记排在断言之前（<c>RegisterShell</c>）：装载中途抛错时壳已挂在场景根上，先断言再登记等于把漏网的面板留给下一轮用例。
        /// </remarks>
        [Test]
        public void LoadPanel_RealTemplateAsset_BuildsShellAndCompletesLoad()
        {
            UITKWindow.SharedPanelSettings = _panelSettings;
            var window = new ProbeWindow();
            window.Init(nameof(ProbeWindow), 1, false, TemplateAssetPath, false, 10, false);

            GameObject shell;
            try
            {
                window.InternalLoad(TemplateAssetPath, null, false, null);
            }
            finally
            {
                shell = RegisterShell(window);
            }

            AssertRealTemplateLoad(window, shell);
        }

        /// <summary>
        /// 异步装载的成功路径：今天在 EditMode 就判得了——编辑器域里资源服务未初始化，异步取值不经过 await 站点，续体就地跑完。
        /// </summary>
        /// <remarks>
        /// <c>ResourceService.TryLoadAssetAsync</c> 的 <c>UNITY_EDITOR</c> 分支在 <c>IsInitialized</c> 为假时直读 <c>AssetDatabase</c> 后 <c>return</c>， <br />
        /// 那条路上一个 await 都没有 ⇒ <c>LoadPanelAsync</c> 交回已完成的任务，<c>UIWindow.InternalLoad</c> 的续体当场把 <c>PanelLoaded</c> 跑完。 <br />
        /// 判据与同步那格同套：<c>IsLoadDone</c>、壳已建出、<c>Document.panelSettings == SharedPanelSettings</c>、激活位、壳进清理表。
        /// </remarks>
        [Test]
        public void LoadPanelAsync_RealTemplateAsset_CompletesInPlaceAndBuildsShell()
        {
            UITKWindow.SharedPanelSettings = _panelSettings;
            var window = new ProbeWindow();
            window.Init(nameof(ProbeWindow), 1, false, TemplateAssetPath, false, 10, false);

            GameObject shell;
            try
            {
                window.InternalLoad(TemplateAssetPath, null, true, null);
            }
            finally
            {
                shell = RegisterShell(window);
            }

            AssertRealTemplateLoad(window, shell);
        }

        /// <summary>
        /// <c>fromResources = true</c> 那一支（<c>Resources.Load&lt;VisualTreeAsset&gt;</c>）的覆盖：模板住在 <c>Editor</c> 装配的 Resources 下，取到了才谈得上覆盖。
        /// </summary>
        /// <remarks>
        /// 第一句是前提断言，不是被测判据：它把「编辑器域里这枚 Editor 侧 Resources 资产取不取到」钉成实测事实， <br />
        /// 取不到时这一格红在前提上（文案自带资源名），而不是让下面的装载判据去猜。 <br />
        /// 走 <c>LoadPanel</c> 的 <c>fromResources</c> 分支 ⇒ 地址按 Resources 名给，不经 <c>AssetDatabase</c> 路径。
        /// </remarks>
        [Test]
        public void LoadPanel_FromResourcesTemplateAsset_BuildsShellAndCompletesLoad()
        {
            Assert.IsNotNull(Resources.Load<VisualTreeAsset>(TemplateResourceName),
                "量具前提坏了：Resources.Load 取不到 {0}，fromResources 那一支无从覆盖", TemplateResourceName);

            UITKWindow.SharedPanelSettings = _panelSettings;
            var window = new ProbeWindow();
            window.Init(nameof(ProbeWindow), 1, false, TemplateResourceName, true, 10, false);

            GameObject shell;
            try
            {
                window.InternalLoad(TemplateResourceName, null, false, null);
            }
            finally
            {
                shell = RegisterShell(window);
            }

            AssertRealTemplateLoad(window, shell);
        }

        #endregion

        #region 装配接缝 [BIND]

        /// <summary>
        /// 装配接缝的两条失败判据：壳物体为 null 判装载失败；壳上没有 <c>UIDocument</c> 按 uGUI 轨缺 Canvas 的同形文案抛错。
        /// </summary>
        /// <remarks>
        /// 抛错前不得改动任何字段——调用方拿不到半个可用面板（比 <c>UGUIWindow.BindPanel</c> 更严：那里先写 <c>_panel</c> 再校验 <c>Canvas</c>）。
        /// </remarks>
        [Test]
        public void BindPanel_NullShellOrMissingDocument_ReportsFailure()
        {
            var window = new ProbeWindow();
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);

            Assert.IsFalse(window.BindPanel(null, null), "null 壳必须判失败");
            Assert.IsNull(window.gameObject, "失败后仍应保持未绑定");
            Assert.IsNull(window.RootVisual, "失败后不得有内容根");

            var bareShell = NewShell("BareShell", false);
            var error = Assert.Throws<System.Exception>(() => window.BindPanel(bareShell, null));

            StringAssert.Contains("Not found UIDocument in panel ProbeWindow", error.Message,
                "缺 UIDocument 的文案要带上窗口名，沿用 uGUI 轨缺 Canvas 的写法");
            Assert.IsNull(window.gameObject, "抛错后不得把半个面板占住引用");
        }

        /// <summary>装配产物：壳物体与文档交回窗口，内容根按窗口类型名建好，后端配好的 PanelSettings 不被改写。</summary>
        [Test]
        public void BindPanel_PrepsShellAndExposesContentRoot()
        {
            var shell = NewShell("Panel");
            var document = shell.GetComponent<UIDocument>();
            var window = new ProbeWindow();
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);

            Assert.IsTrue(window.BindPanel(shell, null), "带 UIDocument 的壳应装配成功");

            Assert.AreSame(shell, window.gameObject, "gameObject 应交回装配好的壳");
            Assert.AreSame(document, window.Document, "Document 应指向壳上那枚文档组件");
            Assert.AreSame(_panelSettings, document.panelSettings, "装配不得改写后端配好的 PanelSettings");
            Assert.AreEqual(nameof(ProbeWindow), shell.name, "壳要改写成窗口类型名（uGUI 轨的命名口径）");
            Assert.AreEqual(Vector3.zero, shell.transform.localPosition, "壳的本地坐标要归零");
            Assert.IsNotNull(window.RootVisual, "内容根必须由窗口自己建出来");
            Assert.AreEqual(nameof(ProbeWindow), window.RootVisual.name, "内容根同样按窗口类型名命名");
            // 夹具那句「未激活的壳压住 attach」在这里落成判据：拿不到文档根元素时装配留着内容根不挂、也不报错
            Assert.IsNull(window.RootVisual.parent, "壳未激活 ⇒ 文档根元素拿不到，内容根应停在未 attach 态而不是抛错");
        }

        #endregion

        #region 三份意图的落地 [APPLY]

        /// <summary>显隐、深度、交互三份意图各自落点：显隐与交互进内容根，深度进文档组件的排序值。</summary>
        [Test]
        public void ApplyIntentions_LandOnContentRootAndDocument()
        {
            var shell = NewShell("Panel");
            // 夹具把文档排序预置成非零：初始深度意图（0）必须真的覆盖它，否则这句断言在「谁都没写」的槽上也能绿
            shell.GetComponent<UIDocument>().sortingOrder = 77f;
            var window = Loaded(shell);

            // 装载当场已按隐藏意图把内容根写成 None、交互意图写成 Ignore、深度意图写成 0（未创建态的初值）
            Assert.AreEqual(DisplayStyle.None, window.RootVisual.style.display.value, "隐藏意图落 display");
            Assert.AreNotEqual(StyleKeyword.Null, window.RootVisual.style.display.keyword,
                "隐藏意图必须真落过笔：keyword 离开 Null 才是落笔凭据，value 同值不能算数");
            Assert.AreEqual(PickingMode.Ignore, window.RootVisual.pickingMode, "不可交互意图落 pickingMode");
            Assert.AreEqual(0f, window.Document.sortingOrder, "初始深度意图要把夹具预置的 77 覆盖成 0");

            window.Visible = true;
            window.Depth = 1200;
            window.Interactable = true;

            Assert.AreEqual(DisplayStyle.Flex, window.RootVisual.style.display.value, "显示意图落 display");
            Assert.AreEqual(1200f, window.Document.sortingOrder, "深度意图落文档排序");
            Assert.AreEqual(PickingMode.Position, window.RootVisual.pickingMode, "放开交互落 pickingMode");

            window.Visible = false;
            window.Depth = 1100;
            window.Interactable = false;

            Assert.AreEqual(DisplayStyle.None, window.RootVisual.style.display.value, "再次隐藏");
            Assert.AreEqual(1100f, window.Document.sortingOrder, "再次改深度：UI Toolkit 没有子画布偏移，直接取绝对值");
            Assert.AreEqual(PickingMode.Ignore, window.RootVisual.pickingMode, "再次屏蔽交互");
        }

        /// <summary>
        /// 装载当场结算显示意图：内容根是刚 new 出来的，<c>display</c> 从没写过 ⇒ 这一笔必须真落下，不能被槽里的初值冒充成「已到位」。
        /// </summary>
        /// <remarks>
        /// 量具前提（第一句）：<c>DisplayStyle.Flex</c> 才是枚举的 0 值，没碰过 <c>display</c> 的元素读回来就是 <c>Flex</c>， <br />
        /// 于是「意图 = 显示」正好撞上 <c>value</c> 同值早退，一次都不写；<c>keyword</c> 离开 <c>StyleKeyword.Null</c> 是这笔落过的唯一凭据。 <br />
        /// 意图 = 隐藏那一侧初值冒充不了（<c>None != Flex</c>），所以判据要挑显示方向才抓得住；两条方向都由 <c>ApplyIntentions_…</c> 续着改。
        /// </remarks>
        [Test]
        public void ApplyVisible_FirstIntentOnFreshContentRoot_WritesIntoStyleSlot()
        {
            var untouched = new VisualElement();
            Assert.AreEqual(StyleKeyword.Null, untouched.style.display.keyword,
                "量具前提坏了：从没写过 display 的元素，槽里 keyword 应当还是 StyleKeyword.Null");

            var shell = NewShell("Panel");
            var window = new ProbeWindow { Fixture = shell };
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);
            // 装载前把「显示」意图压进对象模型：PanelLoaded 结算它时内容根才刚被 new 出来，槽里只有初值
            window.Visible = true;
            window.InternalLoad("Panel", null, false, null);

            Assert.AreEqual(DisplayStyle.Flex, window.RootVisual.style.display.value, "显示意图落 display");
            Assert.AreNotEqual(StyleKeyword.Null, window.RootVisual.style.display.keyword,
                "显示意图必须真写进槽：没写过的槽读回来同样是 Flex，只有 keyword 离开 Null 才证明落过笔");
        }

        /// <summary>
        /// 现行行为：屏蔽交互只掐内容根自己，子元素的 <c>pickingMode</c> 一个都不动（登记现状，不是背书）。
        /// </summary>
        /// <remarks>
        /// <c>pickingMode</c> 是逐元素属性，父元素置 <c>Ignore</c> 屏蔽不了子树 ⇒ <c>UIWindow.LockInteraction()</c> 在这条轨道上只让内容根退出命中树， <br />
        /// 克隆进来的与代码追加的子元素照样可拾取。要连子树一起掐得逐元素铺 <c>pickingMode</c>，而解锁时分不出「本来就 <c>Ignore</c> 的装饰元素」与被锁元素， <br />
        /// 一刀切回 <c>Position</c> 会打穿按元素的意图；uGUI 轨没这个两难（它切 <c>GraphicRaycaster.enabled</c>，开关天然覆盖子树）。 <br />
        /// 摆夹具的时机是要害：装载当场 <c>PanelLoaded</c> 已按 <c>_interactable == false</c> 结算过一次（意图位停在 false），那时子元素还不存在， <br />
        /// 之后直接写 <c>false</c> 会被 <see cref="UIWindow.Interactable"/> 的意图同值早退吃掉、钩子一次都不进。故本格先扳到 <c>true</c>（第一次真转移）， <br />
        /// 再挂子元素，再写回 <c>false</c>（第二次真转移）⇒ 钩子必然带着这枚后挂的子元素被调用一次。 <br />
        /// 变异自证：把 <c>ApplyInteractable</c> 改成连子元素一起铺 <c>pickingMode</c>，最后那句断言当场红（红过才许改成「子元素也被屏蔽」）。
        /// </remarks>
        [Test]
        public void CurrentBehaviour_InteractableLock_LeavesChildPickingModeUntouched()
        {
            var shell = NewShell("Panel");
            var window = Loaded(shell);

            // 先把意图扳到反方向：装载那次结算看不见下面这枚子元素，同值再写又被早退吃掉，只有真转移才喂得到钩子
            window.Interactable = true;
            Assert.AreEqual(PickingMode.Position, window.RootVisual.pickingMode,
                "量具前提坏了：反向那次真转移应让钩子把内容根放回命中树（红在这里说明整格没进 ApplyInteractable）");

            var child = new VisualElement();
            window.RootVisual.Add(child);

            window.Interactable = false;

            Assert.AreEqual(PickingMode.Ignore, window.RootVisual.pickingMode, "内容根自己要先退出命中树");
            Assert.AreEqual(PickingMode.Position, child.pickingMode,
                "现状：子元素的 pickingMode 不被改动（逐元素属性，父 Ignore 屏蔽不了子树）");
        }

        /// <summary>安全区 inset 落进内容根的四条 padding；内容根的既有样式不被其他钩子顺手动。</summary>
        [Test]
        public void ApplySafeInsets_WritesFourPaddings()
        {
            var shell = NewShell("Panel");
            var window = Loaded(shell);

            window.ApplySafeInsets(12f, 30f, 16f, 24f);

            // padding 的取值同名不同形：Unity 6 回 Length（渲染成 "12px"）、2022.3 回裸 float，直接比会把两边写成两种断言
            Assert.AreEqual(12f, PaddingPixels(window.RootVisual.style.paddingLeft.value), "左侧安全区进 paddingLeft");
            Assert.AreEqual(30f, PaddingPixels(window.RootVisual.style.paddingTop.value), "顶部安全区进 paddingTop");
            Assert.AreEqual(16f, PaddingPixels(window.RootVisual.style.paddingRight.value), "右侧安全区进 paddingRight");
            Assert.AreEqual(24f, PaddingPixels(window.RootVisual.style.paddingBottom.value), "底部安全区进 paddingBottom");
        }

        #endregion

        #region 序空间：意图位与文档事实 [ORDER SPACE]

        /// <summary>
        /// 绑定好的窗口：深度意图经 <c>ApplyDepth</c> 落进文档组件的排序值，而 <see cref="UITKWindow.Document"/> 交回的就是被写那一枚。
        /// </summary>
        /// <remarks>
        /// UI Toolkit 没有子画布偏移那一层，落地就是绝对值；判据打在窗基类自己的钩子与它的文档取口上。 <br />
        /// 夹具的 <c>sortingOrder</c> 初值不动 ⇒ 这一笔只有 <c>ApplyDepth</c> 写得出来。
        /// </remarks>
        [Test]
        public void DepthIntent_BoundWindow_LandsOnDocumentSortingOrder()
        {
            var shell = NewShell("Panel");
            var window = Loaded(shell);

            window.Depth = 1200;

            Assert.AreEqual(1200, window.Depth, "意图位跟着这次写入");
            Assert.AreEqual(1200f, window.Document.sortingOrder, "深度意图落进文档组件的序空间");
            Assert.AreSame(shell.GetComponent<UIDocument>(), window.Document,
                "文档取口交回的须是被写入的那一枚：换个口径读就不是面板事实");
        }

        /// <summary>
        /// 回读口径：面板被从背后挪过时，窗口问得出文档上的实际序，而 <c>Depth</c> 仍是自己那份没跟上的意图。
        /// </summary>
        /// <remarks>
        /// 文档组件的序是 <c>float</c>、意图位是 <c>int</c>：夹具写进 <c>1200.7f</c> 这笔小数正是两个口径同形不同物的凭据。 <br />
        /// 共存会话仲裁谁在上面时读的是这一笔面板事实，拿意图位代答会读出一份从没落到面板上的排序。
        /// </remarks>
        [Test]
        public void Document_PanelMovedBehindWindowBack_ReportsFloatSortingOrderWhileIntentStaysStale()
        {
            var shell = NewShell("Panel");
            var window = Loaded(shell);

            window.Document.sortingOrder = 1200.7f;

            Assert.AreEqual(0, window.Depth, "量具前提坏了：夹具没动意图位，窗口侧仍是装载时结算的初值");
            Assert.AreEqual(1200.7f, window.Document.sortingOrder, "文档上的 float 序原样读回：面板事实不由 int 意图位代答");
        }

        /// <summary>负序是 UI Toolkit 序空间里的合法取值：经 <c>ApplyDepth</c> 写进去、读回来仍带符号，不夹到 0。</summary>
        [Test]
        public void DepthIntent_NegativeOrder_WritesThroughWithoutClamping()
        {
            var shell = NewShell("Panel");
            var window = Loaded(shell);

            window.Depth = -1200;

            Assert.AreEqual(-1200f, window.Document.sortingOrder, "文档组件不夹下界：负序原样写进去");
        }

        /// <summary>
        /// 现行行为：意图超出 <c>float</c> 尾数逐整数可表示的范围后，文档上落的是最近的可表示值，回读给出那一个而不是意图位。
        /// </summary>
        /// <remarks>
        /// <c>2^24 + 1</c> 是这条分叉上最小的一笔：<c>float</c> 在此之上步长变 2，写进 <c>UIDocument.sortingOrder</c> 归到 <c>2^24</c>。 <br />
        /// 本格<b>登记</b>这个分叉并钉住「回读给面板事实」的口径：要改判得动 <c>UIWindow.Depth</c> 的类型或序空间约定，不是动这里的断言。 <br />
        /// uGUI 侧的对格在 <c>UGUIWindowTests</c>：画布同样容不下这一笔，但它绕回低位而不是就近归整——两支折法不同、都跟意图位分叉。
        /// </remarks>
        [Test]
        public void CurrentBehaviour_DepthAboveFloatExactIntegerRange_ReadsBackPanelFactNotIntent()
        {
            var shell = NewShell("Panel");
            var window = Loaded(shell);

            window.Depth = 16777217;

            Assert.AreEqual(16777217, window.Depth, "意图位是 int，原样收着这一笔");
            Assert.AreEqual(16777216f, window.Document.sortingOrder, "文档上的 float 落回最近的可表示值：与意图位分叉，且这条分叉量得出来");
        }

        /// <summary>面板从未装载过：写深度只持意图、不抛，文档取口回 null（装载当场由窗口自己结算到面板）。</summary>
        [Test]
        public void DepthWrite_WhilePanelUnbound_HoldsIntentAndKeepsDocumentAbsent()
        {
            var window = new ProbeWindow();
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);

            Assert.DoesNotThrow(() => window.Depth = 900, "面板未绑定时写深度不得触碰不存在的文档组件");
            Assert.AreEqual(900, window.Depth, "写入攒在意图位上，面板不在场时它是唯一可信的深度");
            Assert.IsNull(window.Document, "未装载即没有文档事实可读：取口不得伪造一枚");
        }

        /// <summary>
        /// 拒开过一次（<c>SharedPanelSettings</c> 缺位）：面板同样不在场，深度写入的答复与「从未装载」那一档同形。
        /// </summary>
        /// <remarks>
        /// 这一格走真拒开路径：配置清空后过一遍 <c>InternalLoad</c>，窗口只报<b>一条</b> Error 并回 false， <br />
        /// 壳与内容根都不存在（<c>Document</c> 为 null 是本格的前提而不是假设），此后深度读写只能停在意图位上。 <br />
        /// 与上一格分开各钉一种成因：那一格的面板从没出现过，这一格是被响亮地拒过一次——两种「不在场」都得答同一句。
        /// </remarks>
        [Test]
        public void DepthWrite_AfterRefusedOpen_HoldsIntentWithoutThrowing()
        {
            UITKWindow.SharedPanelSettings = null;
            var window = new ProbeWindow();
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);

            UtfLogExpect.Error();
            window.InternalLoad("Panel", null, false, null);

            Assert.IsNull(window.Document, "量具前提坏了：拒开不得留下文档组件，面板须在「不在场」这一侧");
            Assert.DoesNotThrow(() => window.Depth = 900, "拒开后的窗口写深度必须不抛");
            Assert.AreEqual(900, window.Depth, "面板不在场时意图位是唯一可信的深度");
        }

        #endregion

        #region 主题挂点 [THEME]

        /// <summary>
        /// <c>ThemeLocation</c> 声明了却取不到样式表：只报一条 Error，面板照旧可用。
        /// </summary>
        /// <remarks>
        /// 主题缺失只降级样式、不判装载失败——一个窗口的排版坏不该让整窗打不开；样式表进的是内容根自己的 <c>styleSheets</c>， <br />
        /// 不动 <c>PanelSettings.themeStyleSheet</c>（那是共享资产，一枚窗口改它会影响全部 UITK 窗口）。
        /// </remarks>
        [Test]
        public void BindPanel_MissingThemeAsset_LogsOnceAndKeepsPanelUsable()
        {
            var shell = NewShell("Panel");
            var window = new ProbeWindow { Theme = "Assets/Nope/Missing Sheet.uss" };
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);

            UtfLogExpect.Error();

            Assert.IsTrue(window.BindPanel(shell, null), "缺主题不得判装配失败");
            Assert.AreSame(shell, window.gameObject, "主题缺失只降级样式，面板仍应保持装配好的状态");
            Assert.AreEqual(nameof(ProbeWindow), window.RootVisual.name, "内容根照旧建出来");
        }

        /// <summary>
        /// <c>ThemeLocation</c> 取到了样式表：挂点必须是<b>内容根自己</b>的 <c>styleSheets</c>。
        /// </summary>
        /// <remarks>
        /// 只读消费包内实存的 <c>Debugger UI.uss</c>（与失败侧那一格走同一条 <c>TryLoadAsset</c> 路）： <br />
        /// 判据拿 <c>Contains</c> 对着同一份资产比同一性 ⇒ 样式表若被误挂到 <c>document.rootVisualElement</c>、或改成去动共享的 <c>PanelSettings.themeStyleSheet</c>，这一格必红。
        /// </remarks>
        [Test]
        public void BindPanel_RealThemeAsset_AttachesStyleSheetToContentRoot()
        {
            var sheet = Resources.Load<StyleSheet>(ThemeResourceName);
            Assert.IsNotNull(sheet, "量具前提坏了：取不到 {0} 夹具样式表，挂点判据无从判起", ThemeResourceName);

            var shell = NewShell("Panel");
            var window = new ProbeWindow { Theme = ThemeAssetPath };
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);

            Assert.IsTrue(window.BindPanel(shell, null), "取到主题的装配照常成功");

            Assert.IsTrue(window.RootVisual.styleSheets.Contains(sheet),
                "主题样式表要挂进内容根自己的 styleSheets：挂到文档根元素或改共享 PanelSettings 都不算数");
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>
        /// 壳物体：<c>RectTransform</c> + <c>UIDocument</c> + 夹具 <c>PanelSettings</c>，建成即停在未激活态。
        /// </summary>
        /// <remarks>
        /// 未激活是本文件的量具前提：<c>UIDocument</c> 的根元素与面板由组件自身的启用流程建，EditMode 不保证跑， <br />
        /// 于是 <c>rootVisualElement</c> 回 null，正好把实现侧「attach 拿不到宿主就留着内容根不报错」那一格压住。
        /// </remarks>
        /// <param name="name">壳物体名。</param>
        /// <param name="withDocument">壳上是否挂文档组件（不挂用来测缺组件那一格）。</param>
        private GameObject NewShell(string name, bool withDocument = true)
        {
            var shell = new GameObject(name, typeof(RectTransform));
            shell.SetActive(false);
            shell.transform.SetParent(_host.transform, false);
            _shells.Add(shell);

            if (withDocument)
            {
                var document = shell.AddComponent<UIDocument>();
                document.panelSettings = _panelSettings;
            }

            return shell;
        }

        /// <summary>走一遍装载钩子把壳交给装配（停在未创建态）：三份意图由 <c>PanelLoaded</c> 当场结算。</summary>
        private ProbeWindow Loaded(GameObject fixture)
        {
            var window = new ProbeWindow { Fixture = fixture };
            window.Init(nameof(ProbeWindow), 1, false, "Panel", false, 10, false);
            window.InternalLoad("Panel", null, false, null);
            return window;
        }

        /// <summary>把窗口此刻的壳物体交 <c>TearDown</c> 清理并回给用例；未绑定（回 null）时登记一条空位，清理侧自会跳过。</summary>
        /// <remarks>
        /// 真装载的壳挂在场景根上而不是用例自己的 <c>_host</c> 下，只能靠这份登记回收： <br />
        /// 它必须排在成功判据的断言之前，装载中途抛错或后面的断言红掉都不该把一枚面板留在现场串味下一轮。
        /// </remarks>
        private GameObject RegisterShell(UITKWindow window)
        {
            var shell = window.gameObject;
            _shells.Add(shell);
            return shell;
        }

        /// <summary>真模板装载的共用判据：三格（同步 / 异步 / 内置资源）跑的是同一个成功形状，判据也同套。</summary>
        private void AssertRealTemplateLoad(UITKWindow window, GameObject shell)
        {
            Assert.IsNotNull(shell, "真模板资产必须走完整装载路径建出壳物体");
            Assert.IsTrue(window.IsLoadDone, "装载成功要置准备位");
            Assert.AreEqual(UIService.UIRoot, shell.transform.parent,
                "壳的父级按 UIService.UIRoot 断：EditMode 下它是 null，壳停在场景根；同域真 boot 后壳应跟到 UIRoot 下");
            Assert.AreEqual(nameof(ProbeWindow), shell.name, "壳要改写成窗口类型名（uGUI 轨的命名口径）");
            Assert.AreEqual(Vector3.zero, shell.transform.localPosition, "壳的本地坐标要归零");
            Assert.IsTrue(shell.activeSelf, "壳要带着激活态交出：文档组件的根元素在启用流程里才建");
            Assert.AreSame(_panelSettings, window.Document.panelSettings, "文档组件的 PanelSettings 由装载路径写入，装配不得改写");
            Assert.IsNotNull(window.RootVisual, "内容根必须由窗口自己建出来");
            Assert.AreEqual(nameof(ProbeWindow), window.RootVisual.name, "内容根同样按窗口类型名命名");
            Assert.Greater(window.RootVisual.childCount, 0, "模板要真的克隆进内容根");
        }

        /// <summary>
        /// 把样式取值归一成 float 像素：Unity 6 的 <c>Length</c> 自带 <c>value</c>，2022.3 的取值本就是 <c>float</c>。
        /// </summary>
        /// <param name="raw">样式取值（装箱后交进来，两处静态类型不同形）。</param>
        private static float PaddingPixels(object raw)
        {
#if UNITY_6000_0_OR_NEWER
            return ((Length)raw).value;
#else
            return (float)raw;
#endif
        }

        /// <summary>把用例自备的壳物体当成加载结果交回的后端探针；没有自备壳时走真实现。</summary>
        private sealed class ProbeWindow : UITKWindow
        {
            internal GameObject Fixture;
            internal string Theme;

            // 自备壳在场时把装载判据整段绕开：本文件不依赖资源服务的资产地址，装载路径由 BindPanel 接缝代劳
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                Fixture == null ? base.LoadPanel(assetLocation, fromResources) : BindPanel(Fixture, null);

            protected override string ThemeLocation => Theme;
        }

        #endregion
    }
}
