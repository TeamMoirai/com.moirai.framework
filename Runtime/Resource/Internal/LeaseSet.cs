using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Assets.Internal
{
    internal sealed class LeaseSet<T> where T : UnityEngine.Object
    {
        private readonly Dictionary<long, ResourceLeaseOption> _live =
            new Dictionary<long, ResourceLeaseOption>();
        private readonly Action<ResourceLeaseOption> _released;
        private long _next;
        private bool _closed;

        internal LeaseSet(int capacity, Action<ResourceLeaseOption> released)
        {
            Capacity = capacity;
            _released = released ?? throw new ArgumentNullException(nameof(released));
        }

        internal T Asset { get; private set; }
        internal int Capacity { get; }
        internal int Count => _live.Count;

        internal void Initialize(T asset)
        {
            if (_closed) throw new ObjectDisposedException(nameof(LeaseSet<T>));
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            Asset = asset;
        }

        internal ResourceLease<T> Acquire(ResourceLeaseOption option)
        {
            if (_closed) throw new ObjectDisposedException(nameof(LeaseSet<T>));
            if (Asset == null) throw new InvalidOperationException("The asset is not ready.");
            if (_live.Count >= Capacity)
                throw new InvalidOperationException("Resource lease capacity exceeded.");
            long id = ++_next;
            _live.Add(id, option);
            return new ResourceLease<T>(this, id);
        }

        internal bool IsValid(long id) => !_closed && _live.ContainsKey(id) && Asset != null;

        internal T GetAsset(long id) => IsValid(id)
            ? Asset : throw new ObjectDisposedException(nameof(ResourceLease<T>));

        internal void Release(long id)
        {
            if (_closed || !_live.TryGetValue(id, out ResourceLeaseOption option)) return;
            _live.Remove(id);
            _released(option);
        }

        internal void Invalidate()
        {
            _closed = true;
            _live.Clear();
            Asset = null;
        }
    }
}
