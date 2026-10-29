using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Storage;

namespace Moirai.Atropos.Persistence.Internal
{
    internal static class SaveCommitEngine
    {
        internal static async UniTask<SaveCommit> CommitFileAsync(IFileSystem files,
            string destinationPath, int slot, long sequence, ReadOnlyMemory<byte> contents,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string stagedPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".staged";
            try
            {
                await files.WriteAllBytesAsync(stagedPath, contents.ToArray(), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                StorageCommitResult result = await files.CommitAtomicAsync(stagedPath, destinationPath,
                    cancellationToken);
                return new SaveCommit(slot, sequence, result.PowerLossDurable);
            }
            catch (StorageCommitIndeterminateException exception)
            {
                throw new SaveException(SaveError.CommitOutcomeUnknown,
                    "Storage commit outcome is unknown; recovery is required.", exception);
            }
            catch (Exception failure)
            {
                try { await files.DeleteAsync(stagedPath, CancellationToken.None); }
                catch (Exception cleanupFailure) { throw new AggregateException(failure, cleanupFailure); }
                throw;
            }
        }
    }
}
