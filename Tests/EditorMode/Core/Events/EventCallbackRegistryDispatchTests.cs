using System;
using Moirai.Atropos.Events;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Core.Events
{
    /// <summary>
    /// <see cref="EventCallbackRegistry"/> 的派发健壮性验收：
    /// 回调抛异常之后注册表必须仍然可用，回调列表拷贝构造必须带上阶段计数。
    /// <para>回归目标：<c>m_IsInvoking</c> 未放在 finally 时，一次抛异常会让派发深度永久为正，
    /// 此后所有注册/注销只写进 <c>m_TemporaryCallbacks</c>，而派发仍读 <c>m_Callbacks</c>，
    /// 事件系统整体静默失效；拷贝构造漏设阶段计数会让冒泡/下探路径静默丢祖先。</para>
    /// </summary>
    public class EventCallbackRegistryDispatchTests
    {
        #region 测试替身 [DOUBLES]

        /// <summary>仅用于测试的最小事件类型。</summary>
        public sealed class ProbeEvent : EventBase<ProbeEvent>
        {
            /// <summary>从事件池取出一个实例。</summary>
            /// <returns>可用于派发的测试事件。</returns>
            public static ProbeEvent Take() => GetPooled();
        }

        #endregion

        [Test]
        public void InvokeCallbacks_AfterThrowingCallback_RegistryStillDispatches()
        {
            var registry = new EventCallbackRegistry();
            int firstHits = 0;
            EventCallback<ProbeEvent> first = _ => firstHits++;
            registry.RegisterCallback(first);

            using (var evt = ProbeEvent.Take())
            {
                registry.InvokeCallbacks(evt, PropagationPhase.AtTarget);
            }

            Assert.AreEqual(1, firstHits, "前置条件：正常派发应命中回调");

            // 开发构建下回调异常会 Error 后上抛，发布构建下就地隔离——两种策略都不允许留下泄漏的派发深度，
            // 因此这里只要求"抛过异常的注册表还能继续工作"，不对上抛与否作断言。
            LogAssert.ignoreFailingMessages = true;
            try
            {
                registry.RegisterCallback<ProbeEvent>(_ => throw new Exception("probe callback failure"));
                using var throwing = ProbeEvent.Take();
                try
                {
                    registry.InvokeCallbacks(throwing, PropagationPhase.AtTarget);
                }
                catch (Exception)
                {
                    // 开发构建：按分级策略上抛，此处按预期吞掉
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            int laterHits = 0;
            EventCallback<ProbeEvent> later = _ => laterHits++;
            registry.RegisterCallback(later);

            using (var evt = ProbeEvent.Take())
            {
                registry.InvokeCallbacks(evt, PropagationPhase.AtTarget);
            }

            Assert.AreEqual(1, laterHits, "抛过异常之后新注册的回调仍必须被派发");

            registry.UnregisterCallback(later);

            using (var evt = ProbeEvent.Take())
            {
                registry.InvokeCallbacks(evt, PropagationPhase.AtTarget);
            }

            Assert.AreEqual(1, laterHits, "注销之后的回调不应再被派发");
        }

        [Test]
        public void EventCallbackList_CopyConstructor_PreservesPhaseCounts()
        {
            var source = new EventCallbackList();
            source.Add(new EventCallbackFunctor<ProbeEvent>(_ => { }, CallbackPhase.TrickleDownAndTarget));
            source.Add(new EventCallbackFunctor<ProbeEvent>(_ => { }, CallbackPhase.TargetAndBubbleUp));

            var copy = new EventCallbackList(source);

            Assert.AreEqual(1, copy.TrickleDownCallbackCount, "拷贝构造必须带上源列表的下探阶段计数");
            Assert.AreEqual(1, copy.BubbleUpCallbackCount, "拷贝构造必须带上源列表的冒泡阶段计数");
            Assert.AreEqual(source.Count, copy.Count);
        }
    }
}
