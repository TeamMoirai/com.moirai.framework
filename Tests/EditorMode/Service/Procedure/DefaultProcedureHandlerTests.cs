using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Moirai.Atropos;
using Moirai.Atropos.Procedure;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Procedure
{
    /// <summary>
    /// 默认流程处理器（<see cref="DefaultProcedureHandler"/>）行为测试：
    /// 生命周期时序、切换语义、重入深度防护、运行中重初始化防护、切换历史与广播、重启链路、外观未就绪诊断。
    /// <para>处理器级用例直接构造（与 <c>DefaultTimerHandlerTests</c> 同约定）；
    /// 外观级用例经 <c>ProcedureService.Handler</c> 属性注入真实链路（setter 同步 Internal_Init，无需拉起服务世界）。</para>
    /// </summary>
    [TestFixture]
    public sealed class DefaultProcedureHandlerTests
    {
        #region 测试桩 [TEST PROBES]

        /// <summary>生命周期探针流程——记录回调次序，支持经钩子注入 OnEnter 内切换。</summary>
        private class ProbeProcedure : ProcedureBase
        {
            public readonly List<string> Log = new List<string>();
            public Action OnEnterHook;
            public Action OnLeaveHook;
            public Action OnDestroyHook;

            public void ForceChangeState<T>() where T : ProcedureBase => ChangeState<T>();

            protected internal override void OnInit() => Log.Add("Init");

            protected internal override void OnEnter()
            {
                Log.Add("Enter");
                OnEnterHook?.Invoke();
            }

            protected internal override void OnUpdate(float elapseSeconds, float realElapseSeconds) => Log.Add("Update");

            protected internal override void OnLeave(bool isShutdown)
            {
                Log.Add(isShutdown ? "Leave:Shutdown" : "Leave");
                OnLeaveHook?.Invoke();
            }

            protected internal override void OnDestroy()
            {
                Log.Add("Destroy");
                OnDestroyHook?.Invoke();
            }
        }

        private sealed class ProbeA : ProbeProcedure { }

        private sealed class ProbeB : ProbeProcedure { }

        /// <summary>未注册进状态机的探针，覆盖"切换到不存在流程"路径。</summary>
        private sealed class ProbeC : ProbeProcedure { }

        #endregion

        private DefaultProcedureHandler _handler;
        private ProbeA _a;
        private ProbeB _b;

        [SetUp]
        public void SetUp()
        {
            _handler = new DefaultProcedureHandler();
            _handler.Internal_Init();
            _a = new ProbeA();
            _b = new ProbeB();
        }

        [TearDown]
        public void TearDown()
        {
            _handler.Internal_Shutdown();
        }

        #region 初始化 [INITIALIZE]

        [Test]
        public void Initialize_RegistersAndInitsProcedures()
        {
            _handler.Initialize(_a, _b);

            Assert.AreEqual(2, _handler.Procedures.Count);
            CollectionAssert.AreEquivalent(new[] { "Init" }, _a.Log);
            Assert.IsNull(_handler.CurrentProcedure);
            Assert.AreEqual(0, _handler.TransitionHistory.Count);
        }

        [Test]
        public void Initialize_EmptyOrNullOrDuplicate_Throws()
        {
            Assert.Throws<GameException>(() => _handler.Initialize());
            Assert.Throws<GameException>(() => _handler.Initialize(null));
            Assert.Throws<GameException>(() => _handler.Initialize(_a, _a));
        }

        [Test]
        public void Initialize_WhileRunning_ThrowsAndKeepsState()
        {
            _handler.Initialize(_a, _b);
            _handler.StartProcedure(typeof(ProbeA));

            Assert.Throws<GameException>(() => _handler.Initialize(_a, _b));
            Assert.AreSame(_a, _handler.CurrentProcedure);
        }

        #endregion

        #region 启动与轮询 [START & TICK]

        [Test]
        public void Tick_BeforeStart_DoesNothing()
        {
            _handler.Initialize(_a, _b);

            Assert.DoesNotThrow(() => _handler.Tick(1f, 1f));
            Assert.AreEqual(0f, _handler.CurrentProcedureTime);
            Assert.IsEmpty(_a.Log.FindAll(entry => entry == "Update"));
        }

        [Test]
        public void StartProcedure_Twice_Throws()
        {
            _handler.Initialize(_a, _b);
            _handler.StartProcedure(typeof(ProbeA));

            Assert.Throws<GameException>(() => _handler.StartProcedure(typeof(ProbeB)));
            Assert.AreSame(_a, _handler.CurrentProcedure);
        }

        [Test]
        public void Tick_AccumulatesLogicTime()
        {
            _handler.Initialize(_a, _b);
            _handler.StartProcedure(typeof(ProbeA));

            _handler.Tick(0.5f, 1f);
            _handler.Tick(0.5f, 1f);

            Assert.AreEqual(1f, _handler.CurrentProcedureTime, 0.001f);
            Assert.AreEqual(2, _a.Log.FindAll(entry => entry == "Update").Count);
        }

        #endregion

        #region 切换 [CHANGE STATE]

        [Test]
        public void ChangeState_LeavesEntersAndRecordsHistory()
        {
            _handler.Initialize(_a, _b);
            _handler.StartProcedure(typeof(ProbeA));
            _handler.Tick(2f, 2f);

            _handler.ChangeState(typeof(ProbeB));

            Assert.AreSame(_b, _handler.CurrentProcedure);
            Assert.AreEqual(0f, _handler.CurrentProcedureTime, 0.001f);
            Assert.AreEqual(new[] { "Init", "Enter", "Update", "Leave" }, _a.Log);
            Assert.AreEqual(new[] { "Init", "Enter" }, _b.Log);

            var history = _handler.TransitionHistory;
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(ProcedureTransitionKind.Start, history[0].Kind);
            Assert.IsNull(history[0].From);
            Assert.AreSame(_a, history[0].To);
            Assert.AreEqual(ProcedureTransitionKind.Change, history[1].Kind);
            Assert.AreSame(_a, history[1].From);
            Assert.AreSame(_b, history[1].To);
            Assert.AreEqual(2f, history[1].FromElapsed, 0.001f);
        }

        [Test]
        public void ChangeState_ToUnregisteredOrNull_Throws()
        {
            _handler.Initialize(_a, _b);
            _handler.StartProcedure(typeof(ProbeA));

            Assert.Throws<GameException>(() => _handler.ChangeState(typeof(ProbeC)));
            Assert.Throws<GameException>(() => _handler.ChangeState(null));
            Assert.Throws<GameException>(() => _handler.ChangeState(typeof(string)));
        }

        [Test]
        public void ChangeState_MutualOnEnterLoop_FailsFastInsteadOfStackOverflow()
        {
            _a.OnEnterHook = () => _handler.ChangeState(typeof(ProbeB));
            _b.OnEnterHook = () => _handler.ChangeState(typeof(ProbeA));
            _handler.Initialize(_a, _b);

            Assert.Throws<GameException>(() => _handler.StartProcedure(typeof(ProbeA)));
        }

        [Test]
        public void StartProcedure_OnEnterRedirect_RecordsSettledTarget()
        {
            // 模板合法用法：OnEnter 内直切（如 ProcedureSplash 关闭闪屏直达下一流程）
            _a.OnEnterHook = () => _handler.ChangeState(typeof(ProbeB));
            _handler.Initialize(_a, _b);

            _handler.StartProcedure(typeof(ProbeA));

            Assert.AreSame(_b, _handler.CurrentProcedure);
            var history = _handler.TransitionHistory;
            Assert.AreEqual(2, history.Count);
            // 不变式：每条记录的 To == 广播时刻的 CurrentProcedure
            Assert.AreEqual(ProcedureTransitionKind.Change, history[0].Kind);
            Assert.AreSame(_a, history[0].From);
            Assert.AreSame(_b, history[0].To);
            Assert.AreEqual(ProcedureTransitionKind.Start, history[1].Kind);
            Assert.AreSame(_b, history[1].To);
        }

        [Test]
        public void ForceChangeState_WithoutOwner_Throws()
        {
            Assert.Throws<GameException>(() => _a.ForceChangeState<ProbeB>());
        }

        #endregion

        #region 关停与重启 [SHUTDOWN & RESTART]

        [Test]
        public void Shutdown_LeavesWithShutdownFlagDestroysAllAndKeepsHistory()
        {
            _handler.Initialize(_a, _b);
            _handler.StartProcedure(typeof(ProbeA));

            _handler.Internal_Shutdown();

            Assert.AreEqual(new[] { "Init", "Enter", "Leave:Shutdown", "Destroy" }, _a.Log);
            Assert.AreEqual(new[] { "Init", "Destroy" }, _b.Log);
            Assert.IsEmpty(_handler.Procedures);
            Assert.Throws<GameException>(() => _ = _handler.CurrentProcedure);

            // 关停记录保留供事后诊断，重新 Initialize 时才清空
            var history = _handler.TransitionHistory;
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(ProcedureTransitionKind.Shutdown, history[1].Kind);
            Assert.IsNull(history[1].To);
        }

        [Test]
        public void RestartProcedure_RebuildsMachineAndStartsFirstProcedure()
        {
            _handler.Initialize(_a, _b);
            _handler.StartProcedure(typeof(ProbeA));
            var a2 = new ProbeA();
            var b2 = new ProbeB();

            Assert.IsTrue(_handler.RestartProcedure(b2, a2));

            Assert.AreSame(b2, _handler.CurrentProcedure);
            StringAssert.Contains("Leave:Shutdown", string.Join(",", _a.Log));
            StringAssert.Contains("Destroy", string.Join(",", _a.Log));

            // 重启即新会话：历史仅剩本次启动记录
            Assert.AreEqual(1, _handler.TransitionHistory.Count);
            Assert.AreEqual(ProcedureTransitionKind.Start, _handler.TransitionHistory[0].Kind);
        }

        [Test]
        public void RestartProcedure_EmptyProcedures_Throws()
        {
            Assert.Throws<GameException>(() => _handler.RestartProcedure());
        }

        #endregion

        #region 查询 [QUERIES]

        [Test]
        public void Queries_BeforeInitialize_Throws()
        {
            Assert.IsFalse(_handler.IsStateReady);
            Assert.Throws<GameException>(() => _ = _handler.CurrentProcedure);
            Assert.Throws<GameException>(() => _ = _handler.CurrentProcedureTime);
            Assert.Throws<GameException>(() => _handler.HasProcedure(typeof(ProbeA)));
            Assert.Throws<GameException>(() => _handler.GetProcedure(typeof(ProbeA)));
        }

        [Test]
        public void IsStateReady_TracksInitializeAndShutdown()
        {
            Assert.IsFalse(_handler.IsStateReady);

            _handler.Initialize(_a, _b);
            Assert.IsTrue(_handler.IsStateReady);

            _handler.Internal_Shutdown();
            Assert.IsFalse(_handler.IsStateReady);
        }

        [Test]
        public void Queries_AfterInitialize_ResolveRegisteredProcedures()
        {
            _handler.Initialize(_a, _b);

            Assert.IsTrue(_handler.HasProcedure(typeof(ProbeA)));
            Assert.IsFalse(_handler.HasProcedure(typeof(ProbeC)));
            Assert.AreSame(_b, _handler.GetProcedure(typeof(ProbeB)));
            Assert.IsNull(_handler.GetProcedure(typeof(ProbeC)));
        }

        #endregion

        #region 外观级 [FACADE]

        // 经生成的 Internal_PeekHandler / Internal_UseHandler 存取，实现与执行顺序无关的静态态隔离，
        // 避免直接赋值 Handler 后无法复原未就绪态而破坏 ServiceContractTests 的降级契约断言

        [Test]
        public void Facade_ProcedureChanged_BroadcastsAndIsolatesSubscriberExceptions()
        {
            var savedHandler = ProcedureService.Internal_PeekHandler();
            var handler = new DefaultProcedureHandler();
            int fired = 0;
            ProcedureTransitionRecord lastRecord = default;
            Action<ProcedureTransitionRecord> throwingSubscriber =
                _ => throw new InvalidOperationException("boom");
            Action<ProcedureTransitionRecord> countingSubscriber = record =>
            {
                fired++;
                lastRecord = record;
            };
            ProcedureService.onProcedureChanged += throwingSubscriber;
            ProcedureService.onProcedureChanged += countingSubscriber;

            try
            {
                // 经真实 setter 链路注入（Internal_Init + CAS 交换）
                ProcedureService.Handler = handler;

                var a = new ProbeA();
                var b = new ProbeB();
                ProcedureService.Initialize(a, b);

                // 订阅者抛异常被隔离后转 Error 日志 ×2（启动广播 + 切换广播各一次）
                ExpectErrorLogForUtf();
                ExpectErrorLogForUtf();

                ProcedureService.StartProcedure(typeof(ProbeA));

                Assert.AreEqual(1, fired);
                Assert.AreEqual(ProcedureTransitionKind.Start, lastRecord.Kind);
                Assert.IsNull(lastRecord.From);

                ProcedureService.ChangeState(typeof(ProbeB));

                // 抛异常的订阅者既未中断广播链，也未中断状态机切换
                Assert.AreEqual(2, fired);
                Assert.AreEqual(ProcedureTransitionKind.Change, lastRecord.Kind);
                Assert.AreEqual(typeof(ProbeB), ProcedureService.CurrentProcedure.GetType());
                Assert.AreEqual(2, ProcedureService.TransitionHistory.Count);
            }
            finally
            {
                ProcedureService.onProcedureChanged -= throwingSubscriber;
                ProcedureService.onProcedureChanged -= countingSubscriber;
                handler.Internal_Shutdown();
                ProcedureService.Internal_UseHandler(savedHandler);
            }
        }

        [Test]
        public void Facade_Mutators_DegradeWithWarningWhenNotReady()
        {
            var savedHandler = ProcedureService.Internal_PeekHandler();

            try
            {
                // 强制未就绪态——变更类 API 须告警忽略而非抛异常（查询类静默降级契约由 ServiceContractTests 锁定）
                ProcedureService.Internal_UseHandler(null);
                Assert.IsFalse(ProcedureService.IsValid);

                Assert.DoesNotThrow(() => ProcedureService.Initialize(_a, _b));
                Assert.DoesNotThrow(() => ProcedureService.StartProcedure(typeof(ProbeA)));
                Assert.DoesNotThrow(() => ProcedureService.ChangeState<ProbeB>());
                Assert.IsFalse(ProcedureService.RestartProcedure(_a));
                Assert.IsNull(ProcedureService.CurrentProcedure);
            }
            finally
            {
                ProcedureService.Internal_UseHandler(savedHandler);
            }
        }

        [Test]
        public void Facade_Queries_SilentDegradeWhenHandlerPresentButStateNotReady()
        {
            var savedHandler = ProcedureService.Internal_PeekHandler();
            var handler = new DefaultProcedureHandler();

            try
            {
                // 外观已注册但状态机未 Initialize——后端会 fail-fast，外观必须静默降级
                ProcedureService.Handler = handler;
                Assert.IsTrue(ProcedureService.IsValid);
                Assert.IsFalse(ProcedureService.IsStateReady);

                Assert.IsNull(ProcedureService.CurrentProcedure);
                Assert.AreEqual(0f, ProcedureService.CurrentProcedureTime);
                Assert.IsFalse(ProcedureService.HasProcedure(typeof(ProbeA)));
                Assert.IsFalse(ProcedureService.HasProcedure<ProbeA>());
                Assert.IsNull(ProcedureService.GetProcedure(typeof(ProbeA)));
                Assert.IsNull(ProcedureService.GetProcedure<ProbeA>());
                Assert.IsEmpty(ProcedureService.Procedures);
            }
            finally
            {
                handler.Internal_Shutdown();
                ProcedureService.Internal_UseHandler(savedHandler);
            }
        }

        [Test]
        public void Facade_StartAndChange_IgnoreWhenStateNotReady_ButInitializeStillBootstraps()
        {
            var savedHandler = ProcedureService.Internal_PeekHandler();
            var handler = new DefaultProcedureHandler();

            try
            {
                ProcedureService.Handler = handler;
                Assert.IsFalse(ProcedureService.IsStateReady);

                // Start/Change 依赖已就绪状态机——忽略并告警
                Assert.DoesNotThrow(() => ProcedureService.StartProcedure(typeof(ProbeA)));
                Assert.DoesNotThrow(() => ProcedureService.StartProcedure<ProbeA>());
                Assert.DoesNotThrow(() => ProcedureService.ChangeState(typeof(ProbeB)));
                Assert.DoesNotThrow(() => ProcedureService.ChangeState<ProbeB>());
                Assert.IsFalse(ProcedureService.IsStateReady);
                Assert.IsNull(ProcedureService.CurrentProcedure);

                // Initialize 是引导入口，不要求已就绪
                var a = new ProbeA();
                var b = new ProbeB();
                Assert.DoesNotThrow(() => ProcedureService.Initialize(a, b));
                Assert.IsTrue(ProcedureService.IsStateReady);
                Assert.IsTrue(ProcedureService.HasProcedure<ProbeA>());
                Assert.AreSame(a, ProcedureService.GetProcedure<ProbeA>());

                Assert.DoesNotThrow(() => ProcedureService.StartProcedure(typeof(ProbeA)));
                Assert.AreSame(a, ProcedureService.CurrentProcedure);
            }
            finally
            {
                handler.Internal_Shutdown();
                ProcedureService.Internal_UseHandler(savedHandler);
            }
        }

        [Test]
        public void Facade_RestartProcedure_WorksWhenHandlerPresentButStateNotReady()
        {
            var savedHandler = ProcedureService.Internal_PeekHandler();
            var handler = new DefaultProcedureHandler();

            try
            {
                ProcedureService.Handler = handler;
                Assert.IsFalse(ProcedureService.IsStateReady);

                var a = new ProbeA();
                var b = new ProbeB();
                Assert.IsTrue(ProcedureService.RestartProcedure(a, b));
                Assert.IsTrue(ProcedureService.IsStateReady);
                Assert.AreSame(a, ProcedureService.CurrentProcedure);
            }
            finally
            {
                handler.Internal_Shutdown();
                ProcedureService.Internal_UseHandler(savedHandler);
            }
        }

        [Test]
        public void ChangeState_InsideProcedureChangedBroadcast_Throws()
        {
            Action<ProcedureTransitionRecord> reentrant = null;
            reentrant = _ =>
            {
                ProcedureService.onProcedureChanged -= reentrant;
                Assert.Throws<GameException>(() => _handler.ChangeState(typeof(ProbeB)));
                Assert.Throws<GameException>(() => _handler.StartProcedure(typeof(ProbeB)));
            };
            ProcedureService.onProcedureChanged += reentrant;

            try
            {
                _handler.Initialize(_a, _b);
                _handler.StartProcedure(typeof(ProbeA));

                // 重入被拒，状态机保持在启动目标
                Assert.AreSame(_a, _handler.CurrentProcedure);
                Assert.AreEqual(1, _handler.TransitionHistory.Count);
                Assert.AreEqual(ProcedureTransitionKind.Start, _handler.TransitionHistory[0].Kind);
            }
            finally
            {
                ProcedureService.onProcedureChanged -= reentrant;
            }
        }

        [Test]
        public void Facade_ChangeState_InsideProcedureChanged_IsolatedAndKeepsCurrent()
        {
            var savedHandler = ProcedureService.Internal_PeekHandler();
            var handler = new DefaultProcedureHandler();
            var a = new ProbeA();
            var b = new ProbeB();

            // 经外观订阅：回调内同步 ChangeState 抛出后被 Internal_Raise 隔离，启动仍完成
            Action<ProcedureTransitionRecord> reentrant = null;
            reentrant = _ =>
            {
                ProcedureService.onProcedureChanged -= reentrant;
                ProcedureService.ChangeState(typeof(ProbeB));
            };
            ProcedureService.onProcedureChanged += reentrant;

            try
            {
                ProcedureService.Handler = handler;
                ProcedureService.Initialize(a, b);

                // 回调内同步 ChangeState 抛 GameException，被隔离后转 Error 日志 ×1
                ExpectErrorLogForUtf();

                Assert.DoesNotThrow(() => ProcedureService.StartProcedure(typeof(ProbeA)));
                Assert.AreSame(a, ProcedureService.CurrentProcedure);
                Assert.AreEqual(1, ProcedureService.TransitionHistory.Count);
            }
            finally
            {
                ProcedureService.onProcedureChanged -= reentrant;
                handler.Internal_Shutdown();
                ProcedureService.Internal_UseHandler(savedHandler);
            }
        }

        #endregion

        #region 异常容错 [EXCEPTION RESILIENCE]

        /// <summary>
        /// 为随后一条 Error 日志声明 UTF 预期（黑名单：<c>UnityLoggingHandler</c> 直写 ConsoleWindow
        /// 不经 Debug 通路，声明期望反报「预期未出现」；与 Save 测试同约定）。
        /// </summary>
        private static void ExpectErrorLogForUtf()
        {
            if (LogUtility.Handler is not UnityLoggingHandler)
            {
                LogAssert.Expect(LogType.Error, new Regex(".*"));
            }
        }

        [Test]
        public void Shutdown_IsolatesLeaveAndDestroyExceptions()
        {
            _a.OnLeaveHook = () => throw new InvalidOperationException("leave boom");
            _a.OnDestroyHook = () => throw new InvalidOperationException("destroy boom a");
            _b.OnDestroyHook = () => throw new InvalidOperationException("destroy boom b");
            _handler.Initialize(_a, _b);
            _handler.StartProcedure(typeof(ProbeA));

            // 三条 Error：OnLeave(关停) ×1 + OnDestroy ×2——均被隔离，不阻断关停收尾
            ExpectErrorLogForUtf();
            ExpectErrorLogForUtf();
            ExpectErrorLogForUtf();

            Assert.DoesNotThrow(() => _handler.Internal_Shutdown());

            Assert.AreEqual(new[] { "Init", "Enter", "Leave:Shutdown", "Destroy" }, _a.Log);
            Assert.AreEqual(new[] { "Init", "Destroy" }, _b.Log);
            Assert.IsFalse(_handler.IsStateReady);

            // 关停记录在 finally 保证下仍写入
            var history = _handler.TransitionHistory;
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(ProcedureTransitionKind.Shutdown, history[1].Kind);
            Assert.AreSame(_a, history[1].From);
            Assert.IsNull(history[1].To);
        }

        [Test]
        public void ChangeState_OnEnterThrows_RollsBackToPreviousProcedure()
        {
            _handler.Initialize(_a, _b);
            _handler.StartProcedure(typeof(ProbeA));
            _handler.Tick(2f, 2f);
            _b.OnEnterHook = () => throw new InvalidOperationException("enter boom");

            Assert.Throws<InvalidOperationException>(() => _handler.ChangeState(typeof(ProbeB)));

            // 回滚到切出流程：恢复完整进入态与驻留时长，失败切换不留痕
            Assert.AreSame(_a, _handler.CurrentProcedure);
            Assert.AreEqual(2f, _handler.CurrentProcedureTime, 0.001f);
            Assert.AreEqual(1, _handler.TransitionHistory.Count);

            // 回滚后状态机仍可正常切换
            _b.OnEnterHook = null;
            _handler.ChangeState(typeof(ProbeB));
            Assert.AreSame(_b, _handler.CurrentProcedure);
        }

        [Test]
        public void StartProcedure_OnEnterThrows_RollsBackToNotStarted()
        {
            _a.OnEnterHook = () => throw new InvalidOperationException("enter boom");
            _handler.Initialize(_a, _b);

            Assert.Throws<InvalidOperationException>(() => _handler.StartProcedure(typeof(ProbeA)));

            // 回滚到未启动态：机器仍就绪、无当前流程、无切换记录
            Assert.IsTrue(_handler.IsStateReady);
            Assert.IsNull(_handler.CurrentProcedure);
            Assert.AreEqual(0, _handler.TransitionHistory.Count);

            // 修复后可重新启动
            _a.OnEnterHook = null;
            _handler.StartProcedure(typeof(ProbeA));
            Assert.AreSame(_a, _handler.CurrentProcedure);
        }

        [Test]
        public void ChangeState_OnEnterThrowsAfterNestedRedirect_KeepsSettledTarget()
        {
            _handler.Initialize(_a, _b);
            _handler.StartProcedure(typeof(ProbeA));
            _b.OnEnterHook = () =>
            {
                _handler.ChangeState(typeof(ProbeA));
                throw new InvalidOperationException("enter boom");
            };

            Assert.Throws<InvalidOperationException>(() => _handler.ChangeState(typeof(ProbeB)));

            // 嵌套重定向已完整进入并记录——外层失败不回滚嵌套终态、不追加记录
            Assert.AreSame(_a, _handler.CurrentProcedure);
            var history = _handler.TransitionHistory;
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(ProcedureTransitionKind.Change, history[1].Kind);
            Assert.AreSame(_b, history[1].From);
            Assert.AreSame(_a, history[1].To);
        }

        #endregion
    }
}
