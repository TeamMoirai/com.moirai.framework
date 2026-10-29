using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Unity;
using NUnit.Framework;
using UnityEngine.TestTools;
using KernelLogger = Moirai.Atropos.Kernel.Logging.ILogger;
using LogLevel = Moirai.Atropos.Kernel.Logging.LogLevel;

namespace Kernel
{
    public sealed class UnityPlayerLoopTests
    {
        private sealed class RecordingLogger : KernelLogger
        {
            public bool IsEnabled(LogLevel level, string tag) => true;
            public void Log(LogLevel level, string tag, string message, Exception exception = null)
                => Assert.Fail(exception?.ToString() ?? message);
        }

        [UnityTest]
        public IEnumerator Initialize_Repeatedly_DrivesOneUpdatePerFrame() => UniTask.ToCoroutine(async () =>
        {
            var loop = new UnityPlayerLoop(new RecordingLogger());
            int updates = 0;
            int late = 0;
            try
            {
                loop.AddUpdateListener(() => updates++);
                loop.AddLateUpdateListener(() => late++);
                await loop.InitializeAsync(CancellationToken.None);
                await loop.InitializeAsync(CancellationToken.None);
                await UniTask.NextFrame();
                await UniTask.NextFrame();
                int before = updates;
                int lateBefore = late;
                await UniTask.NextFrame();
                Assert.That(updates - before, Is.EqualTo(1));
                Assert.That(late - lateBefore, Is.EqualTo(1));
            }
            finally { loop.Dispose(); }
        });

        [UnityTest]
        public IEnumerator Dispose_OneInstance_LeavesOtherLoopAndUniTaskAlive() => UniTask.ToCoroutine(async () =>
        {
            var first = new UnityPlayerLoop(new RecordingLogger());
            var second = new UnityPlayerLoop(new RecordingLogger());
            int removed = 0;
            int retained = 0;
            try
            {
                first.AddUpdateListener(() => removed++);
                second.AddUpdateListener(() => retained++);
                await first.InitializeAsync(CancellationToken.None);
                await second.InitializeAsync(CancellationToken.None);
                await UniTask.NextFrame();
                first.Dispose();
                first.Dispose();
                int removedBefore = removed;
                int retainedBefore = retained;
                await UniTask.NextFrame();
                await UniTask.NextFrame();
                Assert.That(removed, Is.EqualTo(removedBefore));
                Assert.That(retained, Is.GreaterThan(retainedBefore));
                Assert.Throws<ObjectDisposedException>(() => first.AddUpdateListener(() => { }));
            }
            finally { first.Dispose(); second.Dispose(); }
        });
    }
}
