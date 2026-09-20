using System;
using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;

namespace Core.PlayerLoop
{
    /// <summary>
    /// <see cref="PlayerLoopDriver"/> 的架构验收测试：帧时钟同帧采样、延迟缓冲按阶段隔离、
    /// 订阅方抛异常不卡死注册、优先级插入、Gizmos / Pause 静态表转发、注销时机。
    /// <para>经 <see cref="GameTime.Handler"/> 注入虚拟时钟，手动调用 <c>Drive*</c> 推进；
    /// 不走 <see cref="PlayerLoopDriver.Initialize"/>，以免改写编辑器全局 PlayerLoop。</para>
    /// </summary>
    public class PlayerLoopDriverTests
    {
        #region 测试替身 [DOUBLES]

        private sealed class VirtualClock : GameTimeHandler
        {
            public float Delta;
            public float UnscaledDelta;
            public float FixedDelta;

            public override double ScaledNow => 0d;
            public override double UnscaledNow => 0d;
            public override float DeltaTime => Delta;
            public override float UnscaledDeltaTime => UnscaledDelta;
            public override float FixedDeltaTime => FixedDelta;
        }

        /// <summary>仅 Update 阶段——避开 Register 的三重载二义性。</summary>
        private class Probe : IUpdateHandler
        {
            private readonly List<string> _order;
            private readonly string _name;

            public int UpdateCalls;
            public float LastDelta;
            public Action DuringUpdate;
            public Exception ThrowOnUpdate;

            public Probe(string name, List<string> order = null)
            {
                _name = name;
                _order = order;
            }

            public void Update(float deltaTime, float unscaledDeltaTime)
            {
                UpdateCalls++;
                LastDelta = deltaTime;
                _order?.Add(_name);
                if (ThrowOnUpdate != null) throw ThrowOnUpdate;
                DuringUpdate?.Invoke();
            }
        }

        private sealed class PriorityProbe : Probe, IPlayerLoopPriority
        {
            public PriorityProbe(string name, List<string> order, int priority) : base(name, order)
            {
                Priority = priority;
            }

            public int Priority { get; }
        }

        /// <summary>三阶段全实现，用于 RegisterAll 与"单阶段注册不得被升级"两项。</summary>
        private sealed class AllStagesProbe : IUpdateHandler, IFixedUpdateHandler, ILateUpdateHandler
        {
            public int UpdateCalls;
            public int FixedCalls;
            public int LateCalls;

            public void Update(float deltaTime, float unscaledDeltaTime) => UpdateCalls++;
            public void FixedUpdate(float fixedDeltaTime, float unscaledDeltaTime) => FixedCalls++;
            public void LateUpdate(float deltaTime, float unscaledDeltaTime) => LateCalls++;
        }

        private sealed class CallbackProbe
        {
            public int Calls;
            public Action Other;
            public bool RemoveOtherNextCall;

            public void Run()
            {
                Calls++;
                if (!RemoveOtherNextCall) return;
                RemoveOtherNextCall = false;
                PlayerLoopDriver.RemoveUpdateCallback(Other);
            }
        }

        #endregion

        private VirtualClock _clock;
        private GameTimeHandler _originalClock;

        [SetUp]
        public void SetUp()
        {
            _originalClock = GameTime.Handler;
            _clock = new VirtualClock();
            GameTime.Handler = _clock;
            PlayerLoopDriver.ResetForTests(true);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerLoopDriver.ResetForTests(false);
            GameTime.Handler = _originalClock ?? new DefaultGameTimeHandler();
        }

        #region 帧时钟 [FRAME CLOCK]

        [Test]
        public void DriveUpdate_SamplesFrameClockBeforeHandlers()
        {
            // 回归：采样曾排在回调循环之后，接口 Handler 读到的是上一帧的 delta
            _clock.Delta = 0.25f;
            _clock.UnscaledDelta = 0.5f;

            var probe = new Probe("h");
            PlayerLoopDriver.Register(probe);
            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(1, probe.UpdateCalls);
            Assert.AreEqual(0.25f, probe.LastDelta, float.Epsilon);
        }

        [Test]
        public void DriveUpdate_WhenShutdown_DoesNothing()
        {
            PlayerLoopDriver.ResetForTests(false);
            var probe = new Probe("h");
            PlayerLoopDriver.Register(probe);

            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(0, probe.UpdateCalls);
        }

        #endregion

        #region 延迟缓冲按阶段隔离 [PER-STAGE PENDING]

