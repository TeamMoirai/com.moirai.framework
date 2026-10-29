using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Screens.Internal
{
    internal sealed class UIService : IUIService
    {
        private sealed class Entry
        {
            internal readonly IUIWindow Window;
            internal readonly string TrackId;
            internal Entry(IUIWindow window, string trackId) { Window = window; TrackId = trackId; }
        }

        private readonly IUIWindowCatalog _catalog;
        private readonly Dictionary<string, IUITrack> _tracks = new Dictionary<string, IUITrack>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> _open = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Entry>> _stacks = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        private readonly List<Entry> _ordered = new List<Entry>();
        private readonly Dictionary<string, UniTaskCompletionSource<UIOpenResult>> _opening =
            new Dictionary<string, UniTaskCompletionSource<UIOpenResult>>(StringComparer.Ordinal);
        private bool _closing;

        internal UIService(IUIWindowCatalog catalog, IEnumerable<IUITrack> tracks)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));
            foreach (IUITrack track in tracks)
            {
                if (track == null) throw new ArgumentException("A UI track cannot be null.");
                if (!_tracks.TryAdd(track.TrackId, track))
                    throw new InvalidOperationException("Duplicate UI track: " + track.TrackId);
            }
        }

        public async UniTask<UIOpenResult> OpenAsync(string windowId, CancellationToken cancellationToken)
        {
            if (_closing) throw new ObjectDisposedException(nameof(UIService));
            if (windowId == null) throw new ArgumentNullException(nameof(windowId));
            cancellationToken.ThrowIfCancellationRequested();
            if (_open.ContainsKey(windowId)) return UIOpenResult.Success();
            if (_opening.TryGetValue(windowId, out UniTaskCompletionSource<UIOpenResult> shared))
            {
                return await shared.Task.AttachExternalCancellation(cancellationToken);
            }
            if (!_catalog.TryGet(windowId, out UIWindowDescriptor descriptor))
                throw new InvalidOperationException("Unknown UI window: " + windowId);
            if (!_tracks.TryGetValue(descriptor.TrackId, out IUITrack track))
                throw new InvalidOperationException("Unknown UI track: " + descriptor.TrackId);
            var completion = new UniTaskCompletionSource<UIOpenResult>();
            _opening.Add(windowId, completion);
            OpenOwnedAsync(track, windowId, descriptor.TrackId, completion).Forget(
                failure => completion.TrySetResult(UIOpenResult.Failure(failure)));
            return await completion.Task.AttachExternalCancellation(cancellationToken);
        }

        private async UniTask OpenOwnedAsync(IUITrack track, string windowId, string trackId,
            UniTaskCompletionSource<UIOpenResult> completion)
        {
            UIOpenResult result = default;
            IUIWindow window = null;
            try
            {
                window = await track.CreateAsync(windowId, CancellationToken.None);
                if (window == null)
                    throw new InvalidOperationException("UI track returned no window: " + windowId);
                await window.OnShowAsync(CancellationToken.None);
                if (_closing)
                {
                    await window.OnHideAsync(true, CancellationToken.None);
                    track.Destroy(window);
                    result = UIOpenResult.Cancellation();
                }
                else
                {
                    var entry = new Entry(window, trackId);
                    _open[windowId] = entry;
                    if (!_stacks.TryGetValue(trackId, out List<Entry> stack))
                    {
                        stack = new List<Entry>();
                        _stacks.Add(trackId, stack);
                    }
                    stack.Add(entry);
                    _ordered.Add(entry);
                    _ordered.Sort((left, right) => LayerOf(left).CompareTo(LayerOf(right)));
                    result = UIOpenResult.Success();
                }
            }
            catch (Exception failure)
            {
                if (window != null) track.Destroy(window);
                result = UIOpenResult.Failure(failure);
            }
            finally
            {
                _opening.Remove(windowId);
                completion.TrySetResult(result);
            }
        }

        public async UniTask CloseAsync(string windowId, CancellationToken cancellationToken)
        {
            if (windowId == null) throw new ArgumentNullException(nameof(windowId));
            cancellationToken.ThrowIfCancellationRequested();
            if (!_open.TryGetValue(windowId, out Entry entry)) return;
            _open.Remove(windowId);
            _stacks[entry.TrackId].Remove(entry);
            _ordered.Remove(entry);
            await entry.Window.OnHideAsync(false, cancellationToken);
            if (!_tracks.TryGetValue(entry.TrackId, out IUITrack track)) return;
            track.Destroy(entry.Window);
        }

        public IReadOnlyList<string> Stack(string trackId)
        {
            if (trackId == null) throw new ArgumentNullException(nameof(trackId));
            if (!_stacks.TryGetValue(trackId, out List<Entry> stack)) return Array.Empty<string>();
            var ids = new string[stack.Count];
            for (int i = 0; i < stack.Count; i++) ids[i] = stack[i].Window.WindowId;
            return ids;
        }

        public IReadOnlyList<string> OpenWindows
        {
            get
            {
                var ids = new string[_ordered.Count];
                for (int i = 0; i < _ordered.Count; i++) ids[i] = _ordered[i].Window.WindowId;
                return ids;
            }
        }

        private int LayerOf(Entry entry) =>
            _tracks.TryGetValue(entry.TrackId, out IUITrack track) ? track.Layer : int.MaxValue;

        internal UniTask ShutdownAsync()
        {
            _closing = true;
            var entries = new List<Entry>(_open.Values);
            _open.Clear();
            _stacks.Clear();
            _ordered.Clear();
            return HideAllAsync(entries);
        }

        private async UniTask HideAllAsync(List<Entry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                try { await entries[i].Window.OnHideAsync(true, CancellationToken.None); }
                catch (Exception) { }
            }
            for (int i = 0; i < entries.Count; i++)
            {
                if (_tracks.TryGetValue(entries[i].TrackId, out IUITrack track))
                    track.Destroy(entries[i].Window);
            }
        }
    }
}
