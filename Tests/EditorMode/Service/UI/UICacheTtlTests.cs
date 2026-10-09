using Moirai.Atropos.Timer;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;

namespace Service.UI
{
    [Window(EUILayer.Tips, cacheInstance:true, cacheTimeToDestroy:5f)]
    internal class TtlProbeWindow : UGUIWindow
    {
        public int DestroyPanelCount;

        // 复用支路的 SetActive 与停放钩子要摸真面板：自带一枚真实物体（同 EventProbeWindow 口径），面板钩子只记账
        private GameObject _panel;

        public override GameObject gameObject => _panel;

        protected internal override bool LoadPanel(string assetLocation, bool fromResources)
        {
            _panel = new GameObject(nameof(TtlProbeWindow));
            return true;
        }

        protected internal override void DestroyPanel() => DestroyPanelCount++;
        protected internal override void ParkPanel() { }
    }

    [Window(EUILayer.Tips, cacheInstance:true)]
    internal class ForeverCacheProbeWindow : UGUIWindow
    {
        // 同 TtlProbeWindow 口径：停放与重取都要摸真面板
        private GameObject _panel;

        public override GameObject gameObject => _panel;

        protected internal override bool LoadPanel(string assetLocation, bool fromResources)
        {
            _panel = new GameObject(nameof(ForeverCacheProbeWindow));
            return true;
        }

        protected internal override void ParkPanel() { }
    }

    /// <summary>TTL：停放启计时、取用取消、0=永久回归、到期由账本移出并终态销毁。</summary>
    [TestFixture]
    public sealed class UICacheTtlTests
    {
        private UIWindowLedger _ledger;
        private TimerServiceHandler _timerHandler;
        private TimerServiceHandler _savedTimerHandler;

        // EditMode 不注册计时后端：静态门面的 Delay 恒回 0 句柄，换一支真空轮进去再原样归还，句柄才有真假可判
        [SetUp]
        public void SetUp()
        {
            _ledger = new UIWindowLedger();
            _timerHandler = new DefaultTimerHandler();
            _timerHandler.Internal_Init();
            _savedTimerHandler = TimerService.Internal_UseHandler(_timerHandler);
        }

        [TearDown]
        public void TearDown()
        {
            TimerService.Internal_UseHandler(_savedTimerHandler);
            _timerHandler.Internal_Shutdown();
            _timerHandler = null;
            _savedTimerHandler = null;
            _ledger = null;
        }

        [Test]
        public void DescriptorPlumbing_AttributeTtlReachesWindow()
        {
            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlAttr", "Panel", false, null);
            var window = (TtlProbeWindow)_ledger.GetWindow("TtlAttr");
            Assert.AreEqual(5f, window.CacheTimeToDestroy, "特性档经描述符落到窗口");
        }

        [Test]
        public void ParkWithTtl_StartsTimer_UnparkCancels()
        {
            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlPark", "Panel", false, null);
            var window = (TtlProbeWindow)_ledger.GetWindow("TtlPark");

            _ledger.CloseUI<TtlProbeWindow>("TtlPark");
            Assert.IsTrue(_ledger.IsParked("TtlPark"), "已停放");
            Assert.AreNotEqual(0UL, window.CacheTimerId, "停放即启计时");

            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlPark", "Panel", false, null);
            Assert.AreEqual(0UL, window.CacheTimerId, "取用即取消计时");
        }

        [Test]
        public void ZeroTtl_StaysForever_NoTimer()
        {
            _ledger.ShowUIImp(typeof(ForeverCacheProbeWindow), false, "TtlZero", "Panel", false, null);
            var window = (ForeverCacheProbeWindow)_ledger.GetWindow("TtlZero");

            _ledger.CloseUI<ForeverCacheProbeWindow>("TtlZero");
            Assert.AreEqual(0UL, window.CacheTimerId, "0=永久不起计时");
            Assert.IsTrue(_ledger.IsParked("TtlZero"), "永久停放回归现行语义");
        }

        [Test]
        public void ExpireParkedWindow_RemovesFromCacheAndDestroys()
        {
            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlExpire", "Panel", false, null);
            var window = (TtlProbeWindow)_ledger.GetWindow("TtlExpire");
            _ledger.CloseUI<TtlProbeWindow>("TtlExpire");

            _ledger.ExpireParkedWindow(window);

            Assert.IsFalse(_ledger.IsParked("TtlExpire"), "到期移出停放表");
            Assert.AreEqual(1, window.DestroyPanelCount, "到期终态销毁面板");
        }

        [Test]
        public void StaleExpire_AfterUnpark_IsNoOp()
        {
            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlStale", "Panel", false, null);
            var window = (TtlProbeWindow)_ledger.GetWindow("TtlStale");
            _ledger.CloseUI<TtlProbeWindow>("TtlStale");
            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlStale", "Panel", false, null); // 取用

            _ledger.ExpireParkedWindow(window); // 迟到的到期

            Assert.IsTrue(_ledger.IsContains("TtlStale"), "取用后的窗不受迟到到期影响");
            Assert.AreEqual(0, window.DestroyPanelCount, "迟到到期不落刀");
        }
    }
}
