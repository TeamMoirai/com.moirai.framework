using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Screens;
using Moirai.Atropos.Screens.Internal;
using NUnit.Framework;
using UnityEngine.TestTools;
using System.Collections;

namespace UI
{
    public sealed class UIStackOrderTests
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
            public string WindowId => Id;
            public UniTask OnShowAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
            public UniTask OnHideAsync(bool isShutdown, CancellationToken cancellationToken) => UniTask.CompletedTask;
        }

        private sealed class Track : IUITrack
        {
            public string TrackId { get; set; }
            public int Layer { get; set; }
            public UniTask<IUIWindow> CreateAsync(string windowId, CancellationToken cancellationToken)
                => UniTask.FromResult<IUIWindow>(new Window { Id = windowId });
            public void Destroy(IUIWindow window) { }
        }

        [UnityTest]
        public IEnumerator OpenWindows_AreOrderedByTrackLayer() => UniTask.ToCoroutine(async () =>
        {
            var top = new Track { TrackId = "top", Layer = 10 };
            var bottom = new Track { TrackId = "bottom", Layer = 0 };
            var service = new UIService(new Catalog(
                new UIWindowDescriptor("overlay", "top"),
                new UIWindowDescriptor("hud", "bottom")), new IUITrack[] { top, bottom });
            await service.OpenAsync("overlay", CancellationToken.None);
            await service.OpenAsync("hud", CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "hud", "overlay" }, service.OpenWindows);
            await service.CloseAsync("hud", CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "overlay" }, service.OpenWindows);
            await service.ShutdownAsync();
        });
    }
}
