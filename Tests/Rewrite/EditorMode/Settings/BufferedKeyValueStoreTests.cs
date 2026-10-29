using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Settings.Internal;
using Moirai.Atropos.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Settings
{
    public sealed class BufferedKeyValueStoreTests
    {
        private sealed class Writer : ISettingsWriter
        {
            internal readonly List<IReadOnlyDictionary<string, string>> Snapshots =
                new List<IReadOnlyDictionary<string, string>>();
            internal UniTaskCompletionSource<bool> Gate;
            internal Exception Failure;

            public async UniTask<StorageCommitResult> CommitAsync(
                IReadOnlyDictionary<string, string> snapshot, CancellationToken cancellationToken)
            {
                Snapshots.Add(new Dictionary<string, string>((IDictionary<string, string>)snapshot));
                if (Gate != null) await Gate.Task.AttachExternalCancellation(cancellationToken);
                if (Failure != null) throw Failure;
                return new StorageCommitResult(true);
            }
        }

        [UnityTest]
        public IEnumerator TypedValues_RoundTripAndSameWriteKeepsRevision() => UniTask.ToCoroutine(async () =>
        {
            var writer = new Writer();
            var store = new BufferedKeyValueStore(new Dictionary<string, string>
            {
                { "flag", "b:True" }, { "count", "i:7" }, { "ratio", "f:0.5" }, { "name", "s:moirai" }
            }, writer);
            Assert.IsFalse(store.IsDirty);
            Assert.IsTrue(store.GetBool("flag"));
            Assert.AreEqual(7, store.GetInt("count"));
            Assert.AreEqual(0.5f, store.GetFloat("ratio"));
            Assert.AreEqual("moirai", store.GetString("name"));
            store.SetInt("count", 7);
            Assert.IsFalse(store.IsDirty);
            Assert.AreEqual(0L, await store.FlushAsync(CancellationToken.None));
            Assert.IsEmpty(writer.Snapshots);
            store.SetInt("count", 8);
            Assert.AreEqual(1L, await store.FlushAsync(CancellationToken.None));
            Assert.AreEqual(1L, await store.FlushAsync(CancellationToken.None));
            Assert.AreEqual(1, writer.Snapshots.Count);
        });

        [UnityTest]
        public IEnumerator WritesDuringFlush_StayDirtyForNextCommit() => UniTask.ToCoroutine(async () =>
        {
            var writer = new Writer { Gate = new UniTaskCompletionSource<bool>() };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            store.SetInt("a", 1);
            var flushing = store.FlushAsync(CancellationToken.None);
            store.SetInt("b", 2);
            Assert.IsTrue(store.IsDirty);
            writer.Gate.TrySetResult(true);
            Assert.AreEqual(1L, await flushing);
            Assert.IsTrue(store.IsDirty);
            Assert.IsFalse(writer.Snapshots[0].ContainsKey("b"));
            await store.FlushAsync(CancellationToken.None);
            Assert.IsFalse(store.IsDirty);
        });

        [UnityTest]
        public IEnumerator OverlappingFlush_RejectedAndFailureKeepsDirty() => UniTask.ToCoroutine(async () =>
        {
            var expected = new InvalidOperationException("disk full");
            var writer = new Writer { Gate = new UniTaskCompletionSource<bool>(), Failure = expected };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            store.SetInt("a", 1);
            var flushing = store.FlushAsync(CancellationToken.None);
            Assert.Throws<InvalidOperationException>(() =>
                store.FlushAsync(CancellationToken.None).GetAwaiter().GetResult());
            writer.Gate.TrySetResult(true);
            Exception observed = null;
            try { await flushing; }
            catch (Exception failure) { observed = failure; }
            Assert.That(observed, Is.SameAs(expected));
            Assert.IsTrue(store.IsDirty);
        });

        [Test]
        public void RemoveAndRemoveAll_TrackDirtyState()
        {
            var store = new BufferedKeyValueStore(new Dictionary<string, string>
            {
                { "a", "i:1" }, { "b", "i:2" }
            }, new Writer());
            Assert.IsTrue(store.Remove("a"));
            Assert.IsFalse(store.Remove("a"));
            Assert.IsTrue(store.IsDirty);
            store.RemoveAll();
            Assert.AreEqual(0, store.Count);
            var keys = new List<string> { "stale" };
            store.GetAllKeys(keys);
            Assert.IsEmpty(keys);
        }

        [Test]
        public void StateMaskedValues_ReturnDefaults()
        {
            var store = new BufferedKeyValueStore(new Dictionary<string, string>
            {
                { "int", "s:text" }, { "broken", "i:oops" }
            }, new Writer());
            Assert.AreEqual(5, store.GetInt("int", 5));
            Assert.AreEqual(6, store.GetInt("broken", 6));
            Assert.AreEqual("text", store.GetString("int"));
            Assert.Throws<ArgumentNullException>(() => store.SetString("null", null));
        }
    }
}
