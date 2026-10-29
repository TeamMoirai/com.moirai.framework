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
    public sealed class GameRootEditModeTests
    {
        private sealed class Probe : IServiceModule, IAsyncInitializable, IAsyncStoppable,
            IAsyncDisposableService, IDisposable
        {
            internal int Initializations;
            internal int Stops;
            internal int Disposals;

            public void Register(IServiceRegistrar registrar)
            {
                registrar.Add(new ServiceDescriptor(typeof(Probe), Type.EmptyTypes,
                    ServiceLifetime.Root, _ => this));
            }
            public UniTask InitializeAsync(CancellationToken cancellationToken)
            {
                Initializations++;
                return UniTask.CompletedTask;
            }
            public UniTask StopAsync(CancellationToken cancellationToken)
            {
                Stops++;
                return UniTask.CompletedTask;
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
            await root.StartAsync(CancellationToken.None);
            Assert.AreEqual(BootPhase.Ready, root.Phase);
            await root.ShutdownAsync(CancellationToken.None);
            Assert.AreEqual(BootPhase.Stopped, root.Phase);
            Assert.AreEqual(1, probe.Initializations);
            Assert.AreEqual(1, probe.Stops);
            Assert.AreEqual(1, probe.Disposals);
        });
    }
}
