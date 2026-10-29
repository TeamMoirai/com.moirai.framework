using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Persistence;
using Moirai.Atropos.Persistence.Internal;
using Moirai.Atropos.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Persistence
{
    public sealed class SaveServiceTests
    {
        private sealed class Files : IFileSystem
        {
            internal readonly Dictionary<string, byte[]> Data =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);

            public UniTask<StorageCapabilities> GetCapabilitiesAsync(CancellationToken ct)
                => UniTask.FromResult(StorageCapabilities.AsyncWrite);
            public UniTask<StorageEntry> GetEntryAsync(string path, CancellationToken ct)
                => UniTask.FromResult<StorageEntry>(null);
            public UniTask<byte[]> ReadAllBytesAsync(string path, CancellationToken ct)
                => Data.TryGetValue(path, out byte[] bytes)
                    ? UniTask.FromResult(bytes)
                    : UniTask.FromException<byte[]>(new System.IO.FileNotFoundException(path));
            public UniTask WriteAllBytesAsync(string path, byte[] contents, CancellationToken ct)
            {
                Data[path] = contents;
                return UniTask.CompletedTask;
            }
            public UniTask DeleteAsync(string path, CancellationToken ct)
            {
                Data.Remove(path);
                return UniTask.CompletedTask;
            }
            public UniTask CreateDirectoryAsync(string path, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask<StorageEntry[]> ListDirectoryAsync(string path, CancellationToken ct)
                => UniTask.FromResult(Array.Empty<StorageEntry>());
            public UniTask<StorageCommitResult> CommitAtomicAsync(string staged, string destination,
                CancellationToken ct)
            {
                if (!Data.TryGetValue(staged, out byte[] bytes))
                    return UniTask.FromException<StorageCommitResult>(new System.IO.FileNotFoundException(staged));
                Data[destination] = bytes;
                Data.Remove(staged);
                return UniTask.FromResult(new StorageCommitResult(true));
            }
        }

        private sealed class Payload { public string Name; }

        private sealed class Utf8Serializer : ISaveSerializer
        {
            public ushort BackendId => SaveFormat.JSON;
            public byte[] Serialize<T>(T snapshot, CancellationToken cancellationToken)
                => Encoding.UTF8.GetBytes(((Payload)(object)snapshot).Name);
            public T Deserialize<T>(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
                => (T)(object)new Payload { Name = Encoding.UTF8.GetString(bytes.Span) };
        }

        private static SaveService Create(Files files)
        {
            var catalog = new InMemorySaveSerializerCatalog(
                new ISaveSerializer[] { new Utf8Serializer() },
                new[] { new SaveWriteDescriptor(typeof(Payload), "payload", 1, SaveFormat.JSON) });
            return new SaveService(catalog, files, slot => "slot" + slot + ".sav");
        }

        [UnityTest]
        public IEnumerator CommitThenRead_UsesWriteDescriptorAndPersistedId() => UniTask.ToCoroutine(async () =>
        {
            var files = new Files();
            var service = Create(files);
            SaveCommit commit = await service.CommitAsync(1, new Payload { Name = "hero" },
                CancellationToken.None);
            Assert.AreEqual(1, commit.Slot);
            Assert.IsTrue(commit.PowerLossDurable);
            Assert.IsTrue(files.Data.ContainsKey("slot1.sav"));
            Payload loaded = await service.ReadAsync<Payload>(1, SaveFormat.JSON, CancellationToken.None);
            Assert.AreEqual("hero", loaded.Name);
        });

        [UnityTest]
        public IEnumerator UnknownPersistedId_FailsWithoutReadingFile() => UniTask.ToCoroutine(async () =>
        {
            var files = new Files();
            var service = Create(files);
            Exception observed = null;
            try { await service.ReadAsync<Payload>(1, SaveFormat.PROTOBUF, CancellationToken.None); }
            catch (Exception failure) { observed = failure; }
            Assert.That(observed, Is.InstanceOf<SaveException>());
            Assert.AreEqual(SaveError.UnknownSerializer, ((SaveException)observed).Error);
        });

        [UnityTest]
        public IEnumerator MissingWriteDescriptor_FailsBeforeSerializing() => UniTask.ToCoroutine(async () =>
        {
            var files = new Files();
            var service = Create(files);
            Exception observed = null;
            try { await service.CommitAsync(1, new object(), CancellationToken.None); }
            catch (Exception failure) { observed = failure; }
            Assert.That(observed, Is.InstanceOf<SaveException>());
            Assert.AreEqual(SaveError.InvalidFormat, ((SaveException)observed).Error);
            Assert.IsEmpty(files.Data);
        });
    }
}
