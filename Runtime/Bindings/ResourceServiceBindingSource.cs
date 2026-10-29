using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Assets;
using UnityEngine;

namespace Moirai.Atropos.Bindings
{
    public sealed class ResourceServiceBindingSource<T> : IBindingAssetSource where T : UnityEngine.Object
    {
        private readonly IResourceService _resources;

        public ResourceServiceBindingSource(IResourceService resources)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        public async UniTask<IBindingAssetLease> AcquireAsync(string addressKey,
            CancellationToken cancellationToken)
        {
            if (addressKey == null) throw new ArgumentNullException(nameof(addressKey));
            ResourceLease<T> lease = await _resources.AcquireLeaseAsync<T>(new ResourceKey(addressKey),
                ResourceLeaseOption.KeepAliveOnRelease, cancellationToken);
            return new Lease(lease);
        }

        private sealed class Lease : IBindingAssetLease
        {
            private ResourceLease<T> _lease;
            private bool _disposed;

            internal Lease(ResourceLease<T> lease) { _lease = lease; }

            public object Asset => _disposed
                ? throw new ObjectDisposedException(nameof(ResourceServiceBindingSource<T>))
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
