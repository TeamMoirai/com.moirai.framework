using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Moirai.Atropos.Tests.EditorMode.Service.Kernel
{
    /// <summary>
    /// EventBus 行为契约：订阅/发布/退订/发布期安全/作用域订阅/异常隔离。
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

        #region 作用域订阅 [SCOPED SUBSCRIPTIONS]

        [Test]
        public void SubscribeScoped_ClearedWhenScopeShutsDown()
        {
            var sceneReceived = 0;
            var appReceived = 0;
            EventDelegate<Ping> sceneHandler = (in Ping _) => sceneReceived++;
            EventDelegate<Ping> appHandler = (in Ping _) => appReceived++;

            EventBus.SubscribeScoped<Ping>(EServiceScopeKind.Scene, sceneHandler);
            EventBus.Subscribe<Ping>(appHandler);

            EventBus.Publish(new Ping());
            Assert.AreEqual(1, sceneReceived, "作用域关停前订阅照常派发");
            Assert.AreEqual(1, appReceived);

            // 隔离世界关停 Scene：台账清空即使该世界从未创建 Scene 作用域（进程级台账，与通道同口径）
            using (var world = new ServiceWorld())
            {
                world.ShutdownScope(EServiceScopeKind.Scene);
            }

            EventBus.Publish(new Ping());
            Assert.AreEqual(1, sceneReceived, "作用域关停后登记订阅被批量退订");
            Assert.AreEqual(2, appReceived, "未登记订阅不受作用域关停影响");
        }

        [Test]
        public void SubscribeScoped_ManualUnsubscribeBeforeClear_IsIdempotent()
        {
            var received = 0;
            EventDelegate<Ping> handler = (in Ping _) => received++;

            EventBus.SubscribeScoped<Ping>(EServiceScopeKind.Gameplay, handler);
            EventBus.Unsubscribe(handler);

            using (var world = new ServiceWorld())
            {
                world.ShutdownScope(EServiceScopeKind.Gameplay);
                world.ShutdownScope(EServiceScopeKind.Gameplay); // 幂等：重复关停不得抛
            }

            EventBus.Publish(new Ping());
            Assert.AreEqual(0, received, "手动退订后清台账不重复触发");
        }

        [Test]
        public void SubscribeScoped_WorldDisposeClearsAllKinds()
        {
            var gameplayReceived = 0;
            var sceneReceived = 0;
            EventDelegate<Ping> gameplayHandler = (in Ping _) => gameplayReceived++;
            EventDelegate<Ping> sceneHandler = (in Ping _) => sceneReceived++;

            EventBus.SubscribeScoped<Ping>(EServiceScopeKind.Gameplay, gameplayHandler);
            EventBus.SubscribeScoped<Ping>(EServiceScopeKind.Scene, sceneHandler);

            using (var world = new ServiceWorld())
            {
                world.Dispose();
            }

            EventBus.Publish(new Ping());
            Assert.AreEqual(0, gameplayReceived, "世界整体销毁清空全部作用域台账");
            Assert.AreEqual(0, sceneReceived, "世界整体销毁清空全部作用域台账");
        }

        #endregion

        #region 编辑器诊断钩子（参数快照 / 重播）[EDITOR DIAGNOSTICS]

        [Test]
        public void Diagnostics_OnPublished_CarriesValueSnapshot_OncePerPublish()
        {
            var types = new List<Type>();
            var values = new List<int>();
            Action<Type, object> hook = (type, snapshot) =>
            {
                types.Add(type);
                values.Add(((Ping)snapshot).Value);
            };

            EventBusDiagnostics.OnPublished += hook;
            try
            {
                // 两个订阅者——钩子每次发布只触发一次，不随订阅者数放大
                EventBus.Subscribe<Ping>((in Ping _) => { });
                EventBus.Subscribe<Ping>((in Ping _) => { });
                EventBus.Publish(new Ping { Value = 42 });

                Assert.AreEqual(1, types.Count, "每次发布仅触发一次诊断钩子");
                Assert.AreEqual(typeof(Ping), types[0]);
                Assert.AreEqual(42, values[0], "钩子携带事件值快照 (struct 装箱副本)");
            }
            finally
            {
                EventBusDiagnostics.OnPublished -= hook;
            }
        }

        [Test]
        public void Diagnostics_ReflectedPublish_ReplaysSnapshotThroughInParameter()
        {
            var received = new List<int>();
            EventBus.Subscribe<Ping>((in Ping evt) => received.Add(evt.Value));

            object snapshot = new Ping { Value = 77 };

            // 复刻 EventBusDebuggerWindow.PublishSnapshot 的反射闭合流程
            System.Reflection.MethodInfo publish = null;
            foreach (var m in typeof(EventBus).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (m.Name != "Publish" || !m.IsGenericMethodDefinition) continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType.IsByRef && ps[0].Name == "evt")
                {
                    publish = m;
                    break;
                }
            }

            Assert.IsNotNull(publish, "应找到 Publish<TEvent>(in TEvent, EDispatchMode) 重载");
            var closed = publish.MakeGenericMethod(typeof(Ping));
            closed.Invoke(null, new object[] { snapshot, EDispatchMode.Immediate });

            Assert.AreEqual(new[] { 77 }, received.ToArray(),
                "反射闭合泛型 + in 参数传入装箱快照可走真实发布路径重放");
        }

        #endregion
    }
}
