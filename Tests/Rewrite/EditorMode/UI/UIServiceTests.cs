using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Screens;
using Moirai.Atropos.Screens.Internal;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace UI
{
    public sealed class UIServiceTests
    {
        private sealed class Catalog : IUIWindowCatalog
        {
            private readonly Dictionary<string, UIWindowDescriptor> _map =
                new Dictionary<string, UIWindowDescriptor>(StringComparer.Ordinal);
            internal Catalog(params UIWindowDescriptor[] descriptors)
            {
                foreach (UIWindowDescriptor descriptor in descriptors) _map.Add(descriptor.WindowId, descriptor);
            }
            public bool TryGet(string windowId, out UIWindowDescriptor descriptor)
                => _map.TryGetValue(windowId, out descriptor);
        }

        private sealed class Window : IUIWindow
        {
            internal string Id;
            internal string TrackId;
            internal UniTaskCompletionSource<bool> ShowGate;
            internal readonly List<string> Calls = new List<string>();
            public string WindowId => Id;
            public async UniTask OnShowAsync(CancellationToken cancellationToken)
            {
                Calls.Add("show");
                if (ShowGate != null) await ShowGate.Task;
            }
            public UniTask OnHideAsync(bool isShutdown, CancellationToken cancellationToken)
            {
                Calls.Add(isShutdown ? "shutdown-hide" : "hide");
                return UniTask.CompletedTask;
            }
        }

        private sealed class Track : IUITrack
        {
            internal readonly List<Window> Created = new List<Window>();
            internal readonly List<string> Destroyed = new List<string>();
            internal UniTaskCompletionSource<bool> ShowGate;
            public string TrackId { get; set; }
            public int Layer { get; set; }
            public UniTask<IUIWindow> CreateAsync(string windowId, CancellationToken cancellationToken)
            {
                var window = new Window { Id = windowId, TrackId = TrackId, ShowGate = ShowGate };
                Created.Add(window);
                return UniTask.FromResult<IUIWindow>(window);
            }
            public void Destroy(IUIWindow window) => Destroyed.Add(window.WindowId);
        }

        private static UIService Create(Track track, params UIWindowDescriptor[] descriptors)
            => new UIService(new Catalog(descriptors), new IUITrack[] { track });

        [UnityTest]
        public IEnumerator SharedOpen_OneCancellation_DoesNotCancelOwner() => UniTask.ToCoroutine(async () =>
        {
            var track = new Track { TrackId = "ugui", Layer = 0,
                ShowGate = new UniTaskCompletionSource<bool>() };
            var service = Create(track, new UIWindowDescriptor("home", "ugui"));
            var first = service.OpenAsync("home", CancellationToken.None);
            var second = service.OpenAsync("home", CancellationToken.None);
            using (var caller = new CancellationTokenSource())
            {
                UniTask<UIOpenResult> cancelled = service.OpenAsync("home", caller.Token);
                caller.Cancel();
                var cancelledResult = await cancelled.SuppressCancellationThrow();
                Assert.IsTrue(cancelledResult.IsCanceled);
            }
            track.ShowGate.TrySetResult(true);
            var firstResult = await first;
            Assert.IsTrue(firstResult.Succeeded, firstResult.Error?.ToString());
            Assert.IsTrue((await second).Succeeded);
            Assert.AreEqual(1, track.Created.Count);
            CollectionAssert.AreEqual(new[] { "home" }, service.Stack("ugui"));
        });

        [UnityTest]
        public IEnumerator Close_HidesBeforeDestroy() => UniTask.ToCoroutine(async () =>
        {
            var track = new Track { TrackId = "ugui", Layer = 0 };
            var service = Create(track, new UIWindowDescriptor("home", "ugui"));
            await service.OpenAsync("home", CancellationToken.None);
            await service.CloseAsync("home", CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "show", "hide" }, track.Created[0].Calls);
            CollectionAssert.AreEqual(new[] { "home" }, track.Destroyed);
            Assert.IsEmpty(service.Stack("ugui"));
        });

        [UnityTest]
        public IEnumerator Shutdown_HidesAll_BeforeDestroyingAny() => UniTask.ToCoroutine(async () =>
        {
            var track = new Track { TrackId = "ugui", Layer = 0 };
            var service = Create(track,
                new UIWindowDescriptor("a", "ugui"), new UIWindowDescriptor("b", "ugui"));
            await service.OpenAsync("a", CancellationToken.None);
            await service.OpenAsync("b", CancellationToken.None);
            await service.ShutdownAsync();
            Assert.AreEqual(1, track.Created[0].Calls.FindAll(x => x == "shutdown-hide").Count);
            Assert.AreEqual(1, track.Created[1].Calls.FindAll(x => x == "shutdown-hide").Count);
            Assert.AreEqual(2, track.Destroyed.Count);
        });
    }
}
