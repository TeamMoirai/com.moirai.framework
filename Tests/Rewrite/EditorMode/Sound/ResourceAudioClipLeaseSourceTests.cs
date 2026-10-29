using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Assets;
using Moirai.Atropos.Assets.Internal;
using Moirai.Atropos.Kernel.Scheduling;
using Moirai.Atropos.Sound;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sound
{
    public sealed class ResourceAudioClipLeaseSourceTests
    {
        private sealed class Clock : IClock
        {
            public float Time { get; set; }
            public float DeltaTime { get; set; }
            public double RealtimeSinceStartup { get; set; }
        }

        private sealed class Loop : IPlayerLoop
        {
            public IDisposable AddUpdateListener(Action listener) => new Lease();
            public IDisposable AddFixedUpdateListener(Action listener) => new Lease();
            public IDisposable AddLateUpdateListener(Action listener) => new Lease();
            private sealed class Lease : IDisposable { public void Dispose() { } }
        }

        private sealed class Payload : IAssetPayload<AudioClip>
        {
            internal Payload(AudioClip clip, Action onDispose) { Asset = clip; OnDispose = onDispose; }
            public AudioClip Asset { get; }
            internal Action OnDispose { get; }
            public UniTask DisposeAsync() { OnDispose(); return UniTask.CompletedTask; }
        }

        private sealed class Loader : IAssetLoader
        {
            internal int Loads;
            internal ResourceKey LastKey;
            internal AudioClip Clip;

            public async UniTask<IAssetPayload<T>> LoadAssetAsync<T>(ResourceKey key,
                CancellationToken ownerToken) where T : UnityEngine.Object
            {
                Loads++;
                LastKey = key;
                await UniTask.SwitchToMainThread();
                return (IAssetPayload<T>)(object)new Payload(Clip, () => { });
            }

            public UniTask<IScenePayload> LoadSceneAsync(ResourceKey key,
                UnityEngine.SceneManagement.LoadSceneParameters parameters, bool activateOnLoad,
                CancellationToken ownerToken)
                => UniTask.FromException<IScenePayload>(new NotSupportedException());
        }

        [UnityTest]
        public IEnumerator Acquire_ReturnsClip_AndDisposeInvalidatesLease() => UniTask.ToCoroutine(async () =>
        {
            var clip = AudioClip.Create("test", 16, 1, 44100, false);
            var loader = new Loader { Clip = clip };
            var service = new ResourceService(loader, new ResourceOptions(), new Clock(), new Loop());
            try
            {
                await service.InitializeAsync(CancellationToken.None);
                var source = new ResourceAudioClipLeaseSource(service);

                IAudioClipLease lease = await source.AcquireAsync("audio/click", CancellationToken.None);

                Assert.That(lease.Clip, Is.SameAs(clip));
                Assert.AreEqual("audio/click", loader.LastKey.Location);
                Assert.AreEqual(1, loader.Loads);

                lease.Dispose();
                Assert.Throws<ObjectDisposedException>(() => _ = lease.Clip);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        });

        [UnityTest]
        public IEnumerator Acquire_WithNullKey_Throws() => UniTask.ToCoroutine(async () =>
        {
            var source = new ResourceAudioClipLeaseSource(
                new ResourceService(new Loader(), new ResourceOptions(), new Clock(), new Loop()));

            Exception observed = null;
            try { await source.AcquireAsync(null, CancellationToken.None); }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.InstanceOf<ArgumentNullException>());
        });
    }
}