        [Test]
        public void AddLateCallbackDuringUpdate_LandsInLateStageOnly()
        {
            // 回归：共享的 pending 缓冲不带阶段信息，提交时会把 Late 回调兜底成 Update
            int seenLate = 0;
            var trigger = new Probe("trigger");
            trigger.DuringUpdate = () => PlayerLoopDriver.AddLateUpdateCallback(() => seenLate++);

            PlayerLoopDriver.Register(trigger);
            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(0, seenLate, "Update 阶段不得触发 Late 回调");
            Assert.AreEqual(1, PlayerLoopDriver.LateUpdateCallbackCount);
            Assert.AreEqual(0, PlayerLoopDriver.UpdateCallbackCount);

            PlayerLoopDriver.DriveLateUpdate();
            Assert.AreEqual(1, seenLate);

            PlayerLoopDriver.DriveUpdate();
            Assert.AreEqual(0, PlayerLoopDriver.UpdateCallbackCount, "Late 回调不得回流到 Update");
            Assert.AreEqual(2, trigger.UpdateCalls);
        }

        [Test]
        public void RegisterLateInterfaceDuringUpdate_DoesNotUpgradeToOtherStages()
        {
            // 回归：pending 只存 object，提交时按 is 重查全部阶段 → 单阶段注册被升级成三阶段
            var multi = new AllStagesProbe();
            var trigger = new Probe("trigger");
            trigger.DuringUpdate = () => PlayerLoopDriver.Register((ILateUpdateHandler)multi);

            PlayerLoopDriver.Register(trigger);
            PlayerLoopDriver.DriveUpdate();

            // Update 槽里只有触发器自己；multi 未被"升级"进其它阶段
            Assert.AreEqual(1, PlayerLoopDriver.LateUpdateHandlerCount);
            Assert.AreEqual(1, PlayerLoopDriver.UpdateHandlerCount);
            Assert.AreEqual(0, PlayerLoopDriver.FixedUpdateHandlerCount);

            PlayerLoopDriver.DriveUpdate();
            Assert.AreEqual(2, trigger.UpdateCalls);
            Assert.AreEqual(0, multi.UpdateCalls, "Update 槽里没有它");
            Assert.AreEqual(0, multi.FixedCalls);
        }

        [Test]
        public void RemoveDuringUpdate_AppliesAfterThisFramesIteration()
        {
            var first = new CallbackProbe();
            var second = new CallbackProbe();
            first.Other = second.Run;
            first.RemoveOtherNextCall = true;

            PlayerLoopDriver.AddUpdateCallback(first.Run);
            PlayerLoopDriver.AddUpdateCallback(second.Run);

            PlayerLoopDriver.DriveUpdate();
            Assert.AreEqual(1, second.Calls, "注销在本阶段迭代结束后提交，被注销者当帧仍被调用");
            Assert.AreEqual(1, PlayerLoopDriver.UpdateCallbackCount, "帧末已提交该注销");

            PlayerLoopDriver.DriveUpdate();
            Assert.AreEqual(2, first.Calls);
            Assert.AreEqual(1, second.Calls, "下一帧起不再调用");
            Assert.AreEqual(1, PlayerLoopDriver.UpdateCallbackCount);
        }

        #endregion

        #region 异常安全 [EXCEPTION SAFETY]

        [Test]
        public void HandlerThrows_RegistrationStaysSynchronous()
        {
            // 回归：缺少 finally 时 s_IsDriving 永久为 true，之后的注册全滞留缓冲且当帧不提交
            var bomb = new Probe("bomb") { ThrowOnUpdate = new InvalidOperationException("boom") };
            PlayerLoopDriver.Register(bomb);
            Assert.Throws<InvalidOperationException>(() => PlayerLoopDriver.DriveUpdate());

            var after = new Probe("after");
            PlayerLoopDriver.Register(after);
            // bomb 仍在槽内；关键是 after 立刻计入而非滞留缓冲
            Assert.AreEqual(2, PlayerLoopDriver.UpdateHandlerCount, "抛异常后注册必须同步生效");

            // 摘掉炸弹才能看到 after 真被驱动：Handler 按注册序调用，bomb 在前会再次抛出并截断本阶段
            PlayerLoopDriver.Unregister(bomb);
            PlayerLoopDriver.DriveUpdate();
            Assert.AreEqual(1, after.UpdateCalls);
        }

        #endregion

        #region 优先级与注册序 [PRIORITY]

        [Test]
        public void PriorityHandler_InsertsBeforeRegistrationOrder()
        {
            var order = new List<string>();
            PlayerLoopDriver.Register(new Probe("normal", order));
            PlayerLoopDriver.Register(new PriorityProbe("early", order, -5));

            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual("early", order[0]);
            Assert.AreEqual("normal", order[1]);
        }

        [Test]
        public void NormalHandler_AfterPositivePriority_StillRunsFirst()
        {
            // 未实现 IPlayerLoopPriority 者有效优先级为 0：即便注册更晚，也必须排在正优先级者之前。
            // 旧实现把非优先级对象无条件尾部追加，此处正是它翻车的方向。
            var order = new List<string>();
            PlayerLoopDriver.Register(new PriorityProbe("late", order, 5));
            PlayerLoopDriver.Register(new Probe("normal", order));

            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(new[] { "normal", "late" }, order.ToArray(), "数字小者先跑，与注册先后无关");
        }

