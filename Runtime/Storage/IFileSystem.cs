using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Storage
{
    public interface IFileSystem
    {
        UniTask<StorageCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken);
        UniTask<StorageEntry> GetEntryAsync(string path, CancellationToken cancellationToken);
        UniTask<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);
        UniTask WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken);
        UniTask DeleteAsync(string path, CancellationToken cancellationToken);
        UniTask CreateDirectoryAsync(string path, CancellationToken cancellationToken);
        UniTask<StorageEntry[]> ListDirectoryAsync(string path, CancellationToken cancellationToken);
        UniTask<StorageCommitResult> CommitAtomicAsync(string stagedPath, string destinationPath,
            CancellationToken cancellationToken);
    }
}
