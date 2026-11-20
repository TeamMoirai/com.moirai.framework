using System;
using Moirai.Atropos;
using Testing;
using NUnit.Framework;

namespace Moirai.Atropos.Tests.EditorMode.Service.Kernel
{
    /// <summary>
    /// 事件发布热路径的 0-Alloc 门禁：稳态发布/订阅/退订均不得触发托管分配。
    /// </summary>
    /// <remarks>
    /// 发布路径经快照缓存数组复用（容量足够时零分配，扩容与重入发布除外）， <br />
    /// 万次循环按累计字节断言，单次均摊超限即失败。
    /// </remarks>
    [TestFixture]
    public sealed class EventChannelAllocationTests
    {
        private static int s_Received;

        private struct Ping : IGameEvent
        {
            public int Value;
        }

        // 命名静态方法组：无闭包捕获，订阅本身零分配
        private static void OnPing(in Ping evt) => s_Received += evt.Value;

        [SetUp]
        public void SetUp()
        {
            EventBus.ResetStaticsForPlayModeReset();
            s_Received = 0;
        }

        [TearDown]
        public void TearDown() => EventBus.ResetStaticsForPlayModeReset();

        [Test]
        public void Publish_TenThousandTimes_StaysWithinBaseline()
        {
            EventDelegate<Ping> handler = OnPing;
            EventBus.Subscribe<Ping>(handler);

            // 预热：jit 与快照数组扩容收敛
            AllocationCapture.Warmup(() => EventBus.Publish(new Ping { Value = 1 }), iterations: 64);

            // 稳态发布走缓存快照复用：零分配（扩容与重入发布除外）
            AllocationCapture.AssertNoAlloc(() => EventBus.Publish(new Ping { Value = 1 }),
                maxBytes: 0, iterations: 10_000, context: "单订阅稳态发布×10000");

            EventBus.Unsubscribe(handler);
        }

        [Test]
        public void SubscribeUnsubscribe_TenThousandCycles_AllocatesZeroBytes()
        {
            AllocationCapture.Warmup(() =>
            {
                EventDelegate<Ping> handler = OnPing;
                EventBus.Subscribe<Ping>(handler);
                EventBus.Unsubscribe(handler);
            }, iterations: 64);

            AllocationCapture.AssertNoAlloc(() =>
            {
                EventDelegate<Ping> handler = OnPing;
                EventBus.Subscribe<Ping>(handler);
                EventBus.Unsubscribe(handler);
            }, maxBytes: 0, iterations: 10_000, context: "订阅退订万次循环");

            EventBus.ResetStaticsForPlayModeReset();
        }
    }
}
