using System;
using System.Threading;

namespace Moirai.Atropos.Persistence
{
    public interface ISaveMigrator
    {
        string BlockKey { get; }
        ushort SerializerId { get; }
        int FromVersion { get; }
        int ToVersion { get; }
        byte[] Migrate(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
    }
}
