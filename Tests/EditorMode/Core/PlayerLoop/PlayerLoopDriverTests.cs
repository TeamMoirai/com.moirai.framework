using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
            public Exception ThrowOnRun;

            public void Run()
            {
                Calls++;
                if (ThrowOnRun != null) throw ThrowOnRun;
                if (!RemoveOtherNextCall) return;
                RemoveOtherNextCall = false;
                PlayerLoopDriver.RemoveUpdateCallback(Other);
            }
        }

        #endregion

        private VirtualClock _clock;
        private GameTimeHandler _originalClock;
        private int _originalTripThreshold;

        [SetUp]
        public void SetUp()
        {
            _originalClock = GameTime.Handler;
            _clock = new VirtualClock();
            GameTime.Handler = _clock;
            // 熔断阈值是驱动器级静态配置，不随 ResetForTests 复位——用例改它必须自行还原
            _originalTripThreshold = PlayerLoopDriver.FailureTripThreshold;
            PlayerLoopDriver.ResetForTests(true);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerLoopDriver.FailureTripThreshold = _originalTripThreshold;
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

        [Test]
        public void PendingAddThenRemove_OnInactiveHandler_LeavesUnregistered()
        {
            // 回归：旧实现先移除后添加地提交缓冲，同帧「注册再注销」被错误落为已注册
            var probe = new Probe("h");
            var trigger = new Probe("trigger");
            trigger.DuringUpdate = () =>
            {
                PlayerLoopDriver.Register(probe);
                PlayerLoopDriver.Unregister(probe);
            };

            PlayerLoopDriver.Register(trigger);
            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(1, PlayerLoopDriver.UpdateHandlerCount, "后调用者生效：只剩 trigger，probe 不得注册");
        }

        [Test]
        public void PendingRemoveThenAdd_OnActiveHandler_KeepsRegistration()
        {
            var probe = new Probe("h");
            var trigger = new Probe("trigger");
            trigger.DuringUpdate = () =>
            {
                PlayerLoopDriver.Unregister(probe);
                PlayerLoopDriver.Register(probe);
            };

            PlayerLoopDriver.Register(trigger);
            PlayerLoopDriver.Register(probe);
            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(2, PlayerLoopDriver.UpdateHandlerCount, "后调用者生效：注销再注册应保持注册");
            PlayerLoopDriver.DriveUpdate();
            Assert.AreEqual(2, probe.UpdateCalls, "两帧均被驱动：注册从未失效");
        }

        [Test]
        public void PendingAddThenRemove_OnActiveHandler_Removes()
        {
            // 已激活者同帧「注册再注销」：冗余注册被对消后仍须把注销提交进去
            var probe = new Probe("h");
            var trigger = new Probe("trigger");
            trigger.DuringUpdate = () =>
            {
                PlayerLoopDriver.Register(probe);
                PlayerLoopDriver.Unregister(probe);
            };

            PlayerLoopDriver.Register(trigger);
            PlayerLoopDriver.Register(probe);
            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(1, PlayerLoopDriver.UpdateHandlerCount, "后调用者生效：probe 应被注销");
        }

        [Test]
        public void PendingAddThenRemove_OnInactiveCallback_LeavesUnregistered()
        {
            int calls = 0;
            System.Action cb = () => calls++;
            var triggerAction = new System.Action(() =>
            {
                PlayerLoopDriver.AddUpdateCallback(cb);
                PlayerLoopDriver.RemoveUpdateCallback(cb);
            });

            PlayerLoopDriver.AddUpdateCallback(triggerAction);
            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(1, PlayerLoopDriver.UpdateCallbackCount, "后调用者生效：只剩 triggerAction");
            Assert.AreEqual(0, calls, "cb 不得被驱动");
        }

        #endregion

        #region 异常安全 [EXCEPTION SAFETY]

        [Test]
        public void HandlerThrows_RegistrationStaysSynchronous()
        {
            // 回归：缺少 finally 时 s_IsDriving 永久为 true，之后的注册全滞留缓冲且当帧不提交
            var bomb = new Probe("bomb") { ThrowOnUpdate = new InvalidOperationException("boom") };
            PlayerLoopDriver.Register(bomb);
            LogAssert.Expect(LogType.Error, new Regex("handler threw"));
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

        [Test]
        public void UpdateHandler_ConsecutiveFailures_TripsAfterThreshold_StopsDriving()
        {
            // 回归：驱动器层没有隔离时，一个故障订户每帧截断整阶段且永不退出
            PlayerLoopDriver.FailureTripThreshold = 3;

            var bomb = new Probe("bomb") { ThrowOnUpdate = new InvalidOperationException("boom") };
            var healthy = new Probe("healthy");
            PlayerLoopDriver.Register(bomb);
            PlayerLoopDriver.Register(healthy);

            for (int frame = 1; frame <= 3; frame++)
            {
                // 开发构建按分级策略记录后上抛；第 3 次失败先熔断摘除再抛
                LogAssert.Expect(LogType.Error, new Regex("handler threw"));
                if (frame == 3) LogAssert.Expect(LogType.Warning, new Regex("was removed after"));
                Assert.Throws<InvalidOperationException>(() => PlayerLoopDriver.DriveUpdate());
            }

            Assert.AreEqual(1, PlayerLoopDriver.UpdateHandlerCount, "只摘故障者，健康者必须留下");
            Assert.DoesNotThrow(() => PlayerLoopDriver.DriveUpdate());
            Assert.AreEqual(3, bomb.UpdateCalls, "熔断后不应有第 4 次尝试");
            Assert.AreEqual(1, healthy.UpdateCalls, "健康者应在故障者被摘除后的那一帧恢复驱动");
        }

        [Test]
        public void UpdateCallback_ConsecutiveFailures_TripsAfterThreshold_StopsDriving()
        {
            PlayerLoopDriver.FailureTripThreshold = 2;

            var bomb = new CallbackProbe { ThrowOnRun = new InvalidOperationException("boom") };
            var healthy = new CallbackProbe();
            PlayerLoopDriver.AddUpdateCallback(bomb.Run);
            PlayerLoopDriver.AddUpdateCallback(healthy.Run);

            for (int frame = 1; frame <= 2; frame++)
            {
                LogAssert.Expect(LogType.Error, new Regex("callback threw"));
                if (frame == 2) LogAssert.Expect(LogType.Warning, new Regex("was removed after"));
                Assert.Throws<InvalidOperationException>(() => PlayerLoopDriver.DriveUpdate());
            }

            Assert.AreEqual(1, PlayerLoopDriver.UpdateCallbackCount);
            PlayerLoopDriver.DriveUpdate();
            Assert.AreEqual(2, bomb.Calls, "熔断后不再被调用");
            Assert.AreEqual(1, healthy.Calls, "健康者应在熔断后恢复");
        }

        [Test]
        public void HandlerSuccessBetweenFailures_ResetsConsecutiveCount()
        {
            // 熔断判据是「连续」失败：间歇性故障每帧仍被驱动，不能被累计成熔断
            PlayerLoopDriver.FailureTripThreshold = 3;
            var bomb = new Probe("bomb") { ThrowOnUpdate = new InvalidOperationException("boom") };
            PlayerLoopDriver.Register(bomb);

            for (int frame = 1; frame <= 2; frame++)
            {
                LogAssert.Expect(LogType.Error, new Regex("handler threw"));
                Assert.Throws<InvalidOperationException>(() => PlayerLoopDriver.DriveUpdate());
            }

            bomb.ThrowOnUpdate = null;
            PlayerLoopDriver.DriveUpdate();

            bomb.ThrowOnUpdate = new InvalidOperationException("boom");
            for (int frame = 1; frame <= 2; frame++)
            {
                LogAssert.Expect(LogType.Error, new Regex("handler threw"));
                Assert.Throws<InvalidOperationException>(() => PlayerLoopDriver.DriveUpdate());
            }

            Assert.AreEqual(1, PlayerLoopDriver.UpdateHandlerCount, "成功一次即归零，2+2 次失败不应触发阈值 3");
        }

        [Test]
        public void CoreUpdateCallback_RunsBeforeUserCallbacks()
        {
            // 回归：服务层心跳曾挤在用户回调表里，与该表的注册序绑定，可能被项目订户排到后面
            var order = new List<string>();
            PlayerLoopDriver.AddUpdateCallback(() => order.Add("user"));
            PlayerLoopDriver.SetCoreUpdateCallback(() => order.Add("core"));

            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(new[] { "core", "user" }, order.ToArray());
        }

        [Test]
        public void CoreUpdateCallback_IsNeverTripped()
        {
            // 核心钩子若参与熔断，项目订户的连抛会把整层服务心跳永久摘除且无恢复路径
            PlayerLoopDriver.FailureTripThreshold = 2;
            int calls = 0;
            PlayerLoopDriver.SetCoreUpdateCallback(() =>
            {
                calls++;
                throw new InvalidOperationException("core boom");
            });

            for (int frame = 1; frame <= 5; frame++)
            {
                LogAssert.Expect(LogType.Error, new Regex("core hook threw"));
                Assert.Throws<InvalidOperationException>(() => PlayerLoopDriver.DriveUpdate());
            }

            Assert.AreEqual(5, calls);
        }

        [Test]
        public void ApplicationQuitBroadcast_WhenOneThrows_OthersStillRun()
        {
            // 关闭广播的职责就是清理，截断等于静默漏掉后续每一项的释放动作，故开发期也不上抛
            LogAssert.Expect(LogType.Error, new Regex("ApplicationQuit callback threw"));

            var order = new List<string>();
            PlayerLoopDriver.AddApplicationQuitCallback(() =>
            {
                order.Add("first");
                throw new InvalidOperationException("boom");
            });
            PlayerLoopDriver.AddApplicationQuitCallback(() => order.Add("second"));

            Assert.DoesNotThrow(() => PlayerLoopDriver.RaiseApplicationQuit());

            Assert.AreEqual(new[] { "first", "second" }, order.ToArray());
        }

        [Test]
        public void ApplicationPauseBroadcast_WhenOneThrows_OthersStillRun()
        {
            // 回归：Pause 多播曾直发不隔离，前序订户抛异常会静默截断后续订户（切后台存档链）
            LogAssert.Expect(LogType.Error, new Regex("ApplicationPause callback threw"));

            var order = new List<string>();
            PlayerLoopDriver.AddApplicationPauseCallback(_ =>
            {
                order.Add("first");
                throw new InvalidOperationException("boom");
            });
            PlayerLoopDriver.AddApplicationPauseCallback(_ => order.Add("second"));

            Assert.DoesNotThrow(() => PlayerLoopDriver.RaiseApplicationPause(true));

            Assert.AreEqual(new[] { "first", "second" }, order.ToArray());
        }

        [Test]
        public void ReRegisteredHandler_FailureCountStartsFresh()
        {
            // 回归：失败计数曾不随注销清除——注销重注册后首次失败即被误熔断
            PlayerLoopDriver.FailureTripThreshold = 2;

            var bomb = new Probe("bomb") { ThrowOnUpdate = new InvalidOperationException("boom") };
            PlayerLoopDriver.Register(bomb);

            LogAssert.Expect(LogType.Error, new Regex("handler threw"));
            Assert.Throws<InvalidOperationException>(() => PlayerLoopDriver.DriveUpdate());

            PlayerLoopDriver.Unregister(bomb);
            PlayerLoopDriver.Register(bomb);

            LogAssert.Expect(LogType.Error, new Regex("handler threw"));
            Assert.Throws<InvalidOperationException>(() => PlayerLoopDriver.DriveUpdate());

            Assert.AreEqual(1, PlayerLoopDriver.UpdateHandlerCount, "重注册后计数从 0 计起，首次失败不应熔断");
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
            int coreCalls = 0;
            PlayerLoopDriver.Register(new Probe("h"));
            PlayerLoopDriver.AddUpdateCallback(() => { });
            PlayerLoopDriver.AddDrawGizmosCallback(() => { });
            PlayerLoopDriver.SetCoreUpdateCallback(() => coreCalls++);

            PlayerLoopDriver.ClearHandlers();

            Assert.AreEqual(0, PlayerLoopDriver.UpdateHandlerCount);
            Assert.AreEqual(0, PlayerLoopDriver.UpdateCallbackCount);
            Assert.IsFalse(PlayerLoopDriver.IsShutdown, "清注册表不应改变驱动活跃位");

            PlayerLoopDriver.DriveUpdate();
            Assert.AreEqual(0, coreCalls, "核心钩子属注册表的一部分，应一并清空（否则跨会话留残钩子）");
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

        #region 公开门面 [PUBLIC FACADE]

        [Test]
        public void GameAppAddFrameHandler_DrivesEveryImplementedStage()
        {
            var probe = new AllStagesProbe();
            GameApp.AddFrameHandler(probe);

            Assert.AreEqual(1, PlayerLoopDriver.UpdateHandlerCount);
            Assert.AreEqual(1, PlayerLoopDriver.FixedUpdateHandlerCount);
            Assert.AreEqual(1, PlayerLoopDriver.LateUpdateHandlerCount);

            PlayerLoopDriver.DriveUpdate();
            PlayerLoopDriver.DriveFixedUpdate();
            PlayerLoopDriver.DriveLateUpdate();

            Assert.AreEqual(1, probe.UpdateCalls);
            Assert.AreEqual(1, probe.FixedCalls);
            Assert.AreEqual(1, probe.LateCalls);

            GameApp.RemoveFrameHandler(probe);
            Assert.AreEqual(0, PlayerLoopDriver.UpdateHandlerCount);
            Assert.AreEqual(0, PlayerLoopDriver.FixedUpdateHandlerCount);
            Assert.AreEqual(0, PlayerLoopDriver.LateUpdateHandlerCount);
        }

        [Test]
        public void GameAppPerStageHandler_TakesMultiStageObjectWithoutCast()
        {
            // 门面按参数类型各自唯一，多阶段对象登记单阶段不必像驱动的同名 Register 三重载那样显式转型
            var multi = new AllStagesProbe();
            GameApp.AddLateUpdateHandler(multi);

            Assert.AreEqual(1, PlayerLoopDriver.LateUpdateHandlerCount);
            Assert.AreEqual(0, PlayerLoopDriver.UpdateHandlerCount, "单阶段注册不得被升级进其它阶段");
            Assert.AreEqual(0, PlayerLoopDriver.FixedUpdateHandlerCount);

            PlayerLoopDriver.DriveLateUpdate();
            Assert.AreEqual(1, multi.LateCalls);
            Assert.AreEqual(0, multi.UpdateCalls);

            GameApp.RemoveLateUpdateHandler(multi);
            Assert.AreEqual(0, PlayerLoopDriver.LateUpdateHandlerCount);
        }

        [Test]
        public void GameAppHandler_ParticipatesInPriorityOrdering()
        {
            // 门面转发到的就是同一张注册表，优先级排序必须一致生效
            var order = new List<string>();
            GameApp.AddUpdateHandler(new PriorityProbe("late", order, 5));
            GameApp.AddUpdateHandler(new Probe("normal", order));

            PlayerLoopDriver.DriveUpdate();

            Assert.AreEqual(new[] { "normal", "late" }, order.ToArray());
        }

        #endregion

        #region 启动相位 [BOOT PHASING]

        // 刻意用显式名次表而非 enum 底层整数：断言依赖的是「执行先后」这一语义，
        // 而 Unity 未承诺 RuntimeInitializeLoadType 的数值单调递增。
        private static readonly RuntimeInitializeLoadType[] PHASE_ORDER =
        {
            RuntimeInitializeLoadType.SubsystemRegistration,
            RuntimeInitializeLoadType.AfterAssembliesLoaded,
            RuntimeInitializeLoadType.BeforeSceneLoad,
            RuntimeInitializeLoadType.AfterSceneLoad,
        };

        private static int PhaseRank(RuntimeInitializeLoadType phase)
        {
            int rank = Array.IndexOf(PHASE_ORDER, phase);
            Assert.AreNotEqual(-1, rank, $"未登记的名次相位 {phase}，请同步 PHASE_ORDER");
            return rank;
        }

        private static RuntimeInitializeLoadType LoadPhase(Type owner, string methodName)
        {
            MethodInfo method = owner.GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(method, $"{owner.Name}.{methodName} 已更名或被移除，请同步本用例");

            var attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            Assert.IsNotNull(attribute, $"{owner.Name}.{methodName} 应挂 [RuntimeInitializeOnLoadMethod]");
            return attribute.loadType;
        }

        [Test]
        public void SelfHealCheck_RunsStrictlyAfterInjectionPhase()
        {
            // 回归：VerifyInjection 曾挂 AfterAssembliesLoaded，而注入发生在 Initiation 的
            // BeforeSceneLoad。早于注入点时 s_Injected 恒为 false、首行即 return，
            // 自愈校验一次也没执行过——第三方在 BeforeSceneLoad 重建 PlayerLoop 导致的
            // 标记丢失无人补插，整框架静默不 Tick，且这个缺陷无法从运行期日志看出。
            RuntimeInitializeLoadType injection = LoadPhase(typeof(GameAppSettings), "Initiation");
            RuntimeInitializeLoadType selfHeal = LoadPhase(typeof(PlayerLoopInjector), "VerifyInjection");

            Assert.Greater(PhaseRank(selfHeal), PhaseRank(injection),
                "自愈校验必须严格晚于注入点，否则它是死代码");
        }

        #endregion
    }
}
