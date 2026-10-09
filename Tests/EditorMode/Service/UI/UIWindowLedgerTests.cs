using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;

namespace Service.UI
{
    /// <summary>
    /// 共享持有者 <see cref="UIWindowLedger"/> 的直读接缝：不经协调者门面，直接锁栈序、关隐与停放表的三条不变式。
    /// </summary>
    /// <remarks>
    /// Task 13A 把窗口栈与编排从 <see cref="UIServiceHandler"/> 的实例字段搬进本持有者；两支 handler（Task 13B）之后
    /// 指向同一份实例，这三格锁的就是那份实例自己的栈序与关隐语义——只要持有者的三条不变式仍在，两支 handler 无论
    /// 怎么分派，都落进同一份栈与同一份停放表。 <br />
    /// 载体只用一个最简的 uGUI 轨探针窗：面板钩子一律只回真、不改运行时状态，本文件的判据是栈与停放本身，与轨无关。 <br />
    /// 线程契约：仅主线程（EditMode 用例即主线程）。
    /// </remarks>
    [TestFixture]
    public sealed class UIWindowLedgerTests
    {
        private UIWindowLedger _ledger;

        [SetUp]
        public void SetUp()
        {
            _ledger = new UIWindowLedger();
        }

        [TearDown]
        public void TearDown()
        {
            _ledger = null;
        }

        /// <summary>
        /// Push 把同层后到者接在本层末位之后：持有者自己按层级找插入点，栈序与两支 handler 无关。
        /// </summary>
        [Test]
        public void Ledger_PushSameLayer_AppendsEachLaterWindowToItsOwnLayerTail()
        {
            var first = NewWindow("LedgerFirst", (int)EUILayer.UI);
            var second = NewWindow("LedgerSecond", (int)EUILayer.UI);

            _ledger.Push(first);
            _ledger.Push(second);

            Assert.AreEqual(2, _ledger.PeekStack().Count, "两次压入都应落在这同一份栈上");
            Assert.AreSame(first, _ledger.PeekStack()[0], "同层先来者在前");
            Assert.AreSame(second, _ledger.PeekStack()[1], "同层后到者紧接本层末位之后");
        }

        /// <summary>
        /// CloseAll 走完停放与销毁两支之后把栈一次清空：非零停放档落停放表、0 档直销毁、栈上不留残余。
        /// </summary>
        [Test]
        public void Ledger_CloseAll_EmptiesStackAndOnlyKeepsCachedInstancesInParkingTable()
        {
            var cached = NewWindow("LedgerCached", (int)EUILayer.UI, cacheTimeToDestroy: -1f);
            var plain = NewWindow("LedgerPlain", (int)EUILayer.UI);

            _ledger.Push(cached);
            _ledger.Push(plain);

            _ledger.CloseAll(isShutDown: false);

            Assert.AreEqual(0, _ledger.PeekStack().Count, "全关之后栈归零");
            Assert.IsTrue(_ledger.IsParked("LedgerCached"), "非零停放档那个落进停放表");
            Assert.IsFalse(_ledger.IsParked("LedgerPlain"), "0 档那个直接销毁，不进停放表");
        }

        /// <summary>
        /// ResetStorage 一次归零栈与停放表：门面的 OnInit 与 OnShutdown 各经这一道门复位那份共享持有者。
        /// </summary>
        [Test]
        public void Ledger_ResetStorage_ClearsStackAndParkingTableTogether()
        {
            var cached = NewWindow("LedgerReset", (int)EUILayer.UI, cacheTimeToDestroy: -1f);
            _ledger.Push(cached);
            _ledger.CloseUI(typeof(LedgerProbeWindow), "LedgerReset");

            Assert.AreEqual(0, _ledger.PeekStack().Count, "CloseUI 把这一名从栈上摘掉");
            Assert.IsTrue(_ledger.IsParked("LedgerReset"), "CloseUI 之后落进停放表");

            _ledger.ResetStorage();

            Assert.AreEqual(0, _ledger.PeekStack().Count, "Reset 后栈归零");
            Assert.IsFalse(_ledger.IsParked("LedgerReset"), "Reset 后停放表也归零");
        }

        private static LedgerProbeWindow NewWindow(string name, int layer, float cacheTimeToDestroy = 0f)
        {
            var window = new LedgerProbeWindow();
            window.Init(name, layer, false, "Panel", false, 10, cacheTimeToDestroy: cacheTimeToDestroy);
            return window;
        }

        /// <summary>
        /// 面板钩子只回真：装载同步成功、显隐与深度与拾取都不落地、停放与销毁都不动本体——栈与停放表的判据
        /// 只由持有者一侧提供，窗体的面板不参与。
        /// </summary>
        private sealed class LedgerProbeWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }

            protected internal override void DestroyPanel() { }
        }
    }
}
