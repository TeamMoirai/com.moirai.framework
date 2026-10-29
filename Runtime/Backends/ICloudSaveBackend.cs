using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Persistence;

namespace Moirai.Atropos.Backends
{
    public enum CloudWriteStatus : byte { Written, Conflict, Unknown }
    public enum CloudReadStatus : byte { Found, Missing, Unknown }

    public readonly struct CloudSnapshot
    {
        public CloudSnapshot(string slotId, long version, SaveSnapshot snapshot)
        {
            SlotId = slotId;
            Version = version;
            Snapshot = snapshot;
        }

        public string SlotId { get; }
        public long Version { get; }
        public SaveSnapshot Snapshot { get; }
    }

    public readonly struct CloudWriteResult
    {
        public CloudWriteResult(CloudWriteStatus status, long version)
        {
            Status = status;
            Version = version;
        }

        public CloudWriteStatus Status { get; }
        public long Version { get; }
        public bool Succeeded => Status == CloudWriteStatus.Written;
    }

    public readonly struct CloudReadResult
    {
        public CloudReadResult(CloudReadStatus status, CloudSnapshot snapshot)
        {
            Status = status;
            Snapshot = snapshot;
        }

        public CloudReadStatus Status { get; }
        public CloudSnapshot Snapshot { get; }
        public bool Succeeded => Status == CloudReadStatus.Found;
    }

    public interface ICloudSaveBackend
    {
        UniTask<CloudWriteResult> WriteAsync(string slotId, SaveSnapshot snapshot,
            long? expectedVersion, CallOptions call, CancellationToken cancellationToken);
        UniTask<CloudReadResult> ReadAsync(string slotId, CallOptions call,
            CancellationToken cancellationToken);
    }
}
