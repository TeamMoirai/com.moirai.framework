using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Utility
{
    /// <summary>
    /// <see cref="DefaultTweenHandler.TweenTask"/> 静态核心的 EditMode 单元测试。
    /// 通过 dt 注入版 <c>Update(float, float)</c> 直接驱动，不依赖引擎时间与帧监听，
    /// 覆盖：ID 版本防别名、槽位回收复用、完成/停止语义、重入安全、
    /// 循环模式（Restart/Yoyo/Incremental/Rewind）、延迟、销毁目标、参数校验。
    /// </summary>
    [TestFixture]
    public class TweenTaskTest
    {
        private GameObject _go;
        private Transform _transform;

        [SetUp]
        public void SetUp()
        {
            DefaultTweenHandler.TweenTask.ResetStatics();
            _go = new GameObject("TweenTaskTest");
            _transform = _go.transform;
            _transform.position = Vector3.zero;
        }

        [TearDown]
        public void TearDown()
        {
            DefaultTweenHandler.TweenTask.ResetStatics();
            UnityEngine.Object.DestroyImmediate(_go);
        }

        #region 创建与 ID [CREATE & ID]

        [Test]
        public void Create_ReturnsNonZeroId_And_IsAlive()
        {
            long id = CreatePositionTween(Vector3.zero, Vector3.one, 1f);

            Assert.AreNotEqual(0L, id);
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(id));
        }

        [Test]
        public void RecycledSlotReuse_OldIdNotAliased_ToNewTween()
        {
            long id1 = CreatePositionTween(Vector3.zero, Vector3.one, 1f);
            DefaultTweenHandler.TweenTask.Stop(id1);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id1), "Stop 后 id1 应失效");

            // 复用同槽位创建新 tween
            long id2 = CreatePositionTween(Vector3.zero, Vector3.one * 2f, 1f);

            Assert.AreNotEqual(id1, id2, "槽位复用后版本号必须不同");
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(id2));
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id1), "旧 id 不得别名到新 tween");

            // 旧 id 的操作必须为 no-op：不得误杀新 tween
            DefaultTweenHandler.TweenTask.Stop(id1);
            DefaultTweenHandler.TweenTask.Complete(id1);
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(id2), "旧 id 操作不得影响新 tween");
        }

        [Test]
        public void StopAll_ClearsTargetTweens_Selectively()
        {
            var go2 = new GameObject("other");
            long id1 = CreatePositionTween(Vector3.zero, Vector3.one, 1f);
            long id2 = CreatePositionTweenOn(go2.transform, Vector3.zero, Vector3.one, 1f);

            int stopped = DefaultTweenHandler.TweenTask.StopAll(_transform);

            Assert.AreEqual(1, stopped);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id1));
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(id2));

            DefaultTweenHandler.TweenTask.StopAll(null);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id2));
            UnityEngine.Object.DestroyImmediate(go2);
        }

        [Test]
        public void IsTweening_And_GetTweenCount_ByTarget()
        {
            long id1 = CreatePositionTween(Vector3.zero, Vector3.one, 1f);
            long id2 = CreatePositionTween(Vector3.zero, Vector3.one, 1f);

            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsTweening(_transform));
            Assert.AreEqual(2, DefaultTweenHandler.TweenTask.GetTweenCount(_transform));
            Assert.AreEqual(0, DefaultTweenHandler.TweenTask.GetTweenCount(new object()));

            DefaultTweenHandler.TweenTask.Stop(id1);
            Assert.AreEqual(1, DefaultTweenHandler.TweenTask.GetTweenCount(_transform));
            DefaultTweenHandler.TweenTask.Stop(id2);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsTweening(_transform));
        }

        #endregion

        #region 完成语义 [COMPLETION SEMANTICS]

        [Test]
        public void NaturalCompletion_AppliesEndValue_And_FiresCallbackOnce()
        {
            int callbacks = 0;
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                onComplete: () => callbacks++);

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(0, callbacks, "半程不应回调");
            Assert.AreEqual(5f, _transform.position.x, 0.0001f, "Linear 半程值");

            DefaultTweenHandler.TweenTask.Update(0.6f, 0.6f);
            Assert.AreEqual(1, callbacks, "超程后恰好回调一次");
            Assert.AreEqual(10f, _transform.position.x, 0.0001f, "完成时落在终值");
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
        }

        [Test]
        public void Complete_AppliesEndValue_And_FiresCallback()
        {
            int callbacks = 0;
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                onComplete: () => callbacks++);

            DefaultTweenHandler.TweenTask.Update(0.25f, 0.25f);
            DefaultTweenHandler.TweenTask.Complete(id);

            Assert.AreEqual(1, callbacks, "强制 Complete 必须触发 OnComplete（与自然完成同语义）");
            Assert.AreEqual(10f, _transform.position.x, 0.0001f);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
        }

        [Test]
        public void Stop_KillsWithoutCallback()
        {
            int callbacks = 0;
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                onComplete: () => callbacks++);

            DefaultTweenHandler.TweenTask.Update(0.25f, 0.25f);
            DefaultTweenHandler.TweenTask.Stop(id);

            Assert.AreEqual(0, callbacks, "Stop = 中断，不触发 OnComplete");
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
            Assert.AreEqual(2.5f, _transform.position.x, 0.0001f, "Stop 停在当前值（2.5），不应用终值");
        }

        [Test]
        public void CompleteAll_FiresCallbacks_And_SnapshotsIteration()
        {
            int callbacks = 0;
            long aliveAfterCompleteAll = 0;
            CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f, onComplete: () =>
            {
                callbacks++;
                // 回调内新建 tween：本轮 CompleteAll 不得级联完成它
                aliveAfterCompleteAll = CreatePositionTween(Vector3.zero, Vector3.one, 1f);
            });
            CreatePositionTween(Vector3.zero, new Vector3(20f, 0f, 0f), 1f, onComplete: () => callbacks++);

            int completed = DefaultTweenHandler.TweenTask.CompleteAll(_transform);

            Assert.AreEqual(2, completed);
            Assert.AreEqual(2, callbacks, "2 个原 tween 各回调一次；回调内新建的不被本轮完成");
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(aliveAfterCompleteAll), "回调内新建的 tween 必须存活");
            DefaultTweenHandler.TweenTask.StopAll(null);
        }

        [Test]
        public void OnComplete_Exception_DoesNotBreakUpdateLoop()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Tween callback threw"));
            long badId = CreatePositionTween(Vector3.zero, new Vector3(5f, 0f, 0f), 1f,
                onComplete: () => throw new InvalidOperationException("boom"));
            int good = 0;
            long goodId = CreatePositionTween(Vector3.zero, new Vector3(7f, 0f, 0f), 1f,
                onComplete: () => good++);

            DefaultTweenHandler.TweenTask.Update(2f, 2f);

            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(badId));
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(goodId));
            Assert.AreEqual(1, good, "异常回调不得中断同帧其他 tween 的完成");
        }

        #endregion

        #region 重入安全 [REENTRANCY]

        [Test]
        public void ReentrantCallback_StopSelf_And_CreateInSameSlot_NewTweenSurvives()
        {
            long newTweenId = 0;
            long id = 0;
            id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f, onComplete: () =>
            {
                // 回调内：先 Stop 自己（应 no-op，因为已回收），再创建新 tween（大概率复用同槽位）
                DefaultTweenHandler.TweenTask.Stop(id);
                newTweenId = CreatePositionTween(Vector3.zero, Vector3.one, 1f);
            });

            DefaultTweenHandler.TweenTask.Update(2f, 2f);

            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(newTweenId),
                "回归测试：回调内创建的新 tween 不得被外层 Recycle 误杀");

            // 新 tween 在同帧创建不推进（快照迭代），下一帧正常计时
            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(0.5f, _transform.position.x, 0.0001f, "新 tween 下一帧正常推进");

            DefaultTweenHandler.TweenTask.StopAll(null);
        }

        [Test]
        public void Callback_Create_DoesNotTickSameFrame_SnapshotIteration()
        {
            long newId = 0;
            CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f, onComplete: () =>
            {
                newId = CreatePositionTween(new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f), 1f);
            });

            DefaultTweenHandler.TweenTask.Update(2f, 2f);

            // 快照迭代：新 tween 当帧不被推进，位置停留在触发者（旧 tween）的终值
            Assert.AreEqual(10f, _transform.position.x, 0.0001f,
                "快照迭代：回调中新建的 tween 当帧不得被推进（位置停留在触发者的终值）");
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(newId));
            DefaultTweenHandler.TweenTask.StopAll(null);
        }

        #endregion

        #region 时间与延迟 [TIMING & DELAY]

        [Test]
        public void StartDelay_DefersProgress()
        {
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f, startDelay: 1f);

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(0f, _transform.position.x, 0.0001f, "延迟期内不推进");
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(id));

            DefaultTweenHandler.TweenTask.Update(0.6f, 0.6f); // 延迟耗尽 + 0.1s 推进
            Assert.AreEqual(1f, _transform.position.x, 0.0001f, "延迟结束后开始计时");

            DefaultTweenHandler.TweenTask.Update(1f, 1f);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
            Assert.AreEqual(10f, _transform.position.x, 0.0001f);
        }

        [Test]
        public void DurationZero_CompletesNextUpdate_WithoutNaN()
        {
            int callbacks = 0;
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 0f,
                onComplete: () => callbacks++);

            // dt=0 首帧：不得产生 NaN（0/0 场景）；Duration=0 语义 = 首次 Update 即完成
            DefaultTweenHandler.TweenTask.Update(0f, 0f);
            Assert.IsFalse(float.IsNaN(_transform.position.x), "Duration=0 且 dt=0 不得产生 NaN");
            Assert.AreEqual(1, callbacks, "Duration=0 首次 Update 即完成（文档化契约）");
            Assert.AreEqual(10f, _transform.position.x, 0.0001f);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));

            // 已完成后再次 Update 不得重复回调
            DefaultTweenHandler.TweenTask.Update(0.016f, 0.016f);
            Assert.AreEqual(1, callbacks, "完成后不得重复回调");
        }

        [Test]
        public void UseUnscaledTime_UsesUnscaledDelta()
        {
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f, useUnscaledTime: true);

            DefaultTweenHandler.TweenTask.Update(0.3f /*scaled*/, 0.6f /*unscaled*/);
            Assert.AreEqual(6f, _transform.position.x, 0.0001f, "应使用 unscaled dt");

            DefaultTweenHandler.TweenTask.Update(0f, 0.6f);
            Assert.AreEqual(10f, _transform.position.x, 0.0001f);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
        }

        [Test]
        public void Delay_CountsDown_And_FiresOnce()
        {
            int callbacks = 0;
            var state = new DefaultTweenHandler.TweenState
            {
                Target = null,
                UnityObject = null,
                Duration = 1f,
                OperationType = DefaultTweenHandler.TweenOperationType.Delay,
                OnComplete = () => callbacks++,
                Cycles = 1,
                CycleMode = TweenUtility.ECycleMode.Restart,
            };
            long id = DefaultTweenHandler.TweenTask.Create(in state);

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(0, callbacks);
            DefaultTweenHandler.TweenTask.Update(0.6f, 0.6f);
            Assert.AreEqual(1, callbacks);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
        }

        #endregion

        #region 参数校验 [VALIDATION]

        [Test]
        public void NegativeDuration_Throws()
        {
            Assert.Throws<GameException>(() => CreatePositionTween(Vector3.zero, Vector3.one, -1f));
        }

        [Test]
        public void CyclesLessThanOne_Throws()
        {
            Assert.Throws<GameException>(() => CreatePositionTween(Vector3.zero, Vector3.one, 1f, cycles: 0));
        }

        [Test]
        public void NegativeStartDelay_Throws()
        {
            Assert.Throws<GameException>(() => CreatePositionTween(Vector3.zero, Vector3.one, 1f, startDelay: -0.1f));
        }

        #endregion

        #region 循环模式 [CYCLE MODES]

        [Test]
        public void Cycles2_Restart_PlaysTwice_And_CallbacksOnceAtEnd()
        {
            int callbacks = 0;
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                cycles: 2, cycleMode: TweenUtility.ECycleMode.Restart, onComplete: () => callbacks++);

            DefaultTweenHandler.TweenTask.Update(1f, 1f); // 第 1 循环完成（dt 恰落边界 → 余量 0，帧值映射为下一循环起点）
            Assert.AreEqual(0, callbacks, "第 1 循环结束不回调");
            Assert.AreEqual(0f, _transform.position.x, 0.0001f, "边界帧（余量 0）写入第 2 循环起点");

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f); // 第 2 循环半程
            Assert.AreEqual(5f, _transform.position.x, 0.0001f, "Restart 从头重放");

            DefaultTweenHandler.TweenTask.Update(0.6f, 0.6f);
            Assert.AreEqual(1, callbacks);
            Assert.AreEqual(10f, _transform.position.x, 0.0001f);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
        }

        [Test]
        public void CycleBoundary_CarriesOvershootRemainder()
        {
            // 一次 dt 跨越循环边界：余量应带入下一循环（无每循环时间损耗）
            CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                cycles: 2, cycleMode: TweenUtility.ECycleMode.Restart);

            DefaultTweenHandler.TweenTask.Update(1.25f, 1.25f); // 循环 1 完成 + 0.25s 余量
            Assert.AreEqual(2.5f, _transform.position.x, 0.0001f,
                "overshoot 余量带入第 2 循环（0.25/1.0 × 10）");
            DefaultTweenHandler.TweenTask.StopAll(null);
        }

        [Test]
        public void Yoyo_SecondCycle_ReturnsToStart()
        {
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                cycles: 2, cycleMode: TweenUtility.ECycleMode.Yoyo);

            DefaultTweenHandler.TweenTask.Update(1f, 1f);
            Assert.AreEqual(10f, _transform.position.x, 0.0001f, "Yoyo 第 1 循环终点");

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            // Yoyo 回程 = 交换起止值后重新施加缓动：Linear 半程 → 5
            Assert.AreEqual(5f, _transform.position.x, 0.0001f);

            DefaultTweenHandler.TweenTask.Update(0.6f, 0.6f);
            Assert.AreEqual(0f, _transform.position.x, 0.0001f, "Yoyo 2 循环结束回到起点");
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
        }

        [Test]
        public void Rewind_ReversesTrajectory_And_DiffersFromYoyo()
        {
            // OutQuad（非对称缓动）区分 Rewind 与 Yoyo：
            // 回程 τ=0.25 处——Rewind = start + ease(1-0.25)*Δ = start + 0.9375Δ
            //                        Yoyo   = end + ease(0.25)*(start-end) = start + 0.4375Δ
            long rewindId = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                cycles: 2, cycleMode: TweenUtility.ECycleMode.Rewind,
                ease: TweenUtility.EEase.OutQuad);

            DefaultTweenHandler.TweenTask.Update(1f, 1f);
            Assert.AreEqual(10f, _transform.position.x, 0.0001f, "Rewind 第 1 循环终点");

            DefaultTweenHandler.TweenTask.Update(0.25f, 0.25f);
            Assert.AreEqual(9.375f, _transform.position.x, 0.001f,
                "Rewind 倒放：ease 作用于 1-t（OutQuad(0.75)=0.9375）");

            DefaultTweenHandler.TweenTask.Update(0.85f, 0.85f);
            Assert.AreEqual(0f, _transform.position.x, 0.001f, "Rewind 2 循环结束回到起点");
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(rewindId));

            // 对照组：Yoyo 同参数在回程 τ=0.25 处取 4.375
            CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                cycles: 2, cycleMode: TweenUtility.ECycleMode.Yoyo,
                ease: TweenUtility.EEase.OutQuad);
            DefaultTweenHandler.TweenTask.Update(1f, 1f);
            DefaultTweenHandler.TweenTask.Update(0.25f, 0.25f);
            Assert.AreEqual(5.625f, _transform.position.x, 0.001f,
                "Yoyo 回程重新施加缓动：swap 后 start=10,end=0，x = 10 + OutQuad(0.25)×(0-10) = 5.625——与 Rewind 轨迹不同");
            DefaultTweenHandler.TweenTask.StopAll(null);
        }

        [Test]
        public void Incremental_SecondCycleContinuesFromEnd()
        {
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                cycles: 2, cycleMode: TweenUtility.ECycleMode.Incremental);

            DefaultTweenHandler.TweenTask.Update(1f, 1f);
            Assert.AreEqual(10f, _transform.position.x, 0.0001f);

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(15f, _transform.position.x, 0.0001f, "Incremental 第 2 循环 10→20 的半程");

            DefaultTweenHandler.TweenTask.Update(0.6f, 0.6f);
            Assert.AreEqual(20f, _transform.position.x, 0.0001f);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
        }

        #endregion

        #region 销毁目标 [DESTROYED TARGET]

        [Test]
        public void DestroyedTarget_KillsTween_WithoutCallback()
        {
            int callbacks = 0;
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                onComplete: () => callbacks++);

            DefaultTweenHandler.TweenTask.Update(0.25f, 0.25f);
            UnityEngine.Object.DestroyImmediate(_go);

            DefaultTweenHandler.TweenTask.Update(0.016f, 0.016f);

            Assert.AreEqual(0, callbacks, "目标销毁 = kill，不触发 OnComplete");
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));

            // 防止 TearDown 二次销毁
            _go = null;
            _transform = null;
        }

        [Test]
        public void DestroyedDelayTarget_Warns_WhenWarnIfTargetDestroyed()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Tween target destroyed"));
            int callbacks = 0;
            var state = new DefaultTweenHandler.TweenState
            {
                Target = _go,
                UnityObject = _go,
                Duration = 1f,
                OperationType = DefaultTweenHandler.TweenOperationType.Delay,
                OnComplete = () => callbacks++,
                Cycles = 1,
                CycleMode = TweenUtility.ECycleMode.Restart,
                WarnIfTargetDestroyed = true,
            };
            long id = DefaultTweenHandler.TweenTask.Create(in state);

            UnityEngine.Object.DestroyImmediate(_go);
            DefaultTweenHandler.TweenTask.Update(0.016f, 0.016f);

            Assert.AreEqual(0, callbacks);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));

            _go = null;
            _transform = null;
        }

        [Test]
        public void DestroyedTarget_SilentKill_WhenNoWarnFlag()
        {
            int callbacks = 0;
            var state = new DefaultTweenHandler.TweenState
            {
                Target = _go,
                UnityObject = _go,
                Duration = 1f,
                OperationType = DefaultTweenHandler.TweenOperationType.Delay,
                OnComplete = () => callbacks++,
                Cycles = 1,
                CycleMode = TweenUtility.ECycleMode.Restart,
                WarnIfTargetDestroyed = false,
            };
            long id = DefaultTweenHandler.TweenTask.Create(in state);

            UnityEngine.Object.DestroyImmediate(_go);
            DefaultTweenHandler.TweenTask.Update(0.016f, 0.016f); // 无告警、无回调、直接 kill

            Assert.AreEqual(0, callbacks);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));

            _go = null;
            _transform = null;
        }

        #endregion

        #region 自定义与贝塞尔 [CUSTOM & BEZIER]

        [Test]
        public void CustomObjectFloat_ZeroClosurePath_ReceivesTargetAndValue()
        {
            var box = new object();
            float received = -1f;
            object receivedTarget = null;
            var state = new DefaultTweenHandler.TweenState
            {
                Target = box,
                OperationType = DefaultTweenHandler.TweenOperationType.CustomFloat,
                Duration = 1f,
                StartX = 0f,
                EndX = 10f,
                OnUpdateObjectFloat = (t, v) =>
                {
                    receivedTarget = t;
                    received = v;
                },
                Cycles = 1,
                CycleMode = TweenUtility.ECycleMode.Restart,
            };
            long id = DefaultTweenHandler.TweenTask.Create(in state);

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);

            Assert.AreSame(box, receivedTarget);
            Assert.AreEqual(5f, received, 0.0001f);
            DefaultTweenHandler.TweenTask.Stop(id);
        }

        [Test]
        public void BezierPath_TwoPointLine_Midpoint()
        {
            var path = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) };
            var state = new DefaultTweenHandler.TweenState
            {
                Target = _transform,
                UnityObject = _transform,
                OperationType = DefaultTweenHandler.TweenOperationType.MoveBezierPath,
                Duration = 1f,
                PathPoints = path,
                Cycles = 1,
                CycleMode = TweenUtility.ECycleMode.Restart,
            };
            long id = DefaultTweenHandler.TweenTask.Create(in state);

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(5f, _transform.position.x, 0.0001f, "二阶贝塞尔退化为直线中点");

            DefaultTweenHandler.TweenTask.Update(0.6f, 0.6f);
            Assert.AreEqual(10f, _transform.position.x, 0.0001f);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
        }

        [Test]
        public void BezierPath_QuadraticKnownValue()
        {
            // 三点二次贝塞尔 t=0.5：(P0 + 2P1 + P2) / 4
            var path = new[] { Vector3.zero, new Vector3(10f, 10f, 0f), new Vector3(20f, 0f, 0f) };
            var state = new DefaultTweenHandler.TweenState
            {
                Target = _transform,
                UnityObject = _transform,
                OperationType = DefaultTweenHandler.TweenOperationType.MoveBezierPath,
                Duration = 1f,
                PathPoints = path,
                Cycles = 1,
                CycleMode = TweenUtility.ECycleMode.Restart,
            };
            long id = DefaultTweenHandler.TweenTask.Create(in state);

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(10f, _transform.position.x, 0.0001f);
            Assert.AreEqual(5f, _transform.position.y, 0.0001f, "(0 + 2×10 + 0) / 4");

            DefaultTweenHandler.TweenTask.Stop(id);
        }

        #endregion

        #region 暂停与恢复 [PAUSE & RESUME]

        [Test]
        public void Pause_FreezesProgress_ResumeContinues()
        {
            int callbacks = 0;
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                onComplete: () => callbacks++);

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(5f, _transform.position.x, 0.0001f);

            DefaultTweenHandler.TweenTask.Pause(id);
            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(5f, _transform.position.x, 0.0001f, "暂停期间进度冻结");
            Assert.AreEqual(0, callbacks, "暂停期间不完成");
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(id), "暂停不影响存活");

            DefaultTweenHandler.TweenTask.Resume(id);
            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(10f, _transform.position.x, 0.0001f, "恢复后从冻结处继续");
            Assert.AreEqual(1, callbacks);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id));
        }

        [Test]
        public void Pause_DuringDelay_FreezesCountdown()
        {
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f, startDelay: 1f);

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f); // 延迟剩 0.5
            DefaultTweenHandler.TweenTask.Pause(id);
            DefaultTweenHandler.TweenTask.Update(2f, 2f);
            Assert.AreEqual(0f, _transform.position.x, 0.0001f, "暂停冻结延迟倒计时");
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(id));

            DefaultTweenHandler.TweenTask.Resume(id);
            DefaultTweenHandler.TweenTask.Update(0.6f, 0.6f); // 延迟耗尽 + 0.1 进度
            Assert.AreEqual(1f, _transform.position.x, 0.0001f, "恢复后延迟继续倒计时");
            DefaultTweenHandler.TweenTask.StopAll(null);
        }

        [Test]
        public void Pause_DeadId_IsSilentNoOp()
        {
            long id = CreatePositionTween(Vector3.zero, Vector3.one, 0.1f);
            DefaultTweenHandler.TweenTask.Update(1f, 1f); // 完成

            Assert.DoesNotThrow(() => DefaultTweenHandler.TweenTask.Pause(id));
            Assert.DoesNotThrow(() => DefaultTweenHandler.TweenTask.Resume(id));
        }

        [Test]
        public void PausedTween_CanStillBeStopped_AndCompleted()
        {
            int callbacks = 0;
            long id1 = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f, onComplete: () => callbacks++);
            long id2 = CreatePositionTween(Vector3.zero, new Vector3(20f, 0f, 0f), 1f, onComplete: () => callbacks++);

            DefaultTweenHandler.TweenTask.Pause(id1);
            DefaultTweenHandler.TweenTask.Pause(id2);

            DefaultTweenHandler.TweenTask.Stop(id1);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id1), "暂停态可 Stop");

            DefaultTweenHandler.TweenTask.Complete(id2);
            Assert.AreEqual(1, callbacks, "暂停态可强制 Complete（触发回调）");
            Assert.AreEqual(20f, _transform.position.x, 0.0001f);
            Assert.IsFalse(DefaultTweenHandler.TweenTask.IsAlive(id2));
        }

        #endregion

        #region 异步等待 [WAIT ASYNC]

        [Test]
        public void WaitAsync_CompletesWhenTweenCompletes()
        {
            int callbacks = 0;
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                onComplete: () => callbacks++);

            UniTask task = DefaultTweenHandler.TweenTask.WaitAsync(id, CancellationToken.None);
            Assert.AreEqual(1, DefaultTweenHandler.TweenTask.PendingAwaiterCount);

            DefaultTweenHandler.TweenTask.Update(2f, 2f); // 完成 → TrySetResult

            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult(), "完成 → 正常返回");
            Assert.AreEqual(1, callbacks);
            Assert.AreEqual(0, DefaultTweenHandler.TweenTask.PendingAwaiterCount, "注册表无泄漏");
        }

        [Test]
        public void WaitAsync_CompletesNormally_WhenStopped()
        {
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f);
            UniTask task = DefaultTweenHandler.TweenTask.WaitAsync(id, CancellationToken.None);

            DefaultTweenHandler.TweenTask.Stop(id);

            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult(),
                "契约：不区分结束原因——Stop 同样正常返回");
            Assert.AreEqual(0, DefaultTweenHandler.TweenTask.PendingAwaiterCount);
        }

        [Test]
        public void WaitAsync_CompletesNormally_WhenTargetKilled()
        {
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f);
            UniTask task = DefaultTweenHandler.TweenTask.WaitAsync(id, CancellationToken.None);

            UnityEngine.Object.DestroyImmediate(_go);
            DefaultTweenHandler.TweenTask.Update(0.016f, 0.016f); // 销毁 kill

            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult(),
                "契约：不区分结束原因——销毁 kill 同样正常返回");
            _go = null;
            _transform = null;
        }

        [Test]
        public void WaitAsync_ExternalCancel_AbandonsWait_TweenContinues()
        {
            int callbacks = 0;
            using var cts = new CancellationTokenSource();
            long id = CreatePositionTween(Vector3.zero, new Vector3(10f, 0f, 0f), 1f,
                onComplete: () => callbacks++);

            UniTask task = DefaultTweenHandler.TweenTask.WaitAsync(id, cts.Token);
            Assert.AreEqual(1, DefaultTweenHandler.TweenTask.PendingAwaiterCount);

            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult(),
                "外部取消 → OCE");
            Assert.AreEqual(0, DefaultTweenHandler.TweenTask.PendingAwaiterCount, "取消即注销");
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(id), "外部取消仅放弃等待，tween 继续");

            DefaultTweenHandler.TweenTask.Update(2f, 2f);
            Assert.AreEqual(1, callbacks, "tween 照常完成");
        }

        [Test]
        public void WaitAsync_DeadId_CompletesImmediately()
        {
            long id = CreatePositionTween(Vector3.zero, Vector3.one, 0.1f);
            DefaultTweenHandler.TweenTask.Update(1f, 1f); // 完成

            UniTask task = DefaultTweenHandler.TweenTask.WaitAsync(id, CancellationToken.None);
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult(), "已结束 id → 立即完成");
            Assert.AreEqual(0, DefaultTweenHandler.TweenTask.PendingAwaiterCount);
        }

        [Test]
        public void WaitAsync_AlreadyCanceledToken_ReturnsCanceled()
        {
            long id = CreatePositionTween(Vector3.zero, Vector3.one, 1f);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            UniTask task = DefaultTweenHandler.TweenTask.WaitAsync(id, cts.Token);

            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.AreEqual(0, DefaultTweenHandler.TweenTask.PendingAwaiterCount);
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(id));
            DefaultTweenHandler.TweenTask.StopAll(null);
        }

        #endregion

        #region 规模 [SCALE]

        [Test]
        public void Scale_1000ConcurrentTweens_AllComplete()
        {
            const int COUNT = 1000;
            int completed = 0;
            var ids = new long[COUNT];

            for (int i = 0; i < COUNT; i++)
            {
                var state = new DefaultTweenHandler.TweenState
                {
                    Target = new object(),
                    OperationType = DefaultTweenHandler.TweenOperationType.CustomFloat,
                    Duration = 1f,
                    StartX = 0f,
                    EndX = 1f,
                    OnComplete = () => completed++,
                    Cycles = 1,
                    CycleMode = TweenUtility.ECycleMode.Restart,
                };
                ids[i] = DefaultTweenHandler.TweenTask.Create(in state);
            }

            DefaultTweenHandler.TweenTask.Update(0.5f, 0.5f);
            Assert.AreEqual(0, completed, "半程无完成");
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(ids[0]));
            Assert.IsTrue(DefaultTweenHandler.TweenTask.IsAlive(ids[COUNT - 1]));

            DefaultTweenHandler.TweenTask.Update(0.6f, 0.6f);
            Assert.AreEqual(COUNT, completed, "全部完成（含槽位扩容 256→1024）");
            Assert.AreEqual(0, DefaultTweenHandler.TweenTask.GetTweenCount(null), "无残留活跃 tween");
        }

        #endregion

        #region 构建助手 [BUILD HELPERS]

        private long CreatePositionTween(Vector3 start, Vector3 end, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0f, bool useUnscaledTime = false, Action onComplete = null)
        {
            return CreatePositionTweenOn(_transform, start, end, duration, ease, cycles, cycleMode,
                startDelay, useUnscaledTime, onComplete);
        }

        private static long CreatePositionTweenOn(Transform target, Vector3 start, Vector3 end, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0f, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = new DefaultTweenHandler.TweenState
            {
                Target = target,
                UnityObject = target,
                OperationType = DefaultTweenHandler.TweenOperationType.Position,
                Duration = duration,
                Ease = ease,
                Cycles = cycles,
                CycleMode = cycleMode,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                UseUnscaledTime = useUnscaledTime,
                OnComplete = onComplete,
                StartX = start.x, StartY = start.y, StartZ = start.z,
                EndX = end.x, EndY = end.y, EndZ = end.z,
            };
            return DefaultTweenHandler.TweenTask.Create(in state);
        }

        #endregion
    }
}
