using System;
using Moirai.Atropos.Input;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 开窗路径的托管分配探针：同步开窗与复用开窗两条链的分配基线，供后续优化 A/B 对照。
    /// </summary>
    /// <remarks>
    /// 计量用 <see cref="GC.GetAllocatedBytesForCurrentThread"/> 差值（线程精确，EditMode 可用）。 <br />
    /// 预热数轮排除 JIT 与首次字典扩容后计量；阈值是回归护栏而非性能承诺，翻倍即红。 <br />
    /// 异步装载与跨帧段不在本夹具射程（EditMode 不推帧），PlayMode 另测。
    /// </remarks>
    [TestFixture]
    public sealed class UIOpenAllocationProbeTests
    {
        private bool _savedPreventInteraction;
        private UIServiceHandler[] _savedEnabledHandlers;

        /// <summary>进门归位后装一支 uGUI 驱动者并走生产初始化：探针走生产开窗链。</summary>
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

        /// <summary>同步开窗（全新实例）的分配基线：单次上限 4KB，翻倍即红。</summary>
        [Test]
        public void SyncOpen_FreshWindow_AllocationWithinBaseline()
        {
            WarmupFresh();

            var before = GC.GetAllocatedBytesForCurrentThread();
            UIService.ShowUI<AllocProbeWindow>("Measured");
            var after = GC.GetAllocatedBytesForCurrentThread();

            var perOpen = after - before;
            TestContext.Out.WriteLine($"sync open allocation: {perOpen} B");
            Assert.LessOrEqual(perOpen, 4096, "同步开窗分配超过基线：链上引入了新分配，先定位再放阈值");
        }

        /// <summary>复用开窗（栈上同名窗重开：Pop+Push+准备回执）的分配基线：百次均值单次上限 1KB。</summary>
        [Test]
        public void RepeatOpen_StackReuse_AllocationWithinBaseline()
        {
            UIService.ShowUI<AllocProbeWindow>("Reused");
            for (int i = 0; i < 5; i++)
            {
                UIService.ShowUI<AllocProbeWindow>("Reused");
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                UIService.ShowUI<AllocProbeWindow>("Reused");
            }
            var after = GC.GetAllocatedBytesForCurrentThread();

            var perOpen = (after - before) / 100;
            TestContext.Out.WriteLine($"repeat open allocation: {perOpen} B/次");
            Assert.LessOrEqual(perOpen, 1024, "复用开窗分配超过基线：链上引入了新分配，先定位再放阈值");
        }

        /// <summary>预热：先开再关若干次，排除 JIT 与首次集合扩容对计量的污染。</summary>
        private static void WarmupFresh()
        {
            for (int i = 0; i < 5; i++)
            {
                UIService.ShowUI<AllocProbeWindow>("Warm" + i);
            }

            UIService.CloseAll(false);
        }

        /// <summary>装载成功的分配探针窗：面板钩子只按成功记账，不建物体。</summary>
        [Window(EUILayer.Tips)]
        internal sealed class AllocProbeWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;

            protected internal override void ApplyVisible(bool value) { }

            protected internal override void ApplyDepth(int value) { }

            protected internal override void ApplyInteractable(bool value) { }

            protected internal override void ParkPanel() { }
        }
    }
}
