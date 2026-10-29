using System;
using Moirai.Atropos.Kernel.Scheduling;
using NUnit.Framework;

namespace Kernel
{
    public sealed class CallbackRegistryTests
    {
        [Test]
        public void Invoke_AddAndRemove_UsesEntryBoundary()
        {
            using (var registry = new CallbackRegistry(error => Assert.Fail(error.ToString())))
            {
                int late = 0;
                int removed = 0;
                IDisposable first = null;
                IDisposable victim = null;
                first = registry.Add(() =>
                {
                    registry.Add(() => late++);
                    victim.Dispose();
                    first.Dispose();
                });
                victim = registry.Add(() => removed++);
                registry.Invoke();
                Assert.That(late, Is.Zero);
                Assert.That(removed, Is.Zero);
                registry.Invoke();
                Assert.That(late, Is.EqualTo(1));
            }
        }

        [Test]
        public void Invoke_SubscriberFails_ReportsOriginalAndContinues()
        {
            var expected = new InvalidOperationException("expected");
            Exception reported = null;
            int remaining = 0;
            using (var registry = new CallbackRegistry(error => reported = error))
            {
                registry.Add(() => throw expected);
                registry.Add(() => remaining++);
                registry.Invoke();
            }
            Assert.That(reported, Is.SameAs(expected));
            Assert.That(remaining, Is.EqualTo(1));
        }

        [Test]
        public void Invoke_AfterDispose_DoesNothingAndAddRejects()
        {
            var registry = new CallbackRegistry(_ => { });
            registry.Dispose();
            registry.Invoke();
            Assert.Throws<ObjectDisposedException>(() => registry.Add(() => { }));
        }
    }
}
