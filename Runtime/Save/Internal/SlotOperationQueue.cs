using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Storage;

namespace Moirai.Atropos.Persistence.Internal
{
    internal sealed class SlotOperationQueue
    {
        private readonly IFileSystem _files;
        private readonly Func<int, string> _path;
        private readonly Dictionary<int, UniTask> _tails = new Dictionary<int, UniTask>();
        private readonly Dictionary<int, long> _sequences = new Dictionary<int, long>();
        private bool _closing;

        internal SlotOperationQueue(IFileSystem files, Func<int, string> path)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        internal UniTask<SaveCommit> EnqueueAsync(int slot, ReadOnlyMemory<byte> contents,
            CancellationToken callerToken)
        {
            if (_closing) throw new ObjectDisposedException(nameof(SlotOperationQueue));
            callerToken.ThrowIfCancellationRequested();
            long sequence = _sequences.TryGetValue(slot, out long last) ? last + 1 : 1;
            _sequences[slot] = sequence;
            UniTask previous = _tails.TryGetValue(slot, out UniTask tail) ? tail : UniTask.CompletedTask;
            UniTask<SaveCommit> operation = RunAfterAsync(previous, slot, sequence, contents, callerToken);
            _tails[slot] = operation.AsUniTask();
            return operation;
        }

        internal void Close() => _closing = true;

        private async UniTask<SaveCommit> RunAfterAsync(UniTask previous, int slot, long sequence,
            ReadOnlyMemory<byte> contents, CancellationToken callerToken)
        {
            try { await previous.AttachExternalCancellation(callerToken); }
            catch (OperationCanceledException) { }
            return await SaveCommitEngine.CommitFileAsync(_files, _path(slot), slot, sequence,
                contents, callerToken);
        }
    }
}
