using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Scene;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Scene
{
    /// <summary>
    /// 场景进度回报契约的 PlayMode 用例：进度变化才回报、首轮询值必报、回落也露出、成功收尾一次 1.0、失败不伪报。
    /// </summary>
    /// <remarks>
    /// 被测的 <see cref="DefaultSceneHandler.AwaitSceneHandle"/> 每轮 <c>await UniTask.Yield</c>，续体要靠 PlayerLoop 推进； <br />
    /// EditMode 的 NUnit 用例在主线程里阻塞、没有能推进 UniTask 的帧泵（<c>MainThreadDispatcher.Pump()</c> 只驱动它自己的队列，
    /// 且本仓 EditMode 用例一律靠 <c>UniTaskStatus</c> 断挂起、从不把 UniTask 跑到完成），故这条契约只能住 PlayMode 侧。
    /// </remarks>
    [TestFixture]
    public sealed class SceneProgressDedupPlayModeTests
    {
        private const int MAX_FRAMES = 60;

        /// <summary>
        /// 轮询驱动的场景句柄替身——<see cref="IsDone"/> 每被读一次推进一格，<see cref="Progress"/> 是当前格的纯函数。
        /// </summary>
        /// <remarks>
        /// 推进绑在 <see cref="IsDone"/> 上（等待循环每轮恰好读一次它），使回报序列与跑了多少帧、也与一轮里 <see cref="Progress"/> 被读几次都无关。
        /// </remarks>
        private sealed class ScriptedSceneHandle : ResourceSceneHandle
        {
            private readonly float[] _script;
            private int _polls;

            public ScriptedSceneHandle(float[] script, string error)
            {
                _script = script;
                Error = error;
            }

            public override string Error { get; }

            public override bool IsDone
            {
                get
                {
                    _polls++;
                    return _polls > _script.Length;
                }
            }

            public override float Progress => _script[Mathf.Clamp(_polls - 1, 0, _script.Length - 1)];

            public override UnityEngine.SceneManagement.Scene SceneObject => default;
            public override bool UnSuspend() => true;
            public override bool ActivateScene() => true;
            public override IResourceOperation UnloadAsync() => null;
            public override void Release() { }
        }

        /// <summary>
        /// 帧驱动等待循环到收尾；超过 <see cref="MAX_FRAMES"/> 帧仍挂起即判失败（契约里的循环必然收敛）。
        /// </summary>
        private IEnumerator AwaitToEnd(float[] script, string error, List<float> reports)
        {
            var handle = new ScriptedSceneHandle(script, error);
            UniTask task = DefaultSceneHandler.AwaitSceneHandle(handle, reports.Add, CancellationToken.None);

            int frames = 0;
            while (task.Status == UniTaskStatus.Pending && frames < MAX_FRAMES)
            {
                frames++;
                yield return null;
            }

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status,
                $"等待循环未在 {MAX_FRAMES} 帧内收敛（实跑 {frames} 帧）——UniTask.Yield 续体没被推进，是用例环境不对而非契约不对");
        }

        [UnityTest]
        public IEnumerator AwaitSceneHandle_SameValueAcrossPolls_ReportsOncePerValue()
        {
            var reports = new List<float>();
            yield return AwaitToEnd(new[] { 0f, 0f, 0.5f, 0.5f, 0.5f }, null, reports);

            CollectionAssert.AreEqual(new[] { 0f, 0.5f, 1f }, reports,
                "0 连续询到三次、0.5 连续询到两次，各自只应回报一次，末尾再加一次收尾 1.0");
        }

        [UnityTest]
        public IEnumerator AwaitSceneHandle_FirstPolledValue_IsAlwaysReported()
        {
            var reports = new List<float>();
            yield return AwaitToEnd(new[] { 0f }, null, reports);

            CollectionAssert.AreEqual(new[] { 0f, 1f }, reports, "判重的起始基准值落在值域外，首轮询到的进度不得被它吞掉");
        }

        [UnityTest]
        public IEnumerator AwaitSceneHandle_ProgressRegresses_IsStillReported()
        {
            var reports = new List<float>();
            yield return AwaitToEnd(new[] { 0.5f, 0.2f }, null, reports);

            CollectionAssert.AreEqual(new[] { 0.5f, 0.2f, 1f }, reports,
                "判据取的是不等而非递增：后端进度回落也必须露出来");
        }

        [UnityTest]
        public IEnumerator AwaitSceneHandle_HandleWithError_NoCompletionReport()
        {
            var reports = new List<float>();
            yield return AwaitToEnd(new[] { 0f, 0f, 0.5f }, "backend refused", reports);

            CollectionAssert.AreEqual(new[] { 0f, 0.5f }, reports, "句柄带错误时不得伪报收尾 1.0");
        }
    }
}
