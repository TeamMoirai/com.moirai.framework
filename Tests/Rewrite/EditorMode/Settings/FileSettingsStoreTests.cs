using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition;
using Moirai.Atropos.Settings;
using Moirai.Atropos.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Settings
{
    public sealed class FileSettingsStoreTests
    {
        private sealed class Attempt
        {
            internal UniTaskCompletionSource<bool> _writeGate;
            internal UniTaskCompletionSource<bool> _beforeCommit;
            internal UniTaskCompletionSource<bool> _afterCommit;
            internal Exception _writeFailure;
            internal Exception _commitFailure;
            internal string _stagedPath;
            internal string _destinationPath;
            internal CancellationToken _writeToken;
            internal CancellationToken _commitToken;
            internal bool _commitCalled;
            internal bool _pastCommitPoint;

            internal void Release()
            {
                _writeGate?.TrySetResult(true);
                _beforeCommit?.TrySetResult(true);
                _afterCommit?.TrySetResult(true);
            }
        }

        private sealed class FileSystem : IFileSystem, IDisposable
        {
            internal readonly Dictionary<string, byte[]> _files =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);
            internal readonly HashSet<string> _directories = new HashSet<string>(StringComparer.Ordinal);
            internal readonly Queue<Attempt> _attempts = new Queue<Attempt>();
            internal readonly List<Attempt> _writes = new List<Attempt>();
            internal readonly List<string> _operations = new List<string>();
            internal readonly List<string> _deletes = new List<string>();
            internal readonly List<CancellationToken> _deleteTokens = new List<CancellationToken>();
            private readonly Dictionary<string, Attempt> _pending = new Dictionary<string, Attempt>();
            internal UniTaskCompletionSource<bool> _entryGate;
            internal UniTaskCompletionSource<bool> _readGate;
            internal UniTaskCompletionSource<bool> _deleteGate;
            internal Exception _deleteFailure;
            internal bool _failCommit;
            internal int _commits;
            internal int _disposals;

            public void Dispose() { _disposals++; }

            public UniTask<StorageCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(StorageCapabilities.AtomicCommit);
            }

            public async UniTask<StorageEntry> GetEntryAsync(string path, CancellationToken cancellationToken)
            {
                _operations.Add("entry:" + path);
                if (_entryGate != null) await _entryGate.Task.AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (_directories.Contains(path)) return new StorageEntry(path, true, 0);
                return _files.TryGetValue(path, out byte[] bytes)
                    ? new StorageEntry(path, false, bytes.Length) : null;
            }

            public async UniTask<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
            {
                _operations.Add("read:" + path);
                byte[] bytes = (byte[])_files[path].Clone();
                if (_readGate != null) await _readGate.Task.AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return bytes;
            }

            public async UniTask WriteAllBytesAsync(string path, byte[] contents,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Attempt attempt = _attempts.Count == 0 ? new Attempt() : _attempts.Dequeue();
                attempt._stagedPath = path;
                attempt._writeToken = cancellationToken;
                _writes.Add(attempt);
                _pending[path] = attempt;
                _operations.Add("write:" + path);
                _files[path] = (byte[])contents.Clone();
                if (attempt._writeGate != null)
                    await attempt._writeGate.Task.AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt._writeFailure != null) throw attempt._writeFailure;
                _operations.Add("written:" + path);
            }

            public async UniTask DeleteAsync(string path, CancellationToken cancellationToken)
            {
                _operations.Add("delete:" + path);
                _deletes.Add(path);
                _deleteTokens.Add(cancellationToken);
                if (_deleteGate != null) await _deleteGate.Task.AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (_deleteFailure != null) throw _deleteFailure;
                _files.Remove(path);
                _pending.Remove(path);
            }

            public UniTask CreateDirectoryAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _operations.Add("directory:" + path);
                return UniTask.CompletedTask;
            }

            public UniTask<StorageEntry[]> ListDirectoryAsync(string path,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entries = new List<StorageEntry>();
                foreach (KeyValuePair<string, byte[]> file in _files)
                    if (string.IsNullOrEmpty(path) || file.Key.StartsWith(path + "/", StringComparison.Ordinal))
                        entries.Add(new StorageEntry(file.Key, false, file.Value.Length));
                return UniTask.FromResult(entries.ToArray());
            }

            public async UniTask<StorageCommitResult> CommitAtomicAsync(string stagedPath,
                string destinationPath, CancellationToken cancellationToken)
            {
                _commits++;
                _operations.Add("commit:" + stagedPath + "->" + destinationPath);
                Attempt attempt = _pending[stagedPath];
                attempt._destinationPath = destinationPath;
                attempt._commitToken = cancellationToken;
                attempt._commitCalled = true;
                if (attempt._beforeCommit != null)
                    await attempt._beforeCommit.Task.AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                attempt._pastCommitPoint = true;
                Exception failure = attempt._commitFailure ?? (_failCommit ? new IOException("commit failed") : null);
                if (failure != null)
                {
                    if (attempt._afterCommit != null) await attempt._afterCommit.Task;
                    throw failure;
                }
                _files[destinationPath] = (byte[])_files[stagedPath].Clone();
                _files.Remove(stagedPath);
                _pending.Remove(stagedPath);
                _operations.Add("replaced:" + destinationPath);
                if (attempt._afterCommit != null) await attempt._afterCommit.Task;
                return new StorageCommitResult(true);
            }
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

        private static SettingsOptions Options => new SettingsOptions();

        [UnityTest]
        public IEnumerator FlushThenReload_KeepsAllValueTypes() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, Options);
            store.SetBool("game.fullscreen", true);
            store.SetInt("game.quality", 3);
            store.SetFloat("audio.master", 0.75f);
            store.SetString("player.name", "Ada");
            store.SetString("player.bio", "line one\nline two with spaces");

            long revision = await store.FlushAsync(CancellationToken.None);

            Assert.That(revision, Is.GreaterThan(0));
            Assert.That(store.IsDirty, Is.False);
            Assert.That(files._files.ContainsKey(Options.FilePath), Is.True);
            CollectionAssert.AreEquivalent(new[] { Options.FilePath }, files._files.Keys);
            Assert.That(files._deletes, Is.Empty);

            IKeyValueStore reloaded = await FileSettingsStore.LoadAsync(files, Options);
            Assert.That(reloaded.GetBool("game.fullscreen"), Is.True);
            Assert.AreEqual(3, reloaded.GetInt("game.quality"));
            Assert.AreEqual(0.75f, reloaded.GetFloat("audio.master"));
            Assert.AreEqual("Ada", reloaded.GetString("player.name"));
            Assert.AreEqual("line one\nline two with spaces", reloaded.GetString("player.bio"));
            Assert.That(reloaded.IsDirty, Is.False);
        });

        [UnityTest]
        public IEnumerator FailedCommit_StaysDirty_AndRetryPersists() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            byte[] old = Bytes("old");
            files._files[Options.FilePath] = old;
            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, Options);
            store.SetString("player.name", "Ada");

            files._failCommit = true;
            Exception observed = null;
            try { await store.FlushAsync(CancellationToken.None); }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.InstanceOf<IOException>());
            Assert.That(store.IsDirty, Is.True);
            CollectionAssert.AreEqual(old, files._files[Options.FilePath]);
            CollectionAssert.AreEquivalent(new[] { Options.FilePath }, files._files.Keys);
            CollectionAssert.AreEqual(new[] { files._writes[0]._stagedPath }, files._deletes);

            files._failCommit = false;
            await store.FlushAsync(CancellationToken.None);

            Assert.That(store.IsDirty, Is.False);
            Assert.That(files._writes[1]._stagedPath, Is.Not.EqualTo(files._writes[0]._stagedPath));
            CollectionAssert.AreEquivalent(new[] { Options.FilePath }, files._files.Keys);
            IKeyValueStore reloaded = await FileSettingsStore.LoadAsync(files, Options);
            Assert.AreEqual("Ada", reloaded.GetString("player.name"));
        });

        [UnityTest]
        public IEnumerator Flush_OverlappingStores_IsolatesStagingAndSnapshots() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            var options = Options;
            files._files[options.FilePath] = Bytes("old");
            IKeyValueStore firstStore = await FileSettingsStore.LoadAsync(files, options);
            IKeyValueStore secondStore = await FileSettingsStore.LoadAsync(files, options);
            var first = new Attempt { _beforeCommit = new UniTaskCompletionSource<bool>() };
            var second = new Attempt { _beforeCommit = new UniTaskCompletionSource<bool>() };
            files._attempts.Enqueue(first);
            files._attempts.Enqueue(second);
            firstStore.SetString("player.name", "first");
            secondStore.SetString("player.name", "second");
            UniTask<long> firstFlush = default;
            UniTask<long> secondFlush = default;
            try
            {
                firstFlush = firstStore.FlushAsync(CancellationToken.None).Preserve();
                secondFlush = secondStore.FlushAsync(CancellationToken.None).Preserve();
                firstStore.SetString("player.name", "first later");
                Assert.That(firstFlush.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(secondFlush.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(first._commitCalled && second._commitCalled, Is.True);
                Assert.That(first._destinationPath, Is.EqualTo(options.FilePath));
                Assert.That(second._destinationPath, Is.EqualTo(options.FilePath));
                Assert.That(first._stagedPath, Is.Not.EqualTo(second._stagedPath));
                CollectionAssert.AreEquivalent(new[] { options.FilePath, first._stagedPath, second._stagedPath },
                    files._files.Keys);
                CollectionAssert.AreEqual(Bytes("old"), files._files[options.FilePath]);
                CollectionAssert.AreEqual(Bytes("first"), files._files[first._stagedPath]);
                CollectionAssert.AreEqual(Bytes("second"), files._files[second._stagedPath]);
                second.Release();
                Assert.That(await secondFlush, Is.EqualTo(1L));
                CollectionAssert.AreEqual(Bytes("second"), files._files[options.FilePath]);
                CollectionAssert.AreEqual(Bytes("first"), files._files[first._stagedPath]);
                CollectionAssert.AreEquivalent(new[] { options.FilePath, first._stagedPath }, files._files.Keys);
                Assert.That(firstFlush.Status, Is.EqualTo(UniTaskStatus.Pending));
                first.Release();
                Assert.That(await firstFlush, Is.EqualTo(1L));
                CollectionAssert.AreEqual(Bytes("first"), files._files[options.FilePath]);
                CollectionAssert.AreEquivalent(new[] { options.FilePath }, files._files.Keys);
                Assert.That(firstStore.IsDirty, Is.True);
                Assert.That(secondStore.IsDirty, Is.False);
                Assert.That(files._deletes, Is.Empty);
            }
            finally
            {
                first.Release();
                second.Release();
                await Capture(firstFlush);
                await Capture(secondFlush);
            }
        });

        [UnityTest]
        public IEnumerator Flush_WriteFailure_CleansStageAndKeepsOldTarget() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            var options = Options;
            files._files[options.FilePath] = Bytes("old");
            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, options);
            var failure = new IOException("partial staged write failed");
            var attempt = new Attempt
            {
                _writeGate = new UniTaskCompletionSource<bool>(), _writeFailure = failure
            };
            files._attempts.Enqueue(attempt);
            store.SetString("player.name", "new");
            var flushing = store.FlushAsync(CancellationToken.None).Preserve();
            try
            {
                Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                CollectionAssert.AreEqual(Bytes("old"), files._files[options.FilePath]);
                CollectionAssert.AreEqual(Bytes("new"), files._files[attempt._stagedPath]);
                Assert.That(attempt._commitCalled, Is.False);
                attempt.Release();
                Assert.That(await Capture(flushing), Is.SameAs(failure));
                Assert.That(store.IsDirty, Is.True);
                Assert.That(files._commits, Is.Zero);
                AssertCleaned(files, options.FilePath, attempt._stagedPath);
            }
            finally
            {
                attempt.Release();
                await Capture(flushing);
            }
        });

        [UnityTest]
        public IEnumerator Flush_WriteCancellation_CleansStageWithUncancelledToken()
            => UniTask.ToCoroutine(() => CancelBeforeCommit(true));

        [UnityTest]
        public IEnumerator Flush_PreCommitCancellation_CleansStageWithUncancelledToken()
            => UniTask.ToCoroutine(() => CancelBeforeCommit(false));

        private static async UniTask CancelBeforeCommit(bool writing)
        {
            var files = new FileSystem();
            var options = Options;
            files._files[options.FilePath] = Bytes("old");
            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, options);
            var gate = new UniTaskCompletionSource<bool>();
            var attempt = new Attempt
            {
                _writeGate = writing ? gate : null,
                _beforeCommit = writing ? null : gate
            };
            files._attempts.Enqueue(attempt);
            using (var caller = new CancellationTokenSource())
            {
                store.SetString("player.name", "new");
                var flushing = store.FlushAsync(caller.Token).Preserve();
                try
                {
                    Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                    Assert.That(attempt._commitCalled, Is.EqualTo(!writing));
                    Assert.That(attempt._pastCommitPoint, Is.False);
                    CollectionAssert.AreEqual(Bytes("old"), files._files[options.FilePath]);
                    CollectionAssert.AreEqual(Bytes("new"), files._files[attempt._stagedPath]);
                    caller.Cancel();
                    Assert.That((writing ? attempt._writeToken : attempt._commitToken).IsCancellationRequested,
                        Is.True);
                    attempt.Release();
                    Assert.That(await Capture(flushing), Is.InstanceOf<OperationCanceledException>());
                    Assert.That(store.IsDirty, Is.True);
                    Assert.That(attempt._pastCommitPoint, Is.False);
                    Assert.That(files._commits, Is.EqualTo(writing ? 0 : 1));
                    AssertCleaned(files, options.FilePath, attempt._stagedPath);
                }
                finally
                {
                    attempt.Release();
                    await Capture(flushing);
                }
            }
        }

        [UnityTest]
        public IEnumerator Flush_CleanupFailure_AggregatesBothErrorsAndKeepsStage() => UniTask.ToCoroutine(async () =>
        {
            var failure = new IOException("replace failed");
            var cleanupFailure = new UnauthorizedAccessException("delete failed");
            var deleteGate = new UniTaskCompletionSource<bool>();
            var files = new FileSystem { _deleteFailure = cleanupFailure, _deleteGate = deleteGate };
            var options = Options;
            files._files[options.FilePath] = Bytes("old");
            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, options);
            var attempt = new Attempt { _commitFailure = failure };
            files._attempts.Enqueue(attempt);
            store.SetString("player.name", "new");
            var flushing = store.FlushAsync(CancellationToken.None).Preserve();
            try
            {
                Assert.That(files._deletes, Is.EqualTo(new[] { attempt._stagedPath }));
                Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                CollectionAssert.AreEqual(Bytes("old"), files._files[options.FilePath]);
                CollectionAssert.AreEqual(Bytes("new"), files._files[attempt._stagedPath]);
                deleteGate.TrySetResult(true);
                var observed = await Capture(flushing) as AggregateException;
                Assert.NotNull(observed);
                CollectionAssert.AreEquivalent(new Exception[] { failure, cleanupFailure },
                    observed.Flatten().InnerExceptions);
                Assert.That(store.IsDirty, Is.True);
                CollectionAssert.AreEquivalent(new[] { options.FilePath, attempt._stagedPath }, files._files.Keys);
                CollectionAssert.AreEqual(Bytes("old"), files._files[options.FilePath]);
                CollectionAssert.AreEqual(Bytes("new"), files._files[attempt._stagedPath]);
                Assert.That(files._deleteTokens[0].CanBeCanceled, Is.False);
                Assert.That(files._operations.IndexOf("delete:" + attempt._stagedPath), Is.GreaterThan(
                    files._operations.IndexOf("commit:" + attempt._stagedPath + "->" + options.FilePath)));
            }
            finally
            {
                attempt.Release();
                deleteGate.TrySetResult(true);
                await Capture(flushing);
            }
        });

        [UnityTest]
        public IEnumerator Shutdown_PendingCleanup_WaitsBeforeDisposingFileSystem()
            => UniTask.ToCoroutine(() => ShutdownDuringCleanup(null));

        [UnityTest]
        public IEnumerator Shutdown_FailedCleanup_ReportsBothErrorsBeforeDisposingFileSystem()
            => UniTask.ToCoroutine(() => ShutdownDuringCleanup(
                new UnauthorizedAccessException("delete failed during shutdown")));

        private static async UniTask ShutdownDuringCleanup(Exception cleanupFailure)
        {
            var failure = new IOException("replace failed before shutdown");
            var deleteGate = new UniTaskCompletionSource<bool>();
            var files = new FileSystem { _deleteGate = deleteGate, _deleteFailure = cleanupFailure };
            var options = Options;
            files._files[options.FilePath] = Bytes("old");
            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, options);
            var attempt = new Attempt { _commitFailure = failure };
            files._attempts.Enqueue(attempt);
            var root = new ServiceContainer(new IServiceModule[]
            {
                new Module(
                    new ServiceDescriptor(typeof(IFileSystem), Array.Empty<Type>(), ServiceLifetime.Root,
                        _ => files),
                    new ServiceDescriptor(typeof(IKeyValueStore), new[] { typeof(IFileSystem) },
                        ServiceLifetime.Root, provider =>
                        {
                            Assert.That(provider.GetService(typeof(IFileSystem)), Is.SameAs(files));
                            return store;
                        }))
            });
            var stopper = store as IAsyncStoppable;
            UniTask<long> flushing = default;
            UniTask callerStop = UniTask.CompletedTask;
            UniTask rootShutdown = UniTask.CompletedTask;
            UniTask otherStop = UniTask.CompletedTask;
            using (var caller = new CancellationTokenSource())
            {
                try
                {
                    Assert.That(root.Get<IKeyValueStore>(), Is.SameAs(store));
                    Assert.NotNull(stopper);
                    store.SetString("player.name", "new");
                    flushing = store.FlushAsync(CancellationToken.None).Preserve();
                    Assert.That(attempt._commitCalled, Is.True);
                    CollectionAssert.AreEqual(new[] { attempt._stagedPath }, files._deletes);
                    Assert.That(files._deleteTokens.Count, Is.EqualTo(1));
                    CancellationToken sourceDeleteToken = files._deleteTokens[0];
                    Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));

                    callerStop = stopper.StopAsync(caller.Token).Preserve();
                    Assert.That(callerStop.Status, Is.EqualTo(UniTaskStatus.Pending));
                    caller.Cancel();
                    Assert.That(callerStop.Status, Is.Not.EqualTo(UniTaskStatus.Pending));
                    Assert.That(await Capture(callerStop), Is.InstanceOf<OperationCanceledException>());
                    rootShutdown = root.ShutdownAsync(CancellationToken.None).Preserve();
                    otherStop = stopper.StopAsync(CancellationToken.None).Preserve();
                    Assert.That(attempt._commitToken.IsCancellationRequested, Is.True);
                    Assert.That(sourceDeleteToken.CanBeCanceled, Is.False);
                    Assert.That(files._disposals, Is.Zero);
                    Assert.That(rootShutdown.Status, Is.EqualTo(UniTaskStatus.Pending));
                    Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                    Assert.That(otherStop.Status, Is.EqualTo(UniTaskStatus.Pending));
                    CollectionAssert.AreEquivalent(new[] { options.FilePath, attempt._stagedPath }, files._files.Keys);
                    CollectionAssert.AreEqual(Bytes("old"), files._files[options.FilePath]);
                    CollectionAssert.AreEqual(Bytes("new"), files._files[attempt._stagedPath]);
                    Assert.That(files._operations.IndexOf("delete:" + attempt._stagedPath), Is.GreaterThan(
                        files._operations.IndexOf("commit:" + attempt._stagedPath + "->" + options.FilePath)));

                    deleteGate.TrySetResult(true);
                    Exception flushError = await Capture(flushing);
                    Exception shutdownError = await Capture(rootShutdown);
                    Exception stopError = await Capture(otherStop);
                    Assert.That(Contains(flushError, failure), Is.True);
                    Assert.That(Contains(shutdownError, failure), Is.True);
                    Assert.That(Contains(stopError, failure), Is.True);
                    if (cleanupFailure == null)
                    {
                        Assert.That(flushError, Is.SameAs(failure));
                        AssertCleaned(files, options.FilePath, attempt._stagedPath);
                    }
                    else
                    {
                        var aggregate = flushError as AggregateException;
                        Assert.NotNull(aggregate);
                        CollectionAssert.AreEquivalent(new[] { failure, cleanupFailure },
                            aggregate.Flatten().InnerExceptions);
                        Assert.That(Contains(shutdownError, cleanupFailure), Is.True);
                        Assert.That(Contains(stopError, cleanupFailure), Is.True);
                        CollectionAssert.AreEquivalent(new[] { options.FilePath, attempt._stagedPath },
                            files._files.Keys);
                        CollectionAssert.AreEqual(Bytes("old"), files._files[options.FilePath]);
                        CollectionAssert.AreEqual(Bytes("new"), files._files[attempt._stagedPath]);
                    }
                    Assert.That(store.IsDirty, Is.True);
                    Assert.That(files._commits, Is.EqualTo(1));
                    CollectionAssert.AreEqual(new[] { attempt._stagedPath }, files._deletes);
                    Assert.That(files._deleteTokens.Count, Is.EqualTo(1));
                    Assert.That(files._disposals, Is.EqualTo(1));
                    Assert.That(Contains(await Capture(root.ShutdownAsync(CancellationToken.None)), failure), Is.True);
                    Assert.That(files._disposals, Is.EqualTo(1));
                }
                finally
                {
                    attempt.Release();
                    deleteGate.TrySetResult(true);
                    await Capture(flushing);
                    await Capture(callerStop);
                    await Capture(rootShutdown);
                    await Capture(otherStop);
                    await Capture(root.ShutdownAsync(CancellationToken.None));
                    if (stopper != null) await Capture(stopper.StopAsync(CancellationToken.None));
                }
            }
        }

        [UnityTest]
        public IEnumerator Flush_CancelledCleanupFailure_DoesNotHideEitherError() => UniTask.ToCoroutine(async () =>
        {
            var cleanupFailure = new IOException("delete failed after cancellation");
            var files = new FileSystem { _deleteFailure = cleanupFailure };
            var options = Options;
            files._files[options.FilePath] = Bytes("old");
            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, options);
            var attempt = new Attempt { _beforeCommit = new UniTaskCompletionSource<bool>() };
            files._attempts.Enqueue(attempt);
            using (var caller = new CancellationTokenSource())
            {
                store.SetString("player.name", "new");
                var flushing = store.FlushAsync(caller.Token).Preserve();
                try
                {
                    Assert.That(attempt._commitCalled, Is.True);
                    Assert.That(attempt._pastCommitPoint, Is.False);
                    caller.Cancel();
                    attempt.Release();
                    var observed = await Capture(flushing) as AggregateException;
                    Assert.NotNull(observed);
                    var errors = observed.Flatten().InnerExceptions;
                    Assert.That(errors.Count, Is.EqualTo(2));
                    Assert.That(errors, Does.Contain(cleanupFailure));
                    Assert.That(errors[0] is OperationCanceledException || errors[1] is OperationCanceledException,
                        Is.True);
                    Assert.That(store.IsDirty, Is.True);
                    CollectionAssert.AreEqual(Bytes("old"), files._files[options.FilePath]);
                    CollectionAssert.AreEqual(Bytes("new"), files._files[attempt._stagedPath]);
                    CollectionAssert.AreEquivalent(new[] { options.FilePath, attempt._stagedPath }, files._files.Keys);
                    CollectionAssert.AreEqual(new[] { attempt._stagedPath }, files._deletes);
                    Assert.That(files._deleteTokens[0].CanBeCanceled, Is.False);
                }
                finally
                {
                    attempt.Release();
                    await Capture(flushing);
                }
            }
        });

        [UnityTest]
        public IEnumerator Flush_IndeterminateCommit_NextStoreCannotOverwriteEvidence() => UniTask.ToCoroutine(async () =>
        {
            var failure = new StorageCommitIndeterminateException("outcome unknown", new IOException("connection lost"));
            var files = new FileSystem();
            var options = Options;
            files._files[options.FilePath] = Bytes("old");
            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, options);
            var uncertain = new Attempt { _commitFailure = failure };
            var next = new Attempt { _beforeCommit = new UniTaskCompletionSource<bool>() };
            files._attempts.Enqueue(uncertain);
            files._attempts.Enqueue(next);
            store.SetString("player.name", "uncertain");
            UniTask<long> firstFlush = default;
            UniTask<long> nextFlush = default;
            try
            {
                firstFlush = store.FlushAsync(CancellationToken.None).Preserve();
                Assert.That(await Capture(firstFlush), Is.SameAs(failure));
                Assert.That(store.IsDirty, Is.True);
                Assert.That(files._commits, Is.EqualTo(1));
                Assert.That(files._deletes, Is.Empty);
                CollectionAssert.AreEquivalent(new[] { options.FilePath, uncertain._stagedPath }, files._files.Keys);
                CollectionAssert.AreEqual(Bytes("old"), files._files[options.FilePath]);
                CollectionAssert.AreEqual(Bytes("uncertain"), files._files[uncertain._stagedPath]);
                IKeyValueStore nextStore = await FileSettingsStore.LoadAsync(files, options);
                nextStore.SetString("player.name", "next");
                nextFlush = nextStore.FlushAsync(CancellationToken.None).Preserve();
                Assert.That(next._commitCalled, Is.True);
                Assert.That(nextFlush.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(next._stagedPath, Is.Not.EqualTo(uncertain._stagedPath));
                CollectionAssert.AreEqual(Bytes("uncertain"), files._files[uncertain._stagedPath]);
                CollectionAssert.AreEqual(Bytes("next"), files._files[next._stagedPath]);
                CollectionAssert.AreEquivalent(new[] { options.FilePath, uncertain._stagedPath, next._stagedPath },
                    files._files.Keys);
                next.Release();
                Assert.That(await nextFlush, Is.EqualTo(1L));
                Assert.That(nextStore.IsDirty, Is.False);
                CollectionAssert.AreEqual(Bytes("next"), files._files[options.FilePath]);
                CollectionAssert.AreEqual(Bytes("uncertain"), files._files[uncertain._stagedPath]);
                CollectionAssert.AreEquivalent(new[] { options.FilePath, uncertain._stagedPath }, files._files.Keys);
                Assert.That(files._deletes, Is.Empty);
                Assert.That(files._commits, Is.EqualTo(2));
            }
            finally
            {
                uncertain.Release();
                next.Release();
                await Capture(firstFlush);
                await Capture(nextFlush);
            }
        });

        [UnityTest]
        public IEnumerator Flush_PostCommitCancellation_ReturnsSuccessAndKeepsNewTarget() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            var options = Options;
            files._files[options.FilePath] = Bytes("old");
            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, options);
            var attempt = new Attempt { _afterCommit = new UniTaskCompletionSource<bool>() };
            files._attempts.Enqueue(attempt);
            using (var caller = new CancellationTokenSource())
            {
                store.SetString("player.name", "new");
                var flushing = store.FlushAsync(caller.Token).Preserve();
                try
                {
                    Assert.That(attempt._pastCommitPoint, Is.True);
                    CollectionAssert.AreEqual(Bytes("new"), files._files[options.FilePath]);
                    CollectionAssert.AreEquivalent(new[] { options.FilePath }, files._files.Keys);
                    caller.Cancel();
                    Assert.That(attempt._commitToken.IsCancellationRequested, Is.True);
                    Assert.That(flushing.Status, Is.EqualTo(UniTaskStatus.Pending));
                    attempt.Release();
                    Assert.That(await flushing, Is.EqualTo(1L));
                    Assert.That(store.IsDirty, Is.False);
                    Assert.That(files._commits, Is.EqualTo(1));
                    Assert.That(files._deletes, Is.Empty);
                    CollectionAssert.AreEqual(Bytes("new"), files._files[options.FilePath]);
                    CollectionAssert.AreEquivalent(new[] { options.FilePath }, files._files.Keys);
                }
                finally
                {
                    attempt.Release();
                    await Capture(flushing);
                }
            }
        });

        [UnityTest]
        public IEnumerator Load_PendingMetadata_UsesAcceptedPathAndFormat() => UniTask.ToCoroutine(async () =>
        {
            var gate = new UniTaskCompletionSource<bool>();
            var files = new FileSystem { _entryGate = gate };
            var options = new SettingsOptions { FilePath = "original.settings", FormatVersion = 2 };
            files._files["original.settings"] = Bytes("original", 2);
            files._files["changed.settings"] = Bytes("changed", 1);
            var loading = FileSettingsStore.LoadAsync(files, options).Preserve();
            try
            {
                Assert.That(loading.Status, Is.EqualTo(UniTaskStatus.Pending));
                CollectionAssert.AreEqual(new[] { "entry:original.settings" }, files._operations);
                options.FilePath = "changed.settings";
                options.FormatVersion = 1;
                gate.TrySetResult(true);
                IKeyValueStore store = await loading;
                Assert.That(store.GetString("player.name"), Is.EqualTo("original"));
                Assert.That(files._operations, Does.Contain("read:original.settings"));
                Assert.That(files._operations, Does.Not.Contain("read:changed.settings"));
                store.SetString("player.name", "saved original");
                Assert.That(await store.FlushAsync(CancellationToken.None), Is.EqualTo(1L));
                CollectionAssert.AreEqual(Bytes("saved original", 2), files._files["original.settings"]);
                CollectionAssert.AreEqual(Bytes("changed", 1), files._files["changed.settings"]);
                CollectionAssert.AreEquivalent(new[] { "original.settings", "changed.settings" }, files._files.Keys);
            }
            finally
            {
                gate.TrySetResult(true);
                await Capture(loading);
            }
        });

        [UnityTest]
        public IEnumerator Load_PendingRead_FreezesOptionsButNextLoadAcceptsChanges() => UniTask.ToCoroutine(async () =>
        {
            var gate = new UniTaskCompletionSource<bool>();
            var files = new FileSystem { _readGate = gate };
            var options = new SettingsOptions { FilePath = "original.settings", FormatVersion = 2 };
            files._files["original.settings"] = Bytes("original", 2);
            files._files["changed.settings"] = Bytes("changed", 1);
            var loading = FileSettingsStore.LoadAsync(files, options).Preserve();
            try
            {
                Assert.That(loading.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(files._operations, Does.Contain("read:original.settings"));
                options.FilePath = "changed.settings";
                options.FormatVersion = 1;
                files._readGate = null;
                IKeyValueStore next = await FileSettingsStore.LoadAsync(files, options);
                Assert.That(next.GetString("player.name"), Is.EqualTo("changed"));
                next.SetString("player.name", "saved changed");
                Assert.That(await next.FlushAsync(CancellationToken.None), Is.EqualTo(1L));
                CollectionAssert.AreEqual(Bytes("saved changed", 1), files._files["changed.settings"]);
                CollectionAssert.AreEqual(Bytes("original", 2), files._files["original.settings"]);
                gate.TrySetResult(true);
                IKeyValueStore original = await loading;
                Assert.That(original.GetString("player.name"), Is.EqualTo("original"));
                original.SetString("player.name", "saved original");
                Assert.That(await original.FlushAsync(CancellationToken.None), Is.EqualTo(1L));
                CollectionAssert.AreEqual(Bytes("saved original", 2), files._files["original.settings"]);
                CollectionAssert.AreEqual(Bytes("saved changed", 1), files._files["changed.settings"]);
                CollectionAssert.AreEquivalent(new[] { "original.settings", "changed.settings" }, files._files.Keys);
                Assert.That(files._writes[0]._destinationPath, Is.EqualTo("changed.settings"));
                Assert.That(files._writes[1]._destinationPath, Is.EqualTo("original.settings"));
            }
            finally
            {
                gate.TrySetResult(true);
                await Capture(loading);
            }
        });

        private static byte[] Bytes(string value, int format = 2)
        {
            return Encoding.UTF8.GetBytes("MOIRAI-SETTINGS " + format + " 1\n" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes("player.name")) + " " +
                Convert.ToBase64String(Encoding.UTF8.GetBytes("s:" + value)) + "\n");
        }

        private static void AssertCleaned(FileSystem files, string path, string staged)
        {
            CollectionAssert.AreEqual(Bytes("old"), files._files[path]);
            CollectionAssert.AreEquivalent(new[] { path }, files._files.Keys);
            CollectionAssert.AreEqual(new[] { staged }, files._deletes);
            Assert.That(files._deleteTokens.Count, Is.EqualTo(1));
            Assert.That(files._deleteTokens[0].CanBeCanceled, Is.False);
            Assert.That(files._operations.IndexOf("delete:" + staged),
                Is.GreaterThan(files._operations.IndexOf("write:" + staged)));
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

        [UnityTest]
        public IEnumerator MissingFile_LoadsEmptyStore() => UniTask.ToCoroutine(async () =>
        {
            IKeyValueStore store = await FileSettingsStore.LoadAsync(new FileSystem(), Options);

            Assert.AreEqual(0, store.Count);
            Assert.That(store.IsDirty, Is.False);
        });

        [UnityTest]
        public IEnumerator NewerFormat_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Encoding.UTF8.GetBytes("MOIRAI-SETTINGS 99 0\n");

            Exception observed = null;
            try { await FileSettingsStore.LoadAsync(files, Options); }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.InstanceOf<InvalidOperationException>());
            Assert.That(observed.Message, Does.Contain("newer"));
        });

        [UnityTest]
        public IEnumerator MalformedHeader_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Encoding.UTF8.GetBytes("{ not json");

            Exception observed = null;
            try { await FileSettingsStore.LoadAsync(files, Options); }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.InstanceOf<InvalidDataException>());
        });

        [UnityTest]
        public IEnumerator ZeroLengthFile_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Array.Empty<byte>();

            Assert.That(await CaptureLoad(files, Options), Is.InstanceOf<InvalidDataException>());
        });

        [UnityTest]
        public IEnumerator DirectoryAtPath_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._directories.Add(Options.FilePath);

            Assert.That(await CaptureLoad(files, Options), Is.InstanceOf<InvalidOperationException>());
        });

        [UnityTest]
        public IEnumerator OlderFormat_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Raw("MOIRAI-SETTINGS 1 1", Entry("player.name", "s:Ada"));

            Exception observed = await CaptureLoad(files, Options);
            Assert.That(observed, Is.InstanceOf<InvalidOperationException>());
            Assert.That(observed.Message, Does.Contain("migration"));
        });

        [UnityTest]
        public IEnumerator OlderFormat_LoadsWhenOptionsMatchFileVersion() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files["settings.v1"] = Raw("MOIRAI-SETTINGS 1 1", Entry("player.name", "s:Ada"));
            var options = new SettingsOptions { FilePath = "settings.v1", FormatVersion = 1 };

            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, options);

            Assert.That(store.GetString("player.name"), Is.EqualTo("Ada"));
        });

        [UnityTest]
        public IEnumerator UnknownValuePrefix_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Raw("MOIRAI-SETTINGS 2 1", Entry("player.name", "x:Ada"));

            Assert.That(await CaptureLoad(files, Options), Is.InstanceOf<InvalidDataException>());
        });

        [UnityTest]
        public IEnumerator MistypedIntValue_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Raw("MOIRAI-SETTINGS 2 1", Entry("game.quality", "i:oops"));

            Assert.That(await CaptureLoad(files, Options), Is.InstanceOf<InvalidDataException>());
        });

        [UnityTest]
        public IEnumerator DuplicateKey_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Raw("MOIRAI-SETTINGS 2 2",
                Entry("player.name", "s:Ada"), Entry("player.name", "s:Grace"));

            Assert.That(await CaptureLoad(files, Options), Is.InstanceOf<InvalidDataException>());
        });

        [UnityTest]
        public IEnumerator ExtraEntries_AreRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Raw("MOIRAI-SETTINGS 2 1",
                Entry("player.name", "s:Ada"), Entry("player.extra", "s:again"));

            Assert.That(await CaptureLoad(files, Options), Is.InstanceOf<InvalidDataException>());
        });

        [UnityTest]
        public IEnumerator EmptyKey_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Raw("MOIRAI-SETTINGS 2 1", Entry(string.Empty, "s:Ada"));

            Assert.That(await CaptureLoad(files, Options), Is.InstanceOf<InvalidDataException>());
        });

        [UnityTest]
        public IEnumerator EmptySnapshot_LoadsEmptyStore() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Raw("MOIRAI-SETTINGS 2 0");

            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, Options);

            Assert.AreEqual(0, store.Count);
            Assert.That(store.IsDirty, Is.False);
        });

        [UnityTest]
        public IEnumerator TruncatedEntries_AreRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Raw("MOIRAI-SETTINGS 2 2", Entry("player.name", "s:Ada"));

            Assert.That(await CaptureLoad(files, Options), Is.InstanceOf<InvalidDataException>());
        });

        [UnityTest]
        public IEnumerator MissingTrailingNewline_Loads() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Encoding.UTF8.GetBytes(
                "MOIRAI-SETTINGS 2 1\n" + Entry("player.name", "s:Ada"));

            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, Options);

            Assert.That(store.GetString("player.name"), Is.EqualTo("Ada"));
        });

        [UnityTest]
        public IEnumerator InvalidUtf8Key_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files._files[Options.FilePath] = Raw("MOIRAI-SETTINGS 2 1",
                "/w== " + Convert.ToBase64String(Encoding.UTF8.GetBytes("s:x")));

            Assert.That(await CaptureLoad(files, Options), Is.InstanceOf<InvalidDataException>());
        });

        private static byte[] Raw(params string[] lines)
            => Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");

        private static string Entry(string key, string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(key)) + " " +
               Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        private static async UniTask<Exception> CaptureLoad(FileSystem files, SettingsOptions options)
        {
            try { await FileSettingsStore.LoadAsync(files, options); return null; }
            catch (Exception error) { return error; }
        }
    }
}
