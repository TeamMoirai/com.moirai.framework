using Moirai.Atropos.Events;
using NUnit.Framework;
using UnityEngine;

namespace Core.Events
{
    /// <summary>
    /// <see cref="MonoEventCoordinator"/> 销毁排空验收：OnDestroy 必须释放仍滞留派发队列的事件，
    /// 配平入队时的 <c>Acquire()</c>，否则池化事件净泄漏 + 残留态。
    /// </summary>
    public sealed class MonoEventCoordinatorDrainTests
    {
        #region 测试替身 [DOUBLES]

        /// <summary>仅用于测试的最小事件类型。</summary>
        public sealed class ProbeEvent : EventBase<ProbeEvent>
        {
            /// <summary>从事件池取出一个实例。</summary>
            public static ProbeEvent Take() => GetPooled();
        }

        /// <summary>暴露 protected 生命周期的测试协调器（EditMode 下 AddComponent 不跑 Awake/OnDestroy）。</summary>
        private sealed class TestCoordinator : MonoEventCoordinator
        {
            public override CallbackEventHandler GetCallbackEventHandler() => null;

            internal void AwakeForTest() => Awake();
            internal void DestroyForTest() => OnDestroy();
        }

        #endregion

        [Test]
        public void OnDestroy_DrainsQueuedEvents_QueueAcquireIsBalanced()
        {
            var host = new GameObject("DrainCoordinator");
            TestCoordinator coordinator = host.AddComponent<TestCoordinator>();
            coordinator.AwakeForTest();

            ProbeEvent evt = ProbeEvent.Take();
            Assert.AreEqual(1, evt.m_RefCount, "前置条件：持有者取池即一次引用");
            coordinator.Dispatch(evt, DispatchMode.Default, MonoDispatchType.FixedUpdate);
            Assert.AreEqual(2, evt.m_RefCount, "前置条件：入队应追加一次引用");

            coordinator.DestroyForTest();

            Assert.AreEqual(1, evt.m_RefCount, "OnDestroy 必须排空队列，配平入队时的 Acquire");
            evt.Dispose();
            Assert.AreEqual(0, evt.m_RefCount);

            Object.DestroyImmediate(host);
        }
    }
}
