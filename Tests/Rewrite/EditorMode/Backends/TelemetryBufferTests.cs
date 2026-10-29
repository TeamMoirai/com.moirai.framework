using Moirai.Atropos.Backends.Policies;
using NUnit.Framework;

namespace Backends
{
    public sealed class TelemetryBufferTests
    {
        [Test]
        public void Capacity_DropsNewest_AndCountsDrops()
        {
            var buffer = new TelemetryBuffer(2);
            Assert.IsTrue(buffer.Enqueue("a"));
            Assert.IsTrue(buffer.Enqueue("b"));
            Assert.IsFalse(buffer.Enqueue("c"));
            Assert.AreEqual(2, buffer.Count);
            Assert.AreEqual(1, buffer.Dropped);
        }

        [Test]
        public void Dequeue_PreservesOrder_AndReportsEmpty()
        {
            var buffer = new TelemetryBuffer(4);
            buffer.Enqueue("a");
            buffer.Enqueue("b");
            Assert.IsTrue(buffer.TryDequeue(out string first));
            Assert.AreEqual("a", first);
            Assert.IsTrue(buffer.TryDequeue(out string second));
            Assert.AreEqual("b", second);
            Assert.IsFalse(buffer.TryDequeue(out _));
        }

        [Test]
        public void RevokeConsent_ClearsQueuedEvents()
        {
            var buffer = new TelemetryBuffer(4);
            buffer.Enqueue("a");
            buffer.Enqueue("b");
            buffer.RevokeConsent();
            Assert.AreEqual(0, buffer.Count);
            Assert.IsFalse(buffer.TryDequeue(out _));
        }
    }
}
