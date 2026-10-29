using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Assets;
using UnityEngine;

namespace Moirai.Atropos.Sound
{
    public sealed class ResourceAudioClipLeaseSource : IAudioClipLeaseSource
    {
        private readonly IResourceService _resources;

        public ResourceAudioClipLeaseSource(IResourceService resources)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        public async UniTask<IAudioClipLease> AcquireAsync(string clipKey,
            CancellationToken cancellationToken)
        {
            if (clipKey == null) throw new ArgumentNullException(nameof(clipKey));
            ResourceLease<AudioClip> lease = await _resources.AcquireLeaseAsync<AudioClip>(
                new ResourceKey(clipKey), ResourceLeaseOption.KeepAliveOnRelease, cancellationToken);
            return new ClipLease(lease);
        }

        private sealed class ClipLease : IAudioClipLease
        {
            private ResourceLease<AudioClip> _lease;
            private bool _disposed;

            internal ClipLease(ResourceLease<AudioClip> lease) { _lease = lease; }

            public AudioClip Clip => _disposed
                ? throw new ObjectDisposedException(nameof(ResourceAudioClipLeaseSource))
                : _lease.Asset;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _lease.Dispose();
                _lease = default;
            }
        }
    }
}
