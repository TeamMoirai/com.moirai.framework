using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Assets;
using Moirai.Atropos.Assets.Internal;
using Moirai.Atropos.Assets.Unity;
using Moirai.Atropos.Kernel.Scheduling;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Resource
{
    public sealed class ResourcesBackendTests
    {
        private sealed class Clock : IClock
        {
            public float Time => UnityEngine.Time.time;
            public float DeltaTime => UnityEngine.Time.deltaTime;
            public double RealtimeSinceStartup => UnityEngine.Time.realtimeSinceStartupAsDouble;
        }

        private sealed class Loop : IPlayerLoop
        {
            public IDisposable AddUpdateListener(Action listener) => new Handle();
            public IDisposable AddFixedUpdateListener(Action listener) => new Handle();
            public IDisposable AddLateUpdateListener(Action listener) => new Handle();
            private sealed class Handle : IDisposable { public void Dispose() { } }
        }

        [UnityTest]
        public IEnumerator BuiltinLoader_LoadsAndReleasesTypedAsset() => UniTask.ToCoroutine(async () =>
        {
            var loader = new ResourcesAssetLoader();
            var key = new ResourceKey("P3Probe");
            IAssetPayload<Texture2D> payload =
                await loader.LoadAssetAsync<Texture2D>(key, CancellationToken.None);
            Assert.That(payload.Asset, Is.Not.Null);
            await payload.DisposeAsync();
            await payload.DisposeAsync();
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator Service_LeasesRealAsset_AndShutdownDrains() => UniTask.ToCoroutine(async () =>
        {
            var service = new ResourceService(new ResourcesAssetLoader(), new ResourceOptions(),
                new Clock(), new Loop());
            await service.InitializeAsync(CancellationToken.None);
            try
            {
                var lease = await service.AcquireLeaseAsync<Texture2D>(new ResourceKey("P3Probe"));
                Assert.IsTrue(lease.IsValid);
                Assert.That(lease.Asset, Is.Not.Null);
                Assert.IsTrue(service.TryGetAsset<Texture2D>(new ResourceKey("P3Probe"), out var cached));
                Assert.That(cached, Is.SameAs(lease.Asset));
                lease.Dispose();
                Assert.IsFalse(lease.IsValid);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }
        });
    }
}
