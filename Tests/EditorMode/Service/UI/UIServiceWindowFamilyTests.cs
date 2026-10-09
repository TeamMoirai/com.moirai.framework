using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace Service.UI
{
    /// <summary>
    /// UI 门面开窗面用例：同名两腿各自的实参形状、错配当场抬错（认不出任何一轨、按名命中的是别的窗口类）与对象模型的回叫落点。
    /// </summary>
    /// <remarks>
    /// 两条腿共用同一个协调者的开栈编排，本文件因此只判这些：同一形状的实参是否各自落进自己那一腿（协调者处留下的形参形状差别）、<br />
    /// 窗口类不与任何一支内建轨配对时是否在进协调者之前抬错（栈上一只窗都不多，数的是共享栈的栈长）、开出来的窗口各自落回哪一轨的窗口基类、<br />
    /// 面板地址在<b>生产驱动者</b>那一侧按窗口类解析出来的结果（<c>[Window(location)]</c> 优先、缺省回类型名）、对象模型的回叫落在那一条共享栈上（含关停后的落空档）。<br />
    /// 两支后端各一枚驱动者，都由门面按启用清单认领进来：本夹具不进槽位——门面上没有换入接缝，<br />
    /// 进门只经 <see cref="UIService.Internal_ResetHandlerSlots"/> 把两支槽与那份共享持有者归回干净域状态，出门同样归位。<br />
    /// 栈上的取用一律叫那一份共享持有者 <see cref="UIService.SharedLedger"/>（栈本就只有一条，两支驱动者手里拿的是同一份），<br />
    /// 门面上的查询与关隐叫门面自己那一批入口——它们走的也是这一份。<br />
    /// 探针窗的 <c>LoadPanel</c> 钩子只记录入参并按「装载成功、面板留空」交回，因此不需要真资产、也不建 <c>UIDocument</c> 壳；<br />
    /// 对象模型的回叫落点与「生产入口之后两轨都开得起来」那一格都由 <see cref="UIService.OnInit"/> 那一句接上，因此走生产入口而不是另开接缝。<br />
    /// 异步那一格吃<b>真装载</b>（<c>DebuggerPanelSettings</c> + 包内实存的 <c>EventsDebugger.uxml</c>，与 <c>UITKWindowTests</c> 同套夹具），
    /// 判的是这一腿开出的窗落回哪一轨的窗口基类；面板就绪与壳/内容根那一半住在 <c>UITKWindowTests</c>，uGUI 那一枚探针窗自己造物体当面板。<br />
    /// UI Toolkit 腿的三枚新格同样吃真装载：窗口级 <c>PanelSettings</c> 落到文档组件且优先于共享那一份（不给时回兜底）、
    /// 腿传 <c>fromResources</c> 时各走 <c>TryReadTree</c> 自己那条支路（两个地址互为反证）、第四枚位置在两支里说的是两件不同的事。
    /// </remarks>
    [TestFixture]
    public sealed class UIServiceWindowFamilyTests
    {
        /// <summary>探针窗 <c>[Window(location)]</c> 上写死的面板地址：不需要资产实存，钩子只记录交来的串。</summary>
        private const string LOCATED_ADDRESS = "Some/Where/ProbePanel";

        /// <summary>同一枚模板的内置资源地址（它住在 <c>Editor/Foundation/Events/Resources/</c> 下）：AB 那一路拿它取不到东西，两档因此互为反证。</summary>
        private const string TEMPLATE_RESOURCE_NAME = "EventsDebugger";

        /// <summary>夹具用的共享面板配置（包内实存资产）：本文件只读消费，用例出门把静态位还原。</summary>
        private const string SHARED_SETTINGS_RESOURCE_NAME = "DebuggerPanelSettings";

        private PanelSettings _savedPanelSettings;

        private UIServiceHandler[] _savedEnabledHandlers;
        private readonly List<GameObject> _shells = new List<GameObject>();

        /// <summary>用例自己克隆出来的 <see cref="PanelSettings"/> 副本：与壳同批按 DestroyImmediate 收，不留到下一轮。</summary>
        private readonly List<PanelSettings> _clonedSettings = new List<PanelSettings>();

        /// <summary>进门归位：交出共享 <c>PanelSettings</c> 的原值，把两支处理器槽与那份共享持有者清回「干净域」那一份状态。</summary>
        /// <remarks>
        /// 归位只清不置：这一道不收实参，也没有把对象放进槽的路径——槽里此后只有门面按启用清单认领那一条来路。 <br />
        /// 窗口栈与停放表是两支后端共用的一份，进门先归零这一份，用例各自叫共享持有者或门面取数。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            _savedPanelSettings = UITKWindow.SharedPanelSettings;
            _savedEnabledHandlers = UIServiceSettings.EnabledHandlers;
            UIService.Internal_ResetHandlerSlots();
            // 后端由配置启用：进门装一份「两支都启用」的清单并走一遍生产初始化，用例拿到的因此都是真驱动者
            UIServiceSettings.Internal_SetEnabledHandlers(new UIServiceHandler[]
            {
                new UGUIHandler(),
                new UITKHandler(),
            });
            new UIService().OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            // 出门归位：两支槽与那份共享持有者一起清零，本夹具留在共享栈上的探针窗因此不归后跑的任何夹具接。
            // 这一道不跑关停：关停把本轨认得的窗交进共享栈的关闭流程，面板本体那一步走 Object.Destroy——编辑模式里它当场报错；
            // 面板本体由下面按 DestroyImmediate 收，与认领进来的驱动者无关。
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(_savedEnabledHandlers);
            UITKWindow.SharedPanelSettings = _savedPanelSettings;

            for (var i = 0; i < _shells.Count; i++)
            {
                if (_shells[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_shells[i]);
                }
            }

            _shells.Clear();

            // 副本排在壳之后销毁：文档组件还指着它，先销毁配置等于拆一枚还在用的面板
            for (var i = 0; i < _clonedSettings.Count; i++)
            {
                if (_clonedSettings[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_clonedSettings[i]);
                }
            }

            _clonedSettings.Clear();
        }

        #region 两腿分派 [TWO LEGS]

        /// <summary>
        /// 一枚实参的同形调用各自命中自己的腿：两支的约束各收在自己的窗口基类上，窗口类型就是选腿依据。
        /// </summary>
        /// <remarks>
        /// 寻址两档自 R10 起两支同形同序 ⇒ 「同形实参」这一次给满三枚位置实参，两支各编一次：绑得上就是二义没有回来（分辨依据仍是各自的窗口基类约束）。 <br />
        /// uGUI 窗与 UI Toolkit 窗互不派生 ⇒ 两支各叫各的实现，观察点是「本轨的实现把窗口送进共享栈」那一步： <br />
        /// 两支都不收面板地址，只收窗口标识：标识经门面按档换算（<see cref="ShowUI_AddressingBandsOnProductionHandler_WindowIdResolvesPerBand"/> 量的就是这一档）。
        /// </remarks>
        [Test]
        public void ShowUI_SameShapeArguments_PerTrackWindows_EachHitsItsOwnLeg()
        {
            UIService.ShowUI<ProbeLocatedUGUIWindow>("UGUILeg", LOCATED_ADDRESS);

            Assert.AreEqual(LOCATED_ADDRESS, PanelProbe.s_Address, "uGUI 腿没给标识：地址由窗口类那条链路答");
            Assert.AreEqual("LoadPanel", PanelProbe.s_Hook, "同步腿走的是同步装载那一档");
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeLocatedUGUIWindow>("UGUILeg"), "开出来的窗落在协调者那一份栈上");

            PanelProbe.s_Address = null;
            UIService.ShowUI<ProbeLocatedUitkWindow>("UITKLeg", LOCATED_ADDRESS);

            Assert.AreEqual(LOCATED_ADDRESS, PanelProbe.s_Address,
                "UI Toolkit 腿同样由窗口类那条链路答地址：它不比 uGUI 腿少这一档");
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeLocatedUitkWindow>("UITKLeg"), "两支落的是同一份栈");

            PanelProbe.s_Address = null;
            UIService.ShowUIAsync<ProbeLocatedUitkWindow>("UITKAsyncLeg", LOCATED_ADDRESS);

            Assert.AreEqual("LoadPanelAsync", PanelProbe.s_Hook, "异步那一支与同步那一支同形，但走异步装载");
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeLocatedUitkWindow>("UITKAsyncLeg"), "异步那一支也落进同一份栈");
        }

        /// <summary>
        /// 两支形参表的差别只剩一枚：寻址两档同形同序，UI Toolkit 腿从第四枚起才是它自己那一档。
        /// </summary>
        /// <remarks>
        /// 两支的第二、三枚都是面板地址与取法，且都原样交给本轨实现；uGUI 腿的第四枚直接就是 <c>ct</c>，UI Toolkit 腿的第四枚是 <c>panelSettings</c>。
        /// </remarks>
        [Test]
        public void ShowUI_SecondArgumentSlot_PerTrackLegsKeepTheirOwnParameterShape()
        {
            UIService.ShowUI<ProbeAddressUGUIWindow>("UGUIGiven", "Given/Address", true);

            Assert.AreEqual("Given/Address", PanelProbe.s_Address, "uGUI 腿把第二枚当窗口标识交下去：内置资源档拼上 Resources 父目录");
            Assert.IsTrue(PanelProbe.s_FromResources, "uGUI 腿把第三枚当 fromResources 原样交下去");
            Assert.AreEqual("LoadPanel", PanelProbe.s_Hook, "同步那一支仍走同步装载");

            PanelProbe.s_Address = null;
            PanelProbe.s_FromResources = false;
            UIService.ShowUI<ProbeAddressUitkWindow>("UITKGiven", "Given/Address", true);

            Assert.AreEqual("Given/Address", PanelProbe.s_Address,
                "UI Toolkit 腿的第二枚同样是窗口标识：换算后才交进共享栈");
            Assert.IsTrue(PanelProbe.s_FromResources, "第三枚同理：取法由腿交下去，不再写死 AB 口径");
        }

        #endregion

        #region 窗口级配置与取法落地 [WINDOW-LEVEL PANEL SETTINGS & FETCH MODES]

        /// <summary>
        /// 窗口级 <c>PanelSettings</c> 真落到文档组件上，且优先于共享那一份；同一条腿不给覆盖时回共享兜底。
        /// </summary>
        /// <remarks>
        /// 这是 R10 那一枚新形参的两半可观测判据：覆盖那一档若被读成共享位、或交接排到了装载之后，第一组断言当场红； <br />
        /// 兜底那一档若被新形参顶掉（覆盖为空时不回共享），最后一句红。两半合起来才证「这是覆盖，不是替换」。 <br />
        /// 副本是 <c>Instantiate</c> 出来的同资产克隆：只为拿一枚与共享位不同一性的对象比同一性，样式与缩放配置随资产原样带过来， <br />
        /// 装载路径因此吃的是一枚真配置，不是一份没有主题的裸 <c>CreateInstance</c>。副本与壳都交 <see cref="TearDown"/> 销毁。
        /// </remarks>
        [Test]
        public void ShowUI_UITKLegWindowLevelPanelSettings_LandsOnDocumentAheadOfShared()
        {
            var shared = Resources.Load<PanelSettings>(SHARED_SETTINGS_RESOURCE_NAME);
            Assert.IsNotNull(shared, "量具前提坏了：取不到 {0} 夹具资产，窗口级配置的优先判据无从判起", SHARED_SETTINGS_RESOURCE_NAME);
            var windowLevel = UnityEngine.Object.Instantiate(shared);
            Assert.IsNotNull(windowLevel, "量具前提坏了：克隆不出第二枚配置，两枚无从比较同一性");
            _clonedSettings.Add(windowLevel);
            UITKWindow.SharedPanelSettings = shared;

            UIService.ShowUI<ProbeLoadedFromResourcesUitkWindow>("OwnPanelKit", TEMPLATE_RESOURCE_NAME, false, windowLevel);
            var withOverride = RegisterShell(UIService.SharedLedger.GetWindow<ProbeLoadedFromResourcesUitkWindow>("OwnPanelKit"));
            Assert.IsTrue(withOverride.IsLoadDone, "带窗口级配置的那一窗要装得上面板");
            Assert.Greater(withOverride.RootVisual.childCount, 0, "模板克隆进内容根：这一窗走的是完整装载路径");
            Assert.AreSame(windowLevel, withOverride.Document.panelSettings, "文档组件拿到的必须是本窗那一枚配置");
            Assert.AreNotSame(shared, withOverride.Document.panelSettings, "共享那一份不得顶掉窗口级覆盖");

            UIService.ShowUI<ProbeLoadedFromResourcesUitkWindow>("SharedPanelKit", TEMPLATE_RESOURCE_NAME);
            var withoutOverride = RegisterShell(UIService.SharedLedger.GetWindow<ProbeLoadedFromResourcesUitkWindow>("SharedPanelKit"));
            Assert.IsNull(withoutOverride.PanelSettingsOverride, "没给覆盖的那一窗，实例位停在空中");
            Assert.AreSame(shared, withoutOverride.Document.panelSettings, "覆盖为空时回共享那一份兜底：新形参是覆盖不是替换");
        }

        /// <summary>
        /// UI Toolkit 腿的内置资源档真走 <c>TryReadTree</c> 的 Resources 支路：标识原样当 Resources 相对路径取到模板。
        /// </summary>
        /// <remarks>
        /// 面板地址从此只有一条来路（开窗标识的换算），AB 支路要求配置表答地址——编辑器测试装配里没有表， 
        /// 那一档的可观测后果改由 <c>UIWindowLoadFailureTests.LoadFailure_SyncShowUI_RollsWindowOffStackAndMarksItFailed</c> 钉（表未就绪 → null → 回滚）。 <br />
        /// 本格因此只留内置资源这一半：标识 <c>EventsDebugger</c> 在父目录留空时原样交给 <c>Resources.Load</c>，取不到模板即红。
        /// </remarks>
        [Test]
        public void ShowUI_UITKLegResourcesBand_RealLoadsTemplateThroughItsOwnReadBranch()
        {
            UITKWindow.SharedPanelSettings = Resources.Load<PanelSettings>(SHARED_SETTINGS_RESOURCE_NAME);
            Assert.IsNotNull(UITKWindow.SharedPanelSettings, "量具前提坏了：取不到 {0} 夹具资产", SHARED_SETTINGS_RESOURCE_NAME);
            Assert.IsNotNull(Resources.Load<VisualTreeAsset>(TEMPLATE_RESOURCE_NAME),
                "量具前提坏了：Resources.Load 取不到内置模板 {0}，取法那一档无从覆盖", TEMPLATE_RESOURCE_NAME);

            UIService.ShowUI<ProbeLoadedFromResourcesUitkWindow>("ResourcesModeKit", TEMPLATE_RESOURCE_NAME);
            var viaResources = RegisterShell(UIService.SharedLedger.GetWindow<ProbeLoadedFromResourcesUitkWindow>("ResourcesModeKit"));
            Assert.IsTrue(viaResources.IsLoadDone, "腿传 fromResources=true 时要真走 Resources 那一档");
            Assert.Greater(viaResources.RootVisual.childCount, 0, "内置资源那一档克隆的是按 Resources 名取到的模板");
        }

        /// <summary>
        /// 载荷腿的实参形状：载荷永远排在第一枚，UI Toolkit 腿的 <c>panelSettings</c> 槽不吃它。
        /// </summary>
        /// <remarks>
        /// 带载荷那一族与无载荷那一族的分辨依据是<b>类型实参枚数</b>（两支各 2 枚 vs 1 枚），不是形参个数 ⇒ 同名重载编得过、绑得准。 <br />
        /// 载荷排第一枚是硬切后的新契约：旧的「寻址三枚之后排 userData」形状已随 <c>params object[]</c> 一起删除，这一格钉的是新形状的两半—— <br />
        /// uGUI 腿那一半证 class 载荷按引用直达（不走擦除、不复制），UI Toolkit 腿那一半证第四枚位置说的是 <c>panelSettings</c> 而不是载荷 <br />
        /// （交真配置时配置落进实例位、载荷仍从第一枚直达 <c>Payload</c>，两头各亮一次）。等待腿用普通 <c>TArg</c> 而非 <c>in</c>：<c>async</c> 方法禁 <c>in</c> 形参（CS1988）。
        /// </remarks>
        [Test]
        public void PayloadLeg_UGUI_PayloadArrivesByReference()
        {
            var payload = new object();
            UIService.ShowUIAsync<ProbeAddressUGUIWindow, object>(payload, "ShapeUGUI", "ShapeUGUI");
            var window = UIService.SharedLedger.GetWindow<ProbeAddressUGUIWindow>("ShapeUGUI");
            Assert.AreSame(payload, window.Payload, "静态腿 class 载荷引用同一性直达");
        }

        /// <summary><see cref="PayloadLeg_UGUI_PayloadArrivesByReference"/> 的 UI Toolkit 对格：第四枚位置是窗口级配置槽，不吃载荷。</summary>
        /// <remarks>
        /// 错位守卫两头都要亮：交<b>真配置</b>时「配置落进实例位」与「载荷仍从第一枚直达」各判一次——
        /// 给 <c>null</c> 覆盖时 <c>HandoffPanelSettings(null)</c> 一枚委托都不建，实例位停在 <c>null</c> 是结构上必然，
        /// 那句 <c>Assert.IsNull</c> 在「槽位被载荷吃掉」的坏实装下同样成立，守不住任何东西，故换成真配置那一档；
        /// 「不给覆盖时回共享兜底、实例位停在空中」那一半仍由 <see cref="ShowUI_UITKLegWindowLevelPanelSettings_LandsOnDocumentAheadOfShared"/> 钉着，没有丢。 <br />
        /// 探针窗覆写 <c>LoadPanel</c> 且按「装载成功、面板留空」交回 ⇒ 这一格不吃共享配置、不建壳，只判交接钩子那一次写入；
        /// 副本是 <c>Instantiate</c> 出来的同资产克隆，交 <see cref="TearDown"/> 销毁。
        /// </remarks>
        [Test]
        public void PayloadLeg_UITK_PanelSettingsSlotDoesNotEatPayload()
        {
            var shared = Resources.Load<PanelSettings>(SHARED_SETTINGS_RESOURCE_NAME);
            Assert.IsNotNull(shared, "量具前提坏了：取不到 {0} 夹具资产，第四枚位置的落点判据无从判起", SHARED_SETTINGS_RESOURCE_NAME);
            var windowLevel = UnityEngine.Object.Instantiate(shared);
            Assert.IsNotNull(windowLevel, "量具前提坏了：克隆不出第二枚配置，与共享位无从比较同一性");
            _clonedSettings.Add(windowLevel);

            var payload = new object();
            // UITK 载荷腿第四枚位置是 panelSettings（这一格给真配置），载荷永远排第一枚：两头各亮一次
            UIService.ShowUIAsync<ProbeAddressUitkWindow, object>(payload, "ShapeKitData", "ShapeKitData", false, windowLevel);
            var window = UIService.SharedLedger.GetWindow<ProbeAddressUitkWindow>("ShapeKitData");
            Assert.IsNotNull(window, "量具前提坏了：UI Toolkit 载荷腿把窗开进了共享栈");
            Assert.AreSame(windowLevel, window.PanelSettingsOverride, "第四枚的窗口级配置落进实例位：那一枚位置说的是配置，不是载荷");
            Assert.AreNotSame(shared, window.PanelSettingsOverride, "交进本窗的是那一枚副本，不是共享位");
            Assert.AreSame(payload, window.Payload, "载荷仍从第一枚直达：配置槽没吃它");
        }

        #endregion

        #region 面板地址按窗口类解析 [PANEL ADDRESS RESOLUTION]

        /// <summary>
        /// 生产驱动者这一侧的标识换算：内置资源档在父目录留空时原样交标识，AB 档没有配置表就答 null；特性上的 <c>fromResources</c> 同样并进取法。
        /// </summary>
        /// <remarks>
        /// 这一格判的是<b>生产那两支驱动者</b>那一侧：门面上没有换入接缝，两支腿叫的都是各自认领进槽的生产处理器，桩不参与。 <br />
        /// 面板地址只有标识这一条来路：<c>CreateInstance</c> 按 <c>fromResources || 特性</c> 选一档——内置资源档拼 <c>UIServiceSettings</c> 的父目录（留空即原样），AB 档交 <c>ConfigTableService</c>。 <br />
        /// 测试装配里没有配置表，AB 档因此答 null（这一档的失败后果由 <c>UIWindowLoadFailureTests</c> 钉），特性没写 <c>fromResources</c> 时取法保持 AB。 <br />
        /// 三只探针窗的 <c>LoadPanel</c> 钩子只记入参并按「装载成功、面板留空」交回：不取资产、不建壳，也就点不亮 <c>UIDocument</c>； <br />
        /// 层级一律取非模态的 <c>Tips</c>，压栈时不会去动下层窗口的可交互位；三只窗各用各的窗口名，那一条共享栈上不会撞「Window is exist」。 <br />
        /// 每档调用前把记录位拨成相反值，断到的必须是这一次写进去的。
        /// </remarks>
        [Test]
        public void ShowUI_AddressingBandsOnProductionHandler_WindowIdResolvesPerBand()
        {
            PanelProbe.s_Address = null;
            PanelProbe.s_FromResources = false;
            UIService.ShowUI<ProbeLocatedUitkWindow>("LocatedUITK", LOCATED_ADDRESS);
            Assert.AreEqual(LOCATED_ADDRESS, PanelProbe.s_Address,
                "腿没给地址时，面板地址由窗口类的 [Window(location)] 给出——两支同一条解析链");
            Assert.IsTrue(PanelProbe.s_FromResources, "特性上的 fromResources 并进交给面板的取法");

            PanelProbe.s_Address = null;
            PanelProbe.s_FromResources = true;
            UIService.ShowUI<ProbeNameFallbackUitkWindow>("FallbackUITK", "FallbackUITK");
            Assert.IsNull(PanelProbe.s_Address,
                "AB 档的地址只由配置表答：测试装配没有表即 null，不再回落类型名或字面地址");
            Assert.IsFalse(PanelProbe.s_FromResources, "特性没写 fromResources 时保持门面交给协调者的 false");

            PanelProbe.s_Address = null;
            PanelProbe.s_FromResources = false;
            UIService.ShowUI<ProbeLocatedUGUIWindow>("LocatedUGUI", LOCATED_ADDRESS);
            Assert.AreEqual(LOCATED_ADDRESS, PanelProbe.s_Address, "uGUI 侧走的是同一条解析链");
        }

        #endregion

        #region 认不出轨当场抬错 [TRACK FAIL-FAST]

        /// <summary><see cref="System.Type"/> 形入口的空白档：窗口类给成 <c>null</c> 时同样抬 <see cref="GameException"/>，不掉进 <see cref="System.NullReferenceException"/>。</summary>
        [Test]
        public void ShowUI_TypeEntry_NullWindowType_FailsFastWithGameException()
        {
            Assert.Throws<GameException>(() => UIService.ShowUI(null, "w"),
                "空窗口类须抬 GameException，而不是取 FullName 时掉 NRE");
            Assert.IsNull(UIService.SharedLedger.GetTopWindow(), "判 null 排在一切取用之前：共享栈上一只窗都没多");

            Assert.Throws<GameException>(() => UIService.ShowUIAsync(null, "w"), "异步那一支同一判据");
            Assert.IsNull(UIService.SharedLedger.GetTopWindow(), "异步腿也没进共享栈");
        }

        /// <summary><c>Type</c> 形入口的错配档：运行期给来的窗口类不落任何一轨时同样当场抬错，两条腿都不叫。</summary>
        [Test]
        public void ShowUI_TypeEntry_WindowClassOnNoTrack_FailsFastBeforeCoordinator()
        {
            Assert.Throws<GameException>(() => UIService.ShowUI(typeof(ProbeBareWindow), "w", "w"),
                "Type 入口错配须抬 GameException");
            Assert.IsNull(UIService.SharedLedger.GetTopWindow(), "错配不开半只窗：两条腿一次都没被叫到");

            Assert.Throws<GameException>(() => UIService.ShowUIAsync(typeof(ProbeBareWindow), "w", "w"), "异步腿同一判据");
            Assert.IsNull(UIService.SharedLedger.GetTopWindow(), "同上");
        }

        /// <summary><c>Type</c> 形入口的配对档：UI Toolkit 轨的类型由那一轨的认轨判定接住，并落进 UI Toolkit 那条腿。</summary>
        [Test]
        public void ShowUI_TypeEntry_UITKWindowType_ClaimsUITKTrack()
        {
            UIService.ShowUI(typeof(ProbeLocatedUitkWindow), "TypeUITK", LOCATED_ADDRESS);

            var opened = UIService.SharedLedger.GetWindow<ProbeLocatedUitkWindow>("TypeUITK");
            Assert.IsNotNull(opened, "认出轨才交给那一轨的实现：窗口在共享栈上");
            Assert.AreEqual("TypeUITK", opened.WindowName, "带的是调用方给的窗口名");
            Assert.AreEqual(LOCATED_ADDRESS, PanelProbe.s_Address,
                "入口没带标识时，UI Toolkit 档也按窗口类那条链路答地址：这一档两支没有别");
        }

        /// <summary>
        /// <c>Type</c> 形入口的 uGUI 档：调用方给的面板地址被 uGUI 那条腿收下——与 UI Toolkit 档同一判据，两支同形。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="ShowUI_TypeEntry_UITKWindowType_ClaimsUITKTrack"/> 成对：那里给的是带 <c>location</c> 的探针窗、这一格用的探针窗不带 <c>location</c>，
        /// 于是交进共享栈的地址只能由入口那一枚给出——两支都收下它，入口这一档的差别自 R10 起只剩「本入口没有窗口级 PanelSettings 那一枚形参」。
        /// </remarks>
        [Test]
        public void ShowUI_TypeEntry_UGUIWindowType_TakesTheGivenAddress()
        {
            PanelProbe.s_Address = null;
            PanelProbe.s_FromResources = false;

            UIService.ShowUI(typeof(ProbeAddressUGUIWindow), "TypeAddr", "Given/Address", true);

            Assert.AreEqual("Given/Address", PanelProbe.s_Address, "入口带来的标识按内置资源档换算后交下去");
            Assert.IsTrue(PanelProbe.s_FromResources, "取法也按入口给的那一枚交下去，不是特性里的默认值");
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeAddressUGUIWindow>("TypeAddr"), "窗口落在协调者那一份栈上");
        }

        /// <summary>
        /// 共存的正反两面：两轨的类型各被自己那一轨接住、各开各的窗，而没挂任何一枚窗口基类的类型仍判认不出。
        /// </summary>
        [Test]
        public void ShowUI_TypeEntry_PerTrackWindowTypes_EachTrackClaimsItsOwnAndRejectsTheBareOne()
        {
            UIService.ShowUI(typeof(ProbeLocatedUGUIWindow), "TypeUGUI", "TypeUGUI");
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeLocatedUGUIWindow>("TypeUGUI"), "uGUI 窗经 Type 入口由 uGUI 那条腿接住");

            UIService.ShowUIAsync(typeof(ProbeLocatedUitkWindow), "TypeUITK", LOCATED_ADDRESS);
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<ProbeLocatedUitkWindow>("TypeUITK"), "另一轨的窗由另一条腿接住，不抢对方那一只");
            Assert.AreSame(UIService.SharedLedger.GetWindow<ProbeLocatedUitkWindow>("TypeUITK"), UIService.SharedLedger.GetTopWindow(),
                "两条腿共存：两次开的窗并进同一份栈，栈顶是后开的那一只");

            Assert.Throws<GameException>(() => UIService.ShowUI(typeof(ProbeBareWindow), "bare", "bare"),
                "两轨都在场时，没挂任何一枚窗口基类的类型照样认不出");
            Assert.AreEqual(2, UIService.SharedLedger.PeekStack().Count, "认不出轨不开半只窗：栈上仍是那两只");
        }

        #endregion

        #region 开出来的窗口各自落回自己那一轨 [OPENED WINDOW PER TRACK]

        /// <summary>
        /// 两支各自开出自己那一轨的窗：面板地址由各自那条腿的来源给出，两只窗并进同一份栈、各落回自己那一轨的窗口基类。
        /// </summary>
        /// <remarks>
        /// 门面在开成功之后不再问一遍归属：窗口交回调用方就是那一只，轨身份由窗口自己的基类答； <br />
        /// 装载走异步那一档（<c>ShowUIAsync</c>）——等待腿那句 <c>UniTask.WaitUntil</c> 在 EditMode 里不会同步落定 <br />
        /// （<c>GetResult</c> 当场抛 "Not yet completed"），面板就绪与壳/内容根那两半的判据住在 <c>UITKWindowTests</c> 的真装载格里。
        /// </remarks>
        [Test]
        public void ShowUIAsync_PerTrackOpenedWindows_ResolveToTheirOwnWindowBases()
        {
            // 异步装载的续延不在同一帧内落定（EditMode 里 await 已完成的 UniTask 仍要过一轮），
            // 因此本格只判「压栈与轨身份」这一半；装载钩子与就绪那一半由协调者侧与 UITKWindow 侧的格子分别钉住。

            UIService.ShowUIAsync<ProbeBoundUGUIWindow>("AsyncUGUI", "AsyncUGUI");

            var openedUgui = UIService.SharedLedger.GetWindow<ProbeBoundUGUIWindow>("AsyncUGUI");
            Assert.IsNotNull(openedUgui, "uGUI 腿开出的窗落在协调者那一份栈上");
            Assert.IsInstanceOf<UGUIWindow>(openedUgui, "uGUI 腿开出的窗口落回 uGUI 那一轨的窗口基类");
            if (openedUgui.gameObject != null)
            {
                _shells.Add(openedUgui.gameObject);
            }

            var settings = Resources.Load<PanelSettings>("DebuggerPanelSettings");
            Assert.IsNotNull(settings, "量具前提坏了：取不到 DebuggerPanelSettings 夹具资产，真装载的壳无从造出");
            UITKWindow.SharedPanelSettings = settings;

            UIService.ShowUIAsync<ProbeLoadedFromResourcesUitkWindow>("AsyncUITK", TEMPLATE_RESOURCE_NAME);

            var openedKit = UIService.SharedLedger.GetWindow<ProbeLoadedFromResourcesUitkWindow>("AsyncUITK");
            Assert.IsNotNull(openedKit, "UI Toolkit 腿开出的窗落进同一份共享栈");
            if (openedKit.gameObject != null)
            {
                // 真装载造出的壳要按出门销毁登记，与上面 uGUI 那一半同一口径
                _shells.Add(openedKit.gameObject);
            }

            Assert.IsInstanceOf<UITKWindow>(openedKit, "UI Toolkit 腿开出的窗口是那一轨的窗口");
            Assert.AreSame(openedKit, UIService.SharedLedger.GetTopWindow(), "栈顶换成后开的那一只：两支并在一处");
            Assert.IsNotInstanceOf<UGUIWindow>(openedKit, "两支的窗口基类互不派生：UI Toolkit 腿那只窗不会被 uGUI 轨认走");
        }

        /// <summary>
        /// 两支等待腿的复用支路：栈上已有同名窗口时同步落定并交回那一只窗，不再压第二只。
        /// </summary>
        /// <remarks>
        /// 等待腿在 EditMode 只能观测这一半——压栈与「已落定」都排在第一个 await 之前，<c>GetResult()</c> 当场答得出； <br />
        /// 等面板就绪的那一半（<c>UniTask.WaitUntil</c>）不同步落定，<c>GetResult()</c> 抛 Not yet completed， <br />
        /// 那一档的端到端本文件无格，交 Task 9 的 PlayMode。探针窗按「装载成功、面板留空」交回，因此不需要资产也不建壳。
        /// </remarks>
        [Test]
        public void ShowUIAsyncAwait_PerTrackLegsOnStackedWindow_ReturnItWithoutWaiting()
        {
            UIService.ShowUI<ProbeAddressUGUIWindow>("AwaitUGUI", "AwaitUGUI");
            var stackedUgui = UIService.SharedLedger.GetWindow<ProbeAddressUGUIWindow>("AwaitUGUI");
            var awaitedUgui = UIService.ShowUIAsyncAwait<ProbeAddressUGUIWindow>("AwaitUGUI", "AwaitUGUI").GetAwaiter().GetResult();
            Assert.AreSame(stackedUgui, awaitedUgui, "uGUI 的等待腿经复用支路同步交回栈上那一只窗");

            UIService.ShowUI<ProbeAddressUitkWindow>("AwaitUITK", "AwaitUITK");
            var stackedKit = UIService.SharedLedger.GetWindow<ProbeAddressUitkWindow>("AwaitUITK");
            var awaitedKit = UIService.ShowUIAsyncAwait<ProbeAddressUitkWindow>("AwaitUITK", "AwaitUITK").GetAwaiter().GetResult();
            Assert.AreSame(stackedKit, awaitedKit, "另一轨的等待腿走同一条支路");

            Assert.AreEqual(2, UIService.SharedLedger.PeekStack().Count, "复用支路只挪栈顶，不压第二只：栈上仍是那两只窗");
        }

        /// <summary>
        /// 等待腿的转型守卫：账本按名命中的实例不是这一腿声明的窗口类时当场抬 <see cref="GameException"/>，不把别的类的窗当成 <c>TWindow</c> 交回。
        /// </summary>
        /// <remarks>
        /// 可达的构造是「同名两只窗共用同一枚载荷槽型」：账本 <c>ResolveOrStartLoad&lt;TArg&gt;</c> 认名不认类，
        /// <c>SetPayloadChecked</c> 只判命中那只窗是否 <c>IUIPayloadSlot&lt;TArg&gt;</c>——<see cref="ProbeAddressUGUIWindow"/> 与
        /// <see cref="ProbeAliasedUGUIWindow"/> 都是 <c>UGUIWindow&lt;object&gt;</c> ⇒ 槽型这一关放行、交回的是 A 那一枚实例，
        /// 「窗口类对不对」只剩门面 <c>window switch</c> 那一道拦得住（两支后门的同一道守卫在 UITK 腿同形）。<br />
        /// A 由同步腿开出、当场就绪 ⇒ 复用支路排在等待腿第一个 await 之前，同帧交回实例，门面那一支 <c>async</c> 腿因此也同帧落定为故障，
        /// <c>GetResult()</c> 当场答得出这一枚 <see cref="GameException"/>（与 <see cref="ShowUIAsyncAwait_PerTrackLegsOnStackedWindow_ReturnItWithoutWaiting"/> 同一档射程）。<br />
        /// 抬错排在装载之后、栈不变：判据吃的是「不静默交回别的类」与「不压第二只、也不收走原来那一只」。
        /// </remarks>
        [Test]
        public void ShowUIAsyncAwait_PayloadLeg_NameHitIsAnotherWindowClass_FailsFastWithGameException()
        {
            var payload = new object();
            UIService.ShowUI<ProbeAddressUGUIWindow, object>(payload, "CastGuard", "CastGuard");
            var opened = UIService.SharedLedger.GetWindow<ProbeAddressUGUIWindow>("CastGuard");
            Assert.IsNotNull(opened, "量具前提坏了：A 已按这个名字开进共享栈");
            Assert.IsTrue(opened.IsLoadDone, "量具前提坏了：A 走的是同步装载，复用支路才能同帧落定");

            var ex = Assert.Throws<GameException>(() =>
                UIService.ShowUIAsyncAwait<ProbeAliasedUGUIWindow, object>(payload, "CastGuard", "CastGuard").GetAwaiter().GetResult(),
                "按名命中的实例不是这一腿的窗口类：等待腿要抬错，不把别的类的窗当成 TWindow 交回");
            StringAssert.Contains(nameof(ProbeAliasedUGUIWindow), ex.Message, "文案带这一腿期望的窗口类名");
            StringAssert.Contains(nameof(ProbeAddressUGUIWindow), ex.Message, "文案带上实际的窗口类名");

            Assert.AreEqual(1, UIService.SharedLedger.PeekStack().Count, "抬错不压第二只：栈上仍是那一只");
            Assert.AreSame(opened, UIService.SharedLedger.GetWindow("CastGuard"), "抬错不收走 A：复用支路交回的仍是原来那一只");
        }

        #endregion

        #region 对象模型的回叫 [WINDOW CALLBACK TARGET]

        /// <summary>
        /// 关停之后落进来的窗口回叫静默落空：两支处理器都摘干净之后 <see cref="UIService.IsValid"/> 回假，回叫既不抬错也不结算那条栈。
        /// </summary>
        /// <remarks>
        /// 这条路是真的可达：<c>UIWindow</c> 在 <c>isShutDown</c> 为真时<b>刻意不注销</b>隐藏转关闭的计时器，而那条计时器绑的正是 <c>Close</c> 钩子； <br />
        /// 守卫住在钩子里：两支驱动者都不在位时回叫静默落空，既不抬错也不结算那条栈——这一格钉的正是它。 <br />
        /// 窗口是关停之后才压上栈的（模拟销毁链里迟到的那一只），落空要看得见就得判它还在不在栈上。
        /// </remarks>
        [Test]
        public void HideAndClose_WindowCallsAfterServiceShutdown_FallSilently()
        {
            new UIService().OnInit();
            new UIService().OnShutdown();
            Assert.IsFalse(UIService.IsValid, "量具前提坏了：关停没把两支处理器都摘掉");

            var window = new ProbeUGUIWindow();
            window.Init("ProbeWindow", 1, false, "Probe", false, 10);
            UIService.SharedLedger.Push(window);

            Assert.DoesNotThrow(() => window.Hide(), "关停后的隐藏回叫不得抬错");
            Assert.DoesNotThrow(() => window.Close(), "关停后的关闭回叫不得抬错");
            Assert.AreEqual(1, UIService.SharedLedger.PeekStack().Count, "落空就是什么都没做：栈上那一只没被移走");
        }

        #endregion

        #region 生产入口与两轨各自的腿 [PRODUCTION ENTRY & PER-TRACK LEGS]

        /// <summary>
        /// 生产入口之后两轨各开一只窗都成立，再走一次同样的入口不改变这一判据。
        /// </summary>
        /// <remarks>
        /// 这一格钉的是门面级事实：<see cref="UIService.OnInit"/> 把两支生产驱动者都就位之后，两支的类型都能经 <see cref="System.Type"/> 形入口开得起来， <br />
        /// 判据里没有任何「先备好什么才开得」的前置——开窗腿认的是窗口基类本身。第二次 <c>OnInit</c> 之后照旧开得起来，钉的是这一入口可重入；可重入的同一起儿还把共享栈归零，因为它是唯一的归零点。
        /// </remarks>
        [Test]
        public void OnInit_ProductionEntry_LeavesBothTracksOpenable_AndSurvivesASecondInit()
        {
            new UIService().OnInit();
            Assert.IsInstanceOf<UGUIHandler>(UIService.Internal_PeekUGUIHandler(), "量具前提坏了：OnInit 没把 uGUI 那一轨的驱动者交出来");
            Assert.IsInstanceOf<UITKHandler>(UIService.Internal_PeekUITKHandler(), "量具前提坏了：OnInit 没把 UI Toolkit 那一轨的驱动者交出来");

            UIService.ShowUI(typeof(ProbeLocatedUGUIWindow), "ProdUGUI", "ProdUGUI");
            UIService.ShowUI(typeof(ProbeLocatedUitkWindow), "ProdUITK", "ProdUITK");
            Assert.AreEqual(2, UIService.SharedLedger.PeekStack().Count, "生产入口之后两轨各开一只窗都成立");

            new UIService().OnInit();
            Assert.AreEqual(0, UIService.SharedLedger.PeekStack().Count, "再走一次生产入口先归零那一份共享栈：门面的 OnInit 是唯一抹栈的地方");

            UIService.ShowUI(typeof(ProbeLocatedUGUIWindow), "ProdUGUI2", "ProdUGUI2");
            UIService.ShowUI(typeof(ProbeLocatedUitkWindow), "ProdUITK2", "ProdUITK2");
            Assert.AreEqual(2, UIService.SharedLedger.PeekStack().Count, "归零之后的第二轮里，两轨照样各开得起来一只");
        }

        /// <summary>
        /// uGUI 腿叫的是本轨的实现：调用方给的面板地址原样送到共享栈那侧，窗口压进协调者手里那一份栈。
        /// </summary>
        /// <remarks>
        /// 「共享默认路径」那一档已在形状上不存在（协调者没有 <c>ShowUI</c> 那条腿可叫），这一格因此判可观测的两半： <br />
        /// 地址这一半只属于 uGUI 腿（另一条腿不给地址），落栈这一半两腿共用协调者那一份。
        /// </remarks>
        [Test]
        public void ShowUI_UGUILeg_OpensThroughItsOwnTrackImplementation()
        {
            PanelProbe.s_Address = null;
            PanelProbe.s_FromResources = false;

            UIService.ShowUI<ProbeAddressUGUIWindow>("OwnUGUI", "Own/Address", true);

            var opened = UIService.SharedLedger.GetWindow<ProbeAddressUGUIWindow>("OwnUGUI");
            Assert.IsNotNull(opened, "uGUI 腿开出的窗落在协调者那一份栈上");
            Assert.AreEqual("Own/Address", PanelProbe.s_Address, "走的是带窗口标识那一档的 uGUI 实现：地址按父目录换算过");
            Assert.IsTrue(PanelProbe.s_FromResources, "取法同样由 uGUI 这条腿交下去");
        }

        /// <summary>
        /// UI Toolkit 腿叫的是本轨的实现：这一格留缺省，地址与取法都由窗口类那条链路答，窗口照样压进协调者手里那一份栈。
        /// </summary>
        [Test]
        public void ShowUI_UITKLeg_OpensThroughItsOwnTrackImplementation()
        {
            PanelProbe.s_Address = null;
            PanelProbe.s_FromResources = true;

            UIService.ShowUI<ProbeAddressUitkWindow>("OwnUITK", "OwnUITK");

            var opened = UIService.SharedLedger.GetWindow<ProbeAddressUitkWindow>("OwnUITK");
            Assert.IsNotNull(opened, "UI Toolkit 腿开出的窗落在同一份共享栈上");
            Assert.IsNull(PanelProbe.s_Address,
                "这一格留 AB 档：标识要交给配置表答地址，测试装配没有表，交进本轨实现的即 null");
            Assert.IsFalse(PanelProbe.s_FromResources, "取法同样留缺省：这只窗不带 fromResources 特性，交给面板的即 AB 口径");
        }

        /// <summary>
        /// 清单里提了 uGUI 那一支、但还没走生产初始化 ⇒ 开窗腿当场抬错，不顺手造一枚，也不留半只窗。
        /// </summary>
        /// <remarks>
        /// 「配置已启用」与「驱动者已就位」是两档，这一格判的就是中间那一档：归位门清掉槽位之后，腿没有「替配置造一枚」那条隐式启用。<br />
        /// 抬错之后栈上一只窗都没有——静默开半只窗正是这一刀要消灭的形状。
        /// </remarks>
        [Test]
        public void ShowUI_EnabledButNotRegistered_LegThrowsAndLeavesNoWindow()
        {
            UIService.Internal_ResetHandlerSlots();

            Assert.Throws<GameException>(() =>
                UIService.ShowUI<ProbeAddressUGUIWindow>("NotRegistered", "Lazy/Address", false),
                "已启用但没注册：开窗腿要抬错，不替配置造一枚驱动者");
            Assert.IsNull(UIService.GetTopWindow(), "抬错之后栈上不留半只窗");
        }

        /// <summary>
        /// 开窗族住在协调者之后仍只有一份栈：经生产入口交出的那一份协调者自己收下开出来的窗，查询与栈顶答的都是同一只窗口。
        /// </summary>
        /// <remarks>
        /// 这一格吃的是<b>生产用的驱动者</b>（<see cref="UGUIHandler"/>）：它由门面的启用清单认领进来并初始化，本夹具不进槽位。 <br />
        /// 探针窗的 <c>LoadPanel</c> 只记录入参并按「装载成功、面板留空」交回，因此不需要资产也不建面板物体，用例出门也没有要销毁的本体。 <br />
        /// 层级取非模态的 <c>Tips</c>：压栈时不去动下层窗口的可交互位，本格的判据只有「这一只窗在不在栈上」。
        /// </remarks>
        [Test]
        public void ShowUI_OnProductionHandler_OpenLandsOnTheSharedStack()
        {
            var handler = UIService.UGUIHandler;
            new UIService().OnInit();
            Assert.AreSame(handler, UIService.Internal_PeekUGUIHandler(), "量具前提坏了：OnInit 认的就是清单里那一份驱动者");

            UIService.ShowUI<ProbeLocatedUGUIWindow>("ProdStack", LOCATED_ADDRESS);

            var opened = UIService.GetWindow<ProbeLocatedUGUIWindow>("ProdStack");
            Assert.IsNotNull(opened, "开窗写的是协调者那一份栈：查询当场答得出这只窗");
            Assert.AreSame(opened, handler.Internal_PeekStack()[handler.Internal_PeekStack().Count - 1], "栈顶查询答的是同一只窗口");
            Assert.IsTrue(UIService.HasWindow<ProbeLocatedUGUIWindow>("ProdStack"), "门面的存在性查询从同一份栈答");
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>
        /// 把真装载开出来的那只窗登记给 <see cref="TearDown"/>（按壳物体）并原样交回用例。
        /// </summary>
        /// <remarks>
        /// 登记排在用例的断言之前：装载中途抛错时壳已经挂到场景里，先断言再登记等于把一枚面板留给下一轮用例串味。 <br />
        /// 壳为空（面板留空那一档）时只把窗口交回，断言侧自己会红在「窗没开出来」上。
        /// </remarks>
        /// <typeparam name="T">探针窗类型。</typeparam>
        /// <param name="window">共享栈上那一窗；没开出来时为 <c>null</c>。</param>
        /// <returns>交回用例的那一窗。</returns>
        private T RegisterShell<T>(T window) where T : UITKWindow
        {
            Assert.IsNotNull(window, "UI Toolkit 腿开出的真装载窗要在共享栈上");
            var shell = window.gameObject;
            if (shell != null)
            {
                _shells.Add(shell);
            }

            return window;
        }

        /// <summary>uGUI 轨的身份探针：只证这一类窗口落回 uGUI 那一轨的窗口基类，不碰面板。</summary>
        internal sealed class ProbeUGUIWindow : UGUIWindow
        {
        }

        /// <summary>
        /// 面板钩子留下的入参：探针窗把交来的地址、取法与「走的哪一枚钩子」记在这里，每一档调用前拨成相反值。
        /// </summary>
        private static class PanelProbe
        {
            /// <summary>交进装载钩子的面板地址。</summary>
            internal static string s_Address;

            /// <summary>交进装载钩子的取法（AB 还是内置资源）。</summary>
            internal static bool s_FromResources;

            /// <summary>走到的是同步那一枚装载钩子还是异步那一枚：分辨开窗的同步腿、异步腿与等待腿。</summary>
            internal static string s_Hook;
        }

        /// <summary>装载钩子的公共记法：记下这一轨被叫到时拿到的三件事，然后按「装载成功、面板留空」交回。</summary>
        /// <remarks>
        /// 回 true 只代表装载档成功：不取资产、不建壳、不绑面板，后续意图钩子全部走各基类的空操作——装载失败回滚语义另有
        /// <see cref="UIWindowLoadFailureTests"/> 专项钉住，本文件的探针不再用「永卡栈」当夹具。
        /// </remarks>
        private static bool RecordAndAccept(string hook, string assetLocation, bool fromResources)
        {
            PanelProbe.s_Address = assetLocation;
            PanelProbe.s_FromResources = fromResources;
            PanelProbe.s_Hook = hook;
            return true;
        }

        /// <summary>带 <c>[Window(location)]</c> 的 UI Toolkit 探针窗：钩子只记录交来的地址与取法，按装载成功交回。</summary>
        [Window(EUILayer.Tips, true)]
        internal sealed class ProbeLocatedUitkWindow : UITKWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                RecordAndAccept("LoadPanel", assetLocation, fromResources);

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct) =>
                UniTask.FromResult(RecordAndAccept("LoadPanelAsync", assetLocation, fromResources));
        }

        /// <summary>不带任何取法特性的 UI Toolkit 探针窗：留 AB 档，标识交给配置表换算。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class ProbeNameFallbackUitkWindow : UITKWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                RecordAndAccept("LoadPanel", assetLocation, fromResources);

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct) =>
                UniTask.FromResult(RecordAndAccept("LoadPanelAsync", assetLocation, fromResources));
        }

        /// <summary>带 <c>[Window(location)]</c> 的 uGUI 探针窗：同一条解析链在 uGUI 侧的样本，同样按装载成功交回。</summary>
        [Window(EUILayer.Tips, true)]
        internal sealed class ProbeLocatedUGUIWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                RecordAndAccept("LoadPanel", assetLocation, fromResources);

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct) =>
                UniTask.FromResult(RecordAndAccept("LoadPanelAsync", assetLocation, fromResources));
        }

        /// <summary>带特性但没有 <c>location</c> 的 uGUI 探针窗：调用方给的地址在这一轨赢过类型名回落；带载荷那一格用它的 <c>object</c> 槽。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class ProbeAddressUGUIWindow : UGUIWindow<object>
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                RecordAndAccept("LoadPanel", assetLocation, fromResources);

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct) =>
                UniTask.FromResult(RecordAndAccept("LoadPanelAsync", assetLocation, fromResources));
        }

        /// <summary>带特性但没有 <c>location</c> 的 UI Toolkit 探针窗：与 uGUI 那一枚同形，用来比两轨的地址来源；带载荷那一格同样用它的 <c>object</c> 槽。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class ProbeAddressUitkWindow : UITKWindow<object>
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                RecordAndAccept("LoadPanel", assetLocation, fromResources);

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct) =>
                UniTask.FromResult(RecordAndAccept("LoadPanelAsync", assetLocation, fromResources));
        }

        /// <summary>
        /// 与 <see cref="ProbeAddressUGUIWindow"/> 同一枚载荷槽型（<c>object</c>）的另一枚 uGUI 窗口类：
        /// 账本按名命中、槽型校验放行，只有门面的转型那一关认得出「这两枚不是同一个类」——转型守卫那一格拿它当 <c>TWindow</c>。
        /// </summary>
        /// <remarks>装载钩子与 <see cref="ProbeAddressUGUIWindow"/> 同形（记录入参、按「装载成功、面板留空」交回）：这一格本不该造出它，兜底也不建壳。</remarks>
        [Window(EUILayer.Tips)]
        internal sealed class ProbeAliasedUGUIWindow : UGUIWindow<object>
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                RecordAndAccept("LoadPanel", assetLocation, fromResources);

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct) =>
                UniTask.FromResult(RecordAndAccept("LoadPanelAsync", assetLocation, fromResources));
        }

        /// <summary>自己造一枚真实物体当面板的 uGUI 探针窗：等待腿那一格要靠它把就绪等出来，不吃 prefab 资产。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class ProbeBoundUGUIWindow : UGUIWindow
        {
            private GameObject _panel;

            private bool Bind()
            {
                _panel = new GameObject(nameof(ProbeBoundUGUIWindow));
                _panel.AddComponent<UnityEngine.Canvas>();
                _panel.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                return BindPanel(_panel);
            }

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => Bind();

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct) => UniTask.FromResult(Bind());
        }


        /// <summary>真装载的 UI Toolkit 探针窗（内置资源档）：模板名与 <c>fromResources</c> 都写死在特性上，装载路径不覆写。</summary>
        [Window(EUILayer.Tips, true)]
        internal sealed class ProbeLoadedFromResourcesUitkWindow : UITKWindow
        {
        }

        /// <summary>
        /// 没挂任何一枚内建窗口基类的裸窗口：内建两轨都不认它，既作认轨守卫的无主样本，也作两轨照旧各开各窗时的对照组。
        /// </summary>
        internal sealed class ProbeBareWindow : UIWindow
        {
        }

        #endregion
    }
}
