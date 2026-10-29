using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Backends;
using Moirai.Atropos.Persistence;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Backends
{
    public sealed class BackendContractTests
    {
        private sealed class CloudSave : ICloudSaveBackend
        {
            internal long Version = 4;
            internal long? SeenExpected;
            public UniTask<CloudWriteResult> WriteAsync(string slotId, SaveSnapshot snapshot,
                long? expectedVersion, CallOptions call, CancellationToken cancellationToken)
            {
                SeenExpected = expectedVersion;
                if (expectedVersion.HasValue && expectedVersion.Value != Version)
                    return UniTask.FromResult(new CloudWriteResult(CloudWriteStatus.Conflict, Version));
                Version++;
                return UniTask.FromResult(new CloudWriteResult(CloudWriteStatus.Written, Version));
            }
            public UniTask<CloudReadResult> ReadAsync(string slotId, CallOptions call,
                CancellationToken cancellationToken)
                => UniTask.FromResult(new CloudReadResult(CloudReadStatus.Missing, new CloudSnapshot(null, 0, null)));
        }

        private sealed class Payment : IPaymentBackend
        {
            public UniTask<PaymentResult> PurchaseAsync(PurchaseRequest request, CallOptions call,
                CancellationToken cancellationToken)
                => UniTask.FromResult(new PaymentResult(PaymentStatus.Unknown, null));
            public UniTask<PaymentResult> ReconcileAsync(string purchaseKey, CallOptions call,
                CancellationToken cancellationToken)
                => UniTask.FromResult(new PaymentResult(PaymentStatus.Succeeded, "receipt"));
        }

        private sealed class Auth : IAuthBackend
        {
            public UniTask<AuthResult> SignInAsync(SignInRequest request, CallOptions call,
                CancellationToken cancellationToken)
                => UniTask.FromResult(new AuthResult(AuthStatus.Rejected, null));
        }

        private static CallOptions Call() => new CallOptions("trace", DateTimeOffset.MaxValue, "key");

        [UnityTest]
        public IEnumerator CloudSave_StaleExpectedVersion_ReportsConflict() => UniTask.ToCoroutine(async () =>
        {
            var backend = new CloudSave();
            var snapshot = new SaveSnapshot(1, new byte[] { 1, 2 });
            CloudWriteResult created = await backend.WriteAsync("slot", snapshot, 4, Call(),
                CancellationToken.None);
            Assert.IsTrue(created.Succeeded);
            CloudWriteResult conflict = await backend.WriteAsync("slot", snapshot, 4, Call(),
                CancellationToken.None);
            Assert.IsFalse(conflict.Succeeded);
            Assert.AreEqual(CloudWriteStatus.Conflict, conflict.Status);
            Assert.AreEqual(5, conflict.Version);
        });

        [UnityTest]
        public IEnumerator Payment_UnknownIsNotSuccess_UntilReconciled() => UniTask.ToCoroutine(async () =>
        {
            var backend = new Payment();
            PaymentResult unknown = await backend.PurchaseAsync(
                new PurchaseRequest("key", "product"), Call(), CancellationToken.None);
            Assert.IsFalse(unknown.Succeeded);
            Assert.AreEqual(PaymentStatus.Unknown, unknown.Status);
            PaymentResult reconciled = await backend.ReconcileAsync("key", Call(),
                CancellationToken.None);
            Assert.IsTrue(reconciled.Succeeded);
        });

        [UnityTest]
        public IEnumerator Auth_ClosedFailure_IsRejectedWithoutToken() => UniTask.ToCoroutine(async () =>
        {
            var backend = new Auth();
            AuthResult result = await backend.SignInAsync(new SignInRequest("a", "b"), Call(),
                CancellationToken.None);
            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Token);
            Assert.AreEqual(AuthStatus.Rejected, result.Status);
        });

        [Test]
        public void CapabilityFlags_AreComposable()
        {
            Moirai.Atropos.Plugins.PluginCapability combined =
                Moirai.Atropos.Plugins.PluginCapability.Resource |
                Moirai.Atropos.Plugins.PluginCapability.Audio;
            Assert.IsTrue((combined & Moirai.Atropos.Plugins.PluginCapability.Resource) != 0);
            Assert.IsTrue((combined & Moirai.Atropos.Plugins.PluginCapability.Audio) != 0);
            Assert.IsTrue((combined & Moirai.Atropos.Plugins.PluginCapability.Input) == 0);
        }
    }
}
