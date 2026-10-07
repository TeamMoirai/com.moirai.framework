using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Input;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 窗口注册表的登记与取用：编译期登记的描述符是元数据唯一真源，未登记类型当场抬错。
    /// </summary>
    /// <remarks>
    /// 登记由 <c>UIWindowCodegen</c> 在模块初始化期完成（本夹具的探针窗随测试程序集一起登记），运行期只读。 <br />
    /// 寻址优先级：调用方给的面板地址与取法赢过特性，特性缺的档按描述符回落——与开窗链路同一判据。
    /// </remarks>
    [TestFixture]
    public sealed class UIWindowRegistryTests
    {
        private bool _savedPreventInteraction;
        private UIServiceHandler[] _savedEnabledHandlers;

        /// <summary>进门归位后装一支 uGUI 驱动者并走生产初始化：Type 形入口要过驱动者那一道才走得到注册表。</summary>
        [SetUp]
        public void SetUp()
        {
            _savedPreventInteraction = InputService.PreventInteractionUI;
            _savedEnabledHandlers = UIServiceSettings.EnabledHandlers;
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(new UIServiceHandler[] { new UGUIHandler() });
            new UIService().OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            UIService.Internal_ResetHandlerSlots();
            UIServiceSettings.Internal_SetEnabledHandlers(_savedEnabledHandlers);
            InputService.PreventInteractionUI = _savedPreventInteraction;
        }

        /// <summary>登记在表：描述符的六份取值按特性解析、工厂按类型可取。</summary>
        [Test]
        public void Registry_RegisteredWindow_ExposesDescriptorResolvedFromAttribute()
        {
            Assert.IsTrue(UIWindowRegistry.TryGet(typeof(RegistryProbeWindow), out var entry), "带 [Window] 的探针窗要在注册表里");

            Assert.AreEqual((int)UILayer.Popup, entry.Descriptor.WindowLayer, "层级按特性解析");
            Assert.AreEqual("Registry/AttrPanel", entry.Descriptor.Location, "特性写了 location 用它");
            Assert.IsFalse(entry.Descriptor.FromResources, "特性 fromResources=false 照实登记");
            Assert.IsTrue(entry.Descriptor.CacheInstance, "命名实参 cacheInstance:true 照实登记");
            Assert.AreEqual((int)UILayer.Popup, entry.Descriptor.WindowLayer, "层级取值稳定");
            var window = entry.Factory();
            Assert.IsInstanceOf<RegistryProbeWindow>(window, "工厂交回的就是登记的那一型");
        }

        /// <summary>描述符落进开窗链路：层级、面板地址与缓存档全部按注册表取值。</summary>
        [Test]
        public void RegistryDescriptor_OpenAppliesMetadataWithoutReflection()
        {
            UIService.ShowUI<RegistryProbeWindow>("RegAttr");

            var window = UIService.SharedLedger.GetWindow<RegistryProbeWindow>("RegAttr");
            Assert.IsNotNull(window, "注册过的窗口正常开出");
            Assert.AreEqual((int)UILayer.Popup, window.WindowLayer, "层级来自描述符");
            Assert.AreEqual("Registry/AttrPanel", RegistryProbeWindow.LastLocation, "面板地址来自描述符");
            Assert.IsTrue(window.CacheInstance, "缓存档来自描述符");

            UIService.SharedLedger.CloseUI(typeof(RegistryProbeWindow), "RegAttr");
            Assert.IsTrue(UIService.SharedLedger.IsParked("RegAttr"), "描述符的 cacheInstance 让关闭落进停放表");
        }

        /// <summary>调用方给的面板地址与取法赢过特性，没给的档按描述符回落。</summary>
        [Test]
        public void RegistryDescriptor_CallerAddressAndFromResources_OverrideAttribute()
        {
            UIService.ShowUI<RegistryProbeWindow>("RegCaller", "Caller/Panel", true);

            Assert.AreEqual("Caller/Panel", RegistryProbeWindow.LastLocation, "调用方给的面板地址赢过特性");
            Assert.IsTrue(RegistryProbeWindow.LastFromResources, "调用方给的内置资源档并入取法（真 || 特性假）");
        }

        /// <summary>缺省窗口名按描述符的反射全名兜底（嵌套类带 <c>+</c>），与按名取窗的判据一致。</summary>
        [Test]
        public void RegistryDescriptor_FullNameFallback_MatchesReflectionFullName()
        {
            UIService.ShowUI<RegistryProbeWindow>();

            Assert.IsNotNull(UIService.SharedLedger.GetWindow(typeof(RegistryProbeWindow).FullName),
                "缺省窗口名就是反射全名，按名取窗答得出");
        }

        /// <summary>未登记类型当场抬错：窗口类没标 [Window] 不再静默兜默认层级与地址。</summary>
        [Test]
        public void Registry_UnregisteredWindow_FailsFastWithGameException()
        {
            Assert.Throws<GameException>(() => UIService.ShowUIAsync(typeof(UnregisteredUGUIWindow), "Nope"),
                "未登记的窗口类必须当场抬错");
            Assert.IsNull(UIService.SharedLedger.GetTopWindow(), "抬错排在压栈之前：栈上不多一只");
        }

        #region 探针 [PROBES]

        /// <summary>带全档特性的注册表探针窗：装载钩子记录入参并按装载成功交回。</summary>
        [Window(UILayer.Popup, false, "Registry/AttrPanel", cacheInstance: true)]
        internal sealed class RegistryProbeWindow : UGUIWindow
        {
            internal static string LastLocation;
            internal static bool LastFromResources;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources)
            {
                LastLocation = assetLocation;
                LastFromResources = fromResources;
                return true;
            }

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct)
            {
                return UniTask.FromResult(LoadPanel(assetLocation, fromResources));
            }

            protected internal override void ParkPanel()
            {
                // 缓存档探针不建面板：停放只记账，未绑定面板不得在即时关闭时抛 NRE
            }
        }

        /// <summary>没标 [Window] 的窗口类：注册表按未登记处理，不得静默兜默认开出。</summary>
        internal sealed class UnregisteredUGUIWindow : UGUIWindow
        {
        }

        #endregion
    }
}
