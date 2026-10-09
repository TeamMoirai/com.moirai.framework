using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.UI
{
    internal sealed class CountingTransition : IUITransition
    {
        public int Plays;
        public int Snaps;
        public bool LastSnapOpen;
        public bool ThrowOnPlay;
        public bool ThrowOnSnap;

        public UniTask Play(bool open, CancellationToken ct)
        {
            Plays++;
            if (ThrowOnPlay) throw new System.InvalidOperationException("play probe");
            return UniTask.CompletedTask;
        }

        public void Snap(bool open)
        {
            Snaps++;
            LastSnapOpen = open;
            if (ThrowOnSnap) throw new System.InvalidOperationException("snap probe");
        }
    }

    [Window(EUILayer.Tips, cacheInstance:true)]
    internal class SnapProbeWindow : UGUIWindow
    {
        public readonly CountingTransition TransitionProbe = new CountingTransition();
        public int ParkCount;

        protected internal override IUITransition Transition => TransitionProbe;
        protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;
        protected internal override void ParkPanel() => ParkCount++;
    }

    /// <summary>N23：缓存停放前补 Snap(false)；Play 抛退 Snap，Snap 抛照常走完。</summary>
    [TestFixture]
    public sealed class UITransitionSnapTests
    {
        private UIWindowLedger _ledger;

        [SetUp]
        public void SetUp() => _ledger = new UIWindowLedger();

        [TearDown]
        public void TearDown() => _ledger = null;

        [Test]
        public void CacheParkViaDestroy_SnapsClosedExactlyOnceBeforePark()
        {
            _ledger.ShowUIImp(typeof(SnapProbeWindow), false, "SnapA", "Panel", false, null);
            var window = (SnapProbeWindow)_ledger.GetWindow("SnapA");
            int snapsBefore = window.TransitionProbe.Snaps;

            window.InternalDestroy(); // 非关停 + 缓存窗：走缓存停放分支

            Assert.AreEqual(snapsBefore + 1, window.TransitionProbe.Snaps, "停放前补一次 Snap");
            Assert.IsFalse(window.TransitionProbe.LastSnapOpen, "补的是关窗终态");
            Assert.AreEqual(1, window.ParkCount, "Snap 之后才停放");
        }

        [Test]
        public void PlayThrowsOnClose_FallsBackToSnapAndStillParks()
        {
            _ledger.ShowUIImp(typeof(SnapProbeWindow), false, "SnapB", "Panel", false, null);
            var window = (SnapProbeWindow)_ledger.GetWindow("SnapB");
            window.TransitionProbe.ThrowOnPlay = true;
            int snapsBefore = window.TransitionProbe.Snaps;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("过渡"));
            _ledger.CloseUI<SnapProbeWindow>("SnapB");

            Assert.AreEqual(snapsBefore + 1, window.TransitionProbe.Snaps, "Play 抛退 Snap");
            Assert.IsFalse(window.TransitionProbe.LastSnapOpen, "退的是关窗终态");
            Assert.IsTrue(_ledger.IsParked("SnapB"), "关闭流程走完照常停放");
        }

        [Test]
        public void SnapAlsoThrowsOnClose_FlowStillCompletes()
        {
            _ledger.ShowUIImp(typeof(SnapProbeWindow), false, "SnapC", "Panel", false, null);
            var window = (SnapProbeWindow)_ledger.GetWindow("SnapC");
            window.TransitionProbe.ThrowOnPlay = true;
            window.TransitionProbe.ThrowOnSnap = true;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("过渡|Snap"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Snap"));
            Assert.DoesNotThrow(() => _ledger.CloseUI<SnapProbeWindow>("SnapC"));
            Assert.IsTrue(_ledger.IsParked("SnapC"), "Snap 抛不挡停放");
        }
    }
}
