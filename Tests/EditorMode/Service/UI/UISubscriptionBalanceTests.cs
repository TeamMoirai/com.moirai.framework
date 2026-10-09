using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;

namespace Service.UI
{
    [Window(EUILayer.Tips, cacheInstance:true)]
    internal class EventProbeWindow : UGUIWindow
    {
        public int RegisterCount;
        public int UnregisterCount;
        public int DestroyPanelCount;

        // 复用支路的 SetActive 与停放钩子要摸真面板：自带一枚真实物体（同 HandlerProbeWindow 口径），面板钩子只记账
        private GameObject _panel;

        public override GameObject gameObject => _panel;

        protected internal override bool LoadPanel(string assetLocation, bool fromResources)
        {
            _panel = new GameObject(nameof(EventProbeWindow));
            return true;
        }

        protected override void RegisterEvent() => RegisterCount++;
        protected override void UnregisterEvent() => UnregisterCount++;
        protected internal override void DestroyPanel() => DestroyPanelCount++;
        protected internal override void ParkPanel() { }
    }

    /// <summary>N21：RegisterEvent/UnregisterEvent 配平（开注关退，停放窗不挂事件）；关停按轨连停放表一起扫。</summary>
    [TestFixture]
    public sealed class UISubscriptionBalanceTests
    {
        private UIWindowLedger _ledger;

        [SetUp]
        public void SetUp() => _ledger = new UIWindowLedger();

        [TearDown]
        public void TearDown() => _ledger = null;

        [Test]
        public void ParkAndReopen_RegisterPairsBalanced()
        {
            _ledger.ShowUIImp(typeof(EventProbeWindow), false, "EventProbe", "Panel", false, null, UIPayload.Empty);
            var window = (EventProbeWindow)_ledger.GetWindow("EventProbe");
            Assert.AreEqual((1, 0), (window.RegisterCount, window.UnregisterCount), "首开注一次");

            _ledger.CloseUI<EventProbeWindow>("EventProbe");
            Assert.AreEqual((1, 1), (window.RegisterCount, window.UnregisterCount), "关（停放）即退订");

            _ledger.ShowUIImp(typeof(EventProbeWindow), false, "EventProbe", "Panel", false, null, UIPayload.Empty);
            Assert.AreEqual((2, 1), (window.RegisterCount, window.UnregisterCount), "停放重取再注一次");

            _ledger.CloseUI<EventProbeWindow>("EventProbe");
            Assert.AreEqual((2, 2), (window.RegisterCount, window.UnregisterCount), "再关再退，配平");
        }

        [Test]
        public void ShutdownSweep_ParkedWindowsOnTrackAreDestroyed()
        {
            _ledger.ShowUIImp(typeof(EventProbeWindow), false, "SweepHit", "Panel", false, null, UIPayload.Empty);
            _ledger.ShowUIImp(typeof(EventProbeWindow), false, "SweepMiss", "Panel", false, null, UIPayload.Empty);
            _ledger.CloseUI<EventProbeWindow>("SweepHit");
            _ledger.CloseUI<EventProbeWindow>("SweepMiss");
            var hit = _ledger.GetWindow("SweepHit") as EventProbeWindow;
            // 停放窗已不在栈上，经 IsParked 取回引用做计数断言
            Assert.IsTrue(_ledger.IsParked("SweepHit") && _ledger.IsParked("SweepMiss"), "两枚都已停放");

            _ledger.CloseAllWhere(true, window => window.WindowName == "SweepHit");

            Assert.IsFalse(_ledger.IsParked("SweepHit"), "被轨认得的停放窗关停时销毁出表");
            Assert.IsTrue(_ledger.IsParked("SweepMiss"), "轨外停放窗原地不动");
        }
    }
}
