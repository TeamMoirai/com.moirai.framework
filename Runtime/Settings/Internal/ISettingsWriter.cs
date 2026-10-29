using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Storage;

namespace Moirai.Atropos.Settings.Internal
{
    internal interface ISettingsWriter
    {
        UniTask<StorageCommitResult> CommitAsync(IReadOnlyDictionary<string, string> snapshot,
            CancellationToken cancellationToken);
    }
}
