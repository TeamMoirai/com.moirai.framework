using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Bindings.Internal
{
    internal sealed class BindingSlotStore : IResourceBindingService
    {
        private sealed class Entry
        {
            internal readonly IBindingAssetLease Lease;
            internal Entry(IBindingAssetLease lease) { Lease = lease; }
        }

        private readonly IBindingAssetSource _source;
        private readonly Dictionary<string, Entry> _slots = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _versions = new Dictionary<string, int>(StringComparer.Ordinal);
        private bool _closing;

        internal BindingSlotStore(IBindingAssetSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public async UniTask BindAsync(string slotId, string addressKey, IBindingTarget target,
            CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            if (slotId == null) throw new ArgumentNullException(nameof(slotId));
            if (addressKey == null) throw new ArgumentNullException(nameof(addressKey));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!target.IsAlive) throw new InvalidOperationException("Binding target is not alive.");
            int version = (_versions.TryGetValue(slotId, out int current) ? current : 0) + 1;
            _versions[slotId] = version;
            IBindingAssetLease lease = await _source.AcquireAsync(addressKey, cancellationToken);
            await UniTask.SwitchToMainThread();
            if (!target.IsAlive || _closing || _versions[slotId] != version)
            {
                lease.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Binding was superseded or the target is gone.");
            }
            try { target.Apply(lease.Asset); }
            catch (Exception)
            {
                lease.Dispose();
                throw;
            }
            if (_slots.TryGetValue(slotId, out Entry previous))
            {
                _slots.Remove(slotId);
                previous.Lease.Dispose();
            }
            _slots.Add(slotId, new Entry(lease));
        }

        public void Unbind(string slotId)
        {
            if (slotId == null) throw new ArgumentNullException(nameof(slotId));
            if (!_slots.TryGetValue(slotId, out Entry entry)) return;
            _slots.Remove(slotId);
            entry.Lease.Dispose();
        }

        internal void Shutdown()
        {
            _closing = true;
            foreach (Entry entry in _slots.Values) entry.Lease.Dispose();
            _slots.Clear();
            _versions.Clear();
        }

        private void EnsureOpen()
        {
            if (_closing) throw new ObjectDisposedException(nameof(BindingSlotStore));
        }
    }
}
