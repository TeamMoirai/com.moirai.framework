using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Rendering;
using Moirai.Atropos.Rendering.Internal;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Graphics
{
    public sealed class GraphicsServiceTests
    {
        private sealed class Applier : IGraphicsApplier
        {
            internal int Applies;
            internal ResolutionInfo LastResolution;
            internal bool LastFullScreen;
            internal WindowMode LastMode;
            internal Exception Failure;
            public bool VSync { get; set; }

            public UniTask<GraphicsState> GetCurrentAsync(CancellationToken cancellationToken)
                => UniTask.FromResult(default(GraphicsState));

            public UniTask ApplyResolutionAsync(ResolutionInfo resolution, bool fullScreen,
                WindowMode mode, CancellationToken cancellationToken)
            {
                Applies++;
                LastResolution = resolution;
                LastFullScreen = fullScreen;
                LastMode = mode;
                return Failure == null ? UniTask.CompletedTask : UniTask.FromException(Failure);
            }
        }

        private static GraphicsService Create(Applier applier, out GraphicsOptions options)
        {
            options = new GraphicsOptions
            {
                DefaultResolution = new ResolutionInfo(1280, 720, 60, 1),
                DefaultFullScreen = false,
                DefaultWindowMode = WindowMode.Windowed,
                DefaultVSync = false
            };
            return new GraphicsService(applier, options);
        }

        [UnityTest]
        public IEnumerator Apply_UpdatesStateOnlyAfterApplierCompletes() => UniTask.ToCoroutine(async () =>
        {
            var applier = new Applier();
            var service = Create(applier, out _);
            var target = new ResolutionInfo(1920, 1080, 120, 1);
            await service.SetResolutionAsync(target, CancellationToken.None);
            Assert.AreEqual(target, service.State.Resolution);
            Assert.AreEqual(1, applier.Applies);
            Assert.AreEqual(false, applier.LastFullScreen);
            Assert.AreEqual(WindowMode.Windowed, applier.LastMode);
        });

        [UnityTest]
        public IEnumerator Failure_KeepsPreviousState() => UniTask.ToCoroutine(async () =>
        {
            var expected = new InvalidOperationException("display rejected");
            var applier = new Applier { Failure = expected };
            var service = Create(applier, out _);
            GraphicsState before = service.State;
            Exception observed = null;
            try { await service.SetFullScreenAsync(true, CancellationToken.None); }
            catch (Exception failure) { observed = failure; }
            Assert.That(observed, Is.SameAs(expected));
            Assert.AreEqual(before.FullScreen, service.State.FullScreen);
            Assert.AreEqual(before.Mode, service.State.Mode);
        });

        [UnityTest]
        public IEnumerator Vsync_IsSynchronousAndMirrored() => UniTask.ToCoroutine(async () =>
        {
            var applier = new Applier();
            var service = Create(applier, out _);
            service.SetVSync(true);
            Assert.IsTrue(applier.VSync);
            Assert.IsTrue(service.State.VSync);
            await UniTask.Yield();
        });
    }
}
