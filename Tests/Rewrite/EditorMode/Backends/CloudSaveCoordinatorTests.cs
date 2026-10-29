using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Backends;
using Moirai.Atropos.Persistence;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Backends
{
    public sealed class CloudSaveCoordinatorTests
    {
        private sealed class Backend : ICloudSaveBackend
        {
            internal long? LastExpectedVersion;
            internal bool ExpectedSeen;
            internal CloudWriteResult WriteResult;
            internal CloudReadResult ReadResult;
            internal Exception Failure;

            public UniTask<CloudWriteResult> WriteAsync(string slotId, SaveSnapshot snapshot,
                long? expectedVersion, CallOptions call, CancellationToken cancellationToken)
            {
                if (Failure != null) return UniTask.FromException<CloudWriteResult>(Failure);
                ExpectedSeen = true;
                LastExpectedVersion = expectedVersion;
                return UniTask.FromResult(WriteResult);
            }

            public UniTask<CloudReadResult> ReadAsync(string slotId, CallOptions call,
                CancellationToken cancellationToken)
            {
                if (Failure != null) return UniTask.FromException<CloudReadResult>(Failure);
                return UniTask.FromResult(ReadResult);
            }
        }

        private static readonly CallOptions Call = new CallOptions("trace", default, null);

        private static SaveSnapshot Snapshot(int slot = 1)
            => new SaveSnapshot(slot, new byte[] { 1, 2, 3 });

        [UnityTest]
        public IEnumerator Upload_FirstTime_SendsNullExpectation_ThenRemembersVersion()
            => UniTask.ToCoroutine(async () =>
        {
            var backend = new Backend { WriteResult = new CloudWriteResult(CloudWriteStatus.Written, 5) };
            var coordinator = new CloudSaveCoordinator(backend);

            CloudWriteResult first = await coordinator.UploadAsync("slot", Snapshot(), Call);

            Assert.That(first.Succeeded, Is.True);
            Assert.That(backend.ExpectedSeen, Is.True);
            Assert.That(backend.LastExpectedVersion, Is.Null);
            coordinator.TryGetVersion("slot", out long version);
            Assert.AreEqual(5, version);
        });

        [UnityTest]
        public IEnumerator Upload_AfterSuccess_SendsRememberedVersion() => UniTask.ToCoroutine(async () =>
        {
            var backend = new Backend { WriteResult = new CloudWriteResult(CloudWriteStatus.Written, 5) };
            var coordinator = new CloudSaveCoordinator(backend);
            await coordinator.UploadAsync("slot", Snapshot(), Call);

            backend.WriteResult = new CloudWriteResult(CloudWriteStatus.Written, 6);
            await coordinator.UploadAsync("slot", Snapshot(), Call);

            Assert.AreEqual(5, backend.LastExpectedVersion);
            coordinator.TryGetVersion("slot", out long version);
            Assert.AreEqual(6, version);
        });

        [UnityTest]
        public IEnumerator ConflictAndUnknown_KeepRememberedVersion() => UniTask.ToCoroutine(async () =>
        {
            var backend = new Backend { WriteResult = new CloudWriteResult(CloudWriteStatus.Written, 3) };
            var coordinator = new CloudSaveCoordinator(backend);
            await coordinator.UploadAsync("slot", Snapshot(), Call);

            backend.WriteResult = new CloudWriteResult(CloudWriteStatus.Conflict, 9);
            CloudWriteResult conflict = await coordinator.UploadAsync("slot", Snapshot(), Call);
            Assert.That(conflict.Status, Is.EqualTo(CloudWriteStatus.Conflict));

            backend.WriteResult = new CloudWriteResult(CloudWriteStatus.Unknown, 0);
            await coordinator.UploadAsync("slot", Snapshot(), Call);

            coordinator.TryGetVersion("slot", out long version);
            Assert.AreEqual(3, version);
        });

        [UnityTest]
        public IEnumerator Download_FoundAdoptsVersion_MissingKeepsOld() => UniTask.ToCoroutine(async () =>
        {
            var backend = new Backend();
            var coordinator = new CloudSaveCoordinator(backend);

            backend.ReadResult = new CloudReadResult(CloudReadStatus.Found,
                new CloudSnapshot("slot", 4, Snapshot()));
            CloudReadResult found = await coordinator.DownloadAsync("slot", Call);
            Assert.That(found.Succeeded, Is.True);
            coordinator.TryGetVersion("slot", out long version);
            Assert.AreEqual(4, version);

            backend.ReadResult = new CloudReadResult(CloudReadStatus.Missing, default);
            await coordinator.DownloadAsync("slot", Call);
            coordinator.TryGetVersion("slot", out version);
            Assert.AreEqual(4, version);

            coordinator.Forget("slot");
            Assert.That(coordinator.TryGetVersion("slot", out _), Is.False);
        });

        [UnityTest]
        public IEnumerator Version_IsTrackedPerSlot() => UniTask.ToCoroutine(async () =>
        {
            var backend = new Backend { WriteResult = new CloudWriteResult(CloudWriteStatus.Written, 8) };
            var coordinator = new CloudSaveCoordinator(backend);
            await coordinator.UploadAsync("a", Snapshot(), Call);
            await coordinator.UploadAsync("b", new SaveSnapshot(2, new byte[] { 4 }), Call);

            coordinator.TryGetVersion("a", out long a);
            coordinator.TryGetVersion("b", out long b);
            Assert.AreEqual(8, a);
            Assert.AreEqual(8, b);
            Assert.That(coordinator.TryGetVersion("c", out _), Is.False);
        });

        [UnityTest]
        public IEnumerator FailedUpload_KeepsRememberedVersionAndRethrows() => UniTask.ToCoroutine(async () =>
        {
            var backend = new Backend { WriteResult = new CloudWriteResult(CloudWriteStatus.Written, 2) };
            var coordinator = new CloudSaveCoordinator(backend);
            await coordinator.UploadAsync("slot", Snapshot(), Call);

            var failure = new InvalidOperationException("offline");
            backend.Failure = failure;
            Exception observed = null;
            try { await coordinator.UploadAsync("slot", Snapshot(), Call); }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.SameAs(failure));
            coordinator.TryGetVersion("slot", out long version);
            Assert.AreEqual(2, version);
        });

        [UnityTest]
        public IEnumerator Upload_WithNullSlot_Throws() => UniTask.ToCoroutine(async () =>
        {
            var coordinator = new CloudSaveCoordinator(new Backend());

            Exception observed = null;
            try { await coordinator.UploadAsync(null, Snapshot(), Call); }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.InstanceOf<ArgumentNullException>());
        });
    }
}
