using System;

namespace Moirai.Atropos.Storage
{
    [Flags]
    public enum StorageCapabilities
    {
        None = 0,
        AsyncRead = 1,
        AsyncWrite = 2,
        AsyncMetadata = 4,
        AsyncDirectoryListing = 8,
        AsyncDelete = 16,
        AsyncDirectoryCreation = 32,
        AtomicCommit = 64,
        PowerLossDurability = 128
    }
}
