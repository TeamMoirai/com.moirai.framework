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
    public sealed class ResourceMaintenanceTests
    {
        private sealed class Clock : IClock
        {
            public float Time { get; set; }
            public float DeltaTime { get; set; }
            public double RealtimeSinceStartup { get; set; }
        }

        private sealed class Loop : IPlayerLoop
        {
            internal Action Tick;
            public IDisposable AddUpdateListener(Action listener) { Tick = listener; return new Handle(); }
            public IDisposable AddFixedUpdateListener(Action listener) => new Handle();
            public IDisposable AddLateUpdateListener(Action listener) => new Handle();
            private sealed class Handle : IDisposable { public void Dispose() { } }
        }

        private sealed class Loader : IAssetLoader
        {
            internal int Disposals;
            internal Texture2D Texture;

            public UniTask<IAssetPayload<T>> LoadAssetAsync<T>(ResourceKey key,
                CancellationToken ownerToken) where T : UnityEngine.Object
            {
                return UniTask.FromResult<IAssetPayload<T>>(
                    (IAssetPayload<T>)(object)new Payload((Texture2D)(object)Texture, () => Disposals++));
            }

            public UniTask<IScenePayload> LoadSceneAsync(ResourceKey key,
                UnityEngine.SceneManagement.LoadSceneParameters parameters, bool activateOnLoad,
                CancellationToken ownerToken)
                => UniTask.FromException<IScenePayload>(new NotSupportedException());

            private sealed class Payload : IAssetPayload<Texture2D>
            {
                private readonly Action _onDispose;
                private bool _disposed;
                internal Payload(Texture2D asset, Action onDispose) { Asset = asset; _onDispose = onDispose; }
                public Texture2D Asset { get; }
                public UniTask DisposeAsync()
                {
                    if (_disposed) return UniTask.CompletedTask;
                    _disposed = true;
                    _onDispose();
                    return UniTask.CompletedTask;
                }
            }
        }

        [UnityTest]
        public IEnumerator LeaseOptions_ControlEviction() => UniTask.ToCoroutine(async () =>
        {
            var texture = new Texture2D(1, 1);
            var loader = new Loader { Texture = texture };
            var clock = new Clock();
            var loop = new Loop();
            var service = new ResourceService(loader,
                new ResourceOptions { KeepAliveSeconds = 5d, MaxEvictionsPerUpdate = 32 }, clock, loop);
            try
            {
                await service.InitializeAsync(CancellationToken.None);
                var pinned = await service.AcquireLeaseAsync<Texture2D>(new ResourceKey("pinned"),
                    ResourceLeaseOption.Pin);
                pinned.Dispose();
                loop.Tick();
                await UniTask.Yield();
                await UniTask.Yield();
                Assert.AreEqual(0, loader.Disposals);

                var immediate = await service.AcquireLeaseAsync<Texture2D>(new ResourceKey("immediate"));
                immediate.Dispose();
                loop.Tick();
                await UniTask.Yield();
                await UniTask.Yield();
                Assert.AreEqual(1, loader.Disposals);

                var keepAlive = await service.AcquireLeaseAsync<Texture2D>(new ResourceKey("keep"),
                    ResourceLeaseOption.KeepAliveOnRelease);
                keepAlive.Dispose();
                clock.RealtimeSinceStartup += 1d;
                loop.Tick();
                await UniTask.Yield();
                await UniTask.Yield();
                Assert.AreEqual(1, loader.Disposals);
                clock.RealtimeSinceStartup += 10d;
                loop.Tick();
                await UniTask.Yield();
                await UniTask.Yield();
                Assert.AreEqual(2, loader.Disposals);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        });
    }
}
