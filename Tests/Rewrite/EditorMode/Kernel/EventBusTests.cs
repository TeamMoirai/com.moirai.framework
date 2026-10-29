using System;
using System.Collections.Generic;
using Moirai.Atropos.Kernel.Events.Internal;
using Moirai.Atropos.Kernel.Logging;
using NUnit.Framework;

namespace Kernel
{
    public sealed class EventBusTests
    {
        private sealed class First { }
        private sealed class Second { }

        private sealed class RecordingLogger : ILogger
        {
            internal readonly List<Exception> Errors = new List<Exception>();
            public bool IsEnabled(LogLevel level, string tag) => true;
            public void Log(LogLevel level, string tag, string message, Exception exception = null)
                => Errors.Add(exception);
        }

        [Test]
        public void Publish_TypesAndInstances_AreIsolated()
        {
            using (var first = new EventBus(new RecordingLogger()))
            using (var second = new EventBus(new RecordingLogger()))
            {
                int calls = 0;
                var payload = new First();
                first.Subscribe<First>(value => { Assert.That(value, Is.SameAs(payload)); calls++; });
                first.Publish(new Second());
                second.Publish(payload);
                Assert.That(calls, Is.Zero);
                first.Publish(payload);
                Assert.That(calls, Is.EqualTo(1));
            }
        }

        [Test]
        public void Publish_SubscribeAndUnsubscribeDuringDispatch_UsesEntryBoundary()
        {
            using (var bus = new EventBus(new RecordingLogger()))
            {
                int late = 0;
                int removed = 0;
                IDisposable self = null;
                IDisposable victim = null;
                self = bus.Subscribe<First>(_ =>
                {
                    bus.Subscribe<First>(value => late++);
                    victim.Dispose();
                    self.Dispose();
                });
                victim = bus.Subscribe<First>(_ => removed++);
                bus.Publish(new First());
                Assert.That(late, Is.Zero);
                Assert.That(removed, Is.Zero);
                bus.Publish(new First());
                Assert.That(late, Is.EqualTo(1));
            }
        }

        [Test]
        public void Publish_Failure_ReportsOriginalAndContinues()
        {
            var logger = new RecordingLogger();
            var expected = new InvalidOperationException("expected");
            int calls = 0;
            using (var bus = new EventBus(logger))
            {
                bus.Subscribe<First>(_ => throw expected);
                bus.Subscribe<First>(_ => calls++);
                bus.Publish(new First());
            }
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(logger.Errors, Is.EqualTo(new[] { expected }));
        }
    }
}
