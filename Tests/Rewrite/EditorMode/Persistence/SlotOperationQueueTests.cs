using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Persistence;
using Moirai.Atropos.Persistence.Internal;
using Moirai.Atropos.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Persistence
{
    public sealed class SlotOperationQueueTests
    {
        private sealed class Files : IFileSystem
        {
            internal readonly List<string> Committed = new List<string>();
            internal readonly List<string> Staged = new List<string>();
            internal bool Indeterminate;

            public UniTask<StorageCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
                => UniTask.FromResult(StorageCapabilities.AsyncWrite);
            public UniTask<StorageEntry> GetEntryAsync(string path, CancellationToken cancellationToken)
                => UniTask.FromResult<StorageEntry>(null);
            public UniTask<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
                => UniTask.FromResult(Array.Empty<byte>());
            public UniTask WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken)
            {
                Staged.Add(path);
                return UniTask.CompletedTask;
            }
            public UniTask DeleteAsync(string path, CancellationToken cancellationToken)
                => UniTask.CompletedTask;
            public UniTask CreateDirectoryAsync(string path, CancellationToken cancellationToken)
                => UniTask.CompletedTask;
            public UniTask<StorageEntry[]> ListDirectoryAsync(string path, CancellationToken cancellationToken)
                => UniTask.FromResult(Array.Empty<StorageEntry>());
            public UniTask<StorageCommitResult> CommitAtomicAsync(string stagedPath, string destinationPath,
                CancellationToken cancellationToken)
            {
                if (Indeterminate)
                    throw new StorageCommitIndeterminateException("unknown", null);
                Committed.Add(destinationPath);
                return UniTask.FromResult(new StorageCommitResult(true));
            }
        }

        [UnityTest]
        public IEnumerator SameSlot_CommitsInArrivalOrder_WithMonotonicSequence() => UniTask.ToCoroutine(async () =>
        {
            var files = new Files();
            var queue = new SlotOperationQueue(files, slot => "slot" + slot + ".sav");
            var first = queue.EnqueueAsync(1, new byte[] { 1 }, CancellationToken.None);
            var second = queue.EnqueueAsync(1, new byte[] { 2 }, CancellationToken.None);
            SaveCommit a = await first;
            SaveCommit b = await second;
            Assert.AreEqual(1, a.Sequence);
            Assert.AreEqual(2, b.Sequence);
            Assert.IsTrue(a.PowerLossDurable);
            Assert.AreEqual(2, files.Committed.Count);
            CollectionAssert.AreEqual(new[] { "slot1.sav", "slot1.sav" }, files.Committed);
        });

        [UnityTest]
        public IEnumerator CancelBeforeCommit_LeavesDestinationUntouched() => UniTask.ToCoroutine(async () =>
        {
            var files = new Files();
            var queue = new SlotOperationQueue(files, slot => "slot" + slot + ".sav");
            using (var caller = new CancellationTokenSource())
            {
                caller.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    queue.EnqueueAsync(2, new byte[] { 3 }, caller.Token).GetAwaiter().GetResult());
                Assert.IsEmpty(files.Committed);
            }
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator IndeterminateCommit_ThrowsExplicitSaveError() => UniTask.ToCoroutine(async () =>
        {
            var files = new Files { Indeterminate = true };
            var queue = new SlotOperationQueue(files, slot => "slot" + slot + ".sav");
            Exception observed = null;
            try { await queue.EnqueueAsync(3, new byte[] { 4 }, CancellationToken.None); }
            catch (Exception failure) { observed = failure; }
            Assert.That(observed, Is.InstanceOf<SaveException>());
            Assert.AreEqual(SaveError.CommitOutcomeUnknown, ((SaveException)observed).Error);
        });
    }
}
