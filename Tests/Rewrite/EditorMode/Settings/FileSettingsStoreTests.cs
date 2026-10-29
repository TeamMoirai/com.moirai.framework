using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Settings;
using Moirai.Atropos.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Settings
{
    public sealed class FileSettingsStoreTests
    {
        private sealed class FileSystem : IFileSystem
        {
            internal readonly Dictionary<string, byte[]> Files =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);
            internal bool FailCommit;
            internal int Commits;

            public UniTask<StorageCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
                => UniTask.FromResult(StorageCapabilities.AtomicCommit);

            public UniTask<StorageEntry> GetEntryAsync(string path, CancellationToken cancellationToken)
                => UniTask.FromResult(Files.TryGetValue(path, out byte[] bytes)
                    ? new StorageEntry(path, false, bytes.Length)
                    : null);

            public UniTask<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
                => UniTask.FromResult(Files[path]);

            public UniTask WriteAllBytesAsync(string path, byte[] contents,
                CancellationToken cancellationToken)
            {
                Files[path] = contents;
                return UniTask.CompletedTask;
            }

            public UniTask DeleteAsync(string path, CancellationToken cancellationToken)
            {
                Files.Remove(path);
                return UniTask.CompletedTask;
            }

            public UniTask CreateDirectoryAsync(string path, CancellationToken cancellationToken)
                => UniTask.CompletedTask;

            public UniTask<StorageEntry[]> ListDirectoryAsync(string path,
                CancellationToken cancellationToken)
                => UniTask.FromResult(Array.Empty<StorageEntry>());

            public UniTask<StorageCommitResult> CommitAtomicAsync(string stagedPath,
                string destinationPath, CancellationToken cancellationToken)
            {
                Commits++;
                if (FailCommit)
                    return UniTask.FromException<StorageCommitResult>(new IOException("commit failed"));
                if (Files.TryGetValue(stagedPath, out byte[] bytes))
                {
                    Files[destinationPath] = bytes;
                    Files.Remove(stagedPath);
                }
                return UniTask.FromResult(new StorageCommitResult(true));
            }
        }

        private static readonly SettingsOptions Options = new SettingsOptions();

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
            Assert.That(files.Files.ContainsKey(Options.FilePath), Is.True);
            Assert.That(files.Files.ContainsKey(Options.FilePath + ".tmp"), Is.False);

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
            IKeyValueStore store = await FileSettingsStore.LoadAsync(files, Options);
            store.SetString("player.name", "Ada");

            files.FailCommit = true;
            Exception observed = null;
            try { await store.FlushAsync(CancellationToken.None); }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.InstanceOf<IOException>());
            Assert.That(store.IsDirty, Is.True);

            files.FailCommit = false;
            await store.FlushAsync(CancellationToken.None);

            Assert.That(store.IsDirty, Is.False);
            IKeyValueStore reloaded = await FileSettingsStore.LoadAsync(files, Options);
            Assert.AreEqual("Ada", reloaded.GetString("player.name"));
        });

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
            files.Files[Options.FilePath] = Encoding.UTF8.GetBytes("MIRAI-SETTINGS 99 0\n");

            Exception observed = null;
            try { await FileSettingsStore.LoadAsync(files, Options); }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.InstanceOf<InvalidOperationException>());
        });

        [UnityTest]
        public IEnumerator MalformedHeader_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            var files = new FileSystem();
            files.Files[Options.FilePath] = Encoding.UTF8.GetBytes("{ not json");

            Exception observed = null;
            try { await FileSettingsStore.LoadAsync(files, Options); }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.InstanceOf<InvalidDataException>());
        });
    }
}
