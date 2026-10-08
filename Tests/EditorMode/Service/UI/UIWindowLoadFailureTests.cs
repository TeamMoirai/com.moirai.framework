using Cysharp.Threading.Tasks;
using Moirai.Atropos.Input;
using Moirai.Atropos.Tests.EditorMode;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 装载失败路径的回滚与结果契约：失败窗从栈上收口、作废位可观察、等待腿按 <see cref="UIOpenResult"/> 分档。
    /// </summary>
    /// <remarks>
    /// 装载失败的窗口必须从栈上回滚、不得进停放表；Shown/Closed 的配对由 PlayMode 的 <c>UIOpenResultContractTests</c> 判 <br />
    /// （<c>EventManager</c> 是播放期单例，EditMode 下 <c>RegisterCallback</c> 不可用）。 <br />
    /// 取消贯通的判据是探针窗记录到的装载令牌：装载在途时关闭窗口，令牌必须翻成已取消。 <br />
    /// 本文件只钉同步可落定的档；跨帧等待与超时档同样住在 PlayMode。
    /// </remarks>
    [TestFixture]
    public sealed class UIWindowLoadFailureTests
    {
        private bool _savedPreventInteraction;
        private UIServiceHandler[] _savedEnabledHandlers;

        /// <summary>进门归位后装一支 uGUI 驱动者并走生产初始化：回滚收口要过 <see cref="UIService.IsValid"/> 那道守卫。</summary>
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

        /// <summary>同步装载失败：窗口当场回滚出栈、置作废位、不进停放表。</summary>
        [Test]
        public void LoadFailure_SyncShowUI_RollsWindowOffStackAndMarksItFailed()
        {
            UtfLogExpect.Error();

            UIService.ShowUI<FailingLoadUGUIWindow>("FailSync");

            Assert.AreEqual(0, UIService.SharedLedger.PeekStack().Count, "装载失败的窗口必须从栈上回滚，不得永占栈位");
            Assert.IsFalse(UIService.SharedLedger.IsParked("FailSync"), "装载失败的窗口不得进停放表");
            Assert.IsTrue(FailingLoadUGUIWindow.Last.IsLoadFailed, "失败位要置上：等待腿据此分档");
            Assert.IsTrue(FailingLoadUGUIWindow.Last.IsDestroyed, "作废位要置上：实例不再可复用");
            Assert.AreEqual(nameof(FailingLoadUGUIWindow), FailingLoadUGUIWindow.LastLocation,
                "装载钩子拿到的是按解析链回落到类型名的面板地址（特性没写 location、调用方也没给地址）");
        }

        /// <summary>异步装载失败（同步落定的失败值）：与同步档同一条回滚链，同帧收口。</summary>
        [Test]
        public void LoadFailure_AsyncShowUI_RollsWindowOffStackTheSameWay()
        {
            UtfLogExpect.Error();

            UIService.ShowUIAsync<FailingLoadUGUIWindow>("FailAsync");

            Assert.AreEqual(0, UIService.SharedLedger.PeekStack().Count, "异步装载失败同样当场回滚出栈");
            Assert.IsFalse(UIService.SharedLedger.IsParked("FailAsync"), "失败窗不进停放表");
            Assert.IsTrue(FailingLoadUGUIWindow.Last.IsLoadFailed, "失败位同样置上");
        }

        /// <summary>装载在途关闭窗口：装载令牌被掐断，迟到的装载失败收口不再改作废位。</summary>
        [Test]
        public void LoadCancel_CloseDuringInFlightLoad_CancelsTokenAndSkipsLateAbort()
        {
            UIService.ShowUIAsync<NeverLoadUGUIWindow>("NeverFail");

            var window = UIService.SharedLedger.GetWindow<NeverLoadUGUIWindow>("NeverFail");
            Assert.IsNotNull(window, "装载在途的窗口在栈上");
            Assert.IsTrue(NeverLoadUGUIWindow.LastToken.CanBeCanceled, "装载收到的是窗口级取消令牌，不是 None 档");

            UIService.CloseUI<NeverLoadUGUIWindow>("NeverFail");

            Assert.AreEqual(0, UIService.SharedLedger.PeekStack().Count, "关闭把在途装载的窗口摘出栈");
            Assert.IsTrue(NeverLoadUGUIWindow.LastToken.IsCancellationRequested, "装载在途关闭必须掐断装载取消源");

            // 迟到的装载失败收口：窗口已被显式关闭，收口不得再翻失败位
            UIService.SharedLedger.RollbackFailedLoad(window);

            Assert.IsFalse(window.IsLoadFailed, "已由 CloseUI 收口的窗不再翻失败位");
        }

        /// <summary>结果腿的失败档：装载失败的窗口按 <see cref="EUIOpenStatus.Failed"/> 交回，隐式布尔为假。</summary>
        [Test]
        public void ShowUIAwaitResult_FailedWindow_ReturnsFailedStatusWithRolledBackWindow()
        {
            UtfLogExpect.Error();

            var result = UIService.ShowUIAwaitResult<FailingLoadUGUIWindow>("FailResult").GetAwaiter().GetResult();

            Assert.AreEqual(EUIOpenStatus.Failed, result.Status, "装载失败按 Failed 档交回");
            Assert.IsFalse(result.Success, "失败档不是就绪成功");
            Assert.IsFalse(result, "隐式布尔在失败档为假");
            Assert.AreSame(FailingLoadUGUIWindow.Last, result.Window, "失败档交回那只已作废的窗供诊断");
            Assert.AreEqual(0, UIService.SharedLedger.PeekStack().Count, "失败窗已回滚，不在栈上");
        }

        /// <summary>结果腿的就绪档：装载成功的窗口按 <see cref="EUIOpenStatus.Opened"/> 交回并留在栈上。</summary>
        [Test]
        public void ShowUIAwaitResult_LoadedWindow_ReturnsOpenedStatus()
        {
            var result = UIService.ShowUIAwaitResult<AcceptLoadUGUIWindow>("OkResult").GetAwaiter().GetResult();

            Assert.AreEqual(EUIOpenStatus.Opened, result.Status, "装载成功按 Opened 档交回");
            Assert.IsTrue(result, "隐式布尔在就绪档为真");
            Assert.AreSame(AcceptLoadUGUIWindow.Last, result.Window, "交回的就是等出来的那一只");
            Assert.IsNotNull(UIService.SharedLedger.GetWindow<AcceptLoadUGUIWindow>("OkResult"), "就绪窗留在栈上");
        }

        /// <summary>取窗结果腿的缺失档：栈上没有这一名时交 <see cref="EUIOpenStatus.Missing"/>，不带窗口。</summary>
        [Test]
        public void GetUIAwaitResult_MissingWindow_ReturnsMissingStatusWithoutWindow()
        {
            var result = UIService.GetUIAwaitResult<AcceptLoadUGUIWindow>().GetAwaiter().GetResult();

            Assert.AreEqual(EUIOpenStatus.Missing, result.Status, "栈上没有这一名按 Missing 档交回");
            Assert.IsNull(result.Window, "缺失档不带窗口");
            Assert.IsFalse(result, "隐式布尔在缺失档为假");
        }

        /// <summary>取窗结果腿的就绪档：按类型全名开出的窗等出 <see cref="EUIOpenStatus.Opened"/>。</summary>
        [Test]
        public void GetUIAwaitResult_OpenedWindow_ReturnsOpenedStatus()
        {
            UIService.ShowUI<AcceptLoadUGUIWindow>();

            var result = UIService.GetUIAwaitResult<AcceptLoadUGUIWindow>().GetAwaiter().GetResult();

            Assert.AreEqual(EUIOpenStatus.Opened, result.Status, "已就绪的窗按 Opened 档交回");
            Assert.IsNotNull(result.Window, "就绪档带着窗口交回");
            Assert.IsTrue(result, "隐式布尔在就绪档为真");
        }

        #region 探针 [PROBES]

        /// <summary>同步装载失败的探针窗：记录实例与装载地址，装载钩子一律回 false。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class FailingLoadUGUIWindow : UGUIWindow
        {
            internal static FailingLoadUGUIWindow Last;
            internal static string LastLocation;

            public FailingLoadUGUIWindow()
            {
                Last = this;
            }

            protected internal override bool LoadPanel(string assetLocation, bool fromResources)
            {
                LastLocation = assetLocation;
                return false;
            }

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct)
            {
                return UniTask.FromResult(LoadPanel(assetLocation, fromResources));
            }
        }

        /// <summary>装载永不落定的探针窗：记录装载收到的取消令牌，异步装载永不完成。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class NeverLoadUGUIWindow : UGUIWindow
        {
            internal static System.Threading.CancellationToken LastToken;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => false;

            protected internal override async UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct)
            {
                LastToken = ct;
                await UniTask.WaitUntil(() => false);
                return false;
            }
        }

        /// <summary>装载成功的探针窗：装载钩子一律回 true，不建面板物体。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class AcceptLoadUGUIWindow : UGUIWindow
        {
            internal static AcceptLoadUGUIWindow Last;

            public AcceptLoadUGUIWindow()
            {
                Last = this;
            }

            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources,
                System.Threading.CancellationToken ct)
            {
                return UniTask.FromResult(true);
            }
        }

        #endregion
    }
}
