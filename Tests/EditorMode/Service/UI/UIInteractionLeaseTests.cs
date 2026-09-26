using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 模态动画交互压制的归属仲裁（<see cref="UIInteractionLease"/>）单元测试：
    /// 非模态窗口不参与归属、后到者接管、非持有者交还被拒（不得清掉别人的压制位）、
    /// 重复交还只认第一次、Reset 区分「丢弃了活归属」与「无归属可丢」。
    /// <para>纯逻辑测试，不依赖 Unity 场景与 UI 后端——全局压制位本身无持有者语义，
    /// 跨窗口拆锁的判定全部收敛在这里。</para>
    /// </summary>
    [TestFixture]
    public sealed class UIInteractionLeaseTests
    {
        private UIInteractionLease _lease;
        private UIWindow _a;
        private UIWindow _b;

        [SetUp]
        public void SetUp()
        {
            _lease = new UIInteractionLease();
            _a = new LeaseTestWindow();
            _b = new LeaseTestWindow();
        }

        [Test]
        public void Acquire_NonModalWindow_IsRejected()
        {
            Assert.IsFalse(_lease.Acquire(_a, false), "非模态窗口不参与归属");
            Assert.IsFalse(_lease.Release(_a), "未获得归属的窗口不得清除压制位");
        }

        [Test]
        public void Acquire_NonModalWindow_DoesNotRevokeExistingHolder()
        {
            Assert.IsTrue(_lease.Acquire(_a, true));

            Assert.IsFalse(_lease.Acquire(_b, false), "非模态窗口的动画不得夺走模态持有者的归属");
            Assert.IsTrue(_lease.Release(_a), "归属仍属于模态窗口 A");
        }

        [Test]
        public void Acquire_LaterModalWindowTakesOwnership()
        {
            Assert.IsTrue(_lease.Acquire(_a, true));
            Assert.IsTrue(_lease.Acquire(_b, true));

            Assert.IsFalse(_lease.Release(_a), "A 已被 B 接管，不得清除 B 持有的压制");
            Assert.IsTrue(_lease.Release(_b), "B 是当前持有者");
        }

        [Test]
        public void Release_NonHolder_LeavesOwnershipIntact()
        {
            Assert.IsTrue(_lease.Acquire(_a, true));

            Assert.IsFalse(_lease.Release(_b));
            Assert.IsTrue(_lease.Release(_a), "越权交还不影响持有者后续正常交还");
        }

        [Test]
        public void Release_AfterHolderReleased_IsRejected()
        {
            Assert.IsTrue(_lease.Acquire(_a, true));
            Assert.IsTrue(_lease.Release(_a));

            Assert.IsFalse(_lease.Release(_a), "重复交还不成立，压制位归下一轮申请方所有");
        }

        [Test]
        public void Reset_WithLiveHolder_ReportsNeedToClearFlag()
        {
            Assert.IsTrue(_lease.Acquire(_a, true));

            Assert.IsTrue(_lease.Reset(), "丢弃仍持有的归属时调用方须同事务清掉全局压制位");
            Assert.IsFalse(_lease.Release(_a), "归属已清空");
        }

        [Test]
        public void Reset_WithoutHolder_ReportsNothingToClear()
        {
            Assert.IsTrue(_lease.Acquire(_a, true));
            Assert.IsTrue(_lease.Release(_a));

            Assert.IsFalse(_lease.Reset(), "无归属可丢弃时不得借机清除压制位");
        }

        /// <summary>
        /// 归属仲裁只做引用相等判定，不触碰窗口成员——空实例即可作为身份键。
        /// </summary>
        private sealed class LeaseTestWindow : UIWindow
        {
        }
    }
}
