using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Plugins.Internal;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Plugins
{
    public sealed class PluginWorkScopeTests
    {
        [UnityTest]
        public IEnumerator Stop_WaitsForLateTicket_BeforeRelease() => UniTask.ToCoroutine(async () =>
        {
            var scope = new PluginWorkScope();
            PluginWorkScope.Ticket first = scope.Begin();
            PluginWorkScope.Ticket late = scope.Begin();
            UniTask stopping = scope.StopAsync(CancellationToken.None);
            first.Complete(new InvalidOperationException("backend failed"));
            Assert.AreEqual(1, scope.InFlight);
            Assert.AreEqual(UniTaskStatus.Pending, stopping.Status);
            Assert.Throws<ObjectDisposedException>(() => scope.Begin());
            late.Complete();
            Exception observed = null;
            try { await stopping; }
            catch (Exception failure) { observed = failure; }
            Assert.IsInstanceOf<AggregateException>(observed);
            Assert.AreEqual(1, ((AggregateException)observed).InnerExceptions.Count);
            Assert.AreEqual(0, scope.InFlight);
            try { await scope.ReleaseAfterDrainAsync(); }
            catch (AggregateException) { }
        });

        [UnityTest]
        public IEnumerator CallerCancellation_DoesNotCancelOwnerDrain() => UniTask.ToCoroutine(async () =>
        {
            var scope = new PluginWorkScope();
            PluginWorkScope.Ticket ticket = scope.Begin();
            using (var caller = new CancellationTokenSource())
            {
                UniTask waiting = scope.StopAsync(caller.Token);
                caller.Cancel();
                bool cancelled = await waiting.SuppressCancellationThrow();
                Assert.IsTrue(cancelled);
                Assert.AreEqual(1, scope.InFlight);
                ticket.Complete();
                await scope.StopAsync(CancellationToken.None);
                Assert.AreEqual(0, scope.InFlight);
            }
            await scope.ReleaseAfterDrainAsync();
        });
    }
}
