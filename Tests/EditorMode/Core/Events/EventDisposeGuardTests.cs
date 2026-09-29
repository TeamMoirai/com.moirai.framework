using Moirai.Atropos.Events;
using NUnit.Framework;
using Expect = Moirai.Atropos.Tests.EditorMode.UtfLogExpect;

namespace Core.Events
{
    /// <summary>
    /// <see cref="EventBase{T}"/> 引用计数守卫验收：重复 Dispose 不得把计数打到负值后永不回池。
    /// </summary>
    /// <remarks>
    /// 判据：引用计数归零后再次 Dispose 会打到 -1，此后永不等于 0、实例永不回池，只靠下次复用时 <c>Init()</c> 的事后告警自愈——守卫必须前置短路。
    /// </remarks>
    public sealed class EventDisposeGuardTests
    {
        #region 测试替身 [DOUBLES]

        /// <summary>
        /// 仅用于测试的最小事件类型。
        /// </summary>
        public sealed class ProbeEvent : EventBase<ProbeEvent>
        {
            /// <summary>
            /// 从事件池取出一个实例。
            /// </summary>
            public static ProbeEvent Take() => GetPooled();
        }

        #endregion

        [Test]
        public void Dispose_MoreTimesThanAcquired_IsIgnored_InstanceStaysReusable()
        {
            ProbeEvent evt = ProbeEvent.Take();
            Assert.AreEqual(1, evt.m_RefCount, "前置条件：取池即持有一次引用");
            evt.Dispose();

            Expect.Warning();
            evt.Dispose();

            Assert.AreEqual(0, evt.m_RefCount, "重复 Dispose 必须被前置短路忽略，不得把计数打到 -1");

            ProbeEvent reused = ProbeEvent.Take();
            Assert.AreSame(evt, reused, "守卫生效后复用应取回同一实例且无 Init 告警");
            Assert.AreEqual(1, reused.m_RefCount);
            reused.Dispose();
        }
    }
}
