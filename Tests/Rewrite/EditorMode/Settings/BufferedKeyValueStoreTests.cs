using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition;
using Moirai.Atropos.Settings;
using Moirai.Atropos.Settings.Internal;
using Moirai.Atropos.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Settings
{
    public sealed class BufferedKeyValueStoreTests
    {
        private sealed class Writer : ISettingsWriter, IDisposable
        {
            internal readonly List<IReadOnlyDictionary<string, string>> _snapshots =
                new List<IReadOnlyDictionary<string, string>>();
            internal UniTaskCompletionSource<bool> _gate;
            internal UniTaskCompletionSource<bool> _afterCommit;
            internal Exception _failure;
            internal CancellationToken _token;
            internal bool _pastCommitPoint;
            internal int _commits;
            internal int _disposals;

            public async UniTask<StorageCommitResult> CommitAsync(
                IReadOnlyDictionary<string, string> snapshot, CancellationToken cancellationToken)
            {
                _token = cancellationToken;
                _snapshots.Add(new Dictionary<string, string>((IDictionary<string, string>)snapshot));
                if (_gate != null) await _gate.Task.AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _pastCommitPoint = true;
                if (_afterCommit != null) await _afterCommit.Task;
                if (_failure != null) throw _failure;
                _commits++;
                return new StorageCommitResult(true);
            }

            internal void Release()
            {
                _gate?.TrySetResult(true);
                _afterCommit?.TrySetResult(true);
            }

            public void Dispose() { _disposals++; }
        }

        private sealed class Storage : IFileSystem, IDisposable
        {
            internal int _disposals;
            public void Dispose() { _disposals++; }
            public UniTask<StorageCapabilities> GetCapabilitiesAsync(CancellationToken token)
                => UniTask.FromResult(StorageCapabilities.AtomicCommit);
            public UniTask<StorageEntry> GetEntryAsync(string path, CancellationToken token)
                => throw new NotSupportedException();
            public UniTask<byte[]> ReadAllBytesAsync(string path, CancellationToken token)
                => throw new NotSupportedException();
            public UniTask WriteAllBytesAsync(string path, byte[] contents, CancellationToken token)
                => throw new NotSupportedException();
            public UniTask DeleteAsync(string path, CancellationToken token)
                => throw new NotSupportedException();
            public UniTask CreateDirectoryAsync(string path, CancellationToken token)
                => throw new NotSupportedException();
            public UniTask<StorageEntry[]> ListDirectoryAsync(string path, CancellationToken token)
                => throw new NotSupportedException();
            public UniTask<StorageCommitResult> CommitAtomicAsync(string staged, string destination,
                CancellationToken token) => throw new NotSupportedException();
        }

        private sealed class Module : IServiceModule
        {
            private readonly ServiceDescriptor[] _descriptors;
            internal Module(params ServiceDescriptor[] descriptors) { _descriptors = descriptors; }
            public void Register(IServiceRegistrar registrar)
            {
                foreach (ServiceDescriptor descriptor in _descriptors) registrar.Add(descriptor);
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
            Assert.IsEmpty(writer._snapshots);
            store.SetInt("count", 8);
            Assert.AreEqual(1L, await store.FlushAsync(CancellationToken.None));
            Assert.AreEqual(1L, await store.FlushAsync(CancellationToken.None));
            Assert.AreEqual(1, writer._snapshots.Count);
        });

        [UnityTest]
        public IEnumerator WritesDuringFlush_StayDirtyForNextCommit() => UniTask.ToCoroutine(async () =>
        {
            var writer = new Writer { _gate = new UniTaskCompletionSource<bool>() };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            store.SetInt("a", 1);
            var flushing = store.FlushAsync(CancellationToken.None).Preserve();
            try
            {
                store.SetInt("b", 2);
                Assert.IsTrue(store.IsDirty);
                writer.Release();
                Assert.AreEqual(1L, await flushing);
                Assert.IsTrue(store.IsDirty);
                Assert.IsFalse(writer._snapshots[0].ContainsKey("b"));
                await store.FlushAsync(CancellationToken.None);
                Assert.IsFalse(store.IsDirty);
            }
            finally
            {
                writer.Release();
                await Capture(flushing);
            }
        });

        [UnityTest]
        public IEnumerator OverlappingFlush_RejectedAndFailureKeepsDirty() => UniTask.ToCoroutine(async () =>
        {
            var expected = new InvalidOperationException("disk full");
            var writer = new Writer { _gate = new UniTaskCompletionSource<bool>(), _failure = expected };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            store.SetInt("a", 1);
            var flushing = store.FlushAsync(CancellationToken.None).Preserve();
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    store.FlushAsync(CancellationToken.None).GetAwaiter().GetResult());
                writer.Release();
                Exception observed = await Capture(flushing);
                Assert.That(observed, Is.SameAs(expected));
                Assert.IsTrue(store.IsDirty);
            }
            finally
            {
                writer.Release();
                await Capture(flushing);
            }
        });

        [UnityTest]
        public IEnumerator Flush_BeforeCommitCancellation_KeepsDirty() => UniTask.ToCoroutine(async () =>
        {
            var writer = new Writer { _gate = new UniTaskCompletionSource<bool>() };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            using (var caller = new CancellationTokenSource())
            {
                store.SetInt("a", 1);
                var flushing = store.FlushAsync(caller.Token).Preserve();
                try
                {
                    Assert.That(writer._snapshots.Count, Is.EqualTo(1));
                    Assert.That(writer._pastCommitPoint, Is.False);
                    caller.Cancel();
                    Assert.That(writer._token.IsCancellationRequested, Is.True);
                    Assert.That(await Capture(flushing), Is.InstanceOf<OperationCanceledException>());
                    Assert.That(writer._commits, Is.Zero);
                    Assert.That(store.IsDirty, Is.True);
                    writer.Release();
                    Assert.That(await store.FlushAsync(CancellationToken.None), Is.EqualTo(1L));
                    Assert.That(store.IsDirty, Is.False);
                }
                finally
                {
                    writer.Release();
                    await Capture(flushing);
                }
            }
        });

        [UnityTest]
        public IEnumerator Flush_AfterCommitCancellation_ReturnsSuccess() => UniTask.ToCoroutine(async () =>
        {
            var writer = new Writer { _afterCommit = new UniTaskCompletionSource<bool>() };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            using (var caller = new CancellationTokenSource())
            {
                store.SetInt("a", 1);
                var flushing = store.FlushAsync(caller.Token).Preserve();
                try
                {
                    Assert.That(writer._pastCommitPoint, Is.True);
                    caller.Cancel();
                    Assert.That(writer._token.IsCancellationRequested, Is.True);
                    Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                    writer.Release();
                    Assert.That(await flushing, Is.EqualTo(1L));
                    Assert.That(writer._commits, Is.EqualTo(1));
                    Assert.That(store.IsDirty, Is.False);
                }
                finally
                {
                    writer.Release();
                    await Capture(flushing);
                }
            }
        });

        [UnityTest]
        public IEnumerator Stop_BeforeCommit_CancelsAcceptedWork() => UniTask.ToCoroutine(async () =>
        {
            var writer = new Writer { _gate = new UniTaskCompletionSource<bool>() };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            store.SetInt("a", 1);
            var flushing = store.FlushAsync(CancellationToken.None).Preserve();
            UniTask stopping = UniTask.CompletedTask;
            try
            {
                var stopper = RequireStop(store);
                Assert.That(writer._pastCommitPoint, Is.False);
                stopping = stopper.StopAsync(CancellationToken.None).Preserve();
                Assert.That(writer._token.IsCancellationRequested, Is.True);
                writer.Release();
                Assert.That(await Capture(flushing), Is.InstanceOf<OperationCanceledException>());
                Assert.That(await Capture(stopping), Is.Null);
                Assert.That(writer._pastCommitPoint, Is.False);
                Assert.That(writer._commits, Is.Zero);
                Assert.That(writer._snapshots.Count, Is.EqualTo(1));
                Assert.That(store.IsDirty, Is.True);
            }
            finally
            {
                writer.Release();
                await Capture(flushing);
                await Capture(stopping);
            }
        });

        [UnityTest]
        public IEnumerator Stop_AcceptedCommit_DrainsWithoutSavingNewChanges() => UniTask.ToCoroutine(async () =>
        {
            var writer = new Writer { _afterCommit = new UniTaskCompletionSource<bool>() };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            store.SetInt("a", 1);
            var flushing = store.FlushAsync(CancellationToken.None).Preserve();
            UniTask stopping = UniTask.CompletedTask;
            try
            {
                var stopper = RequireStop(store);
                store.SetInt("b", 2);
                stopping = stopper.StopAsync(CancellationToken.None).Preserve();
                Assert.That(writer._pastCommitPoint, Is.True);
                Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(stopping.Status, Is.EqualTo(UniTaskStatus.Pending));
                writer.Release();
                Assert.That(await flushing, Is.EqualTo(1L));
                await stopping;
                Assert.That(writer._commits, Is.EqualTo(1));
                Assert.That(writer._snapshots.Count, Is.EqualTo(1));
                Assert.That(writer._snapshots[0].ContainsKey("b"), Is.False);
                Assert.That(store.IsDirty, Is.True);
            }
            finally
            {
                writer.Release();
                await Capture(flushing);
                await Capture(stopping);
            }
        });

        [UnityTest]
        public IEnumerator Stop_CancellationCallbackReenters_DrainsAcceptedCommit()
            => UniTask.ToCoroutine(() => CancellationCallbackReentry(null));

        [UnityTest]
        public IEnumerator Stop_CancellationCallbackReentersAndThrows_DrainsBeforeReportingFailure()
            => UniTask.ToCoroutine(() => CancellationCallbackReentry(
                new InvalidOperationException("cancellation callback failed")));

        private static async UniTask CancellationCallbackReentry(Exception callbackFailure)
        {
            var writer = new Writer { _afterCommit = new UniTaskCompletionSource<bool>() };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            UniTask<long> flushing = default;
            UniTask stopping = UniTask.CompletedTask;
            UniTask reentrantStop = UniTask.CompletedTask;
            CancellationTokenRegistration registration = default;
            int callbacks = 0;
            try
            {
                var stopper = RequireStop(store);
                store.SetInt("a", 1);
                flushing = store.FlushAsync(CancellationToken.None).Preserve();
                Assert.That(writer._pastCommitPoint, Is.True);
                Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                registration = writer._token.Register(() =>
                {
                    callbacks++;
                    reentrantStop = stopper.StopAsync(CancellationToken.None).Preserve();
                    Assert.Throws<ObjectDisposedException>(() => store.SetInt("from.callback", 2));
                    if (callbackFailure != null) throw callbackFailure;
                });

                stopping = stopper.StopAsync(CancellationToken.None).Preserve();
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(writer._token.IsCancellationRequested, Is.True);
                Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(stopping.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(reentrantStop.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(store.Has("from.callback"), Is.False);
                Assert.That(writer._commits, Is.Zero);
                writer.Release();
                Assert.That(await flushing, Is.EqualTo(1L));
                Exception stopError = await Capture(stopping);
                Exception reentrantError = await Capture(reentrantStop);
                if (callbackFailure == null)
                {
                    Assert.That(stopError, Is.Null);
                    Assert.That(reentrantError, Is.Null);
                }
                else
                {
                    Assert.That(Contains(stopError, callbackFailure), Is.True);
                    Assert.That(Contains(reentrantError, callbackFailure), Is.True);
                }
                Assert.That(writer._commits, Is.EqualTo(1));
                Assert.That(writer._snapshots.Count, Is.EqualTo(1));
                Assert.That(store.IsDirty, Is.False);
            }
            finally
            {
                registration.Dispose();
                writer.Release();
                await Capture(flushing);
                await Capture(stopping);
                await Capture(reentrantStop);
                await Capture(store.StopAsync(CancellationToken.None));
            }
        }

        [UnityTest]
        public IEnumerator Stop_LateFailure_FlushAndStopObserveSameError() => UniTask.ToCoroutine(async () =>
        {
            var failure = new InvalidOperationException("late commit failure");
            var writer = new Writer
            {
                _afterCommit = new UniTaskCompletionSource<bool>(), _failure = failure
            };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            using (var caller = new CancellationTokenSource())
            {
                store.SetInt("a", 1);
                var flushing = store.FlushAsync(caller.Token).Preserve();
                UniTask stopping = UniTask.CompletedTask;
                try
                {
                    var stopper = RequireStop(store);
                    Assert.That(writer._pastCommitPoint, Is.True);
                    caller.Cancel();
                    stopping = stopper.StopAsync(CancellationToken.None).Preserve();
                    Assert.That(writer._token.IsCancellationRequested, Is.True);
                    Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                    Assert.That(stopping.Status, Is.EqualTo(UniTaskStatus.Pending));
                    writer.Release();
                    Assert.That(await Capture(flushing), Is.SameAs(failure));
                    Assert.That(Contains(await Capture(stopping), failure), Is.True);
                    Assert.That(store.IsDirty, Is.True);
                    Assert.That(writer._commits, Is.Zero);
                }
                finally
                {
                    writer.Release();
                    await Capture(flushing);
                    await Capture(stopping);
                }
            }
        });

        [UnityTest]
        public IEnumerator Stop_CancelledWaiter_OtherStopsAndDisposalsStillDrain() => UniTask.ToCoroutine(async () =>
        {
            var writer = new Writer { _afterCommit = new UniTaskCompletionSource<bool>() };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            store.SetInt("a", 1);
            var flushing = store.FlushAsync(CancellationToken.None).Preserve();
            var waiting = new List<UniTask<Exception>>();
            using (var caller = new CancellationTokenSource())
            {
                try
                {
                    var stopper = RequireStop(store);
                    var disposable = RequireDispose(store);
                    UniTask<Exception> cancelled = Capture(stopper.StopAsync(caller.Token)).Preserve();
                    waiting.Add(cancelled);
                    Assert.That(cancelled.Status, Is.EqualTo(UniTaskStatus.Pending));
                    caller.Cancel();
                    Assert.That(cancelled.Status, Is.Not.EqualTo(UniTaskStatus.Pending));
                    Assert.That(await cancelled, Is.InstanceOf<OperationCanceledException>());
                    Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                    waiting.Add(Capture(stopper.StopAsync(CancellationToken.None)).Preserve());
                    waiting.Add(Capture(stopper.StopAsync(CancellationToken.None)).Preserve());
                    waiting.Add(Capture(disposable.DisposeAsync()).Preserve());
                    waiting.Add(Capture(disposable.DisposeAsync()).Preserve());
                    for (int i = 1; i < waiting.Count; i++)
                        Assert.That(waiting[i].Status, Is.EqualTo(UniTaskStatus.Pending));
                    writer.Release();
                    Assert.That(await flushing, Is.EqualTo(1L));
                    for (int i = 1; i < waiting.Count; i++) Assert.That(await waiting[i], Is.Null);
                    await stopper.StopAsync(CancellationToken.None);
                    await disposable.DisposeAsync();
                    Assert.That(writer._snapshots.Count, Is.EqualTo(1));
                    Assert.That(writer._commits, Is.EqualTo(1));
                    Assert.That(store.IsDirty, Is.False);
                }
                finally
                {
                    writer.Release();
                    await Capture(flushing);
                    foreach (UniTask<Exception> task in waiting) await task;
                }
            }
        });

        [UnityTest]
        public IEnumerator Stop_InFlight_RejectsAllMutationsSynchronously()
            => UniTask.ToCoroutine(() => ClosingRejectsMutations(false));

        [UnityTest]
        public IEnumerator Dispose_InFlight_RejectsAllMutationsSynchronously()
            => UniTask.ToCoroutine(() => ClosingRejectsMutations(true));

        private static async UniTask ClosingRejectsMutations(bool dispose)
        {
            var writer = new Writer { _afterCommit = new UniTaskCompletionSource<bool>() };
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            store.SetBool("flag", true);
            store.SetInt("count", 1);
            store.SetFloat("ratio", 0.5f);
            store.SetString("name", "Ada");
            var flushing = store.FlushAsync(CancellationToken.None).Preserve();
            UniTask closing = UniTask.CompletedTask;
            UniTask<long> rejected = default;
            try
            {
                closing = (dispose ? RequireDispose(store).DisposeAsync()
                    : RequireStop(store).StopAsync(CancellationToken.None)).Preserve();
                Assert.That(closing.Status, Is.EqualTo(UniTaskStatus.Pending));
                AssertMutationsRejected(store);
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    rejected = store.FlushAsync(CancellationToken.None).Preserve();
                });
                writer.Release();
                Assert.That(await flushing, Is.EqualTo(4L));
                await closing;
                Assert.That(writer._snapshots.Count, Is.EqualTo(1));
                AssertMutationsRejected(store);
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    rejected = store.FlushAsync(CancellationToken.None).Preserve();
                });
            }
            finally
            {
                writer.Release();
                await Capture(flushing);
                await Capture(closing);
                await Capture(rejected);
            }
        }

        [UnityTest]
        public IEnumerator Stop_UnconfirmedDirty_DoesNotSave()
            => UniTask.ToCoroutine(() => ClosingDoesNotSave(false));

        [UnityTest]
        public IEnumerator Dispose_UnconfirmedDirty_DoesNotSave()
            => UniTask.ToCoroutine(() => ClosingDoesNotSave(true));

        private static async UniTask ClosingDoesNotSave(bool dispose)
        {
            var writer = new Writer();
            var store = new BufferedKeyValueStore(new Dictionary<string, string>(), writer);
            store.SetString("name", "unconfirmed");
            UniTask closing = UniTask.CompletedTask;
            try
            {
                closing = (dispose ? RequireDispose(store).DisposeAsync()
                    : RequireStop(store).StopAsync(CancellationToken.None)).Preserve();
                await closing;
                Assert.That(writer._snapshots, Is.Empty);
                Assert.That(writer._commits, Is.Zero);
                Assert.That(store.IsDirty, Is.True);
            }
            finally
            {
                writer.Release();
                await Capture(closing);
            }
        }

        [UnityTest]
        public IEnumerator Shutdown_FailedCommit_ReleasesDependenciesOnlyAfterDrain() => UniTask.ToCoroutine(async () =>
        {
            var failure = new InvalidOperationException("storage failed late");
            var writer = new Writer
            {
                _afterCommit = new UniTaskCompletionSource<bool>(), _failure = failure
            };
            var storage = new Storage();
            var root = new ServiceContainer(new IServiceModule[]
            {
                new Module(
                    new ServiceDescriptor(typeof(IFileSystem), Array.Empty<Type>(), ServiceLifetime.Root,
                        _ => storage),
                    new ServiceDescriptor(typeof(ISettingsWriter), new[] { typeof(IFileSystem) },
                        ServiceLifetime.Root, provider =>
                        {
                            Assert.That(provider.GetService(typeof(IFileSystem)), Is.SameAs(storage));
                            return writer;
                        }),
                    new ServiceDescriptor(typeof(IKeyValueStore), new[] { typeof(ISettingsWriter) },
                        ServiceLifetime.Root, provider => new BufferedKeyValueStore(
                            new Dictionary<string, string>(),
                            (ISettingsWriter)provider.GetService(typeof(ISettingsWriter)))))
            });
            UniTask<long> flushing = default;
            UniTask stopping = UniTask.CompletedTask;
            try
            {
                var store = root.Get<IKeyValueStore>();
                store.SetInt("a", 1);
                flushing = store.FlushAsync(CancellationToken.None).Preserve();
                Assert.That(writer._pastCommitPoint, Is.True);
                stopping = root.ShutdownAsync(CancellationToken.None).Preserve();
                Assert.That(writer._disposals, Is.Zero);
                Assert.That(storage._disposals, Is.Zero);
                Assert.That(stopping.Status, Is.EqualTo(UniTaskStatus.Pending));
                writer.Release();
                Assert.That(await Capture(flushing), Is.SameAs(failure));
                Assert.That(Contains(await Capture(stopping), failure), Is.True);
                Assert.That(writer._disposals, Is.EqualTo(1));
                Assert.That(storage._disposals, Is.EqualTo(1));
                await Capture(root.ShutdownAsync(CancellationToken.None));
                Assert.That(writer._disposals, Is.EqualTo(1));
                Assert.That(storage._disposals, Is.EqualTo(1));
            }
            finally
            {
                writer.Release();
                await Capture(flushing);
                await Capture(stopping);
                await Capture(root.ShutdownAsync(CancellationToken.None));
            }
        });

        private static void AssertMutationsRejected(BufferedKeyValueStore store)
        {
            Assert.Throws<ObjectDisposedException>(() => store.SetBool("flag", false));
            Assert.Throws<ObjectDisposedException>(() => store.SetInt("count", 2));
            Assert.Throws<ObjectDisposedException>(() => store.SetFloat("ratio", 1f));
            Assert.Throws<ObjectDisposedException>(() => store.SetString("name", "Grace"));
            Assert.Throws<ObjectDisposedException>(() => store.SetBool("flag", true));
            Assert.Throws<ObjectDisposedException>(() => store.SetInt("count", 1));
            Assert.Throws<ObjectDisposedException>(() => store.SetFloat("ratio", 0.5f));
            Assert.Throws<ObjectDisposedException>(() => store.SetString("name", "Ada"));
            Assert.Throws<ObjectDisposedException>(() => store.Remove("count"));
            Assert.Throws<ObjectDisposedException>(() => store.Remove("missing"));
            Assert.Throws<ObjectDisposedException>(() => store.RemoveAll());
        }

        private static IAsyncStoppable RequireStop(BufferedKeyValueStore store)
        {
            var stopper = ((object)store) as IAsyncStoppable;
            Assert.NotNull(stopper, "The settings owner must stop and drain accepted commits.");
            return stopper;
        }

        private static IAsyncDisposableService RequireDispose(BufferedKeyValueStore store)
        {
            var disposable = ((object)store) as IAsyncDisposableService;
            Assert.NotNull(disposable, "Disposal must wait for accepted commits.");
            return disposable;
        }

        private static async UniTask<Exception> Capture(UniTask task)
        {
            try { await task; return null; }
            catch (Exception error) { return error; }
        }

        private static async UniTask<Exception> Capture<T>(UniTask<T> task)
        {
            try { await task; return null; }
            catch (Exception error) { return error; }
        }

        private static bool Contains(Exception error, Exception expected)
        {
            if (ReferenceEquals(error, expected)) return true;
            if (error is AggregateException aggregate)
                foreach (Exception inner in aggregate.InnerExceptions)
                    if (Contains(inner, expected)) return true;
            return error?.InnerException != null && Contains(error.InnerException, expected);
        }

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
