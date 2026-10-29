using System;
using System.Threading;

namespace Moirai.Atropos.Persistence
{
    public interface ISaveSerializer
    {
        ushort BackendId { get; }
        byte[] Serialize<T>(T snapshot, CancellationToken cancellationToken);
        T Deserialize<T>(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
    }
}
