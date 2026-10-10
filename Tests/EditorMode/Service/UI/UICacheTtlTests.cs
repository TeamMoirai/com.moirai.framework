using Moirai.Atropos.Timer;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;

namespace Service.UI
{
    [Window(EUILayer.Tips, cacheTimeToDestroy: 5f)]
    internal class TtlProbeWindow : UGUIWindow
    {
        public int DestroyPanelCount;

        // 复用支路的 SetActive 与停放钩子要摸真面板：自带一个真实物体（同 EventProbeWindow 口径），面板钩子只记账
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

    [Window(EUILayer.Tips, cacheTimeToDestroy: -1f)]
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

    [Window(EUILayer.Tips)]
    internal class NoCacheProbeWindow : UGUIWindow
    {
        // 停放档缺省（0）那一档要在真面板上走销毁支路：钩子只记账，物体由用例收尾时拆
        private GameObject _panel;

        public int DestroyPanelCount;
        public int ParkPanelCount;

        public override GameObject gameObject => _panel;

        protected internal override bool LoadPanel(string assetLocation, bool fromResources)
        {
            _panel = new GameObject(nameof(NoCacheProbeWindow));
            return true;
        }

        protected internal override void DestroyPanel() => DestroyPanelCount++;
        protected internal override void ParkPanel() => ParkPanelCount++;
    }

    /// <summary>停放档三态：负数=永久停放不起计时、正数=停放启计时且取用取消、0=不缓存关闭即销毁、到期由账本移出并终态销毁。</summary>
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
            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlAttr", null, UIPayload.Empty);
            var window = (TtlProbeWindow)_ledger.GetWindow("TtlAttr");
            Assert.AreEqual(5f, window.CacheTimeToDestroy, "特性档经描述符落到窗口");
        }

        [Test]
        public void ParkWithTtl_StartsTimer_UnparkCancels()
        {
            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlPark", null, UIPayload.Empty);
            var window = (TtlProbeWindow)_ledger.GetWindow("TtlPark");

            _ledger.CloseUI<TtlProbeWindow>("TtlPark");
            Assert.IsTrue(_ledger.IsParked("TtlPark"), "已停放");
            Assert.AreNotEqual(0UL, window.CacheTimerId, "停放即启计时");

            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlPark", null, UIPayload.Empty);
            Assert.AreEqual(0UL, window.CacheTimerId, "取用即取消计时");
        }

        [Test]
        public void NegativeTtl_StaysForever_NoTimer()
        {
            _ledger.ShowUIImp(typeof(ForeverCacheProbeWindow), false, "TtlForever", null, UIPayload.Empty);
            var window = (ForeverCacheProbeWindow)_ledger.GetWindow("TtlForever");

            _ledger.CloseUI<ForeverCacheProbeWindow>("TtlForever");
            Assert.AreEqual(0UL, window.CacheTimerId, "负数=永久停放不起计时");
            Assert.IsTrue(_ledger.IsParked("TtlForever"), "永久停放照旧落进停放表");
        }

        [Test]
        public void ZeroTtl_CloseIsDestroy_NotParked()
        {
            _ledger.ShowUIImp(typeof(NoCacheProbeWindow), false, "TtlNone", null, UIPayload.Empty);
            var window = (NoCacheProbeWindow)_ledger.GetWindow("TtlNone");

            _ledger.CloseUI<NoCacheProbeWindow>("TtlNone");

            Assert.IsFalse(_ledger.IsParked("TtlNone"), "0 档不落停放表");
            Assert.AreEqual(1, window.DestroyPanelCount, "0 档关闭即走销毁钩子");
            Assert.AreEqual(0, window.ParkPanelCount, "0 档不走停放钩子");
        }

        [Test]
        public void ExpireParkedWindow_RemovesFromCacheAndDestroys()
        {
            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlExpire", null, UIPayload.Empty);
            var window = (TtlProbeWindow)_ledger.GetWindow("TtlExpire");
            _ledger.CloseUI<TtlProbeWindow>("TtlExpire");

            _ledger.ExpireParkedWindow(window);

            Assert.IsFalse(_ledger.IsParked("TtlExpire"), "到期移出停放表");
            Assert.AreEqual(1, window.DestroyPanelCount, "到期终态销毁面板");
        }

        [Test]
        public void StaleExpire_AfterUnpark_IsNoOp()
        {
            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlStale", null, UIPayload.Empty);
            var window = (TtlProbeWindow)_ledger.GetWindow("TtlStale");
            _ledger.CloseUI<TtlProbeWindow>("TtlStale");
            _ledger.ShowUIImp(typeof(TtlProbeWindow), false, "TtlStale", null, UIPayload.Empty); // 取用

            _ledger.ExpireParkedWindow(window); // 迟到的到期

            Assert.IsTrue(_ledger.IsContains("TtlStale"), "取用后的窗不受迟到到期影响");
            Assert.AreEqual(0, window.DestroyPanelCount, "迟到到期不落刀");
        }
    }
}
