using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Bootstrap;
using Moirai.Atropos.Kernel.Composition;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Bootstrap
{
    public sealed class GameRootLifecycleTests
    {
        internal sealed class Probe : IServiceModule, IAsyncInitializable, IAsyncStoppable,
            IAsyncDisposableService, IDisposable
        {
            internal int Initializations;
            internal int Stops;
            internal int Disposals;
            internal bool FailInitialization;
            internal UniTaskCompletionSource<bool> StopGate;

            public void Register(IServiceRegistrar registrar)
            {
                registrar.Add(new ServiceDescriptor(typeof(Probe), Type.EmptyTypes,
                    ServiceLifetime.Root, _ => this));
            }
            public UniTask InitializeAsync(CancellationToken cancellationToken)
            {
                Initializations++;
                return FailInitialization
                    ? UniTask.FromException(new InvalidOperationException("boot failed"))
                    : UniTask.CompletedTask;
            }
            public UniTask StopAsync(CancellationToken cancellationToken)
            {
                Stops++;
                return StopGate == null ? UniTask.CompletedTask : StopGate.Task;
            }
            public UniTask DisposeAsync()
            {
                Disposals++;
                return UniTask.CompletedTask;
            }
            public void Dispose() { }
        }

        [UnityTest]
        public IEnumerator StartThenShutdown_ReachesReadyAndReleasesOnce() => UniTask.ToCoroutine(async () =>
        {
            var probe = new Probe();
            var root = new GameRoot(new IServiceModule[] { probe });
            Assert.AreEqual(BootPhase.NotStarted, root.Phase);
            await root.StartAsync(CancellationToken.None);
            await root.StartAsync(CancellationToken.None);
            Assert.AreEqual(BootPhase.Ready, root.Phase);
            Assert.AreEqual(1, probe.Initializations);
            await root.ShutdownAsync(CancellationToken.None);
            await root.ShutdownAsync(CancellationToken.None);
            Assert.AreEqual(BootPhase.Stopped, root.Phase);
            Assert.AreEqual(1, probe.Stops);
            Assert.AreEqual(1, probe.Disposals);
        });

        [UnityTest]
        public IEnumerator Shutdown_WhileStopPending_OtherWaiterStillDrains() => UniTask.ToCoroutine(async () =>
        {
            var probe = new Probe { StopGate = new UniTaskCompletionSource<bool>() };
            var root = new GameRoot(new IServiceModule[] { probe });
            await root.StartAsync(CancellationToken.None);
            UniTask first = root.ShutdownAsync(CancellationToken.None);
            UniTask second = root.ShutdownAsync(CancellationToken.None);
            using (var caller = new CancellationTokenSource())
            {
                UniTask cancelled = root.ShutdownAsync(caller.Token);
                caller.Cancel();
                bool cancelledResult = await cancelled.SuppressCancellationThrow();
                Assert.IsTrue(cancelledResult);
                Assert.AreEqual(UniTaskStatus.Pending, second.Status);
                Assert.AreEqual(0, probe.Disposals);
                probe.StopGate.TrySetResult(true);
                await first;
                await second;
            }
            Assert.AreEqual(1, probe.Disposals);
            Assert.AreEqual(BootPhase.Stopped, root.Phase);
        });

        [UnityTest]
        public IEnumerator TwoRoots_AreIndependent() => UniTask.ToCoroutine(async () =>
        {
            var first = new Probe();
            var second = new Probe();
            var rootA = new GameRoot(new IServiceModule[] { first });
            var rootB = new GameRoot(new IServiceModule[] { second });
            await rootA.StartAsync(CancellationToken.None);
            await rootB.StartAsync(CancellationToken.None);
            await rootA.ShutdownAsync(CancellationToken.None);
            Assert.AreEqual(BootPhase.Ready, rootB.Phase);
            Assert.AreEqual(0, second.Disposals);
            await rootB.ShutdownAsync(CancellationToken.None);
            Assert.AreEqual(1, first.Disposals);
            Assert.AreEqual(1, second.Disposals);
        });
    }
}
