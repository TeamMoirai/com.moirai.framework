using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Moirai.Atropos.Tests.EditorMode.Service.Kernel
{
    /// <summary>
    /// IEventBus 行为契约：订阅/发布/句柄退订/发布期安全/作用域批量退订/异常隔离。
    /// </summary>
    public sealed class EventBusTests
    {
        [SetUp]
        public void SetUp() => EventBus.ResetStaticsForPlayModeReset();

        public struct Ping : IGameEvent { public int Value; }
        public struct Noop : IGameEvent { }

        [Test]
        public void Publish_ReachesSubscriber_InOrder()
        {
            var received = new List<int>();
            EventBus.Subscribe<Ping>((in Ping evt) => received.Add(evt.Value));

            EventBus.Publish(new Ping { Value = 1 });
            EventBus.Publish(new Ping { Value = 2 });

            Assert.AreEqual(new[] { 1, 2 }, received.ToArray(), "订阅者按发布序收到全部事件");
        }

        [Test]
        public void Unsubscribe_IsIdempotent()
        {
            var count = 0;
            EventDelegate<Ping> handler = (in Ping _) => count++;
            EventBus.Subscribe<Ping>(handler);

            EventBus.Unsubscribe(handler);
            EventBus.Unsubscribe(handler);

            EventBus.Publish(new Ping());
            Assert.AreEqual(0, count, "退订幂等且生效");
        }

        [Test]
        public void Subscribe_DuringPublish_NotDispatchedForCurrentEvent()
        {
            var received = new List<int>();
            var secondSubscribed = false;

            EventBus.Subscribe<Ping>((in Ping evt) =>
            {
                received.Add(evt.Value);
                if (!secondSubscribed)
                {
                    secondSubscribed = true;
                    // 发布期内订阅：本次事件不再派发给它（契约语义）
                    EventBus.Subscribe<Ping>((in Ping e) => received.Add(100 + e.Value));
                }
            });

            EventBus.Publish(new Ping { Value = 1 });
            EventBus.Publish(new Ping { Value = 2 });

            Assert.AreEqual(new[] { 1, 2, 102 }, received.ToArray(),
                "发布期订阅的回调从下一次发布生效");
        }

        [Test]
        public void Unsubscribe_DuringPublish_DoesNotBreakDispatch()
        {
            var received = new List<int>();
            EventDelegate<Ping> unsubscriber = (in Ping _) => { };
            EventBus.Subscribe<Ping>(unsubscriber);
            EventBus.Subscribe<Ping>((in Ping evt) =>
            {
                received.Add(evt.Value);
                EventBus.Unsubscribe(unsubscriber);
            });

            Assert.DoesNotThrow(() => EventBus.Publish(new Ping { Value = 1 }),
                "发布期退订不得破坏派发循环");
            Assert.AreEqual(1, received.Count);
        }

        [Test]
        public void HandlerException_FollowsDispatchPolicy()
        {
            var received = 0;
            EventBus.Subscribe<Ping>((in Ping _) => throw new InvalidOperationException("故意异常"));
            EventBus.Subscribe<Ping>((in Ping _) => received++);

            // 异常分级与 EventDispatchPolicy 同口径：开发期（Editor/DevBuild）RETHROW 上抛快速失败；发布期逐回调隔离
            if (EventDispatchPolicy.RETHROW_DISPATCH_EXCEPTIONS)
            {
                Assert.Throws<InvalidOperationException>(() => EventBus.Publish(new Ping()),
                    "RETHROW 门控开：回调异常直接上抛（调试器断在抛点）");
            }
            else
            {
                UtfLogExpect.Exception();
                Assert.DoesNotThrow(() => EventBus.Publish(new Ping()),
                    "RETHROW 门控关：发布期逐回调隔离，不得中断其他订阅者");
                Assert.AreEqual(1, received, "异常回调之后的订阅者照常收到事件");
            }
        }

        [Test]
        public void Publish_WithoutSubscribers_IsNoop()
        {
            Assert.DoesNotThrow(() => EventBus.Publish(new Ping()));
        }
    }
}
