using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Persistence;

namespace Moirai.Atropos.Backends
{
    /// <summary>
    /// Keeps the last known cloud version per slot so callers upload with the right
    /// compare-and-swap expectation without re-reading first.
    /// </summary>
    public sealed class CloudSaveCoordinator
    {
        private readonly ICloudSaveBackend _backend;
        private readonly Dictionary<string, long> _versions = new Dictionary<string, long>(StringComparer.Ordinal);

        public CloudSaveCoordinator(ICloudSaveBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public bool TryGetVersion(string slotId, out long version)
        {
            if (slotId == null) throw new ArgumentNullException(nameof(slotId));
            return _versions.TryGetValue(slotId, out version);
        }

        public void Forget(string slotId)
        {
            if (slotId == null) throw new ArgumentNullException(nameof(slotId));
            _versions.Remove(slotId);
        }

        public async UniTask<CloudWriteResult> UploadAsync(string slotId, SaveSnapshot snapshot,
            CallOptions call, CancellationToken cancellationToken = default)
        {
            if (slotId == null) throw new ArgumentNullException(nameof(slotId));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            long? expected = _versions.TryGetValue(slotId, out long known) ? known : (long?)null;
            CloudWriteResult result = await _backend.WriteAsync(slotId, snapshot, expected, call,
                cancellationToken);
            if (result.Status == CloudWriteStatus.Written) _versions[slotId] = result.Version;
            return result;
        }

        public async UniTask<CloudReadResult> DownloadAsync(string slotId, CallOptions call,
            CancellationToken cancellationToken = default)
        {
            if (slotId == null) throw new ArgumentNullException(nameof(slotId));
            CloudReadResult result = await _backend.ReadAsync(slotId, call, cancellationToken);
            if (result.Status == CloudReadStatus.Found)
                _versions[slotId] = result.Snapshot.Version;
            return result;
        }
    }
}
