using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Moirai.Atropos.Editor;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Editor.Core
{
    /// <summary>
    /// <see cref="EditorLoadPipeline"/> 的 EditMode 测试：步序与动态登记、切片分摊、后台续体、取消与失败面。
    /// </summary>
    /// <remarks>
    /// 起跑经编辑器 update 延一拍、切片经 update 逐拍驱动，用例统一 <c>[UnityTest]</c> 逐帧泵编辑器循环等待完成。
    /// </remarks>
    [TestFixture]
    public sealed class EditorLoadPipelineTests
    {

        #region 夹具 [FIXTURES]

        private sealed class Probe
        {
            public readonly List<string> Order = new List<string>();
            public int CompletedCount;
            public Exception Failed;
        }

        private static EditorLoadPipeline Create(Probe probe)
        {
            return new EditorLoadPipeline(
                () => probe.CompletedCount++,
                exception => probe.Failed = exception,
                null);
        }

        private static IEnumerator WaitUntil(Func<bool> done, int maxFrames = 1200)
        {
            int frames = 0;
            while (!done() && frames < maxFrames)
            {
                yield return null;
                frames++;
            }
        }

        private static List<int> Items(int count)
        {
            var items = new List<int>(count);
            for (int i = 0; i < count; i++) items.Add(i);
            return items;
        }

        #endregion

        #region 即时步与动态登记 [IMMEDIATE STEPS]

        [UnityTest]
        public IEnumerator ImmediateSteps_RunInOrderAndComplete()
        {
            var probe = new Probe();
            EditorLoadPipeline pipeline = Create(probe);
            pipeline.AddStep("a", () => probe.Order.Add("a"));
            pipeline.AddStep("b", () =>
            {
                probe.Order.Add("b");
                // 装载中登记后续步：发现步产出分组步的形态
                pipeline.AddStep("c", () => probe.Order.Add("c"));
            });

            pipeline.Start();
            yield return WaitUntil(() => probe.CompletedCount > 0);

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, probe.Order, "步序按登记顺序执行，装载中登记的步随后运行");
            Assert.IsNull(probe.Failed, "正常收场不算失败");
            Assert.IsFalse(pipeline.IsRunning);
        }

        #endregion

        #region 切片步 [SLICED STEPS]

        [UnityTest]
        public IEnumerator SlicedStep_SpreadsAcrossTicksAndCompletesInOrder()
        {
            var probe = new Probe();
            var processed = new List<int>();
            List<int> items = Items(40);

            EditorLoadPipeline pipeline = Create(probe);
            pipeline.AddSlicedStep("sliced", items, item =>
            {
                Thread.Sleep(2); // 每条约 2ms、预算 3ms：每拍一两条，必然跨拍
                processed.Add(item);
            }, 3f);

            pipeline.Start();
            yield return null; // 首拍：delayCall 起跑 + 首次切片

            int afterFirstTick = pipeline.ProcessedCount;
            Assert.Less(afterFirstTick, items.Count, "预算分摊下首拍不应吃完所有条目");
            Assert.AreEqual(afterFirstTick, processed.Count, "计数与已处理条目一致");

            yield return WaitUntil(() => !pipeline.IsRunning);

            Assert.AreEqual(items.Count, processed.Count, "全部条目处理完");
            CollectionAssert.AreEqual(items, processed, "切片按序处理");
            Assert.AreEqual(items.Count, pipeline.ProcessedCount);
            Assert.AreEqual(items.Count, pipeline.TotalCount);
            Assert.AreEqual(1f, pipeline.Progress, 0.0001f, "完成后进度归一");
            Assert.AreEqual(1, probe.CompletedCount, "完成回调恰好一次");
        }

        #endregion

        #region 后台步 [BACKGROUND STEPS]

        [UnityTest]
        public IEnumerator BackgroundStep_RunsOffMainThreadAndContinuesOnMain()
        {
            var probe = new Probe();
            int mainThread = Environment.CurrentManagedThreadId;
            int backgroundThread = 0;
            int continuationThread = 0;

            EditorLoadPipeline pipeline = Create(probe);
            pipeline.AddBackgroundStep("bg", token => backgroundThread = Environment.CurrentManagedThreadId);
            pipeline.AddStep("after", () => continuationThread = Environment.CurrentManagedThreadId);

            pipeline.Start();
            yield return WaitUntil(() => probe.CompletedCount > 0);

            Assert.AreNotEqual(mainThread, backgroundThread, "后台步应运行在非主线程");
            Assert.AreEqual(mainThread, continuationThread, "续体应回主线程继续下一步");
        }

        [UnityTest]
        public IEnumerator BackgroundStep_ObservesCancellation()
        {
            var probe = new Probe();
            bool bodyStarted = false;
            bool observedCancel = false;

            EditorLoadPipeline pipeline = Create(probe);
            pipeline.AddBackgroundStep("wait", token =>
            {
                bodyStarted = true;
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!token.IsCancellationRequested && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(5);
                }
                observedCancel = token.IsCancellationRequested;
            });

            pipeline.Start();

            // 后台步起跑同样经 update 延一拍，等它真正开始再取消
            int startGuard = 0;
            while (!bodyStarted && startGuard < 120)
            {
                yield return null;
                startGuard++;
            }
            Assert.IsTrue(bodyStarted, "后台步应已开始执行");
            pipeline.Cancel();

            int frames = 0;
            while (!observedCancel && frames < 300)
            {
                yield return null;
                frames++;
            }

            Assert.IsTrue(observedCancel, "后台步应能观察到取消令牌");
            Assert.AreEqual(0, probe.CompletedCount, "取消不回调完成");
            Assert.IsNull(probe.Failed, "取消不算失败");
        }

        #endregion

        #region 取消与失败 [CANCEL & FAILURE]

        [UnityTest]
        public IEnumerator Cancel_DuringSlice_StopsWithoutCallbacks()
        {
            var probe = new Probe();
            int processed = 0;
            List<int> items = Items(200);

            EditorLoadPipeline pipeline = Create(probe);
            pipeline.AddSlicedStep("slow", items, item =>
            {
                Thread.Sleep(2);
                processed++;
            }, 3f);
            pipeline.Start();

            // 起跑经 update 延一拍、切片再延一拍，轮询到有条目处理过再取消
            int startGuard = 0;
            while (pipeline.ProcessedCount == 0 && startGuard < 120)
            {
                yield return null;
                startGuard++;
            }
            Assert.Greater(pipeline.ProcessedCount, 0, "取消前应有条目已处理");
            pipeline.Cancel();
            Assert.IsFalse(pipeline.IsRunning, "取消后不在途");

            int snapshot = processed;
            yield return null;
            yield return null;

            Assert.AreEqual(snapshot, processed, "取消后不再处理条目");
            Assert.AreEqual(0, probe.CompletedCount, "取消不回调完成");
            Assert.IsNull(probe.Failed, "取消不算失败");
        }

        [UnityTest]
        public IEnumerator FailingStep_SurfacesExceptionAndSkipsCompletion()
        {
            var probe = new Probe();
            var failure = new InvalidOperationException("boom");
            EditorLoadPipeline pipeline = Create(probe);
            pipeline.AddStep("explode", () => throw failure);

            UtfLogExpect.Exception(); // 管线经 Debug.LogException 落 Console
            pipeline.Start();
            yield return WaitUntil(() => probe.Failed != null);

            Assert.AreSame(failure, probe.Failed, "失败回调应收到原始异常");
            Assert.IsTrue(pipeline.IsFailed);
            Assert.IsFalse(pipeline.IsRunning);
            Assert.AreEqual(0, probe.CompletedCount, "失败不得回调完成");
        }

        #endregion

    }
}
