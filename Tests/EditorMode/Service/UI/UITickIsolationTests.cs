using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.UI
{
    /// <summary>N24a：一窗/一控件的 OnUpdate 抛出时，整帧其余窗口与控件照常结算（隔离并继续，手工 try/catch 零分配）。</summary>
    [TestFixture]
    public sealed class UITickIsolationTests
    {
        private UIWindowLedger _ledger;

        [SetUp]
        public void SetUp() => _ledger = new UIWindowLedger();

        [TearDown]
        public void TearDown() => _ledger = null;

        [Test]
        public void Tick_WindowThrows_RestOfStackStillTicks()
        {
            var throwing = NewWindow("TickThrowing");
            throwing.ThrowsOnUpdate = true;
            var counting = NewWindow("TickCounting");

            _ledger.Push(throwing);
            _ledger.Push(counting);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("TickThrowing"));
            Assert.DoesNotThrow(() => _ledger.Tick(), "一窗 OnUpdate 抛不得炸掉整帧结算");
            Assert.AreEqual(1, counting.UpdateTicks, "抛窗之后的窗照常结算本帧");
        }

        [Test]
        public void Tick_WidgetThrows_SiblingWidgetsAndOwnerOnUpdateStillRun()
        {
            var owner = NewWindow("TickOwner");
            var throwingWidget = new TickProbeWidget { Throws = true };
            var countingWidget = new TickProbeWidget();
            throwingWidget.MarkReady();
            countingWidget.MarkReady();
            owner.ChildListWritable.Add(throwingWidget);
            owner.ChildListWritable.Add(countingWidget);

            _ledger.Push(owner);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("OnUpdate"));
            Assert.DoesNotThrow(() => _ledger.Tick(), "一控件抛不得炸掉本窗与其余控件");
            Assert.AreEqual(1, countingWidget.Ticks, "抛控件之后的控件照常结算");
            Assert.AreEqual(1, owner.UpdateTicks, "本窗 OnUpdate 照常结算");
        }

        private static TickProbeWindow NewWindow(string name)
        {
            var window = new TickProbeWindow();
            window.Init(name, (int)EUILayer.UI, false, "Panel", true, 10);
            window.MarkReady();
            return window;
        }

        private sealed class TickProbeWindow : UGUIWindow
        {
            public int UpdateTicks;
            public bool ThrowsOnUpdate;

            protected override void OnUpdate()
            {
                if (ThrowsOnUpdate) throw new System.InvalidOperationException("tick probe");
                UpdateTicks++;
            }

            internal void MarkReady()
            {
                IsPrepare = true;
                Visible = true;
            }
        }

        private sealed class TickProbeWidget : UIWidget
        {
            public int Ticks;
            public bool Throws;

            protected override void OnUpdate()
            {
                if (Throws) throw new System.InvalidOperationException("widget tick probe");
                Ticks++;
            }

            internal void MarkReady() => IsPrepare = true;
        }
    }
}
