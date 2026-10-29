using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Scheduling;
using Moirai.Atropos.Assets;
using Moirai.Atropos.Assets.Internal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Resource
{
    public sealed class ResourceLeaseTests
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
            public IDisposable AddUpdateListener(Action listener) { Tick = listener; return new Lease(); }
            public IDisposable AddFixedUpdateListener(Action listener) => new Lease();
            public IDisposable AddLateUpdateListener(Action listener) => new Lease();
            private sealed class Lease : IDisposable { public void Dispose() { } }
        }

        private sealed class Payload : IAssetPayload<Texture2D>
        {
            internal Payload(Texture2D asset, Action onDispose) { Asset = asset; OnDispose = onDispose; }
            public Texture2D Asset { get; }
            internal Action OnDispose { get; }
            public UniTask DisposeAsync() { OnDispose(); return UniTask.CompletedTask; }
        }

        private sealed class Loader : IAssetLoader
        {
            internal int Loads;
            internal int Disposals;
            internal UniTaskCompletionSource<bool> Gate;
            internal Texture2D Texture;

            public async UniTask<IAssetPayload<T>> LoadAssetAsync<T>(ResourceKey key,
                CancellationToken ownerToken) where T : UnityEngine.Object
            {
                Loads++;
                if (Gate != null) await Gate.Task;
                await UniTask.SwitchToMainThread();
                return (IAssetPayload<T>)(object)new Payload((Texture2D)(object)Texture,
                    () => Disposals++);
            }

            public UniTask<IScenePayload> LoadSceneAsync(ResourceKey key,
                UnityEngine.SceneManagement.LoadSceneParameters parameters, bool activateOnLoad,
                CancellationToken ownerToken)
                => UniTask.FromException<IScenePayload>(new NotSupportedException());
        }

        private static ResourceService Create(Loader loader, out Clock clock)
        {
            clock = new Clock();
            return new ResourceService(loader, new ResourceOptions(), clock, new Loop());
        }

        [UnityTest]
        public IEnumerator TwoLeases_SharePayload_AndReleaseIndependently() => UniTask.ToCoroutine(async () =>
        {
            var texture = new Texture2D(1, 1);
            var loader = new Loader { Texture = texture };
            var service = Create(loader, out _);
            try
            {
                await service.InitializeAsync(CancellationToken.None);
                var first = await service.AcquireLeaseAsync<Texture2D>(new ResourceKey("tex"));
                var second = await service.AcquireLeaseAsync<Texture2D>(new ResourceKey("tex"));
                Assert.That(first.Asset, Is.SameAs(texture));
                Assert.That(second.Asset, Is.SameAs(texture));
                Assert.AreEqual(1, loader.Loads);
                first.Dispose();
                Assert.IsFalse(first.IsValid);
                Assert.IsTrue(second.IsValid);
                second.Dispose();
                Assert.IsFalse(second.IsValid);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        });

        [UnityTest]
        public IEnumerator CancelOneWaiter_OtherStillReceivesAsset() => UniTask.ToCoroutine(async () =>
        {
            var texture = new Texture2D(1, 1);
            var loader = new Loader { Texture = texture, Gate = new UniTaskCompletionSource<bool>() };
            var service = Create(loader, out _);
            try
            {
                await service.InitializeAsync(CancellationToken.None);
                using (var caller = new CancellationTokenSource())
                {
                    var cancelled = service.AcquireLeaseAsync<Texture2D>(new ResourceKey("tex"),
                        ResourceLeaseOption.None, caller.Token);
                    var waiting = service.AcquireLeaseAsync<Texture2D>(new ResourceKey("tex"));
                    caller.Cancel();
                    var cancelledResult = await cancelled.SuppressCancellationThrow();
                    Assert.IsTrue(cancelledResult.IsCanceled);
                    loader.Gate.TrySetResult(true);
                    var lease = await waiting;
                    Assert.That(lease.Asset, Is.SameAs(texture));
                    Assert.AreEqual(1, loader.Loads);
                    lease.Dispose();
                }
            }
            finally
            {
                loader.Gate?.TrySetResult(true);
                await service.StopAsync(CancellationToken.None);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        });

        [UnityTest]
        public IEnumerator Shutdown_DrainsLatePayloadBeforeDispose() => UniTask.ToCoroutine(async () =>
        {
            var texture = new Texture2D(1, 1);
            var loader = new Loader { Texture = texture, Gate = new UniTaskCompletionSource<bool>() };
            var service = Create(loader, out _);
            await service.InitializeAsync(CancellationToken.None);
            var acquiring = service.AcquireLeaseAsync<Texture2D>(new ResourceKey("tex"))
                .SuppressCancellationThrow();
            await UniTask.WaitUntil(() => loader.Loads == 1);
            var stopping = service.StopAsync(CancellationToken.None);
            try
            {
                Assert.AreEqual(UniTaskStatus.Pending, stopping.Status);
                Assert.AreEqual(0, loader.Disposals);
            }
            finally { loader.Gate.TrySetResult(true); }
            await acquiring;
            await stopping;
            Assert.AreEqual(1, loader.Disposals);
            UnityEngine.Object.DestroyImmediate(texture);
        });
    }
}