        [Test]
        public void SamePriority_KeepsRegistrationOrder()
        {
            var order = new List<string>();
            PlayerLoopDriver.Register(new PriorityProbe("a", order, 1));
            PlayerLoopDriver.Register(new PriorityProbe("b", order, 1));
            PlayerLoopDriver.Register(new PriorityProbe("c", order, 1));

            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(new[] { "a", "b", "c" }, order.ToArray(), "插入排序必须稳定");
        }

        [Test]
        public void Register_DuplicateIsIgnored()
        {
            var probe = new Probe("h");
            PlayerLoopDriver.Register(probe);
            PlayerLoopDriver.Register(probe);

            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(1, PlayerLoopDriver.UpdateHandlerCount);
            Assert.AreEqual(1, probe.UpdateCalls);
        }

        [Test]
        public void RegisterAll_CoversEachImplementedStage()
        {
            var probe = new AllStagesProbe();
            PlayerLoopDriver.RegisterAll(probe);
            Assert.AreEqual(1, PlayerLoopDriver.UpdateHandlerCount);
            Assert.AreEqual(1, PlayerLoopDriver.FixedUpdateHandlerCount);
            Assert.AreEqual(1, PlayerLoopDriver.LateUpdateHandlerCount);

            PlayerLoopDriver.UnregisterAll(probe);
            Assert.AreEqual(0, PlayerLoopDriver.UpdateHandlerCount);
            Assert.AreEqual(0, PlayerLoopDriver.FixedUpdateHandlerCount);
            Assert.AreEqual(0, PlayerLoopDriver.LateUpdateHandlerCount);
        }

        #endregion

        #region Unity 事件静态表 [UNITY EVENT TABLES]

        [Test]
        public void DrawGizmosCallbacks_ForwardedAndRemovable()
        {
            int calls = 0;
            Action handler = () => calls++;
            PlayerLoopDriver.AddDrawGizmosCallback(handler);

            PlayerLoopDriver.RaiseDrawGizmos();
            Assert.AreEqual(1, calls);

            PlayerLoopDriver.RemoveDrawGizmosCallback(handler);
            PlayerLoopDriver.RaiseDrawGizmos();
            Assert.AreEqual(1, calls, "注销后不再收到转发");
        }

        [Test]
        public void DrawGizmosSelected_IsIndependentOfDrawGizmos()
        {
            int plain = 0;
            int selected = 0;
            PlayerLoopDriver.AddDrawGizmosCallback(() => plain++);
            PlayerLoopDriver.AddDrawGizmosSelectedCallback(() => selected++);

            PlayerLoopDriver.RaiseDrawGizmosSelected();

            Assert.AreEqual(0, plain);
            Assert.AreEqual(1, selected);
        }

        [Test]
        public void ApplicationPause_ForwardsStatus()
        {
            bool? seen = null;
            Action<bool> handler = paused => seen = paused;
            PlayerLoopDriver.AddApplicationPauseCallback(handler);

            PlayerLoopDriver.RaiseApplicationPause(true);
            Assert.IsTrue(seen.GetValueOrDefault());

            PlayerLoopDriver.RemoveApplicationPauseCallback(handler);
            seen = null;
            PlayerLoopDriver.RaiseApplicationPause(false);
            Assert.IsNull(seen);
        }

        [Test]
        public void ClearHandlers_DropsEveryStageButKeepsDriverActive()
        {
            PlayerLoopDriver.Register(new Probe("h"));
            PlayerLoopDriver.AddUpdateCallback(() => { });
            PlayerLoopDriver.AddDrawGizmosCallback(() => { });

            PlayerLoopDriver.ClearHandlers();

            Assert.AreEqual(0, PlayerLoopDriver.UpdateHandlerCount);
            Assert.AreEqual(0, PlayerLoopDriver.UpdateCallbackCount);
            Assert.IsFalse(PlayerLoopDriver.IsShutdown, "清注册表不应改变驱动活跃位");
        }

        #endregion

        #region 线程契约 [THREAD AFFINITY]

        [Test]
        public void Register_FromBackgroundThread_FailsFast()
        {
            // 判据依赖 s_MainThreadId：SubsystemRegistration 钩子通常已捕获；万一为 0（顺序未定）
            // 就反射补上，避免用例被"未捕获即放行"的分支静默跳过。
            var field = typeof(PlayerLoopDriver).GetField("s_MainThreadId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(field, "s_MainThreadId 已更名，请同步本用例");
            if ((int)field.GetValue(null) == 0)
            {
                field.SetValue(null, System.Threading.Thread.CurrentThread.ManagedThreadId);
            }

            Exception caught = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    PlayerLoopDriver.Register(new Probe("off-thread"));
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            });
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            Assert.IsNotNull(caught,
                "后台线程注册必须 fail-fast：注册表是裸数组 + 无锁计数，越线程写入只会静默丢订阅");
            Assert.AreEqual(0, PlayerLoopDriver.UpdateHandlerCount, "被拒绝的注册不应留下痕迹");
        }

        #endregion
    }
}
