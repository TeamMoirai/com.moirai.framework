using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Assets;
using Moirai.Atropos.Assets.Internal;
using Moirai.Atropos.Bindings;
using Moirai.Atropos.Kernel.Scheduling;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bindings
{
    public sealed class ResourceServiceBindingSourceTests
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
            internal ResourceKey LastKey;
            internal Texture2D Texture;

            public async UniTask<IAssetPayload<T>> LoadAssetAsync<T>(ResourceKey key,
                CancellationToken ownerToken) where T : UnityEngine.Object
            {
                Loads++;
                LastKey = key;
                await UniTask.SwitchToMainThread();
                return (IAssetPayload<T>)(object)new Payload(Texture, () => Disposals++);
            }

            public UniTask<IScenePayload> LoadSceneAsync(ResourceKey key,
                UnityEngine.SceneManagement.LoadSceneParameters parameters, bool activateOnLoad,
                CancellationToken ownerToken)
                => UniTask.FromException<IScenePayload>(new NotSupportedException());
        }

        private static ResourceService Create(Loader loader)
            => new ResourceService(loader, new ResourceOptions(), new Clock(), new Loop());

        [UnityTest]
        public IEnumerator Acquire_MapsAddressKey_AndReleasesOnDispose() => UniTask.ToCoroutine(async () =>
        {
            var texture = new Texture2D(1, 1);
            var loader = new Loader { Texture = texture };
            var service = Create(loader);
            try
            {
                await service.InitializeAsync(CancellationToken.None);
                var source = new ResourceServiceBindingSource<Texture2D>(service);

                IBindingAssetLease lease = await source.AcquireAsync("textures/icon", CancellationToken.None);

                Assert.That(lease.Asset, Is.SameAs(texture));
                Assert.AreEqual("textures/icon", loader.LastKey.Location);
                Assert.AreEqual(1, loader.Loads);

                lease.Dispose();
                Assert.Throws<ObjectDisposedException>(() => _ = lease.Asset);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        });

        [UnityTest]
        public IEnumerator Acquire_WithNullAddressKey_Throws() => UniTask.ToCoroutine(async () =>
        {
            var source = new ResourceServiceBindingSource<Texture2D>(Create(new Loader()));

            Exception observed = null;
            try { await source.AcquireAsync(null, CancellationToken.None); }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.InstanceOf<ArgumentNullException>());
        });
    }
}
