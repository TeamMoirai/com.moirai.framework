using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Utility
{
    /// <summary>
    /// <see cref="MainThreadDispatcher"/> 静态核心的 EditMode 单元测试。
    /// 通过内部 API（InternalsVisibleTo）直接驱动 Pump，不依赖实例生命周期。
    /// </summary>
    [TestFixture]
    public class MainThreadDispatcherTest
    {
        [SetUp]
        public void SetUp()
        {
            MainThreadDispatcher.ResetStatics();
        }

        [TearDown]
        public void TearDown()
        {
            // 恢复可用状态，避免 BeginShutdown 测试污染后续测试
            MainThreadDispatcher.ResetStatics();
        }

        #region 基础执行 [Basic Execution]

        [Test]
        public void Post_ThenPump_ExecutesActionExactlyOnce()
        {
            int executed = 0;
            MainThreadDispatcher.Post(() => Interlocked.Increment(ref executed));

            Assert.AreEqual(0, executed, "Pump 前不应执行");
            Assert.AreEqual(1, MainThreadDispatcher.PendingCount);

            MainThreadDispatcher.Pump();

            Assert.AreEqual(1, executed);
            Assert.AreEqual(0, MainThreadDispatcher.PendingCount);
        }

        [Test]
        public void Post_ExecutesInFifoOrder()
        {
            var order = new List<int>();
            MainThreadDispatcher.Post(() => order.Add(1));
            MainThreadDispatcher.Post(() => order.Add(2));
            MainThreadDispatcher.Post(() => order.Add(3));

            MainThreadDispatcher.Pump();

            Assert.AreEqual(new[] { 1, 2, 3 }, order);
        }

        [Test]
        public void IsMainThread_TrueInEditModeTest()
        {
            Assert.IsTrue(MainThreadDispatcher.IsMainThread, "EditMode 测试运行在编辑器主线程");
        }

        [Test]
        public void Post_FromBackgroundThread_EnqueuesSafely()
        {
            int executed = 0;
            bool isMainThreadInView = true;

            var worker = Task.Run(() =>
            {
                isMainThreadInView = MainThreadDispatcher.IsMainThread;
                MainThreadDispatcher.Post(() => Interlocked.Increment(ref executed));
            });

            Assert.IsTrue(worker.Wait(2000), "后台入队不应阻塞");
            Assert.IsFalse(isMainThreadInView, "工作线程不应被判定为主线程");
            Assert.AreEqual(1, MainThreadDispatcher.PendingCount, "后台线程入队应成功且无需锁竞争");

            MainThreadDispatcher.Pump();
            Assert.AreEqual(1, executed);
        }

        #endregion

        #region 异常隔离 [Exception Isolation]

        [Test]
        public void Pump_ExceptionInAction_IsolatedAndSubsequentActionsStillRun()
        {
            LogAssert.Expect(LogType.Error, new Regex("MainThreadDispatcher action execution failed"));

            int afterCount = 0;
            MainThreadDispatcher.Post(() => throw new InvalidOperationException("boom"));
            MainThreadDispatcher.Post(() => afterCount++);

            // LogUtility.Error 在编辑器中可能耗尽单帧预算，按消费方帧循环语义排空
            for (int guard = 0; guard < 10 && MainThreadDispatcher.PendingCount > 0; guard++)
            {
                MainThreadDispatcher.Pump();
            }

            Assert.AreEqual(1, afterCount, "单个任务异常不应毒化后续任务");
            Assert.AreEqual(0, MainThreadDispatcher.PendingCount);
        }

        [Test]
        public void PostAsync_ExceptionPropagatesToAwaiter()
        {
            // throw lambda 无法推断返回类型，需显式目标类型以消除重载歧义
            Func<int> thrower = () => throw new DivideByZeroException();
            UniTask<int> task = MainThreadDispatcher.PostAsync(thrower);

            MainThreadDispatcher.Pump();

            Assert.ThrowsAsync<DivideByZeroException>(async () => await task);
        }

        [Test]
        public void Send_OnMainThread_ExceptionPropagatesToCaller()
        {
            // 主线程快速路径同步执行，异常直接抛给调用方（语义与 Send 文档一致）
            Assert.Throws<InvalidOperationException>(() =>
                MainThreadDispatcher.Send(() => throw new InvalidOperationException("direct")));
        }

        #endregion

        #region 异步结果 [Async Results]

        [Test]
        public void PostAsync_ReturnsResultFromMainQueueExecution()
        {
            UniTask<int> task = MainThreadDispatcher.PostAsync(() => 41 + 1);

            Assert.AreEqual(UniTaskStatus.Pending, task.Status, "Pump 前不应完成");
            MainThreadDispatcher.Pump();

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
        }

        [Test]
        public void SendAsync_OnMainThread_CompletesSynchronously()
        {
            UniTask<string> task = MainThreadDispatcher.SendAsync(() => "ok");

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status, "主线程快速路径应同步完成");
            Assert.AreEqual("ok", task.GetAwaiter().GetResult());
            Assert.AreEqual(0, MainThreadDispatcher.PendingCount, "不应入队");
        }

        [Test]
        public void SendAsync_ExceptionOnMainThread_PropagatesThroughTask()
        {
            UniTask task = MainThreadDispatcher.SendAsync(() => throw new ArithmeticException());

            Assert.AreEqual(UniTaskStatus.Faulted, task.Status);
            Assert.ThrowsAsync<ArithmeticException>(async () => await task);
        }

        [Test]
        public async Task PostAsync_AwaitedWorkStaysOnMainThreadChain()
        {
            var gate = new TaskCompletionSource<bool>();
            bool resumedOnMain = false;

            UniTask task = MainThreadDispatcher.PostAsync(async () =>
            {
                await gate.Task;
                resumedOnMain = MainThreadDispatcher.IsMainThread;
            });

            MainThreadDispatcher.Pump(); // 启动异步函数，阻塞在 gate
            Assert.AreEqual(UniTaskStatus.Pending, task.Status, "gate 未放行前任务不应完成");

            gate.TrySetResult(true); // 主线程触发续体
            await task;

            Assert.IsTrue(resumedOnMain, "await 续体应驻留主线程（未使用 ConfigureAwait(false)）");
        }

        #endregion

        #region 快速路径 [Fast Path]

        [Test]
        public void Send_OnMainThread_ExecutesImmediatelyWithoutQueueing()
        {
            int executed = 0;
            MainThreadDispatcher.Send(() => executed++);

            Assert.AreEqual(1, executed);
            Assert.AreEqual(0, MainThreadDispatcher.PendingCount);
        }

        [Test]
        public void Send_FromBackgroundThread_FallsBackToQueue()
        {
            int executed = 0;
            var worker = Task.Run(() => MainThreadDispatcher.Send(() => Interlocked.Increment(ref executed)));

            Assert.IsTrue(worker.Wait(2000));
            Assert.AreEqual(0, executed, "后台线程 Send 不应同步执行");
            Assert.AreEqual(1, MainThreadDispatcher.PendingCount);

            MainThreadDispatcher.Pump();
            Assert.AreEqual(1, executed);
        }

        #endregion

        #region 参数校验 [Argument Validation]

        [Test]
        public void Post_NullAction_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => MainThreadDispatcher.Post((Action)null));
        }

        [Test]
        public void Send_NullAction_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => MainThreadDispatcher.Send((Action)null));
        }

        [Test]
        public void PostAsync_NullFunc_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => MainThreadDispatcher.PostAsync((Func<int>)null));
        }

        #endregion

        #region 关闭语义 [Shutdown Semantics]

        [Test]
        public void BeginShutdown_DropsPendingAndLogsCount()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"2 pending action\(s\) dropped"));

            int executed = 0;
            MainThreadDispatcher.Post(() => executed++);
            MainThreadDispatcher.Post(() => executed++);

            MainThreadDispatcher.BeginShutdown();

            Assert.AreEqual(0, executed, "积压任务应被丢弃而非执行");
            Assert.AreEqual(0, MainThreadDispatcher.PendingCount);
        }

        [Test]
        public void Post_AfterShutdown_RejectedWithWarningAndNeverExecuted()
        {
            MainThreadDispatcher.BeginShutdown(); // 空队列关闭：静默，不告警
            Assert.AreEqual(0, MainThreadDispatcher.PendingCount);

            LogAssert.Expect(LogType.Warning, new Regex("rejected"));
            int executed = 0;
            MainThreadDispatcher.Post(() => executed++);

            MainThreadDispatcher.Pump();
            Assert.AreEqual(0, executed, "关闭后的入队应被拒绝");
        }

        [Test]
        public void ResetStatics_RestoresUsableStateAfterShutdown()
        {
            MainThreadDispatcher.BeginShutdown();

            MainThreadDispatcher.ResetStatics();

            int executed = 0;
            MainThreadDispatcher.Post(() => executed++);
            MainThreadDispatcher.Pump();
            Assert.AreEqual(1, executed, "Reset 后应重新接受任务");
        }

        [Test]
        public void Send_CoroutineDuringShutdownWindow_DropsWithWarningInsteadOfNRE()
        {
            LogAssert.Expect(LogType.Warning, new Regex("coroutine dropped"));

            // 反射置位基类退出标记，模拟应用退出窗口（s_ShuttingDown=true → Instance getter 返回 null）
            var field = typeof(SingletonMono<MainThreadDispatcher>).GetField("s_ShuttingDown",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(field, "基类应声明 s_ShuttingDown 静态字段");
            field.SetValue(null, true);

            int started = 0;
            IEnumerator DummyRoutine()
            {
                started++;
                yield break;
            }

            Assert.DoesNotThrow(() => MainThreadDispatcher.Send(DummyRoutine()), "退出窗口的协程 Send 应丢弃并告警，而非 NRE");
            Assert.AreEqual(0, started, "协程不应被启动");
        }

        [Test]
        public void ResetStatics_ClearsStaleQueue()
        {
            int executed = 0;
            MainThreadDispatcher.Post(() => executed++);

            MainThreadDispatcher.ResetStatics();

            Assert.AreEqual(0, MainThreadDispatcher.PendingCount);
            MainThreadDispatcher.Pump();
            Assert.AreEqual(0, executed, "残留任务应随 Reset 一起丢弃");
        }

        #endregion

        #region 可等待操作的停机与取消语义 [Awaitable Shutdown/Cancellation]

        [Test]
        public void PostAsync_Completion_RemovesFromAwaiterRegistry()
        {
            UniTask<int> task = MainThreadDispatcher.PostAsync(() => 1);

            Assert.AreEqual(1, MainThreadDispatcher.PendingAwaiterCount, "挂起期间应在停机注册表中");

            MainThreadDispatcher.Pump();

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status);
            Assert.AreEqual(0, MainThreadDispatcher.PendingAwaiterCount, "完成后应移出停机注册表");
        }

        [Test]
        public void PostAsync_PendingAtBeginShutdown_TransitionsToCanceled()
        {
            UniTask<int> task = MainThreadDispatcher.PostAsync(() => 1); // 已入队，未 Pump

            Assert.AreEqual(UniTaskStatus.Pending, task.Status);
            Assert.AreEqual(1, MainThreadDispatcher.PendingAwaiterCount);

            MainThreadDispatcher.BeginShutdown();

            Assert.AreEqual(UniTaskStatus.Canceled, task.Status, "停机必须终结挂起的可等待任务，而非永久挂起");
            Assert.AreEqual(0, MainThreadDispatcher.PendingAwaiterCount, "停机后注册表应清空");
            // 经 async lambda 断言会被 Task 基础设施包装为 TaskCanceledException 子类，此处用同步 GetResult 暴露原始异常类型
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void PostAsync_AfterShutdown_ReturnsCanceledTaskImmediately()
        {
            MainThreadDispatcher.BeginShutdown();

            UniTask<int> task = MainThreadDispatcher.PostAsync(() => 1);

            Assert.AreEqual(UniTaskStatus.Canceled, task.Status, "停机后的 PostAsync 应立即取消而非入队挂起");
            Assert.AreEqual(0, MainThreadDispatcher.PendingCount, "停机后不应入队");
        }

        [Test]
        public void PostAsync_PreCanceledToken_TaskCanceledWithoutExecution()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            int executed = 0;
            UniTask<int> task = MainThreadDispatcher.PostAsync(() => executed++, cts.Token);

            MainThreadDispatcher.Pump();

            Assert.AreEqual(UniTaskStatus.Canceled, task.Status);
            Assert.AreEqual(0, executed, "已取消的任务在执行前应被跳过");
            Assert.AreEqual(0, MainThreadDispatcher.PendingAwaiterCount);
        }

        [Test]
        public void PostAsync_CancelWhileQueued_SkipsExecutionAndCancelsTask()
        {
            using var cts = new CancellationTokenSource();

            int executed = 0;
            UniTask<int> task = MainThreadDispatcher.PostAsync(() => executed++, cts.Token);

            cts.Cancel(); // 仍在队列中时取消

            MainThreadDispatcher.Pump();

            Assert.AreEqual(UniTaskStatus.Canceled, task.Status);
            Assert.AreEqual(0, executed, "队列中被取消的任务不应执行");
            Assert.AreEqual(0, MainThreadDispatcher.PendingAwaiterCount, "取消后应移出注册表");
        }

        [Test]
        public void SendAsync_PreCanceledTokenOnMainThread_ReturnsCanceledTaskWithoutExecution()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            int executed = 0;
            UniTask<int> task = MainThreadDispatcher.SendAsync(() => executed++, cts.Token);

            Assert.AreEqual(UniTaskStatus.Canceled, task.Status);
            Assert.AreEqual(0, executed);
            Assert.AreEqual(0, MainThreadDispatcher.PendingCount, "主线程快速路径不应入队");
        }

        [Test]
        public void SendAsync_OnMainThread_OperationCanceledException_MapsToCanceled()
        {
            using var cts = new CancellationTokenSource();

            Action thrower = () => throw new OperationCanceledException(cts.Token);
            UniTask task = MainThreadDispatcher.SendAsync(thrower);

            Assert.AreEqual(UniTaskStatus.Canceled, task.Status, "主线程快速路径的 OCE 应映射为取消，与 PostAsync 语义一致");
            Assert.AreNotEqual(UniTaskStatus.Faulted, task.Status);
        }

        [Test]
        public void PostAsync_ActionThrowsOperationCanceled_MapsToCanceled()
        {
            using var cts = new CancellationTokenSource();

            Action thrower = () => throw new OperationCanceledException(cts.Token);
            UniTask task = MainThreadDispatcher.PostAsync(thrower);

            MainThreadDispatcher.Pump();

            Assert.AreEqual(UniTaskStatus.Canceled, task.Status, "排队路径的 OCE 应映射为取消");
            Assert.AreNotEqual(UniTaskStatus.Faulted, task.Status);
        }

        [Test]
        public void ResetStatics_CancelsPendingAwaiters()
        {
            UniTask<int> task = MainThreadDispatcher.PostAsync(() => 1);
            Assert.AreEqual(1, MainThreadDispatcher.PendingAwaiterCount);

            MainThreadDispatcher.ResetStatics();

            Assert.AreEqual(UniTaskStatus.Canceled, task.Status, "Reset 必须取消挂起的可等待任务（异常退出恢复场景，BeginShutdown 可能未执行）");
            Assert.AreEqual(0, MainThreadDispatcher.PendingAwaiterCount);
        }

        [Test]
        public async Task PostAsync_ShutdownCancellation_CarriesCallerToken()
        {
            using var cts = new CancellationTokenSource();

            UniTask<int> task = MainThreadDispatcher.PostAsync(() => 1, cts.Token);
            MainThreadDispatcher.BeginShutdown();

            try
            {
                await task;
                Assert.Fail("任务应已被停机取消");
            }
            catch (OperationCanceledException oce)
            {
                Assert.AreEqual(cts.Token, oce.CancellationToken, "停机取消应回传调用方令牌以支持异常过滤器匹配");
            }
        }

        [Test]
        public void PostAsync_AsyncFunc_SyncThrow_FaultsTask_NeverStaysPending()
        {
            // async 方法体内的用户 try/catch 覆盖同步前缀（状态机首个 MoveNext 同步执行 try 块），
            // 异常经 catch 写入完成源 —— async void 的 SyncContext 逃逸仅发生在未被任何用户 catch 捕获时。
            Func<UniTask> syncThrower = () => throw new InvalidOperationException("sync boom");
            UniTask task = MainThreadDispatcher.PostAsync(syncThrower);

            MainThreadDispatcher.Pump();

            Assert.AreEqual(UniTaskStatus.Faulted, task.Status, "同步抛出的异常必须写入完成源，任务不得永久 Pending");
            Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
            Assert.AreEqual(0, MainThreadDispatcher.PendingAwaiterCount, "finally 应清理注册表");
        }

        [Test]
        public void PostAsync_GenericAsyncFunc_SyncThrow_FaultsTask_NeverStaysPending()
        {
            Func<UniTask<int>> syncThrower = () => throw new InvalidOperationException("sync boom");
            UniTask<int> task = MainThreadDispatcher.PostAsync(syncThrower);

            MainThreadDispatcher.Pump();

            Assert.AreEqual(UniTaskStatus.Faulted, task.Status, "同步抛出的异常必须写入完成源，任务不得永久 Pending");
            Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
            Assert.AreEqual(0, MainThreadDispatcher.PendingAwaiterCount, "finally 应清理注册表");
        }

        #endregion

        #region 背压 [Backpressure]

        [Test]
        public void Pump_TimeBudgetDefersRemainingWorkToNextPump()
        {
            int executed = 0;
            const int total = 3;

            for (int i = 0; i < total; i++)
            {
                MainThreadDispatcher.Post(() =>
                {
                    Thread.Sleep(3); // 单任务耗时超出 2ms 预算
                    Interlocked.Increment(ref executed);
                });
            }

            MainThreadDispatcher.Pump(); // 单次泵受预算约束

            Assert.Less(executed, total, "超出时间预算的任务应推迟");
            Assert.GreaterOrEqual(executed, 1, "至少应执行一个任务");

            for (int guard = 0; guard < 20 && MainThreadDispatcher.PendingCount > 0; guard++)
            {
                MainThreadDispatcher.Pump();
            }

            Assert.AreEqual(total, executed, "多次泵后应全部执行完毕");
        }

        [Test]
        public void Post_BacklogExceedsThreshold_WarnsOnlyOnce()
        {
            LogAssert.Expect(LogType.Warning, new Regex("backlog exceeds"));

            int threshold = MainThreadDispatcher.BACKLOG_WARN_THRESHOLD;
            // 告警在采样点（每 256 次入队）触发；继续入队验证滞回只告警一次
            for (int i = 0; i < threshold + 512; i++)
            {
                MainThreadDispatcher.Post(() => { });
            }

            LogAssert.NoUnexpectedReceived();
        }

        #endregion
    }
}
