using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Assets;
using Moirai.Atropos.Assets.Internal;
using Moirai.Atropos.Kernel.Scheduling;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Resource
{
    public sealed class ResourceSceneTests
    {
        private sealed class Clock : IClock
        {
            public float Time => 0f;
            public float DeltaTime => 0f;
            public double RealtimeSinceStartup => 0d;
        }

        private sealed class Loop : IPlayerLoop
        {
            public IDisposable AddUpdateListener(Action listener) => new Handle();
            public IDisposable AddFixedUpdateListener(Action listener) => new Handle();
            public IDisposable AddLateUpdateListener(Action listener) => new Handle();
            private sealed class Handle : IDisposable { public void Dispose() { } }
        }

        private sealed class ScenePayload : IScenePayload
        {
            internal int Activations;
            internal int Disposals;
            public UnityEngine.SceneManagement.Scene Scene => default;
            public bool IsActivated { get; private set; }
            public UniTask ActivateAsync(CancellationToken cancellationToken)
            {
                Activations++;
                IsActivated = true;
                return UniTask.CompletedTask;
            }
            public UniTask DisposeAsync() { Disposals++; return UniTask.CompletedTask; }
        }

        private sealed class Loader : IAssetLoader
        {
            internal ScenePayload Payload = new ScenePayload();
            public UniTask<IAssetPayload<T>> LoadAssetAsync<T>(ResourceKey key,
                CancellationToken ownerToken) where T : UnityEngine.Object
                => UniTask.FromException<IAssetPayload<T>>(new NotSupportedException());
            public UniTask<IScenePayload> LoadSceneAsync(ResourceKey key,
                UnityEngine.SceneManagement.LoadSceneParameters parameters, bool activateOnLoad,
                CancellationToken ownerToken)
                => UniTask.FromResult<IScenePayload>(Payload);
        }

        [UnityTest]
        public IEnumerator SceneLease_DelegatesActivationAndDisposesOnce() => UniTask.ToCoroutine(async () =>
        {
            var loader = new Loader();
            var service = new ResourceService(loader, new ResourceOptions(), new Clock(), new Loop());
            await service.InitializeAsync(CancellationToken.None);
            try
            {
                ISceneLease lease = await service.LoadSceneAsync(new ResourceKey("scene"),
                    new UnityEngine.SceneManagement.LoadSceneParameters(
                        UnityEngine.SceneManagement.LoadSceneMode.Additive), true);
                Assert.IsFalse(lease.IsActivated);
                await lease.ActivateAsync(CancellationToken.None);
                Assert.IsTrue(lease.IsActivated);
                Assert.AreEqual(1, loader.Payload.Activations);
                await lease.DisposeAsync();
                await lease.DisposeAsync();
                Assert.AreEqual(1, loader.Payload.Disposals);
            }
            finally { await service.StopAsync(CancellationToken.None); }
        });
    }
}
